using Windows.Foundation;

namespace MyApp.Presentation.Controls; // TODO: Replace 'MyApp' with your app's top-level namespace

/// <summary>
/// A responsive, non-virtualizing layout that arranges items of the same height and varying widths in a wrapping grid.
/// Supports column spans, proportional stretch-to-fill per row, and vertical alignment of horizontal gaps across rows.
/// Do NOT enable horizontal scrolling for this layout, as this will break the layout's ability to measure available width.
/// </summary>
/// <remarks>
/// The layout meets the following requirements:<br/>
/// 1. Flows items left to right, top to bottom<br/>
/// 2. Respects the columnspan of each item (all items have the same height). This means that no row can grow shorter than the widest columnspan in the collection<br/>
/// 3. Supports header rows - spans larger than maxColumns are treated as full-width
/// 4. Prevents any remaining horizontal space at the end of each row by increasing the width of the row's items proportionally<br/>
/// 5. Ensures that any horizontal gaps between items align vertically across rows. If this causes a gap at the end of a row, the last item in the row grows wider to eliminate the gap.<br/>
/// </remarks>
public class ResponsiveSpanningGridWrapLayout : NonVirtualizingLayout
{
    #region Dependency Properties

    public static readonly DependencyProperty MinColumnWidthProperty =
        DependencyProperty.Register(
            nameof(MinColumnWidth), typeof(double), typeof(ResponsiveSpanningGridWrapLayout),
            new PropertyMetadata(200.0, OnLayoutPropertyChanged));

    public static readonly DependencyProperty ColumnSpacingProperty =
        DependencyProperty.Register(
            nameof(ColumnSpacing), typeof(double), typeof(ResponsiveSpanningGridWrapLayout),
            new PropertyMetadata(8.0, OnLayoutPropertyChanged));

    public static readonly DependencyProperty RowSpacingProperty =
        DependencyProperty.Register(
            nameof(RowSpacing), typeof(double), typeof(ResponsiveSpanningGridWrapLayout),
            new PropertyMetadata(8.0, OnLayoutPropertyChanged));

    public static readonly DependencyProperty ItemHeightProperty =
        DependencyProperty.Register(
            nameof(ItemHeight), typeof(double), typeof(ResponsiveSpanningGridWrapLayout),
            new PropertyMetadata(200.0, OnLayoutPropertyChanged));

    public double MinColumnWidth
    {
        get => (double)GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    public double ColumnSpacing
    {
        get => (double)GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    public double RowSpacing
    {
        get => (double)GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    #endregion

    #region Attached Property: ColumnSpan

    public static readonly DependencyProperty ColumnSpanProperty =
        DependencyProperty.RegisterAttached(
            "ColumnSpan", typeof(int), typeof(ResponsiveSpanningGridWrapLayout),
            new PropertyMetadata(1));

    public static void SetColumnSpan(DependencyObject element, int value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(ColumnSpanProperty, value);
    }

    public static int GetColumnSpan(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (int)element.GetValue(ColumnSpanProperty);
    }

    #endregion

    static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ResponsiveSpanningGridWrapLayout layout)
        {
            layout.InvalidateMeasure();
        }
    }

    record struct LayoutSlot(int Index, double X, double Y, double Width, double Height, bool AutoHeight);

    List<LayoutSlot> ComputeLayout(NonVirtualizingLayoutContext context, double availableWidth)
    {
        List<LayoutSlot> slots = [];
        int childCount = context.Children.Count;

        if (childCount == 0 || availableWidth <= 0)
        {
            return slots;
        }

        if (double.IsInfinity(availableWidth))
        {
            availableWidth = 800;
        }

        // Compute maxColumns from available width first
        int maxColumns = Math.Max(1, (int)Math.Floor((availableWidth + ColumnSpacing) / (MinColumnWidth + ColumnSpacing)));

        // Only inflate availableWidth for items whose span exceeds maxColumns
        // but cap at a reasonable value — spans larger than maxColumns are treated as full-width
        int maxRequestedSpan = 1;
        for (int i = 0; i < childCount; i++)
        {
            int span = Math.Max(GetColumnSpan(context.Children[i]), 1);
            if (span <= maxColumns)
                maxRequestedSpan = Math.Max(maxRequestedSpan, span);
        }

        if (maxRequestedSpan > maxColumns)
        {
            double minLayoutWidth = MinColumnWidth * maxRequestedSpan + (maxRequestedSpan - 1) * ColumnSpacing;
            availableWidth = Math.Max(availableWidth, minLayoutWidth);
            maxColumns = Math.Max(1, (int)Math.Floor((availableWidth + ColumnSpacing) / (MinColumnWidth + ColumnSpacing)));
        }

        List<List<(int index, int span)>> rows = [];
        List<(int index, int span)> currentRow = [];
        int currentRowSpan = 0;

        for (int i = 0; i < childCount; i++)
        {
            int requestedSpan = Math.Max(GetColumnSpan(context.Children[i]), 1);
            int span = Math.Min(requestedSpan, maxColumns);

            if (currentRowSpan + span > maxColumns && currentRow.Count > 0)
            {
                rows.Add(currentRow);
                currentRow = [];
                currentRowSpan = 0;
            }

            currentRow.Add((i, span));
            currentRowSpan += span;
        }

        if (currentRow.Count > 0)
        {
            rows.Add(currentRow);
        }

        double gridSpacing = (maxColumns - 1) * ColumnSpacing;
        double unitWidth = (availableWidth - gridSpacing) / maxColumns;

        double y = 0.0;
        foreach (var row in rows)
        {
            // Full-width rows (single item spanning all columns) use auto height
            bool isFullWidthRow = row.Count == 1 && row[0].span >= maxColumns;

            double rowHeight = ItemHeight;
            if (isFullWidthRow)
            {
                // Measure at available width to get natural height
                var child = context.Children[row[0].index];
                child.Measure(new Size(availableWidth, double.PositiveInfinity));
                rowHeight = child.DesiredSize.Height;
            }

            double x = 0.0;
            for (int r = 0; r < row.Count; r++)
            {
                (int index, int span) = row[r];
                double itemWidth = unitWidth * span + (span - 1) * ColumnSpacing;

                if (r == row.Count - 1)
                {
                    itemWidth = availableWidth - x;
                }

                slots.Add(new LayoutSlot(index, x, y, itemWidth, rowHeight, isFullWidthRow));
                x += itemWidth + ColumnSpacing;
            }

            y += rowHeight + RowSpacing;
        }

        return slots;
    }

    protected override Size MeasureOverride(NonVirtualizingLayoutContext context, Size availableSize)
    {
        ArgumentNullException.ThrowIfNull(context);
        var slots = ComputeLayout(context, availableSize.Width);

        if (slots.Count == 0)
        {
            return new Size(0, 0);
        }

        foreach (var slot in slots)
        {
            context.Children[slot.Index].Measure(new Size(slot.Width, slot.Height));
        }

        double totalHeight = slots.Max(s => s.Y + s.Height);
        double neededWidth = slots.Max(s => s.X + s.Width);
        double width = double.IsInfinity(availableSize.Width) ? neededWidth : Math.Max(neededWidth, availableSize.Width);

        return new Size(width, totalHeight);
    }

    protected override Size ArrangeOverride(NonVirtualizingLayoutContext context, Size finalSize)
    {
        ArgumentNullException.ThrowIfNull(context);
        var slots = ComputeLayout(context, finalSize.Width);

        if (slots.Count == 0)
        {
            return new Size(0, 0);
        }

        foreach (var slot in slots)
        {
            context.Children[slot.Index].Arrange(new Rect(slot.X, slot.Y, slot.Width, slot.Height));
        }

        double totalHeight = slots.Max(s => s.Y + s.Height);

        return new Size(finalSize.Width, totalHeight);
    }
}
