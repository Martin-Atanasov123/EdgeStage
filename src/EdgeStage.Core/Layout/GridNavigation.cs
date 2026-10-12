namespace EdgeStage.Core.Layout;

public enum NavDirection
{
    Left,
    Right,
    Up,
    Down,
}

/// <summary>A card's rectangle in any consistent coordinate space.</summary>
public readonly record struct CardBox(double X, double Y, double Width, double Height)
{
    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;
}

/// <summary>
/// Arrow-key movement through a grid of cards. Left/Right follow reading order (and so continue
/// onto the next or previous row); Up/Down go to the nearest card in the row above or below,
/// even when rows are centered and have different lengths or a group header sits between them.
/// </summary>
public static class GridNavigation
{
    public static int Next(IReadOnlyList<CardBox> cards, int current, NavDirection direction)
    {
        if (cards.Count == 0)
            return -1;
        if (current < 0 || current >= cards.Count)
            return 0;

        switch (direction)
        {
            case NavDirection.Left:
                return Math.Max(0, current - 1);
            case NavDirection.Right:
                return Math.Min(cards.Count - 1, current + 1);
        }

        CardBox from = cards[current];
        bool up = direction == NavDirection.Up;
        double tolerance = from.Height / 2;
        int best = current;
        double bestRow = double.NaN, bestDx = double.MaxValue;
        for (int i = 0; i < cards.Count; i++)
        {
            double dy = cards[i].CenterY - from.CenterY;
            if (up ? dy > -tolerance : dy < tolerance)
                continue; // same row or the wrong direction
            double row = cards[i].CenterY;
            double dx = Math.Abs(cards[i].CenterX - from.CenterX);
            bool closerRow = double.IsNaN(bestRow) || (up ? row > bestRow + 0.5 : row < bestRow - 0.5);
            bool sameRow = !double.IsNaN(bestRow) && Math.Abs(row - bestRow) <= 0.5;
            if (closerRow || (sameRow && dx < bestDx))
            {
                best = i;
                bestRow = row;
                bestDx = dx;
            }
        }
        return best;
    }
}

/// <summary>Which display an arrow key leads to when it leaves a display's grid.</summary>
public static class DisplayNavigation
{
    /// <summary>Index of the nearest display in <paramref name="direction"/> from display <paramref name="from"/> (by centers), or -1.</summary>
    public static int Neighbor(IReadOnlyList<CardBox> displays, int from, NavDirection direction)
    {
        if (from < 0 || from >= displays.Count)
            return -1;
        CardBox here = displays[from];
        int best = -1;
        double bestDistance = double.MaxValue;
        for (int i = 0; i < displays.Count; i++)
        {
            if (i == from)
                continue;
            double dx = displays[i].CenterX - here.CenterX, dy = displays[i].CenterY - here.CenterY;
            bool inDirection = direction switch
            {
                NavDirection.Left => dx < 0 && Math.Abs(dx) >= Math.Abs(dy),
                NavDirection.Right => dx > 0 && Math.Abs(dx) >= Math.Abs(dy),
                NavDirection.Up => dy < 0 && Math.Abs(dy) > Math.Abs(dx),
                _ => dy > 0 && Math.Abs(dy) > Math.Abs(dx),
            };
            double distance = dx * dx + dy * dy;
            if (inDirection && distance < bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }
        return best;
    }
}
