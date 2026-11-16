using System.Diagnostics;
using System.Text;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace KovanTerm.Interop;

/// <summary>
/// Reads the text a terminal shows, through the UI Automation text pattern the Windows Terminal control provides.
/// The control has no other way to read its buffer. Measured: ~15 ms for the first read of a terminal, under 1 ms
/// after that. Call off the UI thread: the provider lives on the UI thread and answers through window messages.
/// </summary>
public sealed class TerminalScreenReader
{
    private readonly Dictionary<IntPtr, TextPattern> _patterns = new();

    /// <summary>The visible screen's text, or <c>null</c> if the terminal can't be read (closed, not ready).</summary>
    public string? Read(IntPtr hwnd)
    {
        lock (_patterns)
        {
            try
            {
                if (!_patterns.TryGetValue(hwnd, out var pattern))
                {
                    var element = AutomationElement.FromHandle(hwnd);
                    if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var p)) return null;
                    _patterns[hwnd] = pattern = (TextPattern)p;
                }
                var sb = new StringBuilder();
                foreach (TextPatternRange range in pattern.GetVisibleRanges())
                    sb.Append(range.GetText(-1));
                return sb.ToString();
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or ArgumentException
                                           or System.Runtime.InteropServices.COMException)
            {
                // The terminal went away or was recreated: look it up again next time
                _patterns.Remove(hwnd);
                Debug.WriteLine($"Couldn't read terminal screen: {ex.Message}");
                return null;
            }
        }
    }

    public void Forget(IntPtr hwnd)
    {
        lock (_patterns) _patterns.Remove(hwnd);
    }
}
