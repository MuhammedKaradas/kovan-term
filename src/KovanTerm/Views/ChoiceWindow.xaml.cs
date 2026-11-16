using System.Windows;
using Wpf.Ui.Controls;

namespace KovanTerm.Views;

/// <summary>
/// A small modal question with any number of answers. Escape or closing the window gives <c>null</c>.
/// (Shown with <c>ShowDialog</c>: WPF-UI's async MessageBox didn't appear when opened from a command.)
/// </summary>
public partial class ChoiceWindow : FluentWindow
{
    private int? _choice;

    private ChoiceWindow(string title, string message, IReadOnlyList<(string Text, ControlAppearance Appearance)> buttons)
    {
        InitializeComponent();
        Title = title;
        TitleBarControl.Title = title;
        MessageText.Text = message;
        for (var i = 0; i < buttons.Count; i++)
        {
            var index = i;
            var button = new Wpf.Ui.Controls.Button
            {
                Content = buttons[i].Text,
                Appearance = buttons[i].Appearance,
                Margin = new Thickness(8, 0, 0, 0),
                MinWidth = 90,
                IsCancel = buttons[i].Appearance == ControlAppearance.Secondary && i == buttons.Count - 1,
            };
            button.Click += (_, _) =>
            {
                _choice = index;
                DialogResult = true;
            };
            Buttons.Children.Add(button);
        }
    }

    /// <summary>Index of the button pressed; <c>null</c> if the window was closed or Escape pressed.</summary>
    public static int? Ask(Window owner, string title, string message, params (string Text, ControlAppearance Appearance)[] buttons)
    {
        var window = new ChoiceWindow(title, message, buttons) { Owner = owner };
        window.ShowDialog();
        return window._choice;
    }
}
