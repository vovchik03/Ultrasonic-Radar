using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Radar.Controls
{
    /// <summary>
    /// Radar screen: range rings and angle lines over the 0..180° half circle,
    /// the scanned sector and the beam to the last measured point.
    /// Resizes with its container, the sensor sits at the bottom center.
    ///
    /// Mouse: a click on the half circle raises AngleClicked, pressing and dragging
    /// across it raises SectorSelected when the button is released.
    /// </summary>
    public class RadarView : FrameworkElement
    {
        private const double ClickToleranceDeg = 3;    // a press moved less than this is a click, not a drag
        private const double Padding = 32;          // room for the labels around the half circle
        private const double LabelOffset = 16;      // angle labels: distance outside the outer ring
        private const int MinorAngleStep = 10;      // degrees
        private const int MajorAngleStep = 30;
        private const int MinorRingStepMm = 500;
        private const int MajorRingStepMm = 1000;
        private const double LabelGapDeg = 10;      // grid angle labels closer than this to a sector edge are hidden

        public static readonly DependencyProperty MaxRangeMmProperty =
            DependencyProperty.Register(nameof(MaxRangeMm), typeof(double), typeof(RadarView),
                new FrameworkPropertyMetadata(2000.0, FrameworkPropertyMetadataOptions.AffectsRender, OnGridChanged),
                value => (double)value > 0);

        /// <summary>Distance at the outer ring, in mm. 2000 mm is the HC-SR04 limit.</summary>
        public double MaxRangeMm
        {
            get => (double)GetValue(MaxRangeMmProperty);
            set => SetValue(MaxRangeMmProperty, value);
        }

        public static readonly DependencyProperty SectorFromProperty =
            DependencyProperty.Register(nameof(SectorFrom), typeof(double), typeof(RadarView),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnGridChanged),
                IsValidAngle);

        public static readonly DependencyProperty SectorToProperty =
            DependencyProperty.Register(nameof(SectorTo), typeof(double), typeof(RadarView),
                new FrameworkPropertyMetadata(180.0, FrameworkPropertyMetadataOptions.AffectsRender, OnGridChanged),
                IsValidAngle);

        /// <summary>One edge of the scanned sector, 0..180°. The order of From and To does not matter.</summary>
        public double SectorFrom
        {
            get => (double)GetValue(SectorFromProperty);
            set => SetValue(SectorFromProperty, value);
        }

        /// <summary>Other edge of the scanned sector, 0..180°.</summary>
        public double SectorTo
        {
            get => (double)GetValue(SectorToProperty);
            set => SetValue(SectorToProperty, value);
        }

        public static readonly DependencyProperty BeamAngleProperty =
            DependencyProperty.Register(nameof(BeamAngle), typeof(double), typeof(RadarView),
                new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender),
                value => double.IsNaN((double)value) || IsValidAngle(value));

        public static readonly DependencyProperty BeamDistanceMmProperty =
            DependencyProperty.Register(nameof(BeamDistanceMm), typeof(double), typeof(RadarView),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender),
                value => (double)value >= 0);

        /// <summary>Where the sensor points now, 0..180°. NaN = no point measured yet, the beam is hidden.</summary>
        public double BeamAngle
        {
            get => (double)GetValue(BeamAngleProperty);
            set => SetValue(BeamAngleProperty, value);
        }

        /// <summary>Distance of the echo at BeamAngle, in mm. 0 = no echo.</summary>
        public double BeamDistanceMm
        {
            get => (double)GetValue(BeamDistanceMmProperty);
            set => SetValue(BeamDistanceMmProperty, value);
        }

        private static bool IsValidAngle(object value)
        {
            double angle = (double)value;
            return angle >= 0 && angle <= 180;
        }

        /// <summary>Click on the radar: the angle under the cursor, 0..180°.</summary>
        public event Action<int> AngleClicked;

        /// <summary>Drag across the radar: the swept sector, from &lt;= to.</summary>
        public event Action<int, int> SectorSelected;

        // The grid changes only with the size, the range or the sector, so it is built once and reused
        private Drawing gridCache;

        // Mouse state
        private int? hoverAngle;        // angle under the cursor, null = cursor away from the radar
        private Point hoverPoint;
        private bool pressed;
        private bool dragging;
        private int dragStart;
        private int dragCurrent;

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

            DrawSelection(dc, geometry);
            DrawBeam(dc, geometry);
        }

        /// <summary>The sector being dragged out, or the angle under the cursor.</summary>
        private void DrawSelection(DrawingContext dc, RadarGeometry g)
        {
            if (dragging)
            {
                int from = Math.Min(dragStart, dragCurrent);
                int to = Math.Max(dragStart, dragCurrent);
                Geometry sector = Sector(g, from, to, g.Radius);
                dc.DrawGeometry(RadarTheme.SelectionFill, RadarTheme.SelectionEdge, sector);
                DrawLabel(dc, FormatAngle(from), g.PointOnRadius(from, g.Radius + LabelOffset), RadarTheme.SelectionLabel);
                DrawLabel(dc, FormatAngle(to), g.PointOnRadius(to, g.Radius + LabelOffset), RadarTheme.SelectionLabel);
            }
            else if (hoverAngle != null)
            {
                dc.DrawLine(RadarTheme.Hover, g.Center, g.PointOnRadius(hoverAngle.Value, g.Radius));
                DrawText(dc, FormatAngle(hoverAngle.Value), new Point(hoverPoint.X + 14, hoverPoint.Y - 18),
                         RadarTheme.SelectionLabel, RadarTheme.LabelSize);
            }
        }

        /// <summary>
        /// Servo angle under the point, 0..180°. Points under the baseline map to 0° or 180°.
        /// With onlyNear, a point away from the half circle gives null.
        /// </summary>
        private int? AngleAt(Point p, bool onlyNear)
        {
            var g = new RadarGeometry(RenderSize, Padding, MaxRangeMm);
            if (g.Radius <= 0)
            {
                return null;
            }

            double dx = p.X - g.Center.X;
            double dy = g.Center.Y - p.Y;
            if (onlyNear && (Math.Sqrt(dx * dx + dy * dy) > g.Radius + LabelOffset || dy < -LabelOffset))
            {
                return null;
            }

            double deg = Math.Atan2(Math.Max(dy, 0), dx) * 180 / Math.PI;
            return (int)Math.Round(Math.Min(Math.Max(deg, 0), 180));
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);

            int? angle = AngleAt(e.GetPosition(this), true);
            if (angle == null)
            {
                return;
            }

            pressed = true;
            dragging = false;
            dragStart = dragCurrent = angle.Value;
            CaptureMouse();
            e.Handled = true;
            InvalidateVisual();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            hoverPoint = e.GetPosition(this);
            hoverAngle = AngleAt(hoverPoint, true);

            if (pressed)
            {
                // While dragging the cursor may leave the radar, the angle keeps following it
                dragCurrent = AngleAt(hoverPoint, false) ?? dragCurrent;
                if (Math.Abs(dragCurrent - dragStart) >= ClickToleranceDeg)
                {
                    dragging = true;
                }
            }

            Cursor = (hoverAngle != null || pressed) ? Cursors.Cross : null;
            InvalidateVisual();
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (!pressed)
            {
                return;
            }

            bool wasDragging = dragging;
            int from = Math.Min(dragStart, dragCurrent);
            int to = Math.Max(dragStart, dragCurrent);
            ReleaseMouseCapture();      // resets the drag in OnLostMouseCapture
            e.Handled = true;

            if (wasDragging)
            {
                SectorSelected?.Invoke(from, to);
            }
            else
            {
                AngleClicked?.Invoke(dragStart);
            }
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            pressed = false;
            dragging = false;
            InvalidateVisual();
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            hoverAngle = null;
            InvalidateVisual();
        }

        private void DrawBeam(DrawingContext dc, RadarGeometry g)
        {
            double angle = BeamAngle;
            if (double.IsNaN(angle))
            {
                return;
            }

            double distance = BeamDistanceMm;
            Point edge = g.PointOnRadius(angle, g.Radius);

            if (distance <= 0)
            {
                // No echo: only the direction, up to the edge of the view
                dc.DrawLine(RadarTheme.BeamDirection, g.Center, edge);
            }
            else
            {
                // Red up to the echo, the direction continues dimmed behind it
                Point echo = g.PointAt(angle, distance);
                dc.DrawLine(RadarTheme.BeamDirection, echo, edge);
                dc.DrawLine(RadarTheme.BeamGlow, g.Center, echo);
                dc.DrawLine(RadarTheme.Beam, g.Center, echo);
                dc.DrawEllipse(RadarTheme.Beam.Brush, null, echo, 4, 4);
            }

            // Readout in the top right corner, outside the half circle (top left is the link lamp)
            string reading = distance > 0
                ? distance.ToString("0", CultureInfo.InvariantCulture) + " mm"
                : "no echo";
            DrawTextRight(dc, FormatAngle(angle), 10, RadarTheme.BeamText, RadarTheme.ReadoutSize);
            DrawTextRight(dc, reading, 10 + RadarTheme.ReadoutSize * 1.4, RadarTheme.BeamText, RadarTheme.ReadoutSize);
        }

        private Drawing BuildGrid(RadarGeometry g)
        {
            double sectorFrom = Math.Min(SectorFrom, SectorTo);
            double sectorTo = Math.Max(SectorFrom, SectorTo);

            var group = new DrawingGroup();
            using (DrawingContext dc = group.Open())
            {
                dc.DrawGeometry(RadarTheme.Screen, null, Sector(g, 0, 180, g.Radius));
                dc.DrawGeometry(RadarTheme.SectorFill, null, Sector(g, sectorFrom, sectorTo, g.Radius));

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
                    // The sector edges have their own labels, the grid ones would overlap them
                    bool nearEdge = Math.Abs(angle - sectorFrom) < LabelGapDeg || Math.Abs(angle - sectorTo) < LabelGapDeg;
                    if (major && !nearEdge)
                    {
                        DrawLabel(dc, angle + "°", g.PointOnRadius(angle, g.Radius + LabelOffset));
                    }
                }

                // Outer edge on top of the grid
                dc.DrawGeometry(null, RadarTheme.Edge, Sector(g, 0, 180, g.Radius));

                // Sector edges: two lines from the sensor and the convex arc between them
                dc.DrawGeometry(null, RadarTheme.SectorEdge, Sector(g, sectorFrom, sectorTo, g.Radius));
                DrawLabel(dc, FormatAngle(sectorFrom), g.PointOnRadius(sectorFrom, g.Radius + LabelOffset), RadarTheme.SectorLabel);
                if (sectorTo != sectorFrom)
                {
                    DrawLabel(dc, FormatAngle(sectorTo), g.PointOnRadius(sectorTo, g.Radius + LabelOffset), RadarTheme.SectorLabel);
                }

                dc.DrawEllipse(RadarTheme.SectorEdge.Brush, null, g.Center, 3, 3);
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
        private void DrawLabel(DrawingContext dc, string text, Point center, Brush brush = null)
        {
            FormattedText formatted = Format(text, brush ?? RadarTheme.Label, RadarTheme.LabelSize);
            dc.DrawText(formatted, new Point(center.X - formatted.Width / 2, center.Y - formatted.Height / 2));
        }

        /// <summary>Draws the text with its top left corner at the given point.</summary>
        private void DrawText(DrawingContext dc, string text, Point topLeft, Brush brush, double size)
        {
            dc.DrawText(Format(text, brush, size), topLeft);
        }

        /// <summary>Draws the text right-aligned 12 px from the right edge, top at y.</summary>
        private void DrawTextRight(DrawingContext dc, string text, double y, Brush brush, double size)
        {
            FormattedText formatted = Format(text, brush, size);
            dc.DrawText(formatted, new Point(RenderSize.Width - 12 - formatted.Width, y));
        }

        private FormattedText Format(string text, Brush brush, double size)
        {
            return new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                     RadarTheme.LabelTypeface, size, brush,
                                     VisualTreeHelper.GetDpi(this).PixelsPerDip);
        }

        private static string FormatAngle(double angle)
        {
            return angle.ToString("0.#", CultureInfo.InvariantCulture) + "°";
        }

        private static string FormatMeters(double mm)
        {
            return (mm / 1000).ToString("0.#", CultureInfo.InvariantCulture) + " m";
        }
    }
}
