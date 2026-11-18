using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using KovanTerm.Services;
using KovanTerm.ViewModels;

namespace KovanTerm.Views;

/// <summary>
/// Shows a note's Markdown in a <see cref="FlowDocumentScrollViewer"/> (<c>NoteRenderer.Markdown="{Binding NoteText}"</c>).
/// Task checkboxes are live: ticking one changes the note's text.
/// </summary>
public static class NoteRenderer
{
    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.RegisterAttached(
        "Markdown", typeof(string), typeof(NoteRenderer), new PropertyMetadata(null, OnMarkdownChanged));

    public static string? GetMarkdown(DependencyObject d) => (string?)d.GetValue(MarkdownProperty);
    public static void SetMarkdown(DependencyObject d, string? value) => d.SetValue(MarkdownProperty, value);

    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FlowDocumentScrollViewer viewer) viewer.Document = Render((string?)e.NewValue ?? "", viewer);
    }

    private static FlowDocument Render(string text, FlowDocumentScrollViewer viewer)
    {
        var doc = new FlowDocument { PagePadding = new Thickness(12, 8, 12, 12), FontSize = 13, FontFamily = viewer.FontFamily };
        doc.SetResourceReference(FlowDocument.ForegroundProperty, "TextFillColorPrimaryBrush");
        if (text.Trim().Length == 0)
        {
            var hint = new Paragraph(new Run("Empty note. Click Edit to write in Markdown."));
            hint.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorTertiaryBrush");
            doc.Blocks.Add(hint);
            return doc;
        }

        foreach (var block in NoteMarkdown.Parse(text))
        {
            if (block.Kind == NoteBlockKind.Blank) continue;
            var margin = new Thickness(block.Level * 18, 0, 0, 4);
            Block element = block.Kind switch
            {
                NoteBlockKind.Heading => Heading(block),
                NoteBlockKind.Bullet => Prefixed("•  ", block, margin),
                NoteBlockKind.Numbered => Prefixed($"{block.Number}.  ", block, margin),
                NoteBlockKind.Task => Task(block, margin, viewer),
                NoteBlockKind.Quote => Quote(block),
                NoteBlockKind.Code => Code(block),
                NoteBlockKind.Rule => Rule(),
                _ => Spans(new Paragraph { Margin = new Thickness(0, 0, 0, 6) }, block.Text),
            };
            doc.Blocks.Add(element);
        }
        return doc;
    }

    private static Paragraph Spans(Paragraph p, string text)
    {
        foreach (var span in NoteMarkdown.Inline(text))
        {
            Inline inline = span.Kind switch
            {
                NoteSpanKind.Bold => new Bold(new Run(span.Text)),
                NoteSpanKind.Italic => new Italic(new Run(span.Text)),
                NoteSpanKind.Code => CodeRun(span.Text),
                NoteSpanKind.Link => Link(span),
                _ => new Run(span.Text),
            };
            p.Inlines.Add(inline);
        }
        return p;
    }

    private static Paragraph Heading(NoteBlock block)
    {
        var p = Spans(new Paragraph { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 6) }, block.Text);
        p.FontSize = block.Level switch { 1 => 20, 2 => 17, 3 => 15, _ => 13 };
        return p;
    }

    private static Paragraph Prefixed(string prefix, NoteBlock block, Thickness margin)
    {
        var p = new Paragraph { Margin = margin };
        p.Inlines.Add(new Run(prefix));
        return Spans(p, block.Text);
    }

    private static Paragraph Task(NoteBlock block, Thickness margin, FlowDocumentScrollViewer viewer)
    {
        var box = new CheckBox
        {
            IsChecked = block.Checked,
            MinWidth = 0, MinHeight = 0, Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 6, -4),
            ToolTip = block.Checked ? "Mark as not done" : "Mark as done",
        };
        System.Windows.Automation.AutomationProperties.SetName(box, block.Text);
        var line = block.Line;
        // Checked/Unchecked rather than Click: UI Automation's Toggle doesn't raise Click
        RoutedEventHandler toggled = (_, _) =>
        {
            if (viewer.DataContext is TerminalNodeViewModel note) note.ToggleNoteTask(line);
        };
        box.Checked += toggled;
        box.Unchecked += toggled;
        var p = new Paragraph { Margin = margin };
        p.Inlines.Add(new InlineUIContainer(box) { BaselineAlignment = BaselineAlignment.Center });
        Spans(p, block.Text);
        if (block.Checked)
        {
            p.TextDecorations = TextDecorations.Strikethrough;
            p.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorTertiaryBrush");
        }
        return p;
    }

    private static Block Quote(NoteBlock block)
    {
        var p = Spans(new Paragraph { Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(10, 2, 0, 2), BorderThickness = new Thickness(3, 0, 0, 0) }, block.Text);
        p.SetResourceReference(Block.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        p.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorSecondaryBrush");
        return p;
    }

    private static Block Code(NoteBlock block)
    {
        var p = new Paragraph(new Run(block.Text)) { FontFamily = Mono, FontSize = 12, Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 2, 0, 8) };
        p.SetResourceReference(TextElement.BackgroundProperty, "ControlFillColorSecondaryBrush");
        return p;
    }

    private static Block Rule()
    {
        var p = new Paragraph { Margin = new Thickness(0, 6, 0, 10), BorderThickness = new Thickness(0, 0, 0, 1), FontSize = 2 };
        p.SetResourceReference(Block.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        return p;
    }

    private static Run CodeRun(string text)
    {
        var run = new Run(text) { FontFamily = Mono, FontSize = 12 };
        run.SetResourceReference(TextElement.BackgroundProperty, "ControlFillColorSecondaryBrush");
        return run;
    }

    private static Hyperlink Link(NoteSpan span)
    {
        var link = new Hyperlink(new Run(span.Text)) { ToolTip = span.Url };
        link.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(span.Url!) { UseShellExecute = true }); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Debug.WriteLine(ex.Message); }
        };
        return link;
    }
}
