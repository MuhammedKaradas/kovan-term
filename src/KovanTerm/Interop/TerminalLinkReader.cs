using System.Diagnostics;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using KovanTerm.Services;

namespace KovanTerm.Interop;

/// <summary>
/// The link at a point of a terminal, read through the UI Automation text pattern (the line under the point, and the
/// next one for a wrapped link). Call off the UI thread: the provider lives on it.
/// </summary>
public static class TerminalLinkReader
{
    public static string? LinkAt(IntPtr hwnd, System.Windows.Point screenPoint)
    {
        try
        {
            var element = AutomationElement.FromHandle(hwnd);
            if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var p)) return null;
            var pattern = (TextPattern)p;
            var at = pattern.RangeFromPoint(screenPoint);
            var line = at.Clone();
            line.ExpandToEnclosingUnit(TextUnit.Line);
            var before = line.Clone();
            before.MoveEndpointByRange(TextPatternRangeEndpoint.End, at, TextPatternRangeEndpoint.Start);
            var text = line.GetText(-1);
            var column = before.GetText(-1).Length;
            // The provider gives whole rows, padded to the terminal's width
            var next = line.Clone();
            var nextText = next.Move(TextUnit.Line, 1) == 1 ? Expand(next) : null;
            return TerminalLinks.At(text, column, nextText, width: text.TrimEnd('\r', '\n').Length);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or ArgumentException
                                       or System.Runtime.InteropServices.COMException)
        {
            Debug.WriteLine($"Couldn't read the link: {ex.Message}");
            return null;
        }

        static string Expand(TextPatternRange r)
        {
            r.ExpandToEnclosingUnit(TextUnit.Line);
            return r.GetText(-1);
        }
    }
}
