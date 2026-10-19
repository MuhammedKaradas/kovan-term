using System.Diagnostics;
using System.IO;
using System.Text.Json;
using KovanTerm.Models;

namespace KovanTerm.Services;

/// <summary>Çalışma alanını JSON olarak okur/yazar. Varsayılan konum: <c>%APPDATA%\KovanTerm\workspace.json</c>.</summary>
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
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KovanTerm", "workspace.json");
    }

    /// <summary>
    /// Kayıtlı çalışma alanını döndürür; dosya yoksa <c>null</c>.
    /// Dosya bozuksa ya da daha yeni bir sürümle yazılmışsa yanına yedeklenir ve <c>null</c> döner.
    /// </summary>
    public WorkspaceState? Load()
    {
        if (!File.Exists(FilePath)) return null;
        try
        {
            var state = JsonSerializer.Deserialize<WorkspaceState>(File.ReadAllText(FilePath), JsonOptions);
            if (state == null || state.Version > WorkspaceState.CurrentVersion)
                throw new JsonException($"Desteklenmeyen çalışma alanı sürümü: {state?.Version}");
            return state;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            Debug.WriteLine($"Çalışma alanı okunamadı, yedekleniyor: {ex.Message}");
            BackupUnreadableFile();
            return null;
        }
    }

    public void Save(WorkspaceState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        // Yarım yazılmış dosya kalmasın diye önce geçici dosyaya yaz, sonra yer değiştir
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, JsonOptions));
        File.Move(temp, FilePath, overwrite: true);
    }

    private void BackupUnreadableFile()
    {
        var backup = Path.ChangeExtension(FilePath, $".bozuk-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        File.Move(FilePath, backup, overwrite: true);
    }
}
