using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;

namespace Prometheus.Modules.Match.Controls
{
    /// <summary>
    /// Lightweight, theme-aware doughnut chart for post-game part-to-whole data.
    /// The control intentionally renders only geometry; labels and exact values
    /// remain regular WPF elements so they stay accessible and localizable.
    /// </summary>
    public sealed class DoughnutChart : FrameworkElement
    {
        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(
                nameof(ItemsSource),
                typeof(IEnumerable<DoughnutSlice>),
                typeof(DoughnutChart),
                new FrameworkPropertyMetadata(null,
                    FrameworkPropertyMetadataOptions.AffectsRender,
                    HandleItemsSourceChanged));

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register(
                nameof(Maximum),
                typeof(double),
                typeof(DoughnutChart),
                new FrameworkPropertyMetadata(0d,
                    FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty RingThicknessProperty =
            DependencyProperty.Register(
                nameof(RingThickness),
                typeof(double),
                typeof(DoughnutChart),
                new FrameworkPropertyMetadata(22d,
                    FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty TrackBrushProperty =
            DependencyProperty.Register(
                nameof(TrackBrush),
                typeof(Brush),
                typeof(DoughnutChart),
                new FrameworkPropertyMetadata(Brushes.Transparent,
                    FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly Brush[] FallbackPalette =
        [
            CreateBrush("#4C8DFF"),
            CreateBrush("#8B6CE4"),
            CreateBrush("#37AEB4"),
            CreateBrush("#D69A3A"),
            CreateBrush("#D8669A")
        ];

        public IEnumerable<DoughnutSlice> ItemsSource
        {
            get => (IEnumerable<DoughnutSlice>)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        /// <summary>
        /// Optional fixed scale. Zero uses the sum of all slice values.
        /// </summary>
        public double Maximum
        {
            get => (double)GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        public double RingThickness
        {
            get => (double)GetValue(RingThicknessProperty);
            set => SetValue(RingThicknessProperty, value);
        }

        public Brush TrackBrush
        {
            get => (Brush)GetValue(TrackBrushProperty);
            set => SetValue(TrackBrushProperty, value);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var width = double.IsInfinity(availableSize.Width)
                ? 180d
                : availableSize.Width;
            var height = double.IsInfinity(availableSize.Height)
                ? 180d
                : availableSize.Height;
            var side = Math.Max(0, Math.Min(width, height));
            return new Size(side, side);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            var side = Math.Min(ActualWidth, ActualHeight);
            var thickness = Math.Clamp(RingThickness, 1d, side / 2d);
            var radius = Math.Max(0, (side - thickness) / 2d);
            if (radius <= 0)
            {
                return;
            }

            var center = new Point(ActualWidth / 2d, ActualHeight / 2d);
            var trackBrush = TrackBrush ?? Brushes.Transparent;
            drawingContext.DrawEllipse(null, new Pen(trackBrush, thickness),
                center, radius, radius);

            var slices = (ItemsSource ?? Array.Empty<DoughnutSlice>())
                .Where(slice => slice is not null && slice.Value > 0)
                .ToArray();
            var valueSum = slices.Sum(slice => slice.Value);
            var maximum = Maximum > 0 ? Math.Max(Maximum, valueSum) : valueSum;
            if (maximum <= 0)
            {
                return;
            }

            var startAngle = -90d;
            var gap = slices.Length > 1 ? 1.8d : 0d;
            foreach (var slice in slices)
            {
                var sweep = Math.Min(360d, slice.Value / maximum * 360d);
                var visibleSweep = Math.Max(0, sweep - gap);
                var brush = ResolveSliceBrush(slice.PaletteIndex);
                if (visibleSweep >= 359.5d)
                {
                    drawingContext.DrawEllipse(null, new Pen(brush, thickness),
                        center, radius, radius);
                }
                else if (visibleSweep > 0.1d)
                {
                    drawingContext.DrawGeometry(brush, null,
                        CreateRingSegment(center, radius + thickness / 2d,
                            Math.Max(0, radius - thickness / 2d),
                            startAngle + gap / 2d, visibleSweep));
                }

                startAngle += sweep;
            }
        }

        private Brush ResolveSliceBrush(int paletteIndex)
        {
            var normalizedIndex = Math.Abs(paletteIndex) % FallbackPalette.Length;
            return TryFindResource($"PostGamePieSliceBrush{normalizedIndex + 1}")
                    as Brush ??
                FallbackPalette[normalizedIndex];
        }

        private static Geometry CreateRingSegment(Point center, double outerRadius,
            double innerRadius, double startAngle, double sweepAngle)
        {
            var endAngle = startAngle + sweepAngle;
            var outerStart = PointOnCircle(center, outerRadius, startAngle);
            var outerEnd = PointOnCircle(center, outerRadius, endAngle);
            var innerEnd = PointOnCircle(center, innerRadius, endAngle);
            var innerStart = PointOnCircle(center, innerRadius, startAngle);
            var isLargeArc = sweepAngle > 180d;
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(outerStart, true, true);
                context.ArcTo(outerEnd, new Size(outerRadius, outerRadius), 0,
                    isLargeArc, SweepDirection.Clockwise, true, false);
                context.LineTo(innerEnd, true, false);
                context.ArcTo(innerStart, new Size(innerRadius, innerRadius), 0,
                    isLargeArc, SweepDirection.Counterclockwise, true, false);
            }

            geometry.Freeze();
            return geometry;
        }

        private static Point PointOnCircle(Point center, double radius, double angle)
        {
            var radians = angle * Math.PI / 180d;
            return new Point(center.X + radius * Math.Cos(radians),
                center.Y + radius * Math.Sin(radians));
        }

        private static void HandleItemsSourceChanged(DependencyObject dependencyObject,
            DependencyPropertyChangedEventArgs args)
        {
            var chart = (DoughnutChart)dependencyObject;
            if (args.OldValue is INotifyCollectionChanged oldCollection)
            {
                oldCollection.CollectionChanged -= chart.HandleCollectionChanged;
            }

            if (args.NewValue is INotifyCollectionChanged newCollection)
            {
                newCollection.CollectionChanged += chart.HandleCollectionChanged;
            }

            chart.InvalidateVisual();
        }

        private void HandleCollectionChanged(object sender,
            NotifyCollectionChangedEventArgs args)
        {
            InvalidateVisual();
        }

        private static Brush CreateBrush(string color)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        }
    }

    public sealed class DoughnutSlice
    {
        public string DisplayName { get; init; } = string.Empty;

        public double Value { get; init; }

        public string ValueText { get; init; } = string.Empty;

        public string PercentageText { get; init; } = string.Empty;

        public int PaletteIndex { get; init; }

        public bool IsLocalPlayer { get; init; }
    }
}
