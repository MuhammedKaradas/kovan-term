using System.IO;
using KovanTerm.Models;
using KovanTerm.Services;
using KovanTerm.ViewModels;

namespace KovanTerm.Tests;

public class OllamaTests
{
    [Fact]
    public void Parse_reads_model_names_from_api_tags()
    {
        const string json = """
            { "models": [
              { "name": "qwen3-coder:latest", "size": 1 },
              { "name": "gemma4:31b" },
              { "model": "no-name" },
              { "name": "QWEN3-CODER:latest" } ] }
            """;

        Assert.Equal(["gemma4:31b", "qwen3-coder:latest"], OllamaModels.Parse(json));
        Assert.Empty(OllamaModels.Parse("{}"));
    }

    [Fact]
    public void WithModel_sets_every_model_name_Claude_Code_uses()
    {
        var p = OllamaModels.WithModel(ProfileStore.Ollama, " qwen3-coder ");

        foreach (var name in OllamaModels.ModelVariables) Assert.Equal("qwen3-coder", p.Environment![name]);
        Assert.Equal("http://localhost:11434", p.Environment!["ANTHROPIC_BASE_URL"]);
        Assert.Equal("", p.Environment["ANTHROPIC_API_KEY"]);
        // The built-in profile itself isn't changed
        Assert.False(ProfileStore.Ollama.Environment!.ContainsKey("ANTHROPIC_MODEL"));
    }

    [Fact]
    public void WithModel_leaves_profiles_without_a_model_source_alone()
    {
        var claude = ProfileStore.Defaults.First(p => p.Id == "claude");

        Assert.Same(claude, OllamaModels.WithModel(claude, "x"));
        Assert.Same(ProfileStore.Ollama, OllamaModels.WithModel(ProfileStore.Ollama, null));
    }

    private sealed class FakeDialogs : IDialogService
    {
        public bool ShowNewTerminal(NewTerminalViewModel viewModel) => true;
        public bool ShowRecentConversations(RecentConversationsViewModel viewModel) => false;
        public string? PickFolder(string? initialDirectory) => null;
        public Task<WorktreeCloseChoice> AskCloseWorktreeAsync(string title, string message) => Task.FromResult(WorktreeCloseChoice.Keep);
        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;
        public Task<bool> ConfirmAsync(string title, string message, string action) => Task.FromResult(false);
        public void OpenInBrowser(string url) { }
        public Task<int?> ChooseAsync(string title, string message, params string[] buttons) => Task.FromResult<int?>(null);
    }

    private static NewTerminalViewModel Dialog(Func<string, Task<IReadOnlyList<string>?>> list) =>
        new(new FakeDialogs(), [new ProfileOptionViewModel(ProfileStore.Ollama, @"C:\bin\claude.exe")], [Path.GetTempPath()], "claude-ollama")
        {
            ListModels = list,
        };

    [Fact]
    public async Task Dialog_lists_models_and_picks_the_first()
    {
        var d = Dialog(_ => Task.FromResult<IReadOnlyList<string>?>(["gemma4:31b", "qwen3-coder"]));
        await d.LoadModelsAsync();

        Assert.True(d.PicksModel);
        Assert.Equal(["gemma4:31b", "qwen3-coder"], d.ModelOptions);
        Assert.Equal("gemma4:31b", d.Model);
        Assert.Null(d.ValidationMessage);
        Assert.Equal("Claude Code (Ollama · gemma4:31b)", d.ModelTitle);
    }

    [Fact]
    public async Task Dialog_says_when_Ollama_isnt_running_and_needs_a_model()
    {
        var d = Dialog(_ => Task.FromResult<IReadOnlyList<string>?>(null));
        await d.LoadModelsAsync();

        Assert.Contains("isn't answering", d.ModelStatus);
        Assert.Equal("Pick a model.", d.ValidationMessage);
        Assert.False(d.ConfirmCommand.CanExecute(null));

        d.Model = "qwen3-coder";
        Assert.Null(d.ValidationMessage);
    }

    [Fact]
    public async Task Dialog_says_when_no_model_is_installed()
    {
        var d = Dialog(_ => Task.FromResult<IReadOnlyList<string>?>([]));
        await d.LoadModelsAsync();

        Assert.Contains("ollama pull", d.ModelStatus);
    }

    [Fact]
    public void Node_keeps_its_model_and_runs_with_it_again()
    {
        var state = new NodeState { Id = Guid.NewGuid(), Title = "t", CommandLine = "claude", ProfileId = "claude-ollama", Model = "qwen3-coder" };
        var node = TerminalNodeViewModel.FromState(state, profile: OllamaModels.WithModel(ProfileStore.Ollama, state.Model));

        Assert.Equal("qwen3-coder", node.ToState().Model);
        Assert.Equal("qwen3-coder", node.ExtraEnvironment!["ANTHROPIC_MODEL"]);
        Assert.Contains("ANTHROPIC_MODEL=qwen3-coder", node.StartupCommandLine);
    }
}
