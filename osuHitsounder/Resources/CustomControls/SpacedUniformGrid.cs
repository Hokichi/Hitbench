using System.Windows;
using System.Windows.Controls;

namespace Fluxo.Resources.CustomControls
{
    public class SpacedUniformGrid : Panel
    {
        public static readonly DependencyProperty HorizontalGapProperty =
            DependencyProperty.Register(nameof(HorizontalGap), typeof(double), typeof(SpacedUniformGrid),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public static readonly DependencyProperty VerticalGapProperty =
            DependencyProperty.Register(nameof(VerticalGap), typeof(double), typeof(SpacedUniformGrid),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public static readonly DependencyProperty ColumnsProperty =
            DependencyProperty.Register(nameof(Columns), typeof(int), typeof(SpacedUniformGrid),
                new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public static readonly DependencyProperty RowsProperty =
            DependencyProperty.Register(nameof(Rows), typeof(int), typeof(SpacedUniformGrid),
                new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public double HorizontalGap
        {
            get => (double)GetValue(HorizontalGapProperty);
            set => SetValue(HorizontalGapProperty, value);
        }

        public double VerticalGap
        {
            get => (double)GetValue(VerticalGapProperty);
            set => SetValue(VerticalGapProperty, value);
        }

        // 0 = auto-compute from child count
        public int Columns
        {
            get => (int)GetValue(ColumnsProperty);
            set => SetValue(ColumnsProperty, value);
        }

        public int Rows
        {
            get => (int)GetValue(RowsProperty);
            set => SetValue(RowsProperty, value);
        }

        private int _computedColumns;
        private int _computedRows;

        protected override Size MeasureOverride(Size availableSize)
        {
            if (InternalChildren.Count == 0)
                return base.MeasureOverride(availableSize);

            ComputeGridDimensions();

            // Measure children unconstrained first to get their natural size
            foreach (UIElement child in InternalChildren)
                child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            // Use the largest child as the cell size baseline
            double maxChildWidth = InternalChildren.Cast<UIElement>().Max(c => c.DesiredSize.Width);
            double maxChildHeight = InternalChildren.Cast<UIElement>().Max(c => c.DesiredSize.Height);

            double cellWidth = double.IsInfinity(availableSize.Width)
                ? maxChildWidth
                : (availableSize.Width - HorizontalGap * (_computedColumns - 1)) / _computedColumns;

            double cellHeight = double.IsInfinity(availableSize.Height)
                ? maxChildHeight
                : (availableSize.Height - VerticalGap * (_computedRows - 1)) / _computedRows;

            cellWidth = Math.Max(0, cellWidth);
            cellHeight = Math.Max(0, cellHeight);

            // Re-measure with the resolved cell size if it was constrained
            if (!double.IsInfinity(availableSize.Width) || !double.IsInfinity(availableSize.Height))
            {
                foreach (UIElement child in InternalChildren)
                    child.Measure(new Size(cellWidth, cellHeight));
            }

            double totalWidth = cellWidth * _computedColumns + HorizontalGap * (_computedColumns - 1);
            double totalHeight = cellHeight * _computedRows + VerticalGap * (_computedRows - 1);

            return new Size(totalWidth, totalHeight);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            double cellWidth = (finalSize.Width - HorizontalGap * (_computedColumns - 1)) / _computedColumns;
            double cellHeight = (finalSize.Height - VerticalGap * (_computedRows - 1)) / _computedRows;

            cellWidth = Math.Max(0, cellWidth);
            cellHeight = Math.Max(0, cellHeight);

            int index = 0;
            foreach (UIElement child in InternalChildren)
            {
                int col = index % _computedColumns;
                int row = index / _computedColumns;

                double x = col * (cellWidth + HorizontalGap);
                double y = row * (cellHeight + VerticalGap);

                child.Arrange(new Rect(x, y, cellWidth, cellHeight));
                index++;
            }

            return finalSize;
        }

        private void ComputeGridDimensions()
        {
            int count = InternalChildren.Count;
            if (count == 0)
            {
                _computedColumns = 1;
                _computedRows = 1;
                return;
            }

            if (Columns > 0 && Rows > 0)
            {
                // Both explicitly set — respect them as-is
                _computedColumns = Columns;
                _computedRows = Rows;
            }
            else if (Columns > 0)
            {
                _computedColumns = Columns;
                _computedRows = (int)Math.Ceiling((double)count / Columns);
            }
            else if (Rows > 0)
            {
                _computedRows = Rows;
                _computedColumns = (int)Math.Ceiling((double)count / Rows);
            }
            else
            {
                // Mirror WPF's default: auto-compute columns from a square-ish layout
                _computedColumns = (int)Math.Ceiling(Math.Sqrt(count));
                _computedRows = (int)Math.Ceiling((double)count / _computedColumns);
            }
        }
    }
}