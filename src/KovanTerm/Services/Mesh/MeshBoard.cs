using System.IO.Enumeration;

namespace KovanTerm.Services.Mesh;

/// <summary>A message between agents. <see cref="To"/> = <c>null</c>: to everyone.</summary>
public sealed record MeshMessage(long Id, DateTimeOffset At, Guid From, string FromName, Guid? To, string? ToName, string Text);

/// <summary>A task on the shared board: <c>todo</c> → <c>doing</c> (claimed by an agent) → <c>done</c>.</summary>
public sealed record MeshTask(int Id, string Title, string? Detail, string State, Guid? Owner, string? OwnerName, string? Note,
    DateTimeOffset Updated);

/// <summary>An advisory lock: "I'm editing these paths" (globs allowed), until <see cref="Expires"/>.</summary>
public sealed record MeshLease(Guid Owner, string OwnerName, IReadOnlyList<string> Paths, DateTimeOffset Expires);

/// <summary>What's saved with the workspace: tasks and recent messages (leases are short-lived and aren't).</summary>
public sealed record MeshBoardState
{
    public List<MeshTask> Tasks { get; init; } = [];
    public List<MeshMessage> Messages { get; init; } = [];
}

/// <summary>Why an operation was refused, in words an agent can act on.</summary>
public sealed class MeshException(string message) : Exception(message);

/// <summary>
/// Shared state of the agents on the canvas: messages, a task board and file leases. Thread-safe: agents call in
/// through the MCP server on socket threads, the UI reads it and changes tasks too. <see cref="Changed"/> is raised
/// (on the calling thread) after every change.
/// </summary>
public sealed class MeshBoard
{
    public const int MaxMessageLength = 4000;
    public const int MaxMessagesPerMinute = 20;
    public const int KeptMessages = 300;
    public static readonly TimeSpan MaxLease = TimeSpan.FromHours(2);

    private readonly object _lock = new();
    private readonly List<MeshMessage> _messages = [];
    private readonly List<MeshTask> _tasks = [];
    private readonly List<MeshLease> _leases = [];
    private readonly Dictionary<Guid, long> _lastRead = new();
    private readonly Dictionary<Guid, Queue<DateTimeOffset>> _sent = new();
    private long _nextMessage = 1;
    private int _nextTask = 1;
    private readonly Func<DateTimeOffset> _now;

    public MeshBoard(Func<DateTimeOffset>? now = null) => _now = now ?? (() => DateTimeOffset.Now);

    public event Action? Changed;

    // ---- Messages ----

    public MeshMessage Send(Guid from, string fromName, Guid? to, string? toName, string text)
    {
        text = text.Trim();
        if (text.Length == 0) throw new MeshException("The message is empty.");
        if (text.Length > MaxMessageLength) throw new MeshException($"Messages are limited to {MaxMessageLength} characters; send a summary or a file path.");
        MeshMessage message;
        lock (_lock)
        {
            var now = _now();
            var sent = _sent.TryGetValue(from, out var q) ? q : _sent[from] = new Queue<DateTimeOffset>();
            while (sent.Count > 0 && now - sent.Peek() > TimeSpan.FromMinutes(1)) sent.Dequeue();
            if (sent.Count >= MaxMessagesPerMinute) throw new MeshException("Too many messages; wait a minute.");
            sent.Enqueue(now);
            message = new MeshMessage(_nextMessage++, now, from, fromName, to, toName, text);
            _messages.Add(message);
            if (_messages.Count > KeptMessages) _messages.RemoveRange(0, _messages.Count - KeptMessages);
        }
        Changed?.Invoke();
        return message;
    }

    /// <summary>Messages for the agent (to it or to everyone, not its own) it hasn't read; marks them read.</summary>
    public IReadOnlyList<MeshMessage> ReadInbox(Guid agent)
    {
        List<MeshMessage> unread;
        lock (_lock)
        {
            unread = Unread(agent).ToList();
            if (unread.Count > 0) _lastRead[agent] = unread[^1].Id;
        }
        return unread;
    }

    public int UnreadCount(Guid agent)
    {
        lock (_lock) return Unread(agent).Count();
    }

    private IEnumerable<MeshMessage> Unread(Guid agent)
    {
        var last = _lastRead.GetValueOrDefault(agent);
        return _messages.Where(m => m.Id > last && m.From != agent && (m.To == null || m.To == agent));
    }

    public IReadOnlyList<MeshMessage> Messages
    {
        get { lock (_lock) return _messages.ToList(); }
    }

    // ---- Tasks ----

    public IReadOnlyList<MeshTask> Tasks
    {
        get { lock (_lock) return _tasks.ToList(); }
    }

    public MeshTask CreateTask(string title, string? detail)
    {
        title = title.Trim();
        if (title.Length == 0) throw new MeshException("A task needs a title.");
        MeshTask task;
        lock (_lock)
        {
            task = new MeshTask(_nextTask++, Cut(title, 200), detail is { Length: > 0 } d ? Cut(d.Trim(), 2000) : null, "todo", null, null, null, _now());
            _tasks.Add(task);
        }
        Changed?.Invoke();
        return task;
    }

    /// <summary>Takes a task to work on. Refused if another agent already has it.</summary>
    public MeshTask ClaimTask(int id, Guid agent, string agentName) => Update(id, t =>
    {
        if (t.State == "done") throw new MeshException($"Task {id} is already done.");
        if (t.Owner is { } owner && owner != agent) throw new MeshException($"Task {id} is already being done by {t.OwnerName}.");
        return t with { State = "doing", Owner = agent, OwnerName = agentName };
    });

    public MeshTask CompleteTask(int id, Guid agent, string agentName, string? note) => Update(id, t =>
    {
        if (t.Owner is { } owner && owner != agent) throw new MeshException($"Task {id} belongs to {t.OwnerName}; only they can complete it.");
        return t with { State = "done", Owner = agent, OwnerName = agentName, Note = note is { Length: > 0 } n ? Cut(n.Trim(), 2000) : t.Note };
    });

    /// <summary>The user moving a task (Board panel); no ownership checks.</summary>
    public MeshTask MoveTask(int id, string state) => Update(id, t => t with
    {
        State = state,
        Owner = state == "todo" ? null : t.Owner,
        OwnerName = state == "todo" ? null : t.OwnerName,
    });

    public void DeleteTask(int id)
    {
        lock (_lock) _tasks.RemoveAll(t => t.Id == id);
        Changed?.Invoke();
    }

    private MeshTask Update(int id, Func<MeshTask, MeshTask> change)
    {
        MeshTask updated;
        lock (_lock)
        {
            var i = _tasks.FindIndex(t => t.Id == id);
            if (i < 0) throw new MeshException($"There's no task {id}.");
            updated = change(_tasks[i]) with { Updated = _now() };
            _tasks[i] = updated;
        }
        Changed?.Invoke();
        return updated;
    }

    // ---- Leases ----

    /// <summary>
    /// Leases the paths for the agent (replacing its earlier lease on the same paths). Refused, with the conflicts,
    /// when another agent holds an overlapping lease.
    /// </summary>
    public MeshLease AcquireLease(Guid agent, string agentName, IReadOnlyList<string> paths, TimeSpan ttl)
    {
        var clean = paths.Select(NormalizePath).Where(p => p.Length > 0).Distinct().ToList();
        if (clean.Count == 0) throw new MeshException("Give at least one path.");
        if (ttl <= TimeSpan.Zero || ttl > MaxLease) ttl = ttl <= TimeSpan.Zero ? TimeSpan.FromMinutes(30) : MaxLease;
        MeshLease lease;
        lock (_lock)
        {
            ExpireLeases();
            var conflicts = _leases.Where(l => l.Owner != agent && l.Paths.Any(a => clean.Any(b => Overlap(a, b)))).ToList();
            if (conflicts.Count > 0)
                throw new MeshException("Already leased: " + string.Join("; ", conflicts.Select(c =>
                    $"{string.Join(", ", c.Paths)} by {c.OwnerName} until {c.Expires:HH:mm}")) + ". Coordinate with them first.");
            foreach (var mine in _leases.Where(l => l.Owner == agent).ToList())
            {
                var rest = mine.Paths.Where(p => !clean.Contains(p)).ToList();
                _leases.Remove(mine);
                if (rest.Count > 0) _leases.Add(mine with { Paths = rest });
            }
            lease = new MeshLease(agent, agentName, clean, _now() + ttl);
            _leases.Add(lease);
        }
        Changed?.Invoke();
        return lease;
    }

    /// <summary>Releases the agent's leases on the paths, or all of them when none are given.</summary>
    public void ReleaseLeases(Guid agent, IReadOnlyList<string>? paths = null)
    {
        var clean = paths?.Select(NormalizePath).ToHashSet();
        lock (_lock)
        {
            foreach (var mine in _leases.Where(l => l.Owner == agent).ToList())
            {
                _leases.Remove(mine);
                if (clean != null && mine.Paths.Where(p => !clean.Contains(p)).ToList() is { Count: > 0 } rest)
                    _leases.Add(mine with { Paths = rest });
            }
        }
        Changed?.Invoke();
    }

    public IReadOnlyList<MeshLease> Leases
    {
        get
        {
            lock (_lock)
            {
                ExpireLeases();
                return _leases.ToList();
            }
        }
    }

    private void ExpireLeases() => _leases.RemoveAll(l => l.Expires <= _now());

    /// <summary>Repo-relative style: forward slashes, no leading <c>./</c>, case kept (globs compare ignoring case).</summary>
    public static string NormalizePath(string path)
    {
        var p = path.Trim().Replace('\\', '/');
        while (p.StartsWith("./", StringComparison.Ordinal)) p = p[2..];
        return p.TrimEnd('/');
    }

    /// <summary>Do two leased paths cover any common file? A folder covers everything under it; globs match either way.</summary>
    public static bool Overlap(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        if (IsUnder(a, b) || IsUnder(b, a)) return true;
        return Glob(a, b) || Glob(b, a);

        static bool IsUnder(string path, string folder) => !HasWildcard(folder) &&
            path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase);
        static bool Glob(string pattern, string path) => HasWildcard(pattern) &&
            FileSystemName.MatchesSimpleExpression(pattern.Replace("**/", "*").Replace("**", "*"), path, ignoreCase: true);
        static bool HasWildcard(string s) => s.Contains('*') || s.Contains('?');
    }

    // ---- Persistence ----

    public MeshBoardState Snapshot()
    {
        lock (_lock) return new MeshBoardState { Tasks = _tasks.ToList(), Messages = _messages.ToList() };
    }

    public void Load(MeshBoardState? state)
    {
        if (state == null) return;
        lock (_lock)
        {
            _tasks.Clear();
            _tasks.AddRange(state.Tasks);
            _messages.Clear();
            _messages.AddRange(state.Messages.TakeLast(KeptMessages));
            _nextTask = _tasks.Count == 0 ? 1 : _tasks.Max(t => t.Id) + 1;
            _nextMessage = _messages.Count == 0 ? 1 : _messages.Max(m => m.Id) + 1;
        }
    }

    /// <summary>After a restart, every agent has seen everything before it (broadcasts included).</summary>
    public void MarkAllRead(Guid agent)
    {
        lock (_lock) _lastRead[agent] = _nextMessage - 1;
    }

    private static string Cut(string s, int max) => s.Length > max ? s[..max] + "…" : s;
}
