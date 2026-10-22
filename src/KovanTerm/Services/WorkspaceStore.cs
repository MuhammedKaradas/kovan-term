using System.Diagnostics;
using System.IO;
using System.Text.Json;
using KovanTerm.Models;

namespace KovanTerm.Services;

/// <summary>Reads/writes the workspace as JSON. Default location: <c>%APPDATA%\KovanTerm\workspace.json</c>.</summary>
public sealed class WorkspaceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public string FilePath { get; }

    public WorkspaceStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(AppPaths.DataDirectory, "workspace.json");
    }

    /// <summary>
    /// Returns the saved workspace, or <c>null</c> if there's no file.
    /// If the file is corrupt or was written by a newer version, it's backed up next to it and <c>null</c> is returned.
    /// </summary>
    public WorkspaceState? Load()
    {
        if (!File.Exists(FilePath)) return null;
        try
        {
            var state = JsonSerializer.Deserialize<WorkspaceState>(File.ReadAllText(FilePath), JsonOptions);
            if (state == null || state.Version > WorkspaceState.CurrentVersion)
                throw new JsonException($"Unsupported workspace version: {state?.Version}");
            return state;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            Debug.WriteLine($"Couldn't read workspace, backing it up: {ex.Message}");
            BackupUnreadableFile();
            return null;
        }
    }

    public void Save(WorkspaceState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        // Write to a temp file first, then swap, so a half-written file is never left behind
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, JsonOptions));
        File.Move(temp, FilePath, overwrite: true);
    }

    private void BackupUnreadableFile()
    {
        var backup = Path.ChangeExtension(FilePath, $".corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        File.Move(FilePath, backup, overwrite: true);
    }
}
