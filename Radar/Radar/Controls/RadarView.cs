using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Radar.Controls
{
    /// <summary>
    /// Radar screen: range rings and angle lines over the 0..180° half circle.
    /// Resizes with its container, the sensor sits at the bottom center.
    /// </summary>
    public class RadarView : FrameworkElement
    {
        private const double Padding = 32;          // room for the labels around the half circle
        private const double LabelOffset = 16;      // angle labels: distance outside the outer ring
        private const int MinorAngleStep = 10;      // degrees
        private const int MajorAngleStep = 30;
        private const int MinorRingStepMm = 500;
        private const int MajorRingStepMm = 1000;

        public static readonly DependencyProperty MaxRangeMmProperty =
            DependencyProperty.Register(nameof(MaxRangeMm), typeof(double), typeof(RadarView),
                new FrameworkPropertyMetadata(4000.0, FrameworkPropertyMetadataOptions.AffectsRender, OnGridChanged),
                value => (double)value > 0);

        /// <summary>Distance at the outer ring, in mm. 4000 mm is the HC-SR04 limit.</summary>
        public double MaxRangeMm
        {
            get => (double)GetValue(MaxRangeMmProperty);
            set => SetValue(MaxRangeMmProperty, value);
        }

        // The grid changes only with the size or the range, so it is built once and reused
        private Drawing gridCache;

        private static void OnGridChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((RadarView)d).gridCache = null;
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            gridCache = null;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(RadarTheme.Background, null, new Rect(RenderSize));

            var geometry = new RadarGeometry(RenderSize, Padding, MaxRangeMm);
            if (geometry.Radius <= 0)
            {
                return;
            }

            if (gridCache == null)
            {
                gridCache = BuildGrid(geometry);
            }
            dc.DrawDrawing(gridCache);
        }

        private Drawing BuildGrid(RadarGeometry g)
        {
            var group = new DrawingGroup();
            using (DrawingContext dc = group.Open())
            {
                dc.DrawGeometry(RadarTheme.Screen, null, Sector(g, 0, 180, g.Radius));

                // Range rings, labeled every meter under the baseline
                for (int mm = MinorRingStepMm; mm < MaxRangeMm; mm += MinorRingStepMm)
                {
                    bool major = mm % MajorRingStepMm == 0;
                    double r = g.ToRadius(mm);
                    dc.DrawGeometry(null, major ? RadarTheme.MajorGrid : RadarTheme.MinorGrid, Arc(g, 0, 180, r));
                    if (major)
                    {
                        DrawLabel(dc, (mm / 1000) + " m", new Point(g.Center.X + r, g.Center.Y + 10));
                    }
                }
                DrawLabel(dc, FormatMeters(MaxRangeMm), new Point(g.Center.X + g.Radius, g.Center.Y + 10));

                // Angle lines, labeled outside the outer ring
                for (int angle = 0; angle <= 180; angle += MinorAngleStep)
                {
                    bool major = angle % MajorAngleStep == 0;
                    dc.DrawLine(major ? RadarTheme.MajorGrid : RadarTheme.MinorGrid,
                                g.Center, g.PointOnRadius(angle, g.Radius));
                    if (major)
                    {
                        DrawLabel(dc, angle + "°", g.PointOnRadius(angle, g.Radius + LabelOffset));
                    }
                }

                // Outer edge on top of the grid
                dc.DrawGeometry(null, RadarTheme.Edge, Sector(g, 0, 180, g.Radius));
                dc.DrawEllipse(RadarTheme.Edge.Brush, null, g.Center, 3, 3);
            }
            group.Freeze();
            return group;
        }

        /// <summary>Arc from one angle to the other (counterclockwise on screen), at most 180°.</summary>
        private static Geometry Arc(RadarGeometry g, double fromDeg, double toDeg, double radius)
        {
            var geometry = new StreamGeometry();
            using (StreamGeometryContext ctx = geometry.Open())
            {
                ctx.BeginFigure(g.PointOnRadius(fromDeg, radius), false, false);
                ctx.ArcTo(g.PointOnRadius(toDeg, radius), new Size(radius, radius), 0,
                          false, SweepDirection.Counterclockwise, true, false);
            }
            geometry.Freeze();
            return geometry;
        }

        /// <summary>Closed wedge from the center: two edges and the convex arc between them.</summary>
        private static Geometry Sector(RadarGeometry g, double fromDeg, double toDeg, double radius)
        {
            var geometry = new StreamGeometry();
            using (StreamGeometryContext ctx = geometry.Open())
            {
                ctx.BeginFigure(g.Center, true, true);
                ctx.LineTo(g.PointOnRadius(fromDeg, radius), true, false);
                ctx.ArcTo(g.PointOnRadius(toDeg, radius), new Size(radius, radius), 0,
                          false, SweepDirection.Counterclockwise, true, false);
            }
            geometry.Freeze();
            return geometry;
        }

        /// <summary>Draws the text centered on the given point.</summary>
        private void DrawLabel(DrawingContext dc, string text, Point center)
        {
            var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                              RadarTheme.LabelTypeface, RadarTheme.LabelSize, RadarTheme.Label,
                                              VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(formatted, new Point(center.X - formatted.Width / 2, center.Y - formatted.Height / 2));
        }

        private static string FormatMeters(double mm)
        {
            return (mm / 1000).ToString("0.#", CultureInfo.InvariantCulture) + " m";
        }
    }
}
