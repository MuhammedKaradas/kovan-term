using System.IO;

namespace KovanTerm.Services;

/// <summary>Folder where app data (workspace, profiles) is stored.</summary>
public static class AppPaths
{
    /// <summary>
    /// Defaults to <c>%APPDATA%\KovanTerm</c>. Can be overridden with the <c>KOVAN_DATA_DIR</c> environment variable,
    /// so tests or a second copy on the same machine don't touch the user's data.
    /// </summary>
    public static string DataDirectory { get; } =
        Environment.GetEnvironmentVariable("KOVAN_DATA_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KovanTerm");
}
