using System;
using System.Linq;
using System.Text;
using System.Windows.Threading;

namespace Radar.Device
{
    /// <summary>
    /// Stands in for the radar when no board is connected: takes the same text commands
    /// and answers with the same lines as the firmware (see cmd.h), sweeping a made-up room.
    /// The scanning logic mirrors scan.c.
    /// Runs on the UI thread, LineReceived is raised from a DispatcherTimer.
    /// </summary>
    internal sealed class RadarSimulator
    {
        private const int AngleMax = 180;       // SCAN_ANGLE_MAX
        private const int StepDeg = 9;          // SCAN_STEP_DEG
        private const int StepMs = 80;          // one 9° step takes 64..108 ms (scan.c)
        private const int RangeMaxMm = 4000;    // HC-SR04: farther = no echo
        private const int NoiseMm = 15;

        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(StepMs) };
        private readonly Random random = new Random();
        private readonly ushort[] distances = new ushort[AngleMax + 1];

        private int from;
        private int to;
        private int angle;          // where the servo is, the next point is measured here
        private int direction;
        private bool gotoMode;      // GOTO: one-way to target, then hold
        private int target;
        private bool scanning;
        private bool stream;

        public RadarSimulator()
        {
            timer.Tick += (s, e) => Step();
        }

        /// <summary>One reply line, without CR LF.</summary>
        public event Action<string> LineReceived;

        /// <summary>Power-up: idle at 90° with streaming off, waits for a command, as the firmware does.</summary>
        public void Start()
        {
            angle = 90;
            stream = false;
            from = 0;
            to = AngleMax;
            gotoMode = false;
            scanning = false;
            Array.Clear(distances, 0, distances.Length);
            timer.Start();
        }

        /// <summary>Power-off: no more lines.</summary>
        public void Stop()
        {
            timer.Stop();
        }

        /// <summary>Takes one command line, the reply comes through LineReceived.</summary>
        public void Send(string command)
        {
            string[] words = command.Trim().ToUpperInvariant()
                                    .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            string name = words.Length > 0 ? words[0] : "";
            string args = name.Length > 0 ? command.Trim().Substring(name.Length) : "";

            switch (name)
            {
                case "SCAN":
                    Scan(args);
                    break;

                case "GOTO":
                    GoTo(args);
                    break;

                case "STOP":
                    scanning = false;
                    Reply("OK STOP");
                    break;

                case "GET":
                    // Same grid as the scanner: from, from + STEP, ..., to
                    var data = new StringBuilder("DATA");
                    for (int a = from; ; a = Math.Min(a + StepDeg, to))
                    {
                        data.Append(' ').Append(a).Append(':').Append(distances[a]);
                        if (a >= to)
                        {
                            break;
                        }
                    }
                    Reply(data.ToString());
                    break;

                case "STREAM" when words.Length == 2 && (words[1] == "ON" || words[1] == "OFF"):
                    stream = words[1] == "ON";
                    Reply("OK STREAM " + words[1]);
                    break;

                case "STREAM":
                    Reply("ERR ARGS");
                    break;

                default:
                    Reply("ERR UNKNOWN");
                    break;
            }
        }

        private void Scan(string args)
        {
            // "40 70", "40-70" or "40,70"
            string[] parts = args.Split(new[] { ' ', '\t', '-', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || !parts.All(IsNumber))
            {
                Reply("ERR ARGS");
                return;
            }

            int a = int.Parse(parts[0]);
            int b = int.Parse(parts[1]);
            if (a > AngleMax || b > AngleMax)
            {
                Reply("ERR RANGE");
                return;
            }

            StartScan(a, b);
            Reply($"OK SCAN {from} {to}");
        }

        private void GoTo(string args)
        {
            string arg = args.Trim();
            if (!IsNumber(arg))
            {
                Reply("ERR ARGS");
                return;
            }

            int a = int.Parse(arg);
            if (a > AngleMax)
            {
                Reply("ERR RANGE");
                return;
            }

            // The current angle is a sector edge, so the sweep starts right here
            // and heads towards the other edge, the target
            StartScan(angle, a);
            target = a;
            gotoMode = true;
            Reply($"OK GOTO {a}");
        }

        /// <summary>Up to 3 decimal digits, as Cmd_ParseNumber() takes.</summary>
        private static bool IsNumber(string s)
        {
            return s.Length > 0 && s.Length <= 3 && s.All(c => c >= '0' && c <= '9');
        }

        /// <summary>Scan_Start(): clears the map, starts from the sector edge closest to the servo.</summary>
        private void StartScan(int a, int b)
        {
            from = Math.Min(a, b);
            to = Math.Max(a, b);
            gotoMode = false;
            scanning = true;
            Array.Clear(distances, 0, distances.Length);

            if (angle - from <= to - angle)
            {
                direction = 1;
                angle = from;
            }
            else
            {
                direction = -1;
                angle = to;
            }
        }

        /// <summary>Scan_Update(): measure where the servo is, report, move to the next angle.</summary>
        private void Step()
        {
            if (!scanning)
            {
                return;
            }

            distances[angle] = Measure(angle);
            if (stream)
            {
                Reply($"P {angle} {distances[angle]}");
            }
            angle = NextAngle();
        }

        /// <summary>Scan_NextAngle()</summary>
        private int NextAngle()
        {
            if (from == to)
            {
                return from;
            }

            if (gotoMode)
            {
                if (angle == target)
                {
                    return target;
                }
                return direction > 0 ? GridUp(angle) : GridDown(angle);
            }

            // Bounce off the sector edges
            if (angle >= to)
            {
                direction = -1;
            }
            else if (angle <= from)
            {
                direction = 1;
            }
            return direction > 0 ? GridUp(angle) : GridDown(angle);
        }

        /// <summary>Grid point above the angle, the last step is shortened to land on "to".</summary>
        private int GridUp(int a)
        {
            return Math.Min(a + StepDeg, to);
        }

        /// <summary>Grid point below the angle (from, from + STEP, ...), a > from.</summary>
        private int GridDown(int a)
        {
            return from + ((a - from - 1) / StepDeg) * StepDeg;
        }

        /// <summary>The made-up room: a wall 2.8 m ahead with a door in it, a box and a post.</summary>
        private ushort Measure(int deg)
        {
            double mm;
            if (deg >= 12 && deg <= 20)
            {
                mm = 2000;                                          // cabinet on the right
            }
            else if (deg >= 40 && deg <= 55)
            {
                mm = 1200;                                          // box
            }
            else if (deg >= 100 && deg <= 106)
            {
                mm = 800;                                           // post
            }
            else if (deg >= 115 && deg <= 125)
            {
                return 0;                                           // open door: no echo
            }
            else
            {
                mm = 2800 / Math.Sin(deg * Math.PI / 180);          // flat wall, farther at the sides
            }

            mm += random.Next(-NoiseMm, NoiseMm + 1);
            return mm > RangeMaxMm ? (ushort)0 : (ushort)mm;
        }

        private void Reply(string line)
        {
            LineReceived?.Invoke(line);
        }
    }
}
