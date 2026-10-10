using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EdgeStage.UI;

/// <summary>
/// Small non-modal message window. Used instead of MessageBox so a warning never blocks the
/// message loop (hotkeys, the sidebar and exit keep working while it is on screen).
/// </summary>
public sealed class NoticeWindow : Window
{
    public NoticeWindow(string heading, string message, string? actionText = null, Action? action = null)
    {
        Title = "EdgeStage";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        Background = (Brush)Application.Current.Resources["CardBackgroundBrush"];
        Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"];
        FontFamily = (FontFamily)Application.Current.Resources["UiFont"];

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        if (actionText is not null && action is not null)
            buttons.Children.Add(MakeButton(actionText, () => { Close(); action(); }, isDefault: true));
        buttons.Children.Add(MakeButton("OK", Close, isDefault: actionText is null));

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Children =
            {
                new TextBlock { Text = heading, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = message, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap,
                                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] },
                buttons,
            },
        };
    }

    private static Button MakeButton(string text, Action onClick, bool isDefault)
    {
        var button = new Button { Content = text, MinWidth = 88, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0), IsDefault = isDefault };
        button.Click += (_, _) => onClick();
        return button;
    }
}
