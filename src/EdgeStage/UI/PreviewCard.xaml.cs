using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace EdgeStage.UI;

public partial class PreviewCard : UserControl
{
    /// <summary>Bubbles up from the card's × button or its context menu (DataContext is the card).</summary>
    public static readonly RoutedEvent CloseRequestedEvent = EventManager.RegisterRoutedEvent(
        nameof(CloseRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(PreviewCard));

    /// <summary>"Hide this app from EdgeStage" chosen in the context menu.</summary>
    public static readonly RoutedEvent HideAppRequestedEvent = EventManager.RegisterRoutedEvent(
        nameof(HideAppRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(PreviewCard));

    public event RoutedEventHandler CloseRequested { add => AddHandler(CloseRequestedEvent, value); remove => RemoveHandler(CloseRequestedEvent, value); }
    public event RoutedEventHandler HideAppRequested { add => AddHandler(HideAppRequestedEvent, value); remove => RemoveHandler(HideAppRequestedEvent, value); }

    public PreviewCard()
    {
        InitializeComponent();
        ContextMenuOpening += (_, _) => BuildContextMenu();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(CloseRequestedEvent, this));
        e.Handled = true;
    }

    private void BuildContextMenu()
    {
        var menu = new ContextMenu { Style = Application.Current.TryFindResource("TrayMenu") as Style };
        var close = new MenuItem { Header = "Close window" };
        close.Click += (_, _) => RaiseEvent(new RoutedEventArgs(CloseRequestedEvent, this));
        menu.Items.Add(close);

        string app = Card?.ProcessName ?? "";
        var hide = new MenuItem
        {
            Header = app.Length > 0 ? $"Never show {app} in EdgeStage" : "Never show this app in EdgeStage",
            IsEnabled = app.Length > 0,
        };
        hide.Click += (_, _) => RaiseEvent(new RoutedEventArgs(HideAppRequestedEvent, this));
        menu.Items.Add(hide);
        ContextMenu = menu;
    }

    /// <summary>The element whose bounds receive the live DWM thumbnail.</summary>
    public FrameworkElement ThumbnailHost => ThumbHost;

    public CardViewModel? Card => DataContext as CardViewModel;
}

/// <summary>Null → Collapsed (or Visible when <see cref="Invert"/>).</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool hasValue = value is not null && (value is not string s || s.Length > 0);
        return hasValue ^ Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
