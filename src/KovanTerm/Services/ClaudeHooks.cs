using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KovanTerm.Services;

/// <summary>
/// Per-node Claude Code settings that send its hook events to <see cref="AgentHookServer"/>. Passed with
/// <c>--settings</c>, so only sessions Kovan starts report, and the user's own <c>~/.claude/settings.json</c>
/// is never touched. Claude merges these hooks with the user's own.
/// </summary>
public sealed class ClaudeHooks
{
    /// <summary>Events that change what the node shows (see <see cref="AgentStatusReducer"/>).</summary>
    public static IReadOnlyList<string> Events { get; } =
    [
        "SessionStart", "UserPromptSubmit", "PreToolUse", "PostToolUse", "PostToolUseFailure", "PermissionRequest",
        "PermissionDenied", "Notification", "Stop", "StopFailure", "SubagentStart", "SubagentStop", "SessionEnd",
    ];

    // Hooks run before Claude continues; if Kovan isn't answering, give up quickly
    private const int TimeoutSeconds = 3;

    private readonly AgentHookServer _server;
    private readonly string _directory;

    public ClaudeHooks(AgentHookServer server, string? directory = null)
    {
        _server = server;
        _directory = directory ?? Path.Combine(AppPaths.DataDirectory, "hooks");
    }

    /// <summary>Is this executable Claude Code? (<c>claude</c>, <c>claude.exe</c>, <c>claude.cmd</c>…)</summary>
    public static bool IsClaude(string executablePath) =>
        Path.GetFileNameWithoutExtension(executablePath).Equals("claude", StringComparison.OrdinalIgnoreCase);

    public string SettingsPath(Guid nodeId) => Path.Combine(_directory, $"{nodeId:D}.json");

    /// <summary>
    /// Writes the node's settings file (the port changes every run, so it's rewritten at each launch) and returns
    /// the arguments to add to the command line; empty if the file couldn't be written, so the agent still starts.
    /// </summary>
    public string PrepareArguments(Guid nodeId)
    {
        var path = SettingsPath(nodeId);
        try
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(path, BuildSettings(_server.UrlFor(nodeId), _server.Token));
            return $"--settings \"{path}\"";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Couldn't write hook settings, status won't be shown: {ex.Message}");
            return "";
        }
    }

    public void Remove(Guid nodeId)
    {
        try
        {
            File.Delete(SettingsPath(nodeId));
            File.Delete(McpConfigPath(nodeId));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Debug.WriteLine(ex.Message); }
    }

    // ---- Agent mesh (MCP) ----

    public string McpConfigPath(Guid nodeId) => Path.Combine(_directory, $"{nodeId:D}.mcp.json");

    /// <summary>
    /// Claude Code: an <c>--mcp-config</c> file adding Kovan's MCP server (<see cref="Mesh.MeshServer"/>) next to the
    /// user's own servers. Empty if the file couldn't be written.
    /// </summary>
    public string PrepareMcpArguments(Guid nodeId)
    {
        var path = McpConfigPath(nodeId);
        try
        {
            Directory.CreateDirectory(_directory);
            var config = new JsonObject
            {
                ["mcpServers"] = new JsonObject
                {
                    ["kovan"] = new JsonObject { ["type"] = "http", ["url"] = _server.McpUrlFor(nodeId) },
                },
            };
            File.WriteAllText(path, config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return $"--mcp-config \"{path}\"";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Couldn't write MCP config: {ex.Message}");
            return "";
        }
    }

    /// <summary>Codex: the same server through a config override (its value is a TOML string; a URL needs no quoting).</summary>
    public string CodexMcpArguments(Guid nodeId) => $"-c mcp_servers.kovan.url={_server.McpUrlFor(nodeId)}";

    public static string BuildSettings(string url, string token)
    {
        var hooks = new JsonObject();
        foreach (var name in Events)
        {
            hooks[name] = new JsonArray(new JsonObject
            {
                ["hooks"] = new JsonArray(new JsonObject
                {
                    ["type"] = "http",
                    ["url"] = url,
                    ["headers"] = new JsonObject { [AgentHookServer.TokenHeader] = token },
                    ["timeout"] = TimeoutSeconds,
                }),
            });
        }
        return new JsonObject { ["hooks"] = hooks }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
