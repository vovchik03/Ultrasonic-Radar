using System;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Windows.Threading;

namespace Radar.Device
{
    /// <summary>
    /// The radar on a COM port: USART1 through a USB-UART adapter, 115200 8N1 (see setup.md).
    /// </summary>
    internal sealed class SerialRadarLink : IRadarLink
    {
        private const int BaudRate = 115200;

        private readonly SerialPort port;
        private readonly Dispatcher dispatcher;
        private readonly StringBuilder rxLine = new StringBuilder();

        /// <summary>Opens the port. Throws IOException, UnauthorizedAccessException
        /// or ArgumentException when it can not be opened.</summary>
        public SerialRadarLink(string portName, Dispatcher dispatcher)
        {
            this.dispatcher = dispatcher;
            port = new SerialPort(portName, BaudRate, Parity.None, 8, StopBits.One)
            {
                NewLine = "\r\n",
                WriteTimeout = 500
            };
            port.DataReceived += Port_DataReceived;
            port.Open();
        }

        public event Action<string> LineReceived;

        public string PortName => port.PortName;

        public void Send(string command)
        {
            port.WriteLine(command);
        }

        public void Dispose()
        {
            port.DataReceived -= Port_DataReceived;
            try
            {
                port.Close();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // The adapter was unplugged, nothing left to close
            }
        }

        // Runs on a thread pool thread: collects bytes into lines, raises them on the UI thread
        private void Port_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            string chunk;
            try
            {
                chunk = port.ReadExisting();
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidOperationException)
            {
                return;
            }

            foreach (char c in chunk)
            {
                if (c == '\n')
                {
                    string line = rxLine.ToString();
                    rxLine.Clear();
                    // BeginInvoke, not Invoke: Invoke can deadlock with SerialPort.Close()
                    dispatcher.BeginInvoke(new Action(() => LineReceived?.Invoke(line)));
                }
                else if (c != '\r')
                {
                    rxLine.Append(c);
                }
            }
        }
    }
}
