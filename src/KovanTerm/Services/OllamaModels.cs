using System.Net.Http;
using System.Text.Json;
using KovanTerm.Models;

namespace KovanTerm.Services;

/// <summary>
/// Claude Code on a local Ollama (its Anthropic-compatible API, Ollama 0.14+): the installed models for the New
/// terminal dialog, and the profile a node runs with once a model is picked.
/// </summary>
public static class OllamaModels
{
    public const string Source = "ollama";
    public const string DefaultAddress = "http://localhost:11434";

    /// <summary>
    /// Claude Code asks for a model under several names (the main one, Opus/Sonnet/Haiku for side tasks, subagents);
    /// Ollama knows none of them, so every one is the picked model.
    /// </summary>
    public static IReadOnlyList<string> ModelVariables { get; } =
    [
        "ANTHROPIC_MODEL", "ANTHROPIC_DEFAULT_OPUS_MODEL", "ANTHROPIC_DEFAULT_SONNET_MODEL", "ANTHROPIC_DEFAULT_HAIKU_MODEL",
        "CLAUDE_CODE_SUBAGENT_MODEL",
    ];

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

    /// <summary>The installed models' names (<c>GET /api/tags</c>), sorted; <c>null</c> if Ollama isn't answering.</summary>
    public static async Task<IReadOnlyList<string>?> ListAsync(string address = DefaultAddress)
    {
        try
        {
            var json = await Http.GetStringAsync(address.TrimEnd('/') + "/api/tags").ConfigureAwait(false);
            return Parse(json);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Model names from an <c>/api/tags</c> answer: <c>{"models":[{"name":"qwen3-coder:latest",…}]}</c>.</summary>
    public static IReadOnlyList<string> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array) return [];
        return models.EnumerateArray()
            .Select(m => m.ValueKind == JsonValueKind.Object && m.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null)
            .OfType<string>()
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The profile with <paramref name="model"/> set for every model name; unchanged without a model.</summary>
    public static TerminalProfile WithModel(TerminalProfile profile, string? model)
    {
        model = model?.Trim();
        if (string.IsNullOrEmpty(model) || profile.ModelSource == null) return profile;
        var environment = new Dictionary<string, string>(profile.Environment ?? new Dictionary<string, string>());
        foreach (var name in ModelVariables) environment[name] = model;
        return profile with { Environment = environment };
    }
}
