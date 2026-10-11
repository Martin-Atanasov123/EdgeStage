namespace EdgeStage.Core.Layout;

public enum LayoutDensity
{
    Compact,
    Balanced,
    Spacious,
}

/// <summary>
/// Sizes of the sidebar and its cards (DIPs), derived from the user's appearance settings.
/// The thumbnail area keeps a 16:10 shape; card height follows from the sidebar width.
/// </summary>
public sealed record CardMetrics(
    double SidebarWidth,
    double CardPadding,
    double CardSpacing,
    double ListPadding,
    double CornerRadius,
    bool ShowTitle = true)
{
    public const double MinSidebarWidth = 220;
    public const double MaxSidebarWidth = 400;
    public const double TitleHeight = 20;
    public const double TitleGap = 4;
    public const double CardBorder = 1.5;
    public const double PanelBorder = 1;
    public const double GroupHeaderHeight = 26;
    public const double SearchBarHeight = 44; // 36 bar + 8 top margin
    public const double ThumbAspect = 16.0 / 10.0;

    public static CardMetrics Default { get; } = new(264, 5, 6, 8, 10);

    /// <summary>Padding inside a card for each density preset.</summary>
    public static double PaddingFor(LayoutDensity density) => density switch
    {
        LayoutDensity.Compact => 3,
        LayoutDensity.Spacious => 8,
        _ => 5,
    };

    /// <summary>Default gap between cards for each density preset (the user can fine-tune it).</summary>
    public static double SpacingFor(LayoutDensity density) => density switch
    {
        LayoutDensity.Compact => 4,
        LayoutDensity.Spacious => 10,
        _ => 6,
    };

    /// <summary>Height of the title row plus gap (0 when titles are hidden).</summary>
    public double ChromeHeight => ShowTitle ? TitleHeight + TitleGap : 0;

    public double CardInnerWidth => SidebarWidth - 2 * ListPadding - 2 * PanelBorder - 2 * CardBorder - 2 * CardPadding;

    public double ThumbHeight => Math.Round(CardInnerWidth / ThumbAspect);

    public double CardHeight => 2 * CardBorder + 2 * CardPadding + ChromeHeight + ThumbHeight;

    /// <summary>Corner radius of the preview inside the card, concentric with the card's corner.</summary>
    public double ThumbCornerRadius => Math.Max(0, CornerRadius - CardPadding);

    /// <summary>Vertical space the sidebar needs for the given cards and group headers.</summary>
    public double ContentHeight(int cards, int headers, bool searchBar)
    {
        double search = searchBar ? SearchBarHeight : 0;
        int items = cards + headers;
        if (items == 0)
            return search + 120;
        return search + ListPadding
               + cards * (CardHeight + CardSpacing)
               + headers * (GroupHeaderHeight + CardSpacing)
               + ListBottomMargin + 2 * PanelBorder;
    }

    /// <summary>Every item carries <see cref="CardSpacing"/> as a bottom margin; the list's own bottom margin tops it up to <see cref="ListPadding"/>.</summary>
    public double ListBottomMargin => Math.Max(0, ListPadding - CardSpacing);
}
