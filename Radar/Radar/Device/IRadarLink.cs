using System;

namespace Radar.Device
{
    /// <summary>
    /// Text line transport to the radar: the COM port or the simulator.
    /// Commands and replies are the lines described in cmd.h, without CR LF.
    /// </summary>
    internal interface IRadarLink : IDisposable
    {
        /// <summary>One reply line from the radar, raised on the UI thread.</summary>
        event Action<string> LineReceived;

        /// <summary>Sends one command line. Throws IOException, InvalidOperationException
        /// or TimeoutException when the link is broken.</summary>
        void Send(string command);
    }
}
