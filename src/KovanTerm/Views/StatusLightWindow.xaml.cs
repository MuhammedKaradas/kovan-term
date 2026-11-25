using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace KovanTerm.Views;

/// <summary>
/// The status light: a small always-on-top pill on the edge of the screen while Kovan is in the background, showing how
/// many agents work (green) and wait for you (yellow). Clicking it brings Kovan forward on the waiting agent; dragging
/// moves it along the edge. It never takes focus and isn't in Alt+Tab.
/// </summary>
public partial class StatusLightWindow : Window
{
    private const int GWL_EXSTYLE = -20, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80;
    [DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

    private const double EdgeGap = 2;

    /// <summary>The pill was clicked.</summary>
    public event Action? Clicked;

    /// <summary>The user moved it (edge and height as a fraction of the work area), or turned it off.</summary>
    public event Action<bool, double>? Moved;
    public event Action? TurnedOff;

    /// <summary>The user switched between one dot and the dots with counts.</summary>
    public event Action<bool>? CompactChanged;

    private bool _compact;

    public void SetCompact(bool compact)
    {
        _compact = compact;
        CompactItem.IsChecked = compact;
        CompactDot.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        FullView.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        Pill.Padding = compact ? new Thickness(5) : new Thickness(5, 7, 5, 7);
    }

    private void Compact_Click(object sender, RoutedEventArgs e)
    {
        SetCompact(CompactItem.IsChecked);
        CompactChanged?.Invoke(_compact);
    }

    private bool _rightEdge = true;
    private double _top = 0.4;
    private Point _pressedAt;

    public StatusLightWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
        };
        SizeChanged += (_, _) => Place();
    }

    /// <summary>Which edge, and how far down the work area (0–1).</summary>
    public void SetPosition(bool rightEdge, double top)
    {
        _rightEdge = rightEdge;
        _top = Math.Clamp(top, 0, 1);
        Place();
    }

    /// <summary>Shows the counts, and the pill if it was hidden.</summary>
    public void ShowCounts(int working, int waiting)
    {
        WorkingText.Text = working.ToString();
        WaitingText.Text = waiting.ToString();
        WorkingDot.Opacity = WorkingText.Opacity = working > 0 ? 1 : 0.25;
        WaitingDot.Opacity = WaitingText.Opacity = waiting > 0 ? 1 : 0.25;
        CompactDot.Fill = waiting > 0 ? WaitingDot.Fill : WorkingDot.Fill;
        ToolTip = $"Kovan: {working} working, {waiting} waiting for you. Click to go there.";
        PillButton.SetValue(System.Windows.Automation.AutomationProperties.HelpTextProperty, ToolTip);
        if (!IsVisible) Show();
        Place();
    }

    private void Place()
    {
        var area = SystemParameters.WorkArea;
        Left = _rightEdge ? area.Right - ActualWidth - EdgeGap : area.Left + EdgeGap;
        Top = area.Top + _top * Math.Max(0, area.Height - ActualHeight);
    }

    // Invoked without the mouse (UI Automation, keyboard)
    private void Pill_Click(object sender, RoutedEventArgs e) => Clicked?.Invoke();

    // With the mouse: a press and release in place is a click; a press and move drags the pill along the edge
    private void Pill_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // DragMove takes the release, so the button wouldn't see a click: handled here either way
        e.Handled = true;
        _pressedAt = PointToScreen(e.GetPosition(this));
        // DragMove needs the button still down; released already, it was a click
        try { if (Mouse.LeftButton == MouseButtonState.Pressed) DragMove(); }
        catch (InvalidOperationException) { }
        var area = SystemParameters.WorkArea;
        var moved = (PointToScreen(e.GetPosition(this)) - _pressedAt).Length > 4;
        if (!moved)
        {
            Clicked?.Invoke();
            return;
        }
        // Back onto the nearer edge
        _rightEdge = Left + ActualWidth / 2 > area.Left + area.Width / 2;
        _top = Math.Clamp((Top - area.Top) / Math.Max(1, area.Height - ActualHeight), 0, 1);
        Place();
        Moved?.Invoke(_rightEdge, _top);
    }

    private void RightEdge_Click(object sender, RoutedEventArgs e) => MoveToEdge(right: true);

    private void LeftEdge_Click(object sender, RoutedEventArgs e) => MoveToEdge(right: false);

    private void MoveToEdge(bool right)
    {
        _rightEdge = right;
        Place();
        Moved?.Invoke(_rightEdge, _top);
    }

    private void TurnOff_Click(object sender, RoutedEventArgs e) => TurnedOff?.Invoke();
}
