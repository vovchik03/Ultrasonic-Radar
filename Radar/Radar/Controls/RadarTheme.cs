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

        public static readonly Typeface LabelTypeface = new Typeface("Consolas");
        public const double LabelSize = 11;

        private static Brush Solid(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        private static Pen MakePen(Brush brush, double thickness)
        {
            var pen = new Pen(brush, thickness);
            pen.Freeze();
            return pen;
        }
    }
}
