using System.IO;
using KovanTerm.Models;
using KovanTerm.Services;
using KovanTerm.ViewModels;

namespace KovanTerm.Tests;

public sealed class WorkspaceStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kovan-test-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "workspace.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Load_DosyaYoksa_NullDoner()
    {
        Assert.Null(new WorkspaceStore(FilePath).Load());
    }

    [Fact]
    public void SaveLoad_DugumleriVeGorunumuKorur()
    {
        var store = new WorkspaceStore(FilePath);
        var state = new WorkspaceState
        {
            Viewport = new ViewportState { Zoom = 0.75, X = -120, Y = 40 },
            Nodes =
            [
                new NodeState { Title = "Claude Code", CommandLine = "cmd.exe /k claude", WorkingDirectory = @"C:\proje", X = 10, Y = 20, Width = 600, Height = 400 },
                new NodeState { Title = "PowerShell", CommandLine = "powershell.exe -NoLogo", X = 640, Y = 20, Width = 560, Height = 340 },
            ],
        };

        store.Save(state);
        var loaded = store.Load();

        Assert.NotNull(loaded);
        Assert.Equal(state.Viewport, loaded.Viewport);
        Assert.Equal(state.Nodes, loaded.Nodes);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void Load_BozukDosyada_NullDonerVeYedekler()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ bu json değil");

        var loaded = new WorkspaceStore(FilePath).Load();

        Assert.Null(loaded);
        Assert.False(File.Exists(FilePath));
        Assert.Single(Directory.GetFiles(_dir, "workspace.bozuk-*.json"));
    }

    [Fact]
    public void Load_DahaYeniSurumde_NullDonerVeYedekler()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, $$"""{ "version": {{WorkspaceState.CurrentVersion + 1}}, "nodes": [] }""");

        Assert.Null(new WorkspaceStore(FilePath).Load());
        Assert.Single(Directory.GetFiles(_dir, "workspace.bozuk-*.json"));
    }

    [Fact]
    public void FromState_EksikBoyuttaVarsayilaniKullanir()
    {
        var node = TerminalNodeViewModel.FromState(new NodeState { Title = "cmd", CommandLine = "cmd.exe" });

        Assert.Equal(TerminalNodeViewModel.DefaultWidth, node.Width);
        Assert.Equal(TerminalNodeViewModel.DefaultHeight, node.Height);
    }
}
