using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EasyWindowsTerminalControl;
using Microsoft.Web.WebView2.Wpf;
using KovanTerm.Interop;
using KovanTerm.Services;
using Microsoft.Terminal.Wpf;
using Nodify;

namespace KovanTerm.Views;

/// <summary>
/// Handles the HwndHost limitations of the terminal controls on the canvas:
/// <list type="bullet">
/// <item>While panning/zooming/dragging, shows snapshots instead of the live terminals ("snapshot mode").</item>
/// <item>Caches snapshots in the background while idle, so switching into motion is instant.</item>
/// <item>Puts terminals outside the viewport to sleep; when zoomed far out, shows the last snapshot instead of the live terminal.</item>
/// <item>Since the HWND doesn't scale with WPF zoom, adjusts the font size to the zoom.</item>
/// <item>Kills the terminal's process tree when the node closes.</item>
/// </list>
/// The terminal control is expected to sit inside a <see cref="Grid"/> with an <see cref="Image"/> sibling.
/// </summary>
public sealed class TerminalCanvasController
{
    private const int BaseFontSize = 13;
    private const int MinFontSize = 4;
    /// <summary>Below this zoom text is unreadable (≤5 pt); the last snapshots are shown instead of the live terminals.</summary>
    public const double SleepZoomThreshold = 0.4;

    /// <summary>A terminal on the canvas and its cached snapshot.</summary>
    private sealed class Entry(FrameworkElement host, EasyTerminalControl? terminal, WebView2? browser)
    {
        /// <summary>What's hidden and shown and snapshotted: the terminal control, or the preview's host border.</summary>
        public FrameworkElement Host { get; } = host;
        public EasyTerminalControl? Terminal { get; } = terminal;
        /// <summary>A web preview node's WebView2 (also an HWND, so the same rules apply).</summary>
        public WebView2? Browser { get; } = browser;
        /// <summary>When a preview was last captured (<see cref="Environment.TickCount64"/>); pages change without telling.</summary>
        public long LastCapture;
        /// <summary>Has the terminal output changed since the last snapshot? Written from the reader thread.</summary>
        public volatile bool Dirty = true;
        public BitmapSource? Snapshot;
        /// <summary>
        /// A sleeping terminal's HWND is hidden: it isn't rendered and its snapshot isn't refreshed. The process keeps
        /// running and output keeps going into the terminal buffer; the current state is rendered on wake.
        /// </summary>
        public bool Sleeping;
        /// <summary>Window region currently applied to the HWND (<see cref="TerminalClip"/>); <c>null</c> = none.</summary>
        public Native.RECT? Region;
        /// <summary>Output arrived since the screen was last read for screen rules. Written from the reader thread.</summary>
        public volatile bool ScreenChanged = true;
        public bool ScreenReading;
        /// <summary>When output last arrived / when the screen was last read (<see cref="Environment.TickCount64"/>).</summary>
        public long LastOutput, LastScreenRead;
    }

    private readonly NodifyEditor _editor;
    // Key: the terminal's DataContext (the node ViewModel). Kept independent of the visual tree
    // so the terminal can still be found after the node is removed from the list.
    private readonly Dictionary<object, Entry> _byNode = new();

    // Switch back to the live terminals this long after motion stops
    private readonly DispatcherTimer _settleTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private bool _frozen;
    // For open-ended interactions such as resizing, stay in snapshot mode until the interaction ends
    private bool _held;
    private readonly DispatcherTimer _snapshotCleanupTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private int _appliedFontSize = BaseFontSize;
    private bool _dark = true;

    // Cache: snapshots of terminals whose output changed are refreshed in the background.
    // PrintWindow waits ~one DWM frame but doesn't block the UI thread.
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private bool _refreshInFlight;

    // Node to focus; if it's sleeping or in snapshot mode, it's focused once the live terminal is back
    private object? _pendingFocus;

    /// <summary>Raised when a terminal gets keyboard focus (click, shortcut); the argument is the node ViewModel.</summary>
    public event Action<object>? NodeFocused;

    /// <summary>The user typed a key that answers a prompt (Enter, Escape, a digit) into a terminal (see <see cref="PromptKeys"/>).</summary>
    public event Action<object>? AnswerKeyTyped;

    /// <summary>A terminal printed a local web address (see <see cref="LocalUrls"/>).</summary>
    public event Action<object, string>? LocalUrlPrinted;

    /// <summary>The program in a terminal reported its status with OSC 7501 (see <see cref="ProgramStatusParser"/>).</summary>
    public event Action<object, IReadOnlyList<ProgramStatusReport>>? ProgramStatusReported;

    /// <summary>Snapshot mode enter/exit timings (for measurement; see <see cref="PerfSelfTest"/>).</summary>
    public event Action<string, TimeSpan>? Timing;

    /// <summary>Snapshot mode starts (<c>true</c>) or ends: the canvas is moving, other heavy content can simplify too.</summary>
    public event Action<bool>? MotionChanged;

    // ---- Screen rules ----

    /// <summary>Which nodes' screens to read (those whose agent has screen rules); none if not set.</summary>
    public Func<object, bool>? WatchScreen { get; set; }

    /// <summary>A watched terminal's visible text, read after its output changed.</summary>
    public event Action<object, string>? ScreenRead;

    private readonly TerminalScreenReader _screenReader = new();
    private readonly DispatcherTimer _screenTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    /// <summary>A program showing a prompt waits for the user, so its output stops: read once it has settled this long.</summary>
    private const long ScreenSettleMs = 400;
    /// <summary>A terminal that keeps printing (a spinner, a build) is read at most this often.</summary>
    private const long BusyScreenReadMs = 3000;
    /// <summary>Even after output settles, no terminal is read more often than this: a prompt shows within 1.5 s.</summary>
    private const long MinScreenReadMs = 1500;

    private void ReadChangedScreens()
    {
        if (WatchScreen == null) return;
        var now = Environment.TickCount64;
        foreach (var (node, entry) in _byNode)
        {
            if (!entry.ScreenChanged || entry.ScreenReading || !WatchScreen(node)) continue;
            var settled = now - entry.LastOutput >= ScreenSettleMs;
            var sinceRead = now - entry.LastScreenRead;
            if (sinceRead < MinScreenReadMs || (!settled && sinceRead < BusyScreenReadMs)) continue;
            entry.LastScreenRead = now;
            if (entry.Terminal?.Terminal is not { } terminal || TerminalSnapshot.FindHwnd(terminal) is not { } hwnd) continue;
            entry.ScreenChanged = false;
            entry.ScreenReading = true;
            Task.Run(() => _screenReader.Read(hwnd)).ContinueWith(t =>
            {
                entry.ScreenReading = false;
                if (t.Result is { } text && _byNode.ContainsKey(node)) ScreenRead?.Invoke(node, text);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
    }

    public TerminalCanvasController(NodifyEditor editor)
    {
        _editor = editor;
        _settleTimer.Tick += (_, _) =>
        {
            _settleTimer.Stop();
            if (!_held) Unfreeze();
        };
        _snapshotCleanupTimer.Tick += (_, _) => RemoveSnapshots();
        _refreshTimer.Tick += (_, _) => RefreshDirtySnapshots();
        _refreshTimer.Start();
        _screenTimer.Tick += (_, _) => ReadChangedScreens();
        _screenTimer.Start();
        _editor.ViewportUpdated += (_, _) => OnCanvasMoving();
        _editor.AddHandler(ItemContainer.LocationChangedEvent, new RoutedEventHandler((_, _) => OnCanvasMoving()));
    }

    public void Register(EasyTerminalControl term)
    {
        if (term.DataContext is not { } node || _byNode.ContainsKey(node)) return;
        var entry = new Entry(term, term, null);
        _byNode[node] = entry;
        // The snapshot goes stale as output arrives; it must also be retaken when the size changes (resize, font size)
        if (term.ConPTYTerm != null)
        {
            var status = new ProgramStatusParser();
            term.ConPTYTerm.TerminalOutput += (_, e) =>
            {
                entry.Dirty = true;
                entry.ScreenChanged = true;
                entry.LastOutput = Environment.TickCount64;
                if (LocalUrls.FindLast(e.Data) is { } url)
                    term.Dispatcher.BeginInvoke(() => LocalUrlPrinted?.Invoke(node, url));
                // Reader thread: only a vectorized search per chunk unless a status report is in it
                var reports = status.Feed(e.Data);
                if (reports.Count > 0)
                    term.Dispatcher.BeginInvoke(() => ProgramStatusReported?.Invoke(node, reports));
            };
            term.ConPTYTerm.InterceptInputToTermApp += (ref Span<char> input) =>
            {
                if (PromptKeys.IsAnswer(input))
                    term.Dispatcher.BeginInvoke(() => AnswerKeyTyped?.Invoke(node));
            };
        }
        term.SizeChanged += (_, _) => entry.Dirty = true;
        term.IsKeyboardFocusWithinChanged += (_, e) =>
        {
            if (e.NewValue is true) NodeFocused?.Invoke(node);
        };
        // Once the terminal is ready, apply the font size for the current zoom (the workspace may have opened at a different zoom)
        term.Dispatcher.BeginInvoke(() =>
        {
            _appliedFontSize = FontSizeForZoom(_editor.ViewportZoom);
            SetFont(term, _appliedFontSize);
            UpdateSleepStates();
            UpdateClipRegions();
        }, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>Types text into the node's terminal as if the user had (works while it sleeps too).</summary>
    public void SendInput(object node, string text)
    {
        if (_byNode.TryGetValue(node, out var entry))
            entry.Terminal?.ConPTYTerm?.WriteToTerm(text);
    }

    private const long PreviewRecaptureMs = 2000;

    /// <summary>
    /// A web preview node: its WebView2 is an HWND like a terminal, so it's hidden behind a snapshot while the canvas
    /// moves, sleeps off screen and is clipped to the canvas. <paramref name="host"/> wraps the WebView2 and sits in a
    /// Grid next to the snapshot Image.
    /// </summary>
    public void RegisterPreview(FrameworkElement host, WebView2 browser)
    {
        if (host.DataContext is not { } node || _byNode.ContainsKey(node)) return;
        _byNode[node] = new Entry(host, null, browser);
        browser.ZoomFactor = _editor.ViewportZoom;
        host.Dispatcher.BeginInvoke(() =>
        {
            UpdateSleepStates();
            UpdateClipRegions();
        }, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>The preview's page changed (navigation): its snapshot must be retaken.</summary>
    public void PreviewChanged(object node)
    {
        if (_byNode.TryGetValue(node, out var entry)) entry.Dirty = true;
    }

    private void Release(Entry entry)
    {
        if (entry.Terminal is { } term)
        {
            if (term.Terminal is { } terminal && TerminalSnapshot.FindHwnd(terminal) is { } hwnd) _screenReader.Forget(hwnd);
            KillProcessTree(term);
        }
        entry.Browser?.Dispose();
    }

    /// <summary>Kills the node's terminal and every process under it (e.g. cmd → claude → MCP servers).</summary>
    public void Terminate(object node)
    {
        if (!_byNode.Remove(node, out var entry)) return;
        Release(entry);
    }

    public void TerminateAll()
    {
        _refreshTimer.Stop();
        foreach (var entry in _byNode.Values)
            Release(entry);
        _byNode.Clear();
    }

    private static void KillProcessTree(EasyTerminalControl term)
    {
        var pty = term.ConPTYTerm;
        if (pty == null) return;
        try
        {
            if (pty.Process is { HasExited: false } process)
                process.Kill(EntireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException)
        {
            // The process may have exited on its own in the meantime
            Debug.WriteLine($"Couldn't kill terminal process: {ex.Message}");
        }

        // The library doesn't close the pseudo-console when the process ends, leaving conhost.exe running.
        // ClosePseudoConsole can block, so it isn't called on the UI thread.
        Task.Run(() =>
        {
            try { ((ITerminalConnection)pty).Close(); }
            catch (Exception ex) { Debug.WriteLine($"Couldn't close pseudo-console: {ex.Message}"); }
        });
    }

    /// <summary>
    /// Gives keyboard focus to the node's terminal. If the terminal is currently hidden (snapshot mode or sleeping),
    /// it's focused once the live terminal is back.
    /// </summary>
    /// <summary>Another workspace is shown: wake its terminals in view, put the hidden ones to sleep.</summary>
    public void RefreshVisibility()
    {
        UpdateSleepStates();
        UpdateClipRegions();
    }

    public void FocusNode(object node)
    {
        if (!_byNode.TryGetValue(node, out var entry)) return;
        if (_frozen || entry.Sleeping)
        {
            _pendingFocus = node;
            return;
        }
        _pendingFocus = null;
        // The terminal control is the TerminalControl containing the HwndHost; focusing it calls SetFocus on the HWND
        if (entry.Terminal?.Terminal?.Focus() != true)
            entry.Host.Focus();
    }

    /// <summary>Replaces terminals with snapshots for the duration of an interaction (e.g. resizing).</summary>
    public void BeginInteraction()
    {
        _held = true;
        Freeze();
    }

    /// <summary>Interaction ended; switches back to the live terminals shortly after.</summary>
    public void EndInteraction()
    {
        _held = false;
        _settleTimer.Stop();
        _settleTimer.Start();
    }

    private void OnCanvasMoving()
    {
        if (!_editor.IsLoaded) return;
        Freeze();
        _settleTimer.Stop();
        _settleTimer.Start();
    }

    private void Freeze()
    {
        if (_frozen) return;
        _frozen = true;
        _snapshotCleanupTimer.Stop();
        MotionChanged?.Invoke(true);
        var sw = Stopwatch.StartNew();
        // Use cached snapshots that are up to date; capture the rest now in one go. A sleeping terminal is
        // hidden and can't be captured; its existing snapshot is used.
        var clip = CanvasScreenRect();
        var stale = _byNode.Values
            .Where(e => !e.Sleeping && (e.Dirty || e.Snapshot == null))
            .Select(e => (Entry: e, Target: TerminalSnapshot.GetTarget(e.Host, clip)))
            .Where(x => x.Target != null)
            .ToList();
        var shots = TerminalSnapshot.CaptureAll(stale.Select(x => x.Target!).ToList(), TerminalThemes.BackgroundFor(_dark));
        for (var i = 0; i < stale.Count; i++)
        {
            if (shots[i] is not { } shot) continue;
            stale[i].Entry.Snapshot = shot;
            stale[i].Entry.Dirty = false;
        }

        foreach (var entry in _byNode.Values)
        {
            // A stale snapshot is better than none
            if (entry.Snapshot is not { } shot || GetSnapshotImage(entry.Host) is not { } image) continue;
            image.Source = shot;
            image.Visibility = Visibility.Visible;
            entry.Host.Visibility = Visibility.Hidden;
        }
        int captured = stale.Count, fromCache = _byNode.Count - captured;
        Timing?.Invoke($"enter snapshot mode ({_byNode.Count} terminals: {fromCache} from cache, {captured} captured now)", sw.Elapsed);
    }

    private void Unfreeze()
    {
        if (!_frozen) return;
        _frozen = false;
        MotionChanged?.Invoke(false);
        var sw = Stopwatch.StartNew();
        ApplyZoomToTerminals();
        foreach (var entry in _byNode.Values)
        {
            // Location/font size may have changed; the snapshot must be retaken
            entry.Dirty = true;
            entry.Sleeping = false;
        }
        var sleeping = UpdateSleepStates();
        Timing?.Invoke($"back to live terminals ({_byNode.Count} terminals, {sleeping} sleeping)", sw.Elapsed);
        if (_pendingFocus is { } pending)
            FocusNode(pending);
        // Keep the snapshot behind until the terminal renders its first frame (especially if the font size changed);
        // removing it immediately shows a brief blank frame
        _snapshotCleanupTimer.Start();
        // HWNDs are repositioned in the next layout pass; clip them once that has happened
        _editor.Dispatcher.BeginInvoke(UpdateClipRegions, DispatcherPriority.Loaded);
    }

    private void RemoveSnapshots()
    {
        _snapshotCleanupTimer.Stop();
        if (_frozen) return;
        UpdateClipRegions();
        foreach (var entry in _byNode.Values)
        {
            // A sleeping terminal's snapshot (when zoomed far out) stays in place
            if (entry.Sleeping) continue;
            if (GetSnapshotImage(entry.Host) is { } image) { image.Visibility = Visibility.Collapsed; image.Source = null; }
        }
    }

    /// <summary>
    /// Puts terminals outside the viewport to sleep and wakes those that come into view. When zoomed far out
    /// (<see cref="SleepZoomThreshold"/>), all terminals that have a snapshot sleep and their last snapshots are shown.
    /// Not called in snapshot mode; <see cref="Unfreeze"/> calls it when motion stops. Returns the number of sleeping terminals.
    /// </summary>
    /// <summary>Clips every awake terminal to the canvas so it can't draw over the toolbar/title bar (#41).</summary>
    private void UpdateClipRegions()
    {
        if (_frozen || CanvasScreenRect() is not { } clip) return;
        foreach (var entry in _byNode.Values)
        {
            if (entry.Sleeping || TerminalSnapshot.FindHwnd(entry.Host) is not { } hwnd) continue;
            entry.Region = TerminalClip.Apply(hwnd, clip, entry.Region);
        }
    }

    /// <summary>The canvas area in screen (device) coordinates, or <c>null</c> if it isn't on screen.</summary>
    private Native.RECT? CanvasScreenRect()
    {
        if (PresentationSource.FromVisual(_editor) == null) return null;
        var topLeft = _editor.PointToScreen(new Point(0, 0));
        var bottomRight = _editor.PointToScreen(new Point(_editor.ActualWidth, _editor.ActualHeight));
        return new Native.RECT { Left = (int)topLeft.X, Top = (int)topLeft.Y, Right = (int)bottomRight.X, Bottom = (int)bottomRight.Y };
    }

    private int UpdateSleepStates()
    {
        if (_frozen) return 0;
        var viewport = new Rect(_editor.ViewportLocation, _editor.ViewportSize);
        var lowZoom = _editor.ViewportZoom < SleepZoomThreshold;
        var count = 0;
        foreach (var entry in _byNode.Values)
        {
            if (entry.Host.DataContext is not ViewModels.TerminalNodeViewModel node) continue;
            // A node on another workspace is hidden (its container is collapsed) and sleeps like one off screen
            var offscreen = !node.IsInActiveWorkspace || !viewport.IntersectsWith(node.Bounds);
            // A terminal without a snapshot isn't put to sleep when zoomed out, otherwise a blank would show in its place
            var sleep = offscreen || (lowZoom && entry.Snapshot != null);
            var image = GetSnapshotImage(entry.Host);
            if (sleep)
            {
                count++;
                entry.Sleeping = true;
                entry.Host.Visibility = Visibility.Hidden;
                if (image == null) continue;
                var showImage = !offscreen && entry.Snapshot != null;
                image.Source = showImage ? entry.Snapshot : null;
                image.Visibility = showImage ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                if (entry.Sleeping) entry.Dirty = true; // output may have arrived while sleeping
                entry.Sleeping = false;
                entry.Host.Visibility = Visibility.Visible;
            }
        }
        return count;
    }

    /// <summary>
    /// Refreshes the snapshots of all visible terminals whose output changed, with one capture of the window
    /// on a background thread (~one DWM frame). The UI thread is used only briefly to collect the target info.
    /// </summary>
    private void RefreshDirtySnapshots()
    {
        if (_frozen || _refreshInFlight) return;
        var clip = CanvasScreenRect();
        var batch = new List<(Entry Entry, TerminalSnapshot.Target Target)>();
        foreach (var entry in _byNode.Values)
        {
            // A page changes without telling: previews are recaptured every couple of seconds while shown
            var due = entry.Dirty || (entry.Browser != null && Environment.TickCount64 - entry.LastCapture > PreviewRecaptureMs);
            if (!due || entry.Host.Visibility != Visibility.Visible) continue;
            entry.LastCapture = Environment.TickCount64;
            if (TerminalSnapshot.GetTarget(entry.Host, clip) is not { } target) continue;
            // Clear the flag first so output arriving during capture marks it dirty again
            entry.Dirty = false;
            batch.Add((entry, target));
        }
        if (batch.Count == 0) return;

        _refreshInFlight = true;
        var ui = TaskScheduler.FromCurrentSynchronizationContext();
        var targets = batch.Select(b => b.Target).ToList();
        Task.Run(() => TerminalSnapshot.CaptureAll(targets, TerminalThemes.BackgroundFor(_dark)))
            .ContinueWith(t =>
            {
                _refreshInFlight = false;
                for (var i = 0; i < batch.Count; i++)
                {
                    if (t.Result[i] is { } shot) batch[i].Entry.Snapshot = shot;
                    else batch[i].Entry.Dirty = true;
                }
            }, ui);
    }

    private static Image? GetSnapshotImage(FrameworkElement host) =>
        (host.Parent as Grid)?.Children.OfType<Image>().FirstOrDefault();

    private void ApplyZoomToTerminals()
    {
        // A preview's page is zoomed like the canvas, so it looks the same live and in snapshots
        foreach (var entry in _byNode.Values)
            if (entry.Browser is { } browser && Math.Abs(browser.ZoomFactor - _editor.ViewportZoom) > 0.001)
                browser.ZoomFactor = _editor.ViewportZoom;
        var size = FontSizeForZoom(_editor.ViewportZoom);
        if (size == _appliedFontSize) return;
        _appliedFontSize = size;
        foreach (var entry in _byNode.Values)
            if (entry.Terminal is { } term) SetFont(term, size);
    }

    private static int FontSizeForZoom(double zoom) => Math.Max(MinFontSize, (int)Math.Round(BaseFontSize * zoom));

    /// <summary>Switches every terminal's color scheme; snapshots are retaken with the new colors.</summary>
    public void SetDarkTheme(bool dark)
    {
        if (dark == _dark) return;
        _dark = dark;
        foreach (var entry in _byNode.Values)
        {
            if (entry.Terminal is { } term) SetFont(term, _appliedFontSize);
            entry.Dirty = true;
        }
    }

    private void SetFont(EasyTerminalControl term, int size) =>
        term.Terminal?.SetTheme(TerminalThemes.For(_dark), "Cascadia Code", (short)size);
}
