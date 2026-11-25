using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KovanTerm.Models;

namespace KovanTerm.Services;

/// <summary>
/// Receives Claude Code's HTTP hooks: a minimal HTTP server on 127.0.0.1 with a random port. Each request is
/// <c>POST /hook/&lt;node id&gt;</c> with the hook's JSON input and the per-run token in <see cref="TokenHeader"/>.
/// It never answers with a decision, so the agent behaves exactly as without Kovan.
/// A plain socket instead of HttpListener: HttpListener needs a URL reservation (admin) on Windows.
/// </summary>
public sealed class AgentHookServer : IDisposable
{
    public const string TokenHeader = "X-Kovan-Token";
    private const int MaxHeaderBytes = 16 * 1024;
    private const int MaxBodyBytes = 1024 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private static readonly byte[] OkResponse = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
    private static readonly byte[] ForbiddenResponse = Encoding.ASCII.GetBytes("HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
    private static readonly byte[] AcceptedResponse = Encoding.ASCII.GetBytes("HTTP/1.1 202 Accepted\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
    private static readonly byte[] MethodNotAllowedResponse = Encoding.ASCII.GetBytes("HTTP/1.1 405 Method Not Allowed\r\nAllow: POST\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
    private static readonly byte[] BadRequestResponse = Encoding.ASCII.GetBytes("HTTP/1.1 400 Bad Request\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cts = new();

    /// <summary>Random per run; only hook files written by this run can report.</summary>
    public string Token { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    public int Port { get; }

    /// <summary>Raised on a thread-pool thread for every valid hook request.</summary>
    public event Action<Guid, AgentEvent>? EventReceived;

    /// <summary>
    /// A hook event from a Claude Code session started outside Kovan (<see cref="OutsideHook"/>), on a thread-pool
    /// thread. Sessions Kovan started report here too; those are told apart by their session id.
    /// </summary>
    public event Action<AgentEvent>? OutsideEventReceived;

    public const string OutsidePath = "/outside";
    public string OutsideUrl => $"http://127.0.0.1:{Port}{OutsidePath}";

    /// <summary>
    /// Optional JSON to answer a hook with (e.g. <c>additionalContext</c> for <c>UserPromptSubmit</c>); called on a
    /// thread-pool thread, so it must be thread-safe. <c>null</c>: an empty answer, the agent carries on as usual.
    /// </summary>
    public Func<Guid, AgentEvent, string?>? HookResponse { get; set; }

    /// <summary>Handles an MCP request body for a node (thread-pool thread); <c>null</c> = no body (a notification).</summary>
    public Func<Guid, string, string?>? McpHandler { get; set; }

    public AgentHookServer()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = AcceptLoop();
    }

    public string UrlFor(Guid nodeId) => $"http://127.0.0.1:{Port}/hook/{nodeId:D}";

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_cts.Token); }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException) { return; }
            _ = Handle(client);
        }
    }

    private async Task Handle(TcpClient client)
    {
        using var _ = client;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            var stream = client.GetStream();
            var request = await ReadRequest(stream, timeout.Token);
            var response = request switch
            {
                null => BadRequestResponse,
                // MCP: the token is in the URL, since not every agent can send custom headers to an MCP server
                { } r when ParseMcpPath(r.Path) is var (mcpNode, mcpToken) =>
                    !TokenMatches(mcpToken) ? ForbiddenResponse
                    : r.Method != "POST" ? MethodNotAllowedResponse
                    : McpHandler?.Invoke(mcpNode, Encoding.UTF8.GetString(r.Body)) is { } json ? JsonResponse(json)
                    : AcceptedResponse,
                { } r when !TokenMatches(r.Token) => ForbiddenResponse,
                { Method: "POST", Path: OutsidePath } r when ParseEvent(r.Body) is { } e => RaiseOutside(e),
                { Method: "POST" } r when ParsePath(r.Path) is { } nodeId && ParseEvent(r.Body) is { } e => Raise(nodeId, e),
                _ => BadRequestResponse,
            };
            await stream.WriteAsync(response, timeout.Token);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException or ObjectDisposedException)
        {
            Debug.WriteLine($"Hook request failed: {ex.Message}");
        }
    }

    private byte[] Raise(Guid nodeId, AgentEvent e)
    {
        EventReceived?.Invoke(nodeId, e);
        return HookResponse?.Invoke(nodeId, e) is { } json ? JsonResponse(json) : OkResponse;
    }

    private byte[] RaiseOutside(AgentEvent e)
    {
        OutsideEventReceived?.Invoke(e);
        return OkResponse;
    }

    private static byte[] JsonResponse(string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        return [.. head, .. body];
    }

    public string McpUrlFor(Guid nodeId) => $"http://127.0.0.1:{Port}/mcp/{nodeId:D}/{Token}";

    /// <summary>Node and token from <c>/mcp/&lt;guid&gt;/&lt;token&gt;</c>; <c>null</c> for anything else.</summary>
    public static (Guid Node, string Token)? ParseMcpPath(string path)
    {
        if (!path.StartsWith("/mcp/", StringComparison.Ordinal)) return null;
        var parts = path["/mcp/".Length..].Split('/', 2);
        return parts.Length == 2 && Guid.TryParse(parts[0], out var id) ? (id, parts[1]) : null;
    }

    private bool TokenMatches(string? token) =>
        token != null && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(token), Encoding.ASCII.GetBytes(Token));

    private sealed record Request(string Method, string Path, string? Token, byte[] Body);

    private static async Task<Request?> ReadRequest(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[MaxHeaderBytes];
        var length = 0;
        int headerEnd;
        while ((headerEnd = IndexOfHeaderEnd(buffer, length)) < 0)
        {
            if (length == buffer.Length) return null;
            var n = await stream.ReadAsync(buffer.AsMemory(length), ct);
            if (n == 0) return null;
            length += n;
        }

        var lines = Encoding.ASCII.GetString(buffer, 0, headerEnd).Split("\r\n");
        var requestLine = lines[0].Split(' ');
        if (requestLine is not [var method, var path, _]) return null;

        string? token = null;
        var contentLength = 0;
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (name.Equals(TokenHeader, StringComparison.OrdinalIgnoreCase)) token = value;
            else if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) && !int.TryParse(value, out contentLength)) return null;
        }
        if (contentLength is < 0 or > MaxBodyBytes) return null;

        var body = new byte[contentLength];
        var already = Math.Min(length - (headerEnd + 4), contentLength);
        Array.Copy(buffer, headerEnd + 4, body, 0, already);
        await stream.ReadExactlyAsync(body.AsMemory(already), ct);
        return new Request(method, path, token, body);
    }

    private static int IndexOfHeaderEnd(byte[] buffer, int length) =>
        buffer.AsSpan(0, length).IndexOf("\r\n\r\n"u8);

    /// <summary>Node id from <c>/hook/&lt;guid&gt;</c>; <c>null</c> for anything else.</summary>
    public static Guid? ParsePath(string path) =>
        path.StartsWith("/hook/", StringComparison.Ordinal) && Guid.TryParse(path["/hook/".Length..], out var id) ? id : null;

    /// <summary>The fields Kovan uses from a hook's JSON input; <c>null</c> if it isn't one.</summary>
    public static AgentEvent? ParseEvent(ReadOnlySpan<byte> json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json.ToArray());
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Str(root, "hook_event_name") is not { } name) return null;
            return new AgentEvent(name, Str(root, "session_id"), Str(root, "tool_name"), Str(root, "notification_type"),
                Str(root, "transcript_path"), Str(root, "agent_id"), Str(root, "agent_type"), Str(root, "cwd"));
        }
        catch (JsonException)
        {
            return null;
        }

        static string? Str(JsonElement o, string key) =>
            o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
    }
}
