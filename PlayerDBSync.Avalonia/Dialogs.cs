using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PlayerDBSync
{
    /// <summary>Avalonia has no message box, so here is one: a heading, some text and a row of buttons.</summary>
    internal static class Dialogs
    {
        /// <summary>
        ///  Shows the dialog over <paramref name="owner"/> and returns the label of the button clicked, or null if it was
        ///  closed another way. The last button is the default one (Enter, and where focus starts).
        /// </summary>
        public static async Task<string?> AskAsync(Window owner, string title, string? heading, string text, params string[] buttons)
        {
            string? answer = null;
            var dialog = new Window
            {
                Title = title,
                SizeToContent = SizeToContent.WidthAndHeight,
                MaxWidth = 520,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
            };

            var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
            if (heading != null)
            {
                panel.Children.Add(new TextBlock { Text = heading, FontWeight = FontWeight.SemiBold, FontSize = 15, TextWrapping = TextWrapping.Wrap });
            }
            panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            foreach (var label in buttons)
            {
                var button = new Button { Content = label, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
                if (label == buttons[^1])
                {
                    button.Classes.Add("primary");
                    button.IsDefault = true;
                    dialog.Opened += (_, _) => button.Focus();
                }
                button.Click += (_, _) =>
                {
                    answer = label;
                    dialog.Close();
                };
                row.Children.Add(button);
            }
            panel.Children.Add(row);
            dialog.Content = panel;

            await dialog.ShowDialog(owner);
            return answer;
        }

        public static Task ShowAsync(Window owner, string title, string? heading, string text) =>
            AskAsync(owner, title, heading, text, "OK");
    }
}
