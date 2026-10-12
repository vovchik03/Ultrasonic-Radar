using System;
using System.Windows;

namespace Radar.Controls
{
    /// <summary>
    /// Maps radar coordinates (servo angle, distance) to pixels.
    /// The sensor sits at the bottom center, the screen is the half circle above it.
    /// Angles: 0° = right, 90° = up, 180° = left.
    /// </summary>
    internal sealed class RadarGeometry
    {
        public RadarGeometry(Size size, double padding, double maxRangeMm)
        {
            MaxRangeMm = maxRangeMm;

            // The half circle needs 2R of width and R of height
            Radius = Math.Max(0, Math.Min((size.Width - 2 * padding) / 2, size.Height - 2 * padding));
            Center = new Point(size.Width / 2, size.Height - padding);
        }

        /// <summary>Position of the sensor, in pixels.</summary>
        public Point Center { get; }

        /// <summary>Radius of the outer ring, in pixels.</summary>
        public double Radius { get; }

        /// <summary>Distance at the outer ring, in mm.</summary>
        public double MaxRangeMm { get; }

        /// <summary>Distance in mm -> radius in pixels, clamped to the outer ring.</summary>
        public double ToRadius(double distanceMm)
        {
            return Radius * Math.Min(Math.Max(distanceMm, 0), MaxRangeMm) / MaxRangeMm;
        }

        /// <summary>Point at the given angle and radius in pixels.</summary>
        public Point PointOnRadius(double angleDeg, double radius)
        {
            double rad = angleDeg * Math.PI / 180;
            return new Point(Center.X + radius * Math.Cos(rad), Center.Y - radius * Math.Sin(rad));
        }

        /// <summary>Point at the given angle and distance in mm.</summary>
        public Point PointAt(double angleDeg, double distanceMm)
        {
            return PointOnRadius(angleDeg, ToRadius(distanceMm));
        }
    }
}
