using System.Windows;
using System.Windows.Controls;
using EdgeStage.Core.Layout;

namespace EdgeStage.UI;

/// <summary>
/// Items panel of the Exposé grid. The grid size (<see cref="ExposeGrid"/>) is computed by the
/// window from <see cref="ExposeLayout"/>; this panel only places items: each row of cards is
/// centered, every group header starts a new row spanning the grid, and the whole block is
/// centered vertically when it is shorter than the viewport.
/// </summary>
internal sealed class ExposeGridPanel : Panel
{
    private readonly List<Rect> _slots = [];
    private ExposeGrid _grid;
    private double _spacing;
    private double _headerHeight;
    private double _viewportHeight;

    public void Configure(ExposeGrid grid, double spacing, double headerHeight, double viewportHeight)
    {
        if (grid == _grid && spacing == _spacing && headerHeight == _headerHeight && viewportHeight == _viewportHeight)
            return;
        _grid = grid;
        _spacing = spacing;
        _headerHeight = headerHeight;
        _viewportHeight = viewportHeight;
        InvalidateMeasure();
    }

    /// <summary>Card rectangles (panel coordinates) in item order, for arrow-key navigation.</summary>
    public IReadOnlyList<CardBox> CardBoxes()
    {
        var boxes = new List<CardBox>();
        for (int i = 0; i < InternalChildren.Count && i < _slots.Count; i++)
            if (!IsHeader(InternalChildren[i]))
                boxes.Add(new CardBox(_slots[i].X, _slots[i].Y, _slots[i].Width, _slots[i].Height));
        return boxes;
    }

    protected override Size MeasureOverride(Size available)
    {
        double width = double.IsInfinity(available.Width) ? GridWidth : available.Width;
        double height = Place(width);
        for (int i = 0; i < InternalChildren.Count; i++)
            InternalChildren[i].Measure(i < _slots.Count ? _slots[i].Size : new Size(0, 0));
        return new Size(width, Math.Max(height, _viewportHeight));
    }

    protected override Size ArrangeOverride(Size final)
    {
        Place(final.Width);
        for (int i = 0; i < InternalChildren.Count; i++)
            InternalChildren[i].Arrange(i < _slots.Count ? _slots[i] : new Rect());
        return final;
    }

    private double GridWidth => _grid.Columns <= 0 ? 0 : _grid.Columns * _grid.CardWidth + (_grid.Columns - 1) * _spacing;

    private static bool IsHeader(UIElement child) => child is FrameworkElement { DataContext: GroupHeaderViewModel };

    /// <summary>Computes every child's slot; returns the content height.</summary>
    private double Place(double width)
    {
        _slots.Clear();
        int count = InternalChildren.Count;
        if (_grid.Columns <= 0 || count == 0)
        {
            for (int i = 0; i < count; i++)
                _slots.Add(new Rect());
            return 0;
        }

        double gridWidth = GridWidth;
        double gridLeft = Math.Max(0, (width - gridWidth) / 2);
        double y = 0;
        var row = new List<int>();

        void FlushRow()
        {
            if (row.Count == 0)
                return;
            double rowWidth = row.Count * _grid.CardWidth + (row.Count - 1) * _spacing;
            double x = Math.Max(0, (width - rowWidth) / 2);
            foreach (int index in row)
            {
                _slots[index] = new Rect(x, y, _grid.CardWidth, _grid.CardHeight);
                x += _grid.CardWidth + _spacing;
            }
            y += _grid.CardHeight + _spacing;
            row.Clear();
        }

        for (int i = 0; i < count; i++)
        {
            _slots.Add(new Rect());
            if (IsHeader(InternalChildren[i]))
            {
                FlushRow();
                _slots[i] = new Rect(gridLeft, y, gridWidth, _headerHeight);
                y += _headerHeight + _spacing;
                continue;
            }
            row.Add(i);
            if (row.Count == _grid.Columns)
                FlushRow();
        }
        FlushRow();
        double content = Math.Max(0, y - _spacing);

        // Center the block vertically when it fits.
        double offset = Math.Max(0, (_viewportHeight - content) / 2);
        if (offset > 0)
            for (int i = 0; i < _slots.Count; i++)
                _slots[i] = _slots[i] with { Y = _slots[i].Y + offset };
        return content + offset;
    }
}
