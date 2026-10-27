using System.Windows.Media;

namespace Radar.Controls
{
    /// <summary>
    /// Colors of the radar screen: dark olive green. All brushes and pens are frozen.
    /// </summary>
    internal static class RadarTheme
    {
        public static readonly Brush Background = Solid(0x0B, 0x10, 0x08);
        public static readonly Brush Screen     = Solid(0x11, 0x1A, 0x0C);   // the half disk
        public static readonly Brush Label      = Solid(0x7A, 0x8B, 0x4A);

        public static readonly Pen MinorGrid = MakePen(Solid(0x1F, 0x2A, 0x14), 1);
        public static readonly Pen MajorGrid = MakePen(Solid(0x34, 0x43, 0x1F), 1);
        public static readonly Pen Edge      = MakePen(Solid(0x5E, 0x71, 0x34), 1.5);

        // Active scan sector
        public static readonly Brush SectorFill  = Solid(0x7C, 0x8F, 0x45, 0x1A);
        public static readonly Pen   SectorEdge  = MakePen(Solid(0xA3, 0xB8, 0x5C), 1.5);
        public static readonly Brush SectorLabel = Solid(0xB5, 0xC9, 0x6A);

        // Beam: red up to the echo, a wide transparent line under it gives the glow
        public static readonly Pen   Beam          = MakePen(Solid(0xE2, 0x4B, 0x4A), 2);
        public static readonly Pen   BeamGlow      = MakePen(Solid(0xE2, 0x4B, 0x4A, 0x40), 7);
        public static readonly Pen   BeamDirection = MakePen(Solid(0xE2, 0x4B, 0x4A, 0x60), 1.5, dashed: true);
        public static readonly Brush BeamText      = Solid(0xE2, 0x4B, 0x4A);

        // Mouse: angle under the cursor and the sector being dragged out
        public static readonly Pen   Hover          = MakePen(Solid(0xB5, 0xC9, 0x6A, 0x80), 1, dashed: true);
        public static readonly Brush SelectionFill  = Solid(0xE8, 0xC5, 0x4A, 0x22);
        public static readonly Pen   SelectionEdge  = MakePen(Solid(0xE8, 0xC5, 0x4A), 1.5, dashed: true);
        public static readonly Brush SelectionLabel = Solid(0xE8, 0xC5, 0x4A);

        public static readonly Typeface LabelTypeface = new Typeface("Consolas");
        public const double LabelSize = 11;
        public const double ReadoutSize = 13;

        private static Brush Solid(byte r, byte g, byte b, byte a = 0xFF)
        {
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }

        private static Pen MakePen(Brush brush, double thickness, bool dashed = false)
        {
            // Round joins: a miter join spikes past the sensor point on a narrow sector
            var pen = new Pen(brush, thickness)
            {
                LineJoin = PenLineJoin.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            if (dashed)
            {
                pen.DashStyle = new DashStyle(new[] { 2.0, 3.0 }, 0);   // in pen widths
            }
            pen.Freeze();
            return pen;
        }
    }
}
