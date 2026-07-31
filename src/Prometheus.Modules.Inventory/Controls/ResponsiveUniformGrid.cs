using System.Windows;
using System.Windows.Controls;

namespace Prometheus.Modules.Inventory.Controls
{
    public class ResponsiveUniformGrid : Panel
    {
        public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
            nameof(MinItemWidth),
            typeof(double),
            typeof(ResponsiveUniformGrid),
            new FrameworkPropertyMetadata(
                142d,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange),
            IsPositiveFiniteValue);

        public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
            nameof(Spacing),
            typeof(double),
            typeof(ResponsiveUniformGrid),
            new FrameworkPropertyMetadata(
                0d,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange),
            IsNonNegativeFiniteValue);

        public double MinItemWidth
        {
            get => (double)GetValue(MinItemWidthProperty);
            set => SetValue(MinItemWidthProperty, value);
        }

        public double Spacing
        {
            get => (double)GetValue(SpacingProperty);
            set => SetValue(SpacingProperty, value);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            if (InternalChildren.Count == 0)
            {
                return new Size();
            }

            var availableWidth = GetAvailableWidth(availableSize.Width);
            var columnCount = CalculateColumnCount(availableWidth);
            var itemWidth = CalculateItemWidth(availableWidth, columnCount);
            var itemHeight = 0d;

            foreach (UIElement child in InternalChildren)
            {
                child.Measure(new Size(itemWidth, double.PositiveInfinity));
                itemHeight = Math.Max(itemHeight, child.DesiredSize.Height);
            }

            var rowCount = (int)Math.Ceiling((double)InternalChildren.Count / columnCount);
            var desiredHeight = rowCount * itemHeight + Math.Max(0, rowCount - 1) * Spacing;

            return new Size(availableWidth, desiredHeight);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (InternalChildren.Count == 0)
            {
                return finalSize;
            }

            var availableWidth = Math.Max(0, finalSize.Width);
            var columnCount = CalculateColumnCount(availableWidth);
            var itemWidth = CalculateItemWidth(availableWidth, columnCount);
            var itemHeight = InternalChildren
                .Cast<UIElement>()
                .Max(child => child.DesiredSize.Height);

            for (var index = 0; index < InternalChildren.Count; index++)
            {
                var column = index % columnCount;
                var row = index / columnCount;
                var x = column * (itemWidth + Spacing);
                var y = row * (itemHeight + Spacing);

                InternalChildren[index].Arrange(new Rect(x, y, itemWidth, itemHeight));
            }

            return finalSize;
        }

        private double GetAvailableWidth(double width)
        {
            if (!double.IsNaN(width) && !double.IsInfinity(width))
            {
                return Math.Max(0, width);
            }

            return InternalChildren.Count * MinItemWidth + Math.Max(0, InternalChildren.Count - 1) * Spacing;
        }

        private int CalculateColumnCount(double availableWidth)
        {
            return Math.Max(1, (int)Math.Floor((availableWidth + Spacing) / (MinItemWidth + Spacing)));
        }

        private double CalculateItemWidth(double availableWidth, int columnCount)
        {
            var totalSpacing = Math.Max(0, columnCount - 1) * Spacing;
            return Math.Max(0, (availableWidth - totalSpacing) / columnCount);
        }

        private static bool IsPositiveFiniteValue(object value)
        {
            var number = (double)value;
            return number > 0 && !double.IsInfinity(number) && !double.IsNaN(number);
        }

        private static bool IsNonNegativeFiniteValue(object value)
        {
            var number = (double)value;
            return number >= 0 && !double.IsInfinity(number) && !double.IsNaN(number);
        }
    }
}
