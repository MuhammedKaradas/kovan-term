using System.IO;
using KovanTerm.Services;
using Microsoft.Web.WebView2.Core;

namespace KovanTerm.Views;

/// <summary>
/// One WebView2 environment shared by every preview node: one browser process group and one profile
/// (<c>&lt;data dir&gt;\WebView2</c>), instead of a set per preview.
/// </summary>
public static class PreviewEnvironment
{
    private static Task<CoreWebView2Environment>? _environment;

    public static Task<CoreWebView2Environment> GetAsync() =>
        _environment ??= CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(AppPaths.DataDirectory, "WebView2"));
}
