using System;

namespace Radar.Device
{
    internal enum RadarReplyKind
    {
        Point,      // P 45 523
        Scan,       // OK SCAN 40 70
        GoTo,       // OK GOTO 120
        Speed,      // OK SPEED 90
        Step,       // OK STEP 5
        Ping,       // OK PING
        Error,      // ERR UNKNOWN / ERR ARGS / ERR RANGE
        Other       // OK STOP, OK STREAM ON, DATA ..., anything unexpected
    }

    /// <summary>One parsed reply line. Only the fields of its kind are set.</summary>
    internal sealed class RadarReply
    {
        public RadarReplyKind Kind;
        public string Line;
        public int Angle;          // Point, GoTo
        public int DistanceMm;     // Point, 0 = no echo
        public int From;           // Scan
        public int To;             // Scan
        public int Speed;          // Speed, deg/s, 0 = no limit
        public int Step;           // Step, deg
    }

    /// <summary>
    /// Commands and replies of the radar firmware (see cmd.h).
    /// </summary>
    internal static class RadarProtocol
    {
        public const int AngleMax = 180;        // SCAN_ANGLE_MAX
        public const int SpeedMax = 500;        // SCAN_SPEED_MAX_DPS
        public const int StepMax = 30;          // SCAN_STEP_MAX_DEG

        // How long one move between two points takes, see StepForSpeed
        private const int MoveMs = 50;

        // Limits of the speed -> step rule (the firmware itself takes 1..StepMax)
        private const int AutoStepMin = 2;      // finer than the ~15° beam adds only smoothness
        private const int AutoStepMax = 8;      // coarser leaves gaps in the picture

        // Connection check, sent when the radar has been quiet for a while
        public const string PingCommand = "PING";
        public const string PingReply = "OK PING";

        public static string Scan(int from, int to) => $"SCAN {from} {to}";
        public static string GoTo(int angle) => $"GOTO {angle}";
        public static string SetSpeed(int degPerSecond) => $"SPEED {degPerSecond}";
        public static string SetStep(int degrees) => $"STEP {degrees}";

        /// <summary>
        /// Grid step for a servo speed: the angle the servo turns in MoveMs, rounded,
        /// AutoStepMin..AutoStepMax. Every move then takes ~50 ms whatever the speed, so the
        /// points keep coming at ~10 Hz (40 ms settle + 50 ms move + echo, scan.h) and a slower
        /// servo gives a denser picture: 30 deg/s -> 2°, 90 deg/s -> 5°, 160 deg/s and up -> 8°.
        /// Above 160 deg/s the step stays 8°, so the moves get shorter and the points come faster.
        /// </summary>
        public static int StepForSpeed(int degPerSecond)
        {
            int step = (degPerSecond * MoveMs + 500) / 1000;
            return Math.Max(AutoStepMin, Math.Min(AutoStepMax, step));
        }
        public const string StreamOn = "STREAM ON";

        public static RadarReply Parse(string line)
        {
            line = line.Trim();
            var reply = new RadarReply { Kind = RadarReplyKind.Other, Line = line };
            string[] w = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (w.Length == 3 && w[0] == "P"
                && TryAngle(w[1], out reply.Angle) && TryNumber(w[2], ushort.MaxValue, out reply.DistanceMm))
            {
                reply.Kind = RadarReplyKind.Point;
            }
            else if (w.Length == 4 && w[0] == "OK" && w[1] == "SCAN"
                     && TryAngle(w[2], out reply.From) && TryAngle(w[3], out reply.To))
            {
                reply.Kind = RadarReplyKind.Scan;
            }
            else if (w.Length == 3 && w[0] == "OK" && w[1] == "GOTO" && TryAngle(w[2], out reply.Angle))
            {
                reply.Kind = RadarReplyKind.GoTo;
            }
            else if (w.Length == 3 && w[0] == "OK" && w[1] == "SPEED" && TryNumber(w[2], SpeedMax, out reply.Speed))
            {
                reply.Kind = RadarReplyKind.Speed;
            }
            else if (w.Length == 3 && w[0] == "OK" && w[1] == "STEP"
                     && TryNumber(w[2], StepMax, out reply.Step) && reply.Step > 0)
            {
                reply.Kind = RadarReplyKind.Step;
            }
            else if (line == PingReply)
            {
                reply.Kind = RadarReplyKind.Ping;
            }
            else if (line.StartsWith("ERR"))
            {
                reply.Kind = RadarReplyKind.Error;
            }
            return reply;
        }

        private static bool TryAngle(string s, out int angle)
        {
            return TryNumber(s, AngleMax, out angle);
        }

        private static bool TryNumber(string s, int max, out int value)
        {
            return int.TryParse(s, out value) && value >= 0 && value <= max;
        }
    }
}
