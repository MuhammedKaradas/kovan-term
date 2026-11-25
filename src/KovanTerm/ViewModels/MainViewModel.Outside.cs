using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using KovanTerm.Models;
using KovanTerm.Services;

namespace KovanTerm.ViewModels;

/// <summary>Claude Code sessions started outside Kovan, shown under "Elsewhere" in the Agents panel (#138).</summary>
public partial class MainViewModel
{
    // A session closed without SessionEnd (its window was closed, the computer slept) stops reporting
    private static readonly TimeSpan OutsideQuietLimit = TimeSpan.FromHours(6);

    private AgentHookServer? _outsideServer;
    private OutsideHook? _outsideHook;

    public ObservableCollection<OutsideSessionViewModel> OutsideSessions { get; } = [];

    public bool HasOutsideSessions => OutsideSessions.Count > 0;

    /// <summary>Outside sessions working / waiting (their dots breathe like nodes').</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    public partial int OutsideWorkingCount { get; private set; }

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    public partial int OutsideWaitingCount { get; private set; }

    private void UpdateOutsideCounts()
    {
        OutsideWorkingCount = OutsideSessions.Count(s => s.Status == NodeStatus.Working);
        OutsideWaitingCount = OutsideSessions.Count(s => s.Status == NodeStatus.Waiting);
    }

    /// <summary>An outside session started waiting for the user (the main window notifies).</summary>
    public event Action<OutsideSessionViewModel>? OutsideAttentionNeeded;

    private void InitOutsideSessions(AgentHookServer? server, OutsideHook? hook)
    {
        if (server == null) return;
        _outsideServer = server;
        _outsideHook = hook ?? new OutsideHook();
        OutsideSessions.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasOutsideSessions));
            UpdateOutsideCounts();
        };
        server.OutsideEventReceived += e => _dispatcher.BeginInvoke(() => OnOutsideEvent(e));
        if (!_settings.Load().ShowOutsideSessions) return;
        try
        {
            _outsideHook.WriteEndpoint(server.OutsideUrl, server.Token);
            // Brings the hook's path up to date if Kovan was moved since it was added
            _outsideHook.Install();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Debug.WriteLine($"Couldn't set up the hook for sessions outside Kovan: {ex.Message}");
        }
    }

    /// <summary>Kovan is closing: the hook finds no endpoint and returns at once.</summary>
    public void StopOutsideSessions() => _outsideHook?.DeleteEndpoint();

    /// <summary>Settings: adds or removes the hook and saves the choice; why it failed, or <c>null</c>.</summary>
    public string? SetOutsideSessions(bool on)
    {
        if (_outsideHook == null || _outsideServer == null) return "Kovan's hook server isn't running.";
        try
        {
            if (on)
            {
                _outsideHook.WriteEndpoint(_outsideServer.OutsideUrl, _outsideServer.Token);
                _outsideHook.Install();
            }
            else
            {
                _outsideHook.Uninstall();
                OutsideSessions.Clear();
            }
        }
        catch (JsonException)
        {
            return $"{_outsideHook.SettingsPath} isn't valid JSON, so Kovan left it as it is.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Couldn't change {_outsideHook.SettingsPath}: {ex.Message}";
        }
        _settings.Save(_settings.Load() with { ShowOutsideSessions = on });
        if (on) UsageStats.Count("feature.outside");
        return null;
    }

    /// <summary>An event from the outside hook (public for tests; the server raises it on a thread-pool thread).</summary>
    public void OnOutsideEvent(AgentEvent e)
    {
        if (e.SessionId is not { Length: > 0 } id || !_settings.Load().ShowOutsideSessions) return;
        // Sessions Kovan started report here as well (the user's settings apply to them too)
        if (Nodes.Any(n => string.Equals(n.SessionId, id, StringComparison.OrdinalIgnoreCase))) return;
        var now = DateTime.Now;
        foreach (var quiet in OutsideSessions.Where(s => now - s.LastSeen > OutsideQuietLimit).ToList()) OutsideSessions.Remove(quiet);

        var session = OutsideSessions.FirstOrDefault(s => string.Equals(s.SessionId, id, StringComparison.OrdinalIgnoreCase));
        if (e.Name == "SessionEnd")
        {
            if (session != null) OutsideSessions.Remove(session);
            return;
        }
        if (session == null)
        {
            session = new OutsideSessionViewModel(id);
            OutsideSessions.Add(session);
        }
        StatusLog($"outside {session.Title} hook {e.Name} {e.ToolName}");
        if (session.Apply(e, now)) OutsideAttentionNeeded?.Invoke(session);
        UpdateOutsideCounts();
    }

    /// <summary>A node took the conversation over (it was opened here): it's no longer "elsewhere".</summary>
    private void ForgetOutsideSession(string? sessionId)
    {
        if (sessionId == null) return;
        if (OutsideSessions.FirstOrDefault(s => string.Equals(s.SessionId, sessionId, StringComparison.OrdinalIgnoreCase)) is { } session)
            OutsideSessions.Remove(session);
    }

    /// <summary>Opens the conversation on the canvas, where it left off (the outside session isn't stopped).</summary>
    [RelayCommand]
    private async Task OpenOutsideSession(OutsideSessionViewModel? session)
    {
        if (session == null) return;
        if (Nodes.FirstOrDefault(n => string.Equals(n.SessionId, session.SessionId, StringComparison.OrdinalIgnoreCase)) is { } existing)
        {
            GoTo(existing);
            return;
        }
        if (FindClaude() is not var (profile, path))
        {
            await _dialogs.ShowMessageAsync("Open in Kovan", "Claude Code ('claude') wasn't found. Make sure it's installed and on PATH.");
            return;
        }
        if (session.Folder is not { } folder || !Directory.Exists(folder))
        {
            await _dialogs.ShowMessageAsync("Open in Kovan", $"The session's folder wasn't found:\n{session.Folder}");
            return;
        }
        RememberDirectory(folder);
        var node = CreateNode(profile, path, folder, ViewportCenterForNewNode(), existingSessionId: session.SessionId);
        AddNode(node);
        OutsideSessions.Remove(session);
        UsageStats.Count("feature.outside.open");
        CurrentNode = node;
    }

    [RelayCommand]
    private void DismissOutsideSession(OutsideSessionViewModel? session)
    {
        if (session != null) OutsideSessions.Remove(session);
    }
}
