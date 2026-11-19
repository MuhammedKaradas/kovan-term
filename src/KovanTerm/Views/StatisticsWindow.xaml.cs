using System.IO;
using System.Windows;
using System.Windows.Input;
using KovanTerm.Services;
using KovanTerm.Services.Stats;
using KovanTerm.ViewModels;
using Wpf.Ui.Controls;

namespace KovanTerm.Views;

/// <summary>Statistics: its own window, so the canvas's terminals don't cover it.</summary>
public partial class StatisticsWindow : FluentWindow
{
    private static StatisticsWindow? _open;

    private StatisticsWindow(StatisticsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Close, (_, _) => Close()));
        Closed += (_, _) => _open = null;
    }

    /// <summary>
    /// A picture of the period, its numbers and the calendar, at twice the screen's resolution, on the window's own
    /// background: copied to the clipboard and saved to Pictures\Kovan Term.
    /// </summary>
    private void Share_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not StatisticsViewModel vm) return;
        ShareTitle.Text = $"Kovan Term · {vm.PeriodText}";
        ShareTitle.Visibility = Visibility.Visible;
        ShareArea.UpdateLayout();
        try
        {
            var bounds = new Rect(ShareArea.RenderSize);
            const double scale = 2;
            var visual = new System.Windows.Media.DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var background = TryFindResource("ApplicationBackgroundBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.White;
                dc.DrawRectangle(background, null, new Rect(-20, -20, bounds.Width + 40, bounds.Height + 40));
                dc.DrawRectangle(new System.Windows.Media.VisualBrush(ShareArea), null, bounds);
            }
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)((bounds.Width + 40) * scale), (int)((bounds.Height + 40) * scale), 96 * scale, 96 * scale, System.Windows.Media.PixelFormats.Pbgra32);
            var shifted = new System.Windows.Media.DrawingVisual();
            using (var dc = shifted.RenderOpen())
            {
                dc.PushTransform(new System.Windows.Media.TranslateTransform(20, 20));
                dc.DrawDrawing(visual.Drawing);
            }
            bitmap.Render(shifted);
            bitmap.Freeze();
            Clipboard.SetImage(bitmap);
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Kovan Term");
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, $"Kovan Term {DateTime.Now:yyyy-MM-dd HHmmss}.png");
            using (var stream = File.Create(file))
            {
                var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
                png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                png.Save(stream);
            }
            ShareStatus.Text = "Copied; saved to Pictures\\Kovan Term";
            ShareStatus.ToolTip = file;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            ShareStatus.Text = $"Couldn't copy the picture: {ex.Message}";
        }
        finally
        {
            ShareTitle.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Opens the window, or brings it forward if it's open; reads the conversations in the background.</summary>
    public static void Show(Window owner)
    {
        if (_open != null)
        {
            if (_open.WindowState == WindowState.Minimized) _open.WindowState = WindowState.Normal;
            _open.Activate();
            return;
        }
        var cache = Path.Combine(AppPaths.DataDirectory, "stats-cache.json");
        var vm = new StatisticsViewModel(progress => WorkStats.Load(cache, progress: progress), loadCommits: sessions =>
            Task.Run(() => WorkCommits.LoadAsync(WorkCommits.Repositories(sessions.Select(s => s.Cwd)), DateTime.Now.AddYears(-1))));
        _open = new StatisticsWindow(vm) { Owner = owner };
        _open.Show();
        _ = vm.LoadAsync();
    }
}
