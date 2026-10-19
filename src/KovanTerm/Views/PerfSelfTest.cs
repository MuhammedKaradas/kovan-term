using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using Nodify;

namespace KovanTerm.Views;

/// <summary>
/// Fare girdisi olmadan tekrarlanabilir akıcılık ölçümü. <c>KOVAN_PERF_SELFTEST=&lt;log dosyası&gt;</c>
/// ortam değişkeniyle açılır: terminaller başladıktan sonra programatik zoom ve kaydırma yapar,
/// kare aralıklarını ve fotoğraf modu sürelerini dosyaya yazar, sonra pencereyi kapatır.
/// </summary>
internal sealed class PerfSelfTest
{
    // Her ekran karesinde bir adım: kare aralıkları gerçek çizim maliyetini gösterir
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
        // Terminallerin başlayıp ilk çıktılarını çizmesini bekle
        await Task.Delay(8000);

        await MeasureAsync("zoom 1.0 → 0.5 → 1.0", i => _editor.ViewportZoom = 1.0 - 0.5 * Math.Sin(Math.PI * i / Steps));

        var start = _editor.ViewportLocation;
        await MeasureAsync("kaydırma 600 birim sağa ve geri", i => _editor.ViewportLocation = start + new Vector(600 * Math.Sin(Math.PI * i / Steps), 0));

        // Art arda iki hareket: ikincisi önbellek yenilenmeden başlarsa görüntüler anında alınır
        await MeasureAsync("hemen ardından tekrar zoom", i => _editor.ViewportZoom = 1.0 - 0.3 * Math.Sin(Math.PI * i / Steps), settleMs: 2000);

        _log.Insert(0, $"Kovan Term akıcılık öz-testi {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}");
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
            // RenderingTime aynı kare için birden çok kez gelebilir
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
            _log.AppendLine($"kare: {sorted.Count} | ort {sorted.Average():F1} ms (~{1000 / sorted.Average():F0} fps) | p95 {sorted[(int)(sorted.Count * 0.95)]:F1} ms | en kötü {sorted[^1]:F1} ms | 33 ms üstü {sorted.Count(g => g > 33)}");
        // Fotoğraf modundan çıkışı da ölçebilmek için yerleşmesini bekle
        await Task.Delay(settleMs);
    }

}
