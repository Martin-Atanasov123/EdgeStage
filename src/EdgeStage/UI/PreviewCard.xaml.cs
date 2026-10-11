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

    /// <summary>Show the icon/title row above the preview (Exposé can hide it).</summary>
    public static readonly DependencyProperty ShowTitleProperty = DependencyProperty.Register(
        nameof(ShowTitle), typeof(bool), typeof(PreviewCard), new PropertyMetadata(true, (d, _) => ((PreviewCard)d).ApplyTitleVisibility()));

    public bool ShowTitle
    {
        get => (bool)GetValue(ShowTitleProperty);
        set => SetValue(ShowTitleProperty, value);
    }

    /// <summary>Show the app icon in the title row.</summary>
    public static readonly DependencyProperty ShowIconProperty = DependencyProperty.Register(
        nameof(ShowIcon), typeof(bool), typeof(PreviewCard), new PropertyMetadata(true, (d, _) => ((PreviewCard)d).ApplyIconVisibility()));

    public bool ShowIcon
    {
        get => (bool)GetValue(ShowIconProperty);
        set => SetValue(ShowIconProperty, value);
    }

    private void ApplyIconVisibility()
    {
        IconColumn.Width = new GridLength(ShowIcon ? 16 : 0);
        IconGapColumn.Width = new GridLength(ShowIcon ? 6 : 0);
        TitleIcon.Visibility = ShowIcon ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyTitleVisibility()
    {
        TitleRow.Height = new GridLength(ShowTitle ? 20 : 0);
        GapRow.Height = new GridLength(ShowTitle ? 4 : 0);
        TitleBar.Visibility = ShowTitle ? Visibility.Visible : Visibility.Collapsed;
    }

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
