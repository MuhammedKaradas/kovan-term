using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KovanTerm.Services;

/// <summary>
/// Shows Claude Code sessions started outside Kovan (VS Code, Windows Terminal…) in the Agents panel (#138). Only
/// when the user turns it on in Settings: then one command hook, <c>KovanLaunch.exe --hook</c>, is added to the
/// user's <c>~/.claude/settings.json</c> for a few events, and turning it off takes it out again. The hook runs in the
/// background (<c>async</c>), reads Kovan's address from <see cref="EndpointPath"/> and returns at once when Kovan
/// isn't running, so a session started elsewhere behaves exactly as before.
/// </summary>
public sealed class OutsideHook
{
    /// <summary>Enough to tell working from waiting from done; PreToolUse is left out, it fires before every tool.</summary>
    public static IReadOnlyList<string> Events { get; } =
        ["SessionStart", "UserPromptSubmit", "PermissionRequest", "Notification", "PostToolUse", "Stop", "StopFailure", "SessionEnd"];

    private const int TimeoutSeconds = 5;

    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        // Keep the user's own text as they wrote it (no \u escapes for non-ASCII characters, '+' or '<')
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public OutsideHook(string? settingsPath = null, string? endpointPath = null, string? launcherPath = null)
    {
        SettingsPath = settingsPath
            ?? (Environment.GetEnvironmentVariable("KOVAN_CLAUDE_SETTINGS") is { Length: > 0 } custom
                ? custom
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json"));
        EndpointPath = endpointPath ?? Path.Combine(AppPaths.DataDirectory, "outside.endpoint");
        LauncherPath = launcherPath ?? EnvironmentLauncher.LauncherPath;
    }

    /// <summary>The user's Claude Code settings (<c>KOVAN_CLAUDE_SETTINGS</c> overrides it for tests).</summary>
    public string SettingsPath { get; }

    /// <summary>Kovan's hook address and token while it runs: the port changes with every run.</summary>
    public string EndpointPath { get; }

    public string LauncherPath { get; }

    /// <summary>Forward slashes: Claude Code may run hook commands with Git Bash, where a backslash is an escape.</summary>
    public string Command => $"\"{LauncherPath.Replace('\\', '/')}\" --hook \"{EndpointPath.Replace('\\', '/')}\"";

    /// <summary>Is this one of Kovan's hook commands (from this or another Kovan folder)?</summary>
    public static bool IsKovanCommand(string? command) =>
        command != null && command.Contains("KovanLaunch", StringComparison.OrdinalIgnoreCase)
        && command.Contains("--hook", StringComparison.Ordinal);

    public bool IsInstalled()
    {
        try
        {
            return File.Exists(SettingsPath) && Contains(File.ReadAllText(SettingsPath), Command);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Adds the hook (or brings its path up to date, e.g. after Kovan moved). Throws if the file can't be read as JSON.</summary>
    public void Install() => Rewrite(json => Add(json, Command));

    /// <summary>Takes Kovan's hook out; the rest of the file stays as it is.</summary>
    public void Uninstall()
    {
        if (File.Exists(SettingsPath)) Rewrite(Remove);
        DeleteEndpoint();
    }

    public void WriteEndpoint(string url, string token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(EndpointPath)!);
        File.WriteAllText(EndpointPath, $"{url}\n{token}\n");
    }

    public void DeleteEndpoint()
    {
        try { File.Delete(EndpointPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private void Rewrite(Func<string?, string?> change)
    {
        var current = File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath) : null;
        if (change(current) is not { } updated || updated == current) return;
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        // Written next to it and swapped in, so Claude Code never reads half a file
        var temp = SettingsPath + ".kovan.tmp";
        File.WriteAllText(temp, updated);
        File.Move(temp, SettingsPath, overwrite: true);
    }

    /// <summary>The settings with Kovan's hook on <see cref="Events"/> (older Kovan entries replaced).</summary>
    public static string Add(string? json, string command)
    {
        var root = Parse(json);
        RemoveFrom(root);
        var hooks = root["hooks"] as JsonObject ?? new JsonObject();
        root["hooks"] = hooks;
        foreach (var name in Events)
        {
            var list = hooks[name] as JsonArray ?? new JsonArray();
            hooks[name] = list;
            list.Add(new JsonObject
            {
                ["hooks"] = new JsonArray(new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = command,
                    ["timeout"] = TimeoutSeconds,
                    ["async"] = true,
                }),
            });
        }
        return root.ToJsonString(Indented);
    }

    /// <summary>The settings without Kovan's hook; <c>null</c> if it wasn't there (the file is left untouched).</summary>
    public static string? Remove(string? json)
    {
        if (json == null) return null;
        var root = Parse(json);
        return RemoveFrom(root) ? root.ToJsonString(Indented) : null;
    }

    public static bool Contains(string json, string command)
    {
        try
        {
            return Parse(json)["hooks"] is JsonObject hooks
                && Events.All(name => hooks[name] is JsonArray list && list.Any(m => Commands(m).Contains(command)));
        }
        catch (JsonException) { return false; }
    }

    private static JsonObject Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new JsonObject();
        return JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonObject
            ?? throw new JsonException("Claude Code's settings aren't a JSON object.");
    }

    private static IEnumerable<string?> Commands(JsonNode? matcher) =>
        matcher?["hooks"] is JsonArray list
            ? list.Select(h => h?["command"] is JsonValue v && v.TryGetValue<string>(out var c) ? c : null)
            : [];

    /// <summary>Drops Kovan's entries, and the matchers and events they leave empty. Did it find any?</summary>
    private static bool RemoveFrom(JsonObject root)
    {
        if (root["hooks"] is not JsonObject hooks) return false;
        var found = false;
        foreach (var (name, value) in hooks.ToList())
        {
            if (value is not JsonArray matchers) continue;
            var here = false;
            foreach (var matcher in matchers.ToList())
            {
                if (matcher?["hooks"] is not JsonArray list) continue;
                var ours = list.Where(h => h is JsonObject o && o["command"] is JsonValue v
                    && v.TryGetValue<string>(out var c) && IsKovanCommand(c)).ToList();
                if (ours.Count == 0) continue;
                foreach (var hook in ours) list.Remove(hook);
                if (list.Count == 0) matchers.Remove(matcher);
                here = true;
            }
            if (here && matchers.Count == 0) hooks.Remove(name);
            found |= here;
        }
        if (found && hooks.Count == 0) root.Remove("hooks");
        return found;
    }
}
