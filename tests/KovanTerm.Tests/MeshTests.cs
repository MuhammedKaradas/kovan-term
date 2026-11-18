using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using KovanTerm.Models;
using KovanTerm.Services;
using KovanTerm.Services.Mesh;

namespace KovanTerm.Tests;

public sealed class MeshTests
{
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid();
    private static readonly MeshAgent[] Agents =
    [
        new(A, "Claude Code (aaaa)", @"C:\shop", "kovan/claude-1", "working"),
        new(B, "Codex (bbbb)", @"C:\shop", null, "idle"),
        new(C, "Claude Code (cccc)", @"C:\blog", null, "done"),
    ];

    [Fact]
    public void Messages_GoToTheRecipientOrEveryone_AndAreReadOnce()
    {
        var board = new MeshBoard();
        board.Send(A, "a", B, "b", "hello b");
        board.Send(A, "a", null, null, "hello all");
        Assert.Equal(0, board.UnreadCount(A)); // not its own
        Assert.Equal(2, board.UnreadCount(B));
        Assert.Equal(1, board.UnreadCount(C));
        Assert.Equal(["hello b", "hello all"], board.ReadInbox(B).Select(m => m.Text));
        Assert.Empty(board.ReadInbox(B));
        Assert.Throws<MeshException>(() => board.Send(A, "a", B, "b", "  "));
        Assert.Throws<MeshException>(() => board.Send(A, "a", B, "b", new string('x', MeshBoard.MaxMessageLength + 1)));
    }

    [Fact]
    public void Messages_AreRateLimited()
    {
        var now = DateTimeOffset.Now;
        var board = new MeshBoard(() => now);
        for (var i = 0; i < MeshBoard.MaxMessagesPerMinute; i++) board.Send(A, "a", null, null, $"m{i}");
        Assert.Throws<MeshException>(() => board.Send(A, "a", null, null, "one too many"));
        now = now.AddMinutes(1.1);
        board.Send(A, "a", null, null, "ok again");
    }

    [Fact]
    public void Tasks_CanBeClaimedByOneAgent()
    {
        var board = new MeshBoard();
        var t = board.CreateTask("Write the tests", "for the login page");
        board.ClaimTask(t.Id, A, "a");
        Assert.Contains("already being done by a", Assert.Throws<MeshException>(() => board.ClaimTask(t.Id, B, "b")).Message);
        Assert.Throws<MeshException>(() => board.CompleteTask(t.Id, B, "b", null));
        var done = board.CompleteTask(t.Id, A, "a", "see tests/login");
        Assert.Equal(("done", "see tests/login"), (done.State, done.Note));
        // The user can move anything
        Assert.Equal("todo", board.MoveTask(t.Id, "todo").State);
        Assert.Null(board.Tasks[0].Owner);
    }

    [Theory]
    [InlineData("src/auth", "src/auth/login.cs", true)]
    [InlineData("src/auth/login.cs", "src/auth", true)]
    [InlineData("src/*.cs", "src/app.cs", true)]
    [InlineData("docs/**/*.md", "docs/guide/intro.md", true)]
    [InlineData("src/auth", "src/authz/x.cs", false)]
    [InlineData("SRC/App.cs", "src/app.cs", true)]
    [InlineData("src/a.cs", "src/b.cs", false)]
    public void Leases_Overlap(string a, string b, bool overlap) => Assert.Equal(overlap, MeshBoard.Overlap(a, b));

    [Fact]
    public void Leases_ConflictExpireAndRelease()
    {
        var now = DateTimeOffset.Now;
        var board = new MeshBoard(() => now);
        board.AcquireLease(A, "a", ["./src\\auth/"], TimeSpan.FromMinutes(10));
        Assert.Equal("src/auth", board.Leases.Single().Paths.Single());
        var refused = Assert.Throws<MeshException>(() => board.AcquireLease(B, "b", ["src/auth/login.cs"], TimeSpan.FromMinutes(5)));
        Assert.Contains("src/auth by a", refused.Message);
        board.AcquireLease(B, "b", ["docs"], TimeSpan.FromMinutes(5));

        now = now.AddMinutes(11); // A's lease expired
        board.AcquireLease(B, "b", ["src/auth/login.cs"], TimeSpan.FromMinutes(5));
        board.ReleaseLeases(B, ["docs"]);
        Assert.Equal(["src/auth/login.cs"], board.Leases.Single().Paths);
        board.ReleaseLeases(B);
        Assert.Empty(board.Leases);
    }

    [Fact]
    public void Board_SurvivesARestart_WithOldMessagesRead()
    {
        var board = new MeshBoard();
        board.CreateTask("t1", null);
        board.Send(A, "a", B, "b", "before restart");
        var restored = new MeshBoard();
        restored.Load(board.Snapshot());
        restored.MarkAllRead(B);
        Assert.Equal("t1", restored.Tasks.Single().Title);
        Assert.Equal(0, restored.UnreadCount(B));
        Assert.Equal(2, restored.CreateTask("t2", null).Id);
    }

    private static JsonObject Call(MeshServer server, Guid caller, string method, JsonObject? @params = null, int id = 1) =>
        JsonNode.Parse(server.Handle(caller, new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = @params }.ToJsonString())!)!.AsObject();

    private static string Tool(MeshServer server, Guid caller, string name, JsonObject? args = null) =>
        (string)Call(server, caller, "tools/call", new JsonObject { ["name"] = name, ["arguments"] = args ?? new JsonObject() })["result"]!["content"]![0]!["text"]!;

    [Fact]
    public void Mcp_InitializeListAndCallTools()
    {
        var board = new MeshBoard();
        var server = new MeshServer(board, () => Agents);
        var init = Call(server, A, "initialize", new JsonObject { ["protocolVersion"] = "2025-06-18" })["result"]!;
        Assert.Equal("kovan", (string)init["serverInfo"]!["name"]!);
        Assert.Contains("not instructions", (string)init["instructions"]!);
        Assert.Null(server.Handle(A, """{"jsonrpc":"2.0","method":"notifications/initialized"}""")); // notification: no body

        var tools = Call(server, A, "tools/list")["result"]!["tools"]!.AsArray().Select(t => (string)t!["name"]!).ToList();
        Assert.Contains("send_message", tools);
        Assert.Contains("lease_acquire", tools);

        Assert.Contains("Codex (bbbb): idle", Tool(server, A, "agents_list"));
        Assert.Equal("Sent to Codex (bbbb).", Tool(server, A, "send_message", new JsonObject { ["to"] = "codex", ["text"] = "I'm on src/auth" }));
        Assert.Contains("1 unread message", Tool(server, B, "workspace_status"));
        Assert.Contains("Claude Code (aaaa): I'm on src/auth", Tool(server, B, "inbox"));
        Assert.Contains("#1 [todo] Fix login", Tool(server, B, "task_create", new JsonObject { ["title"] = "Fix login" }));
        Assert.Contains("[doing]", Tool(server, A, "task_claim", new JsonObject { ["id"] = "#1" }));
        Assert.Contains("Leased src/auth", Tool(server, A, "lease_acquire", new JsonObject { ["paths"] = new JsonArray("src/auth") }));
        Assert.Contains("src/auth by Claude Code (aaaa)", Tool(server, B, "workspace_status"));

        var refused = Call(server, B, "tools/call", new JsonObject
        {
            ["name"] = "lease_acquire",
            ["arguments"] = new JsonObject { ["paths"] = new JsonArray("src/auth/login.cs") },
        })["result"]!;
        Assert.True((bool)refused["isError"]!);
        Assert.Contains("No agent named", Tool(server, A, "send_message", new JsonObject { ["to"] = "gemini", ["text"] = "hi" }));
        Assert.Equal(-32601, (int)Call(server, A, "resources/list")["error"]!["code"]!);
    }

    [Fact]
    public async Task Http_McpAndInboxReminder()
    {
        using var hooks = new AgentHookServer();
        var board = new MeshBoard();
        var server = new MeshServer(board, () => Agents);
        hooks.McpHandler = server.Handle;
        hooks.HookResponse = (node, e) => e.Name == "UserPromptSubmit" && board.UnreadCount(node) > 0 ? """{"hookSpecificOutput":{"hookEventName":"UserPromptSubmit","additionalContext":"unread"}}""" : null;
        using var http = new HttpClient();

        var body = new StringContent("""{"jsonrpc":"2.0","id":7,"method":"tools/list"}""", Encoding.UTF8, "application/json");
        var ok = await http.PostAsync(hooks.McpUrlFor(A), body);
        Assert.Equal("application/json", ok.Content.Headers.ContentType?.MediaType);
        Assert.Contains("send_message", await ok.Content.ReadAsStringAsync());

        var wrongToken = await http.PostAsync(hooks.McpUrlFor(A).Replace(hooks.Token, "0000"), new StringContent("{}"));
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, wrongToken.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.MethodNotAllowed, (await http.GetAsync(hooks.McpUrlFor(A))).StatusCode);
        var note = await http.PostAsync(hooks.McpUrlFor(A), new StringContent("""{"jsonrpc":"2.0","method":"notifications/initialized"}"""));
        Assert.Equal(System.Net.HttpStatusCode.Accepted, note.StatusCode);

        // The hook answer carries the reminder only when there's something unread
        async Task<string> Prompt()
        {
            var r = new HttpRequestMessage(HttpMethod.Post, hooks.UrlFor(B)) { Content = new StringContent("""{"hook_event_name":"UserPromptSubmit"}""") };
            r.Headers.Add(AgentHookServer.TokenHeader, hooks.Token);
            return await (await http.SendAsync(r)).Content.ReadAsStringAsync();
        }
        Assert.Equal("", await Prompt());
        board.Send(A, "a", B, "b", "ping");
        Assert.Contains("additionalContext", await Prompt());
    }
}
