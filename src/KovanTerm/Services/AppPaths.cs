using System.IO;

namespace KovanTerm.Services;

/// <summary>Uygulama verilerinin (çalışma alanı, profiller) tutulduğu klasör.</summary>
public static class AppPaths
{
    /// <summary>
    /// Varsayılan <c>%APPDATA%\KovanTerm</c>. <c>KOVAN_DATA_DIR</c> ortam değişkeniyle değiştirilebilir;
    /// testlerde ya da aynı makinede ikinci bir kopya çalıştırırken kullanıcının verisine dokunmamak için.
    /// </summary>
    public static string DataDirectory { get; } =
        Environment.GetEnvironmentVariable("KOVAN_DATA_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KovanTerm");
}
