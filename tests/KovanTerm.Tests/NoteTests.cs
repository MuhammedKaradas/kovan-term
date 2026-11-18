using KovanTerm.Models;
using KovanTerm.Services;
using KovanTerm.ViewModels;

namespace KovanTerm.Tests;

public sealed class NoteTests
{
    [Fact]
    public void Parse_RecognizesBlocks()
    {
        var blocks = NoteMarkdown.Parse("# Plan\n\n- [x] done\n- [ ] todo\n  - nested\n3. third\n> quote\n---\ntext");

        Assert.Collection(blocks.Where(b => b.Kind != NoteBlockKind.Blank),
            b => Assert.Equal((NoteBlockKind.Heading, "Plan", 1), (b.Kind, b.Text, b.Level)),
            b => Assert.Equal((NoteBlockKind.Task, "done", true, 2), (b.Kind, b.Text, b.Checked, b.Line)),
            b => Assert.Equal((NoteBlockKind.Task, "todo", false), (b.Kind, b.Text, b.Checked)),
            b => Assert.Equal((NoteBlockKind.Bullet, "nested", 1), (b.Kind, b.Text, b.Level)),
            b => Assert.Equal((NoteBlockKind.Numbered, "third", "3"), (b.Kind, b.Text, b.Number)),
            b => Assert.Equal((NoteBlockKind.Quote, "quote"), (b.Kind, b.Text)),
            b => Assert.Equal(NoteBlockKind.Rule, b.Kind),
            b => Assert.Equal((NoteBlockKind.Paragraph, "text"), (b.Kind, b.Text)));
    }

    [Fact]
    public void Parse_KeepsFencedCodeAsIs()
    {
        var blocks = NoteMarkdown.Parse("```\n# not a heading\n- not a list\n```\nafter");

        Assert.Equal((NoteBlockKind.Code, "# not a heading\n- not a list"), (blocks[0].Kind, blocks[0].Text));
        Assert.Equal((NoteBlockKind.Paragraph, 4), (blocks[1].Kind, blocks[1].Line));
    }

    [Fact]
    public void ToggleTask_ChangesOnlyThatLine_AndKeepsLineEndings()
    {
        var text = "- [ ] a\r\n  - [x] b\r\nplain";

        Assert.Equal("- [x] a\r\n  - [x] b\r\nplain", NoteMarkdown.ToggleTask(text, 0));
        Assert.Equal("- [ ] a\r\n  - [ ] b\r\nplain", NoteMarkdown.ToggleTask(text, 1));
        Assert.Equal(text, NoteMarkdown.ToggleTask(text, 2));
        Assert.Equal(text, NoteMarkdown.ToggleTask(text, 9));
    }

    [Fact]
    public void Inline_FindsBoldItalicCodeAndLinks()
    {
        var spans = NoteMarkdown.Inline("Fix **parser**, *then* `dotnet test`; see [docs](https://example.com) or snake_case_name.");

        Assert.Equal(
            [
                (NoteSpanKind.Text, "Fix "), (NoteSpanKind.Bold, "parser"), (NoteSpanKind.Text, ", "),
                (NoteSpanKind.Italic, "then"), (NoteSpanKind.Text, " "), (NoteSpanKind.Code, "dotnet test"),
                (NoteSpanKind.Text, "; see "), (NoteSpanKind.Link, "docs"), (NoteSpanKind.Text, " or snake_case_name."),
            ],
            spans.Select(s => (s.Kind, s.Text)));
        Assert.Equal("https://example.com", spans.Single(s => s.Kind == NoteSpanKind.Link).Url);
    }

    [Fact]
    public void Inline_OnlyLinksToWebAddresses()
    {
        var spans = NoteMarkdown.Inline("[x](file:///C:/secret)");

        Assert.Equal([NoteSpanKind.Text], spans.Select(s => s.Kind));
    }

    [Fact]
    public void NoteNode_RoundTripsThroughState()
    {
        var node = new TerminalNodeViewModel { IsNote = true, Title = "Note", CommandLine = "", NoteText = "- [ ] a" };
        node.ToggleNoteTask(0);

        var state = node.ToState();
        var restored = TerminalNodeViewModel.FromState(state);

        Assert.Equal("- [x] a", state.Note);
        Assert.True(restored.IsNote);
        Assert.False(restored.IsTerminal);
        Assert.Equal("- [x] a", restored.NoteText);
        Assert.Null(TerminalNodeViewModel.FromState(new NodeState { Title = "cmd", CommandLine = "cmd.exe" }).ToState().Note);
    }
}
