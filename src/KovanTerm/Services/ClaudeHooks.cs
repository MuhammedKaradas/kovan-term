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
            File.Delete(EndpointPath(nodeId));
            File.Delete(WslSettingsPath(nodeId));
            File.Delete(WslMcpConfigPath(nodeId));
            File.Delete(WslMcpBridgePath(nodeId));
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

    // ---- Claude Code inside WSL ----
    //
    // With WSL 2's default (NAT) network, a distro can't reach Kovan's 127.0.0.1 listener, and listening on the WSL
    // network adapter would bring up a firewall prompt. But a distro can run Windows programs: the hooks and the MCP
    // bridge call Windows' own curl.exe, which connects from the Windows side. The address and token are read from
    // the endpoint file at every call, because the port changes with every Kovan run while an agent in a tmux session
    // (see WslLauncher.TmuxScript) keeps the settings it started with.

    public string EndpointPath(Guid nodeId) => Path.Combine(_directory, $"{nodeId:D}.endpoint");
    public string WslSettingsPath(Guid nodeId) => Path.Combine(_directory, $"{nodeId:D}.wsl.json");
    public string WslMcpConfigPath(Guid nodeId) => Path.Combine(_directory, $"{nodeId:D}.wsl.mcp.json");
    public string WslMcpBridgePath(Guid nodeId) => Path.Combine(_directory, $"{nodeId:D}.wsl.mcp.sh");

    /// <summary>Windows' curl.exe as seen from WSL.</summary>
    public static string WslCurl => ToWslPath(Path.Combine(Environment.SystemDirectory, "curl.exe"));

    /// <summary><c>C:\Users\me</c> → <c>/mnt/c/Users/me</c> (WSL's default mount point).</summary>
    public static string ToWslPath(string windowsPath) =>
        windowsPath.Length >= 2 && windowsPath[1] == ':'
            ? $"/mnt/{char.ToLowerInvariant(windowsPath[0])}{windowsPath[2..].Replace('\\', '/')}"
            : windowsPath.Replace('\\', '/');

    /// <summary>
    /// Claude Code in WSL: writes the endpoint file (hook URL, token, MCP URL), command-hook settings and an MCP
    /// config with a stdio bridge, and returns the arguments for them (shell-quoted Linux paths); empty on failure.
    /// </summary>
    public string PrepareWslArguments(Guid nodeId)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(EndpointPath(nodeId), $"{_server.UrlFor(nodeId)}\n{_server.Token}\n{_server.McpUrlFor(nodeId)}\n");
            var endpoint = ToWslPath(EndpointPath(nodeId));
            File.WriteAllText(WslSettingsPath(nodeId), BuildWslSettings(endpoint, WslCurl));
            File.WriteAllText(WslMcpConfigPath(nodeId), BuildWslMcpConfig(endpoint, WslCurl));
            return $"--settings {WslLauncher.ShellQuote(ToWslPath(WslSettingsPath(nodeId)))}"
                + $" --mcp-config {WslLauncher.ShellQuote(ToWslPath(WslMcpConfigPath(nodeId)))}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Couldn't write WSL hook settings, status won't be shown: {ex.Message}");
            return "";
        }
    }

    /// <summary>
    /// Codex in WSL: the agent mesh through the same stdio bridge as Claude Code's, as a script file Codex runs with
    /// <c>sh</c> (config overrides are TOML; single-quoted literal strings, so no double quote has to survive wsl.exe's
    /// command line). Codex has no hooks: its status comes from its OSC 7501 reports, which reach the terminal from
    /// anywhere. Empty on failure.
    /// </summary>
    public string PrepareWslCodexArguments(Guid nodeId)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(EndpointPath(nodeId), $"{_server.UrlFor(nodeId)}\n{_server.Token}\n{_server.McpUrlFor(nodeId)}\n");
            // LF only: sh reads a CR as part of the command
            File.WriteAllText(WslMcpBridgePath(nodeId), WslMcpBridge(ToWslPath(EndpointPath(nodeId)), WslCurl) + "\n");
            return CodexWslMcpArguments(ToWslPath(WslMcpBridgePath(nodeId)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Couldn't write the WSL MCP bridge, Codex won't reach the agent mesh: {ex.Message}");
            return "";
        }
    }

    public static string CodexWslMcpArguments(string bridgeScript) =>
        $"-c {WslLauncher.ShellQuote("mcp_servers.kovan.command='sh'")}"
        + $" -c {WslLauncher.ShellQuote($"mcp_servers.kovan.args=['{bridgeScript}']")}";

    /// <summary>
    /// Command hooks that post the event (stdin) with Windows' curl and print Kovan's answer (stdout, e.g. the
    /// agent mesh's inbox reminder). A missing endpoint file or a closed Kovan just does nothing.
    /// </summary>
    public static string BuildWslSettings(string endpointFile, string curl)
    {
        var f = WslLauncher.ShellQuote(endpointFile);
        var command = $"[ -r {f} ] && {WslLauncher.ShellQuote(curl)} -s -m {TimeoutSeconds} -H \"{AgentHookServer.TokenHeader}: $(sed -n 2p {f})\""
            + $" -H 'Content-Type: application/json' --data-binary @- \"$(sed -n 1p {f})\" || true";
        var hooks = new JsonObject();
        foreach (var name in Events)
        {
            hooks[name] = new JsonArray(new JsonObject
            {
                ["hooks"] = new JsonArray(new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = command,
                    // Starting a Windows program from WSL takes a moment
                    ["timeout"] = TimeoutSeconds + 5,
                }),
            });
        }
        return new JsonObject { ["hooks"] = hooks }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>An MCP stdio server that forwards each JSON-RPC line to Kovan's MCP endpoint and prints the answer.</summary>
    public static string BuildWslMcpConfig(string endpointFile, string curl)
    {
        var bridge = WslMcpBridge(endpointFile, curl);
        return new JsonObject
        {
            ["mcpServers"] = new JsonObject
            {
                ["kovan"] = new JsonObject { ["type"] = "stdio", ["command"] = "sh", ["args"] = new JsonArray("-c", bridge) },
            },
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>A shell loop that posts each JSON-RPC line from stdin to Kovan's MCP endpoint and prints the answer.</summary>
    public static string WslMcpBridge(string endpointFile, string curl)
    {
        var f = WslLauncher.ShellQuote(endpointFile);
        // A blank line isn't a message: posting it would only bring back a parse error
        return "while IFS= read -r line; do [ -z \"$line\" ] && continue;"
            + $" r=$(printf '%s' \"$line\" | {WslLauncher.ShellQuote(curl)} -s -m 60 -H 'Content-Type: application/json' --data-binary @- \"$(sed -n 3p {f})\");"
            + " [ -n \"$r\" ] && printf '%s\\n' \"$r\"; done";
    }

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
