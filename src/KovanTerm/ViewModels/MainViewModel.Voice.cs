using KovanTerm.Services.Voice;

namespace KovanTerm.ViewModels;

/// <summary>Voice commands (#21): what <see cref="VoiceCommands"/> understood, carried out.</summary>
public partial class MainViewModel
{
    /// <summary>A voice command the view carries out (fit all, zoom), with the node it names if any.</summary>
    public event Action<VoiceCommandKind, TerminalNodeViewModel?>? ViewCommandRequested;

    /// <summary>
    /// Carries out a voice command and says how it went, for the status line. Sending text to an agent, allowing a
    /// permission and closing a terminal ask first (the command may have been misheard).
    /// </summary>
    public async Task<string> RunVoiceCommandAsync(string heard)
    {
        var nodes = Nodes.Where(n => !n.IsGroup).Select(n => new VoiceTarget(n, n.Title)).ToList();
        var workspaces = Workspaces.Select(w => new VoiceTarget(w, w.Name)).ToList();
        var command = VoiceCommands.Parse(heard, nodes, workspaces);
        StatusLog($"voice command \"{heard}\" -> {command.Kind} {string.Join(",", command.Targets.OfType<TerminalNodeViewModel>().Select(n => n.Title))}");
        var quoted = $"\"{heard.Trim()}\"";

        if (command.Kind == VoiceCommandKind.Unknown)
            return $"Didn't understand {quoted}. Try {VoiceCommands.Examples}.";
        if (command.NameNotFound)
            return $"Nothing is called \"{command.NameHeard}\" ({quoted}).";

        switch (command.Kind)
        {
            case VoiceCommandKind.GoToWaiting:
                if (!Nodes.Any(n => n.Status == Models.NodeStatus.Waiting)) return "No agent is waiting for you.";
                GoToWaiting();
                return $"Went to {CurrentNode?.Title ?? "the waiting agent"}.";

            case VoiceCommandKind.GoTo:
                if (await PickNodeAsync(command, "Go to") is not { } goTo) return "";
                GoTo(goTo);
                CurrentNode = goTo;
                return $"Went to {goTo.Title}.";

            case VoiceCommandKind.Zoom:
                var zoomTo = command.Targets.Count > 0 ? await PickNodeAsync(command, "Zoom to") : CurrentNode;
                if (zoomTo == null) return command.Targets.Count > 0 ? "" : "Select a terminal to zoom to first.";
                if (zoomTo.WorkspaceId != ActiveWorkspace.Id) SwitchWorkspace(Workspaces.FirstOrDefault(w => w.Id == zoomTo.WorkspaceId));
                CurrentNode = zoomTo;
                ViewCommandRequested?.Invoke(VoiceCommandKind.Zoom, zoomTo);
                return $"Zoomed to {zoomTo.Title}.";

            case VoiceCommandKind.FitAll:
                ViewCommandRequested?.Invoke(VoiceCommandKind.FitAll, null);
                return "Showing all terminals.";

            case VoiceCommandKind.Tell:
                if (await PickNodeAsync(command, "Tell", terminalsOnly: true) is not { } agent) return "";
                if (string.IsNullOrWhiteSpace(command.Text)) return $"What should {agent.Title} be told? ({quoted})";
                if (!await _dialogs.ConfirmAsync("Voice command", $"Send this to {agent.Title}?\n\n{command.Text}", "Send"))
                    return "Not sent.";
                // One paste, then Enter: the confirmation was the review a note gets in the terminal
                TerminalInputRequested?.Invoke(agent, ChangesViewModel.BracketedPaste(command.Text) + "\r");
                GoTo(agent);
                return $"Sent to {agent.Title}.";

            case VoiceCommandKind.Allow:
            case VoiceCommandKind.Deny:
                var allow = command.Kind == VoiceCommandKind.Allow;
                if (await PickAskingAsync(command) is not { } asking)
                    return command.Targets.Count > 0 ? $"\"{command.NameHeard}\" isn't asking for permission." : "No agent is asking for permission.";
                if (allow && !await _dialogs.ConfirmAsync("Voice command",
                        $"Allow {asking.Title}?\n\n{asking.StatusText ?? "It's asking for permission."}", "Allow"))
                    return "Not allowed.";
                if (allow) AllowPermission(asking);
                else DenyPermission(asking);
                return allow ? $"Allowed {asking.Title}." : $"Denied {asking.Title}.";

            case VoiceCommandKind.Close:
                if (await PickNodeAsync(command, "Close") is not { } close) return "";
                if (!await _dialogs.ConfirmAsync("Voice command",
                        close.IsTerminal ? $"Close {close.Title}? Its process is stopped." : $"Close {close.Title}?", "Close"))
                    return "Not closed.";
                await CloseNodeAsync(close, askForNote: true);
                return Nodes.Contains(close) ? "Not closed." : $"Closed {close.Title}.";

            case VoiceCommandKind.NewTerminal:
                await NewTerminalCommand.ExecuteAsync(null);
                return "";

            case VoiceCommandKind.NewNote:
                NewNote(null);
                if (CurrentNode is { IsNote: true } note && !string.IsNullOrWhiteSpace(command.Text))
                {
                    note.NoteText = command.Text;
                    note.IsEditingNote = false;
                }
                return "Added a note.";

            case VoiceCommandKind.NextWorkspace:
                NextWorkspace();
                return $"Showing {ActiveWorkspace.Name}.";
            case VoiceCommandKind.PreviousWorkspace:
                PreviousWorkspace();
                return $"Showing {ActiveWorkspace.Name}.";
            case VoiceCommandKind.SwitchWorkspace:
                var workspace = command.Targets.OfType<WorkspaceViewModel>().First();
                SwitchWorkspace(workspace);
                return $"Showing {workspace.Name}.";

            case VoiceCommandKind.ToggleBoard:
                ToggleBoard();
                return "";
            case VoiceCommandKind.ToggleChanges:
                ToggleChangesPanel();
                return "";
            case VoiceCommandKind.ToggleAgents:
                ToggleAgentsPanel();
                return "";
        }
        return "";
    }

    /// <summary>The node the command names; asks which one if the name fits several.</summary>
    private async Task<TerminalNodeViewModel?> PickNodeAsync(VoiceCommand command, string verb, bool terminalsOnly = false)
    {
        var candidates = command.Targets.OfType<TerminalNodeViewModel>().Where(n => !terminalsOnly || n.IsTerminal).ToList();
        if (candidates.Count <= 1) return candidates.FirstOrDefault();
        // Several with the same name: this workspace's first
        candidates = candidates.OrderBy(n => n.IsInActiveWorkspace ? 0 : 1).Take(4).ToList();
        var labels = candidates.Select(n => n.IsInActiveWorkspace || n.OtherWorkspaceName == null ? n.Title : $"{n.Title} ({n.OtherWorkspaceName})");
        var choice = await _dialogs.ChooseAsync("Voice command", $"{verb} which one?", [.. labels, "Cancel"]);
        return choice is { } i && i < candidates.Count ? candidates[i] : null;
    }

    /// <summary>The agent a permission answer is for: the one named, or the one asking (asks which if several are).</summary>
    private async Task<TerminalNodeViewModel?> PickAskingAsync(VoiceCommand command)
    {
        if (command.Targets.Count > 0)
            return await PickNodeAsync(command, command.Kind == VoiceCommandKind.Allow ? "Allow" : "Deny") is { AwaitsPermission: true } named
                ? named
                : null;
        var asking = Nodes.Where(n => n.AwaitsPermission).ToList();
        // The current one if it's asking, so "allow" answers what's on screen
        if (CurrentNode is { AwaitsPermission: true } current) return current;
        return await PickNodeAsync(new VoiceCommand(command.Kind, asking), command.Kind == VoiceCommandKind.Allow ? "Allow" : "Deny");
    }
}
