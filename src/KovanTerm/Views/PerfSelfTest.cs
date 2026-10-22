using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using Nodify;

namespace KovanTerm.Views;

/// <summary>
/// Repeatable smoothness measurement without mouse input. Enabled with the
/// <c>KOVAN_PERF_SELFTEST=&lt;log file&gt;</c> environment variable: after the terminals start it zooms and pans
/// programmatically, writes frame intervals and snapshot mode timings to the file, then closes the window.
/// </summary>
internal sealed class PerfSelfTest
{
    // One step per rendered frame: frame intervals reflect the real rendering cost
    private const int Steps = 60;

    private readonly Window _window;
    private readonly NodifyEditor _editor;
    private readonly string _logPath;
    private readonly List<double> _frameGapsMs = [];
    private TimeSpan _lastFrame;
    private readonly StringBuilder _log = new();

    public static void AttachIfRequested(Window window, NodifyEditor editor, TerminalCanvasController canvas)
    {
        if (Environment.GetEnvironmentVariable("KOVAN_PERF_SELFTEST") is not { Length: > 0 } logPath) return;
        var test = new PerfSelfTest(window, editor, logPath);
        canvas.Timing += test.OnTiming;
        window.Loaded += async (_, _) => await test.RunAsync();
    }

    private PerfSelfTest(Window window, NodifyEditor editor, string logPath)
    {
        _window = window;
        _editor = editor;
        _logPath = logPath;
    }

    private void OnTiming(string name, TimeSpan duration) =>
        _log.AppendLine($"{name}: {duration.TotalMilliseconds:F1} ms");

    private async Task RunAsync()
    {
        // Wait for the terminals to start and render their first output
        await Task.Delay(8000);

        await MeasureAsync("zoom 1.0 → 0.5 → 1.0", i => _editor.ViewportZoom = 1.0 - 0.5 * Math.Sin(Math.PI * i / Steps));

        var start = _editor.ViewportLocation;
        await MeasureAsync("pan 600 units right and back", i => _editor.ViewportLocation = start + new Vector(600 * Math.Sin(Math.PI * i / Steps), 0));

        // Two moves back to back: if the second starts before the cache refreshes, snapshots are captured on the spot
        await MeasureAsync("zoom again right after", i => _editor.ViewportZoom = 1.0 - 0.3 * Math.Sin(Math.PI * i / Steps), settleMs: 2000);

        _log.Insert(0, $"Kovan Term smoothness self-test {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}");
        File.WriteAllText(_logPath, _log.ToString());
        _window.Close();
    }

    private async Task MeasureAsync(string name, Action<int> step, int settleMs = 400)
    {
        _log.AppendLine().AppendLine($"== {name}");
        _frameGapsMs.Clear();
        _lastFrame = TimeSpan.Zero;
        var done = new TaskCompletionSource();
        var i = 0;
        EventHandler onFrame = (_, e) =>
        {
            // RenderingTime can fire more than once for the same frame
            if (e is not RenderingEventArgs r || r.RenderingTime == _lastFrame) return;
            if (_lastFrame != TimeSpan.Zero) _frameGapsMs.Add((r.RenderingTime - _lastFrame).TotalMilliseconds);
            _lastFrame = r.RenderingTime;
            if (i > Steps) { done.TrySetResult(); return; }
            step(i++);
        };
        CompositionTarget.Rendering += onFrame;
        await done.Task;
        CompositionTarget.Rendering -= onFrame;
        var sorted = _frameGapsMs.Order().ToList();
        if (sorted.Count > 0)
            _log.AppendLine($"frames: {sorted.Count} | avg {sorted.Average():F1} ms (~{1000 / sorted.Average():F0} fps) | p95 {sorted[(int)(sorted.Count * 0.95)]:F1} ms | worst {sorted[^1]:F1} ms | over 33 ms {sorted.Count(g => g > 33)}");
        // Wait for things to settle so leaving snapshot mode is measured too
        await Task.Delay(settleMs);
    }

}
