namespace KovanTerm.Models;

/// <summary>Yeni bir terminal düğümü başlatmak için gereken bilgiler. #3'te profillere dönüşecek.</summary>
public sealed record TerminalLaunch(string Title, string CommandLine);
