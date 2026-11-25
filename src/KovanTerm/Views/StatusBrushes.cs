using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using KovanTerm.Models;

namespace KovanTerm.Views;

/// <summary>
/// The status dots' brushes. Working and waiting dots "breathe": one shared brush per status has its opacity changed,
/// so every dot of that status breathes in step and the cost doesn't grow with the number of nodes.
/// <para>
/// A redraw isn't free: about 1.2 ms on WPF's render thread for one small change, more when the changes are spread
/// over the canvas (WPF merges them into larger regions: ~2.8 ms for 5 dots on 10 nodes), plus UI-thread work (#150).
/// Breathing every dot all day would cost several % of a core, so it's rationed: a timer steps the opacity 8 times a second (a WPF animation redrew far more often even with a low
/// DesiredFrameRate), only while Kovan is the active window, and only for <see cref="BreathFor"/> after the counts
/// last changed; then the dots stay still. Its job is to catch the eye when something changes, not to run all day.
/// Off when Windows' animation effects are off.
/// </para>
/// </summary>
public static class StatusBrushes
{
    /// <summary>The status colours, and the diff's added and removed line tints.</summary>
    public sealed record Palette(Color Working, Color Waiting, Color Done, Color Error, Color Added, Color Removed, Color Hunk);

    public static Palette Default { get; } = new(Color.FromRgb(0x32, 0xCD, 0x32), Color.FromRgb(0xFF, 0xD7, 0x00),
        Color.FromRgb(0x46, 0x82, 0xB4), Color.FromRgb(0xCD, 0x5C, 0x5C),
        Color.FromArgb(0x33, 0x2E, 0xA0, 0x43), Color.FromArgb(0x33, 0xF8, 0x51, 0x49), Color.FromArgb(0x1A, 0x38, 0x8B, 0xFD));

    /// <summary>
    /// Okabe–Ito colours, which stay apart with the common kinds of colour blindness, where green, yellow and red don't:
    /// working bluish green, waiting orange, done sky blue, failed vermillion. Diffs use blue for added and orange for
    /// removed lines, as GitHub's colour-blind themes do.
    /// </summary>
    public static Palette ColorBlind { get; } = new(Color.FromRgb(0x00, 0x9E, 0x73), Color.FromRgb(0xE6, 0x9F, 0x00),
        Color.FromRgb(0x56, 0xB4, 0xE9), Color.FromRgb(0xD5, 0x5E, 0x00),
        Color.FromArgb(0x38, 0x38, 0x8B, 0xFD), Color.FromArgb(0x38, 0xDB, 0x6D, 0x28), Color.FromArgb(0x1A, 0x80, 0x80, 0x80));

    // Not frozen: the colour-blind palette recolours them in place, so every dot follows at once
    public static readonly SolidColorBrush Working = new(Default.Working);
    public static readonly SolidColorBrush Waiting = new(Default.Waiting);
    public static readonly SolidColorBrush Done = new(Default.Done);
    public static readonly SolidColorBrush Error = new(Default.Error);
    public static readonly Brush Idle = Frozen(Colors.Gray);

    public const string DiffAddedKey = "DiffAddedBrush", DiffRemovedKey = "DiffRemovedBrush", DiffHunkKey = "DiffHunkBrush";

    /// <summary>Switches the palette (Settings → Appearance); the diff tints are app resources.</summary>
    public static void ApplyPalette(bool colorBlind, ResourceDictionary resources)
    {
        var p = colorBlind ? ColorBlind : Default;
        Working.Color = p.Working;
        Waiting.Color = p.Waiting;
        Done.Color = p.Done;
        Error.Color = p.Error;
        resources[DiffAddedKey] = Frozen(p.Added);
        resources[DiffRemovedKey] = Frozen(p.Removed);
        resources[DiffHunkKey] = Frozen(p.Hunk);
    }

    public static readonly TimeSpan Step = TimeSpan.FromMilliseconds(StepMs);
    private const int StepMs = 125;

    // One breath (in and out): working is calm, waiting slower and deeper, so it catches the eye
    private static readonly Breath WorkingBreath = new(Working, Low: 0.35, Seconds: 2.0);
    private static readonly Breath WaitingBreath = new(Waiting, Low: 0.15, Seconds: 2.5);

    /// <summary>How long dots breathe after agents start or stop working or waiting.</summary>
    public static readonly TimeSpan BreathFor = TimeSpan.FromSeconds(60);

    private static DispatcherTimer? _timer;
    private static DateTime _start;
    private static DateTime _until;

    private sealed record Breath(SolidColorBrush Brush, double Low, double Seconds)
    {
        public bool On { get; set; }
    }

    public static Brush For(NodeStatus status) => status switch
    {
        NodeStatus.Working => Working,
        NodeStatus.Waiting => Waiting,
        NodeStatus.Done => Done,
        NodeStatus.Error => Error,
        _ => Idle,
    };

    /// <summary>Opacity at a moment of a breath: 1 at its start, <paramref name="low"/> halfway, eased (cosine).</summary>
    public static double OpacityAt(double seconds, double period, double low)
    {
        var phase = seconds % period / period;
        return low + (1 - low) * (1 + Math.Cos(2 * Math.PI * phase)) / 2;
    }

    /// <summary>
    /// Starts or stops each status' breathing (UI thread). <paramref name="changed"/>: the counts changed, so the dots
    /// breathe again for <see cref="BreathFor"/>; otherwise (Kovan activated or deactivated) only <paramref name="active"/> applies.
    /// </summary>
    public static void Update(bool anyWorking, bool anyWaiting, bool active, bool changed)
    {
        var now = DateTime.UtcNow;
        if (changed && (anyWorking, anyWaiting) != (false, false)) _until = now + BreathFor;
        var on = active && now < _until && SystemParameters.ClientAreaAnimation;
        WorkingBreath.On = on && anyWorking;
        WaitingBreath.On = on && anyWaiting;
        Apply(now);
    }

    private static void Apply(DateTime now)
    {
        if (now >= _until) WorkingBreath.On = WaitingBreath.On = false;
        if (!WorkingBreath.On) Working.Opacity = 1;
        if (!WaitingBreath.On) Waiting.Opacity = 1;

        var needed = WorkingBreath.On || WaitingBreath.On;
        if (needed && _timer == null)
        {
            _start = now;
            // Background priority: input and the canvas always come first
            _timer = new DispatcherTimer(Step, DispatcherPriority.Background, (_, _) => Tick(), Dispatcher.CurrentDispatcher);
            _timer.Start();
        }
        else if (!needed && _timer != null)
        {
            _timer.Stop();
            _timer = null;
        }
    }

    private static void Tick()
    {
        var now = DateTime.UtcNow;
        var t = (now - _start).TotalSeconds;
        foreach (var b in new[] { WorkingBreath, WaitingBreath })
            if (b.On) b.Brush.Opacity = Math.Round(OpacityAt(t, b.Seconds, b.Low), 2);
        if (now >= _until) Apply(now);
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
