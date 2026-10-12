using System;
using System.Linq;
using System.Text;
using System.Windows.Threading;

namespace Radar.Device
{
    /// <summary>
    /// Stands in for the radar when no board is connected: takes the same text commands
    /// and answers with the same lines as the firmware (see cmd.h), sweeping a made-up room.
    /// Runs on the UI thread, LineReceived is raised from a DispatcherTimer.
    /// </summary>
    internal sealed class RadarSimulator
    {
        private const int AngleMax = 180;       // SCAN_ANGLE_MAX
        private const int StepMs = 60;          // ~60 ms per degree, as in setup.md
        private const int RangeMaxMm = 4000;    // HC-SR04: farther = no echo
        private const int NoiseMm = 15;

        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(StepMs) };
        private readonly Random random = new Random();
        private readonly ushort[] distances = new ushort[AngleMax + 1];

        private int from;
        private int to;
        private int angle;
        private int direction;
        private bool scanning;
        private bool stream;

        public RadarSimulator()
        {
            timer.Tick += (s, e) => Step();
        }

        /// <summary>One reply line, without CR LF.</summary>
        public event Action<string> LineReceived;

        /// <summary>Power-up: sweeps 0..180 with streaming off, as the firmware does.</summary>
        public void Start()
        {
            from = 0;
            to = AngleMax;
            angle = 90;
            direction = 1;
            scanning = true;
            stream = false;
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

            switch (name)
            {
                case "SCAN":
                    Scan(command.Trim().Substring(name.Length));
                    break;

                case "STOP":
                    scanning = false;
                    Reply("OK STOP");
                    break;

                case "GET":
                    var data = new StringBuilder("DATA");
                    for (int a = from; a <= to; a++)
                    {
                        data.Append(' ').Append(a).Append(':').Append(distances[a]);
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
            // "40 70", "40-70" or "40,70", up to 3 digits each
            string[] parts = args.Split(new[] { ' ', '\t', '-', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || !parts.All(p => p.Length <= 3 && p.All(c => c >= '0' && c <= '9')))
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

            from = Math.Min(a, b);
            to = Math.Max(a, b);
            angle = Math.Min(Math.Max(angle, from), to);
            scanning = true;
            Array.Clear(distances, 0, distances.Length);
            Reply($"OK SCAN {from} {to}");
        }

        private void Step()
        {
            if (!scanning)
            {
                return;
            }

            // Back and forth between the sector edges, 1° per step
            if (angle + direction > to || angle + direction < from)
            {
                direction = -direction;
            }
            angle = Math.Min(Math.Max(angle + direction, from), to);

            distances[angle] = Measure(angle);
            if (stream)
            {
                Reply($"P {angle} {distances[angle]}");
            }
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
