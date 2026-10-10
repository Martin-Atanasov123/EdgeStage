using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace EdgeStage.UI;

public partial class PreviewCard : UserControl
{
    public PreviewCard()
    {
        InitializeComponent();
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
