using System;
using System.IO;
using System.Windows.Threading;

namespace Radar.Device
{
    /// <summary>
    /// Keeps the link to the radar and tells whether the radar answers.
    ///
    /// Alive = some line came from the radar within AliveTimeoutMs. When the radar is
    /// quiet (idle, streaming off) a PING is sent every HeartbeatMs. Any reply counts,
    /// so firmware without PING (it answers ERR UNKNOWN) still shows as alive.
    /// A link that failed (adapter unplugged) is reopened every ReconnectMs.
    /// </summary>
    internal sealed class RadarConnection
    {
        private const int TickMs = 250;
        private const int HeartbeatMs = 1000;
        private const int AliveTimeoutMs = 2500;
        private const int ReconnectMs = 2000;

        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TickMs) };

        private Func<IRadarLink> open;      // how to (re)open the link, null = disconnected on purpose
        private IRadarLink link;
        private DateTime lastReceived = DateTime.MinValue;
        private DateTime lastPing = DateTime.MinValue;
        private DateTime lastOpenAttempt = DateTime.MinValue;
        private bool pingPending;

        public RadarConnection()
        {
            timer.Tick += (s, e) => Tick();
        }

        /// <summary>Every reply except the answers to PING.</summary>
        public event Action<RadarReply> ReplyReceived;

        /// <summary>The radar started or stopped answering.</summary>
        public event Action<bool> AliveChanged;

        public bool IsAlive { get; private set; }

        public bool IsOpen => link != null;

        /// <summary>
        /// Switches to a new link and keeps it open: if it fails now or later, it is
        /// reopened every ReconnectMs. Returns the error of the first attempt, or null.
        /// </summary>
        public string Connect(Func<IRadarLink> openLink)
        {
            Disconnect();
            open = openLink;
            timer.Start();
            return TryOpen();
        }

        public void Disconnect()
        {
            open = null;
            timer.Stop();
            CloseLink();
        }

        /// <summary>Sends one command. Throws when there is no link or it just broke.</summary>
        public void Send(string command)
        {
            if (link == null)
            {
                throw new InvalidOperationException("radar is not connected");
            }

            try
            {
                link.Send(command);
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is TimeoutException)
            {
                CloseLink();
                throw;
            }
        }

        private string TryOpen()
        {
            lastOpenAttempt = DateTime.UtcNow;
            try
            {
                link = open();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                return ex.Message;
            }

            link.LineReceived += Link_LineReceived;
            lastReceived = DateTime.MinValue;
            pingPending = false;
            Tick();     // first PING right away, the lamp does not wait a second
            return null;
        }

        private void CloseLink()
        {
            if (link != null)
            {
                link.LineReceived -= Link_LineReceived;
                link.Dispose();
                link = null;
            }
            SetAlive(false);
        }

        private void Link_LineReceived(string line)
        {
            lastReceived = DateTime.UtcNow;
            SetAlive(true);

            RadarReply reply = RadarProtocol.Parse(line);

            // Answer to our PING; old firmware without PING answers ERR UNKNOWN
            if (pingPending && (reply.Kind == RadarReplyKind.Ping || reply.Line == "ERR UNKNOWN"))
            {
                pingPending = false;
                return;
            }

            ReplyReceived?.Invoke(reply);
        }

        private void Tick()
        {
            DateTime now = DateTime.UtcNow;

            if (link == null)
            {
                if (open != null && (now - lastOpenAttempt).TotalMilliseconds >= ReconnectMs)
                {
                    TryOpen();
                }
                return;
            }

            if (IsAlive && (now - lastReceived).TotalMilliseconds > AliveTimeoutMs)
            {
                SetAlive(false);
            }

            if ((now - lastReceived).TotalMilliseconds >= HeartbeatMs
                && (now - lastPing).TotalMilliseconds >= HeartbeatMs)
            {
                try
                {
                    link.Send(RadarProtocol.PingCommand);
                    lastPing = now;
                    pingPending = true;
                }
                catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is TimeoutException)
                {
                    CloseLink();    // reopened on a later tick
                }
            }
        }

        private void SetAlive(bool alive)
        {
            if (IsAlive != alive)
            {
                IsAlive = alive;
                AliveChanged?.Invoke(alive);
            }
        }
    }
}
