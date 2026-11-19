using System.Windows;
using System.Windows.Controls;
using KovanTerm.Terminals.WindowsTerminal;

namespace KovanTerm.Terminals;

/// <summary>
/// Puts a terminal in the node template: creates the <see cref="ITerminalSurface"/>, starts it with
/// <see cref="CommandLine"/> in <see cref="WorkingDirectory"/> when first loaded, and raises <see cref="Ready"/> once
/// the terminal is in the visual tree.
/// </summary>
public sealed class TerminalHost : Decorator
{
    /// <summary>Which terminal implementation to use.</summary>
    private static ITerminalSurface CreateSurface() => new WindowsTerminalSurface();

    public static readonly DependencyProperty CommandLineProperty =
        DependencyProperty.Register(nameof(CommandLine), typeof(string), typeof(TerminalHost));

    public static readonly DependencyProperty WorkingDirectoryProperty =
        DependencyProperty.Register(nameof(WorkingDirectory), typeof(string), typeof(TerminalHost));

    public static readonly RoutedEvent ReadyEvent =
        EventManager.RegisterRoutedEvent(nameof(Ready), RoutingStrategy.Direct, typeof(RoutedEventHandler), typeof(TerminalHost));

    public string? CommandLine
    {
        get => (string?)GetValue(CommandLineProperty);
        set => SetValue(CommandLineProperty, value);
    }

    public string? WorkingDirectory
    {
        get => (string?)GetValue(WorkingDirectoryProperty);
        set => SetValue(WorkingDirectoryProperty, value);
    }

    /// <summary>The terminal's view was loaded (again, after it left the tree); <see cref="Surface"/> is running.</summary>
    public event RoutedEventHandler Ready
    {
        add => AddHandler(ReadyEvent, value);
        remove => RemoveHandler(ReadyEvent, value);
    }

    /// <summary>The terminal; <c>null</c> until the host is first loaded.</summary>
    public ITerminalSurface? Surface { get; private set; }

    public TerminalHost()
    {
        Loaded += (_, _) =>
        {
            if (Surface != null) return;
            var surface = CreateSurface();
            surface.Start(CommandLine ?? "", WorkingDirectory);
            surface.View.Loaded += (_, _) => RaiseEvent(new RoutedEventArgs(ReadyEvent, this));
            Surface = surface;
            Child = surface.View;
        };
    }
}
