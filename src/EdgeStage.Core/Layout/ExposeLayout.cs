namespace EdgeStage.Core.Layout;

public enum ExposeDensity
{
    Spacious,
    Auto,
    Dense,
}

public enum CardSize
{
    Small,
    Standard,
    Large,
}

/// <summary>Inputs for <see cref="ExposeLayout.Compute"/>, all in DIPs.</summary>
public sealed record ExposeLayoutInput(
    double AreaWidth,
    double AreaHeight,
    IReadOnlyList<int> GroupSizes,
    double CardChromeHeight,
    double CardPadding,
    double Spacing,
    double HeaderHeight,
    double MinCardWidth,
    double MaxCardWidth,
    double ThumbAspect = 16.0 / 10.0);

/// <summary>Result: a uniform grid of cards. Each group starts on a new row, below its header.</summary>
public readonly record struct ExposeGrid(int Columns, double CardWidth, double CardHeight, int Rows, double TotalHeight, bool NeedsScroll)
{
    public static ExposeGrid Empty => new(0, 0, 0, 0, 0, false);
}

/// <summary>
/// Adaptive Exposé grid: picks the column count that gives the largest cards that still fit the
/// area, clamped to the density's card-size range. When even the minimum card size does not fit,
/// the grid keeps the minimum size and scrolls vertically, so every card stays selectable.
/// </summary>
public static class ExposeLayout
{
    /// <summary>Card width range and spacing for a density and card size.</summary>
    public static (double Min, double Max, double Spacing) SizeRange(ExposeDensity density, CardSize size)
    {
        (double min, double max) = size switch
        {
            CardSize.Small => (150.0, 320.0),
            CardSize.Large => (240.0, 640.0),
            _ => (190.0, 460.0),
        };
        return density switch
        {
            ExposeDensity.Spacious => (min * 1.15, max * 1.15, 40),
            ExposeDensity.Dense => (min * 0.8, max * 0.8, 14),
            _ => (min, max, 26),
        };
    }

    public static double CardHeightFor(double cardWidth, double chromeHeight, double padding, double aspect) =>
        chromeHeight + Math.Max(0, cardWidth - 2 * padding) / aspect;

    public static ExposeGrid Compute(ExposeLayoutInput input)
    {
        // A header row precedes each group, collapsed (size 0) ones included (HeaderHeight 0 = ungrouped).
        int headers = input.HeaderHeight > 0 ? input.GroupSizes.Count : 0;
        if ((input.GroupSizes.Sum() <= 0 && headers == 0) || input.AreaWidth <= 0 || input.AreaHeight <= 0)
            return ExposeGrid.Empty;
        double headerSpace = headers * (input.HeaderHeight + input.Spacing);
        int maxColumns = Math.Max(1, input.GroupSizes.Max());

        ExposeGrid best = ExposeGrid.Empty;
        for (int columns = 1; columns <= maxColumns; columns++)
        {
            int rows = RowsFor(input.GroupSizes, columns);
            double byWidth = (input.AreaWidth - (columns - 1) * input.Spacing) / columns;
            double perRow = rows == 0 ? double.MaxValue : (input.AreaHeight - headerSpace - (rows - 1) * input.Spacing) / rows;
            double byHeight = (perRow - input.CardChromeHeight) * input.ThumbAspect + 2 * input.CardPadding;
            double width = Math.Min(Math.Min(byWidth, byHeight), input.MaxCardWidth);
            if (width > best.CardWidth + 0.01)
                best = Build(input, columns, width, headerSpace);
        }

        if (best.CardWidth >= input.MinCardWidth)
            return best;

        // Too many windows to fit: keep the minimum size, use as many columns as fit, and scroll.
        int fitColumns = Math.Max(1, (int)Math.Floor((input.AreaWidth + input.Spacing) / (input.MinCardWidth + input.Spacing)));
        fitColumns = Math.Min(fitColumns, maxColumns);
        double minWidth = Math.Min(input.MinCardWidth, input.AreaWidth);
        return Build(input, fitColumns, minWidth, headerSpace);
    }

    private static ExposeGrid Build(ExposeLayoutInput input, int columns, double width, double headerSpace)
    {
        int rows = RowsFor(input.GroupSizes, columns);
        double height = CardHeightFor(width, input.CardChromeHeight, input.CardPadding, input.ThumbAspect);
        double total = headerSpace + rows * height + Math.Max(0, rows - 1) * input.Spacing;
        if (rows == 0)
            total = Math.Max(0, headerSpace - input.Spacing); // only collapsed groups
        return new ExposeGrid(columns, Math.Floor(width), Math.Floor(height), rows, total, total > input.AreaHeight + 0.5);
    }

    private static int RowsFor(IReadOnlyList<int> groups, int columns) =>
        groups.Where(g => g > 0).Sum(g => (g + columns - 1) / columns);
}
