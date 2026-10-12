using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Radar
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // USART1 of the radar, 115200 8N1 (see setup.md)
        private const int BaudRate = 115200;

        private SerialPort port;
        private readonly StringBuilder rxLine = new StringBuilder();

        public MainWindow()
        {
            InitializeComponent();
            RefreshPorts();
            Closed += (s, e) => ClosePort();
        }

        private void RefreshPorts()
        {
            string[] names = SerialPort.GetPortNames();
            if (PortBox.ItemsSource is string[] current && current.SequenceEqual(names))
            {
                return;
            }

            object selected = PortBox.SelectedItem;
            PortBox.ItemsSource = names;
            PortBox.SelectedItem = selected;
            if (PortBox.SelectedIndex < 0 && PortBox.Items.Count > 0)
            {
                PortBox.SelectedIndex = 0;
            }
        }

        private void PortBox_DropDownOpened(object sender, EventArgs e)
        {
            RefreshPorts();
        }

        private void PortBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Reopened with the new name on the next command
            if (port != null && port.PortName != PortBox.SelectedItem as string)
            {
                ClosePort();
            }
        }

        private bool OpenPort()
        {
            if (port != null && port.IsOpen)
            {
                return true;
            }

            string name = PortBox.SelectedItem as string;
            if (name == null)
            {
                ShowStatus("no COM port selected", true);
                return false;
            }

            try
            {
                port = new SerialPort(name, BaudRate, Parity.None, 8, StopBits.One)
                {
                    NewLine = "\r\n",
                    WriteTimeout = 500
                };
                port.DataReceived += Port_DataReceived;
                port.Open();
                rxLine.Clear();
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                ShowStatus(ex.Message, true);
                port = null;
                return false;
            }
        }

        private void ClosePort()
        {
            if (port != null)
            {
                port.DataReceived -= Port_DataReceived;
                port.Close();
                port = null;
            }
        }

        private void Send(string command)
        {
            port.WriteLine(command);
        }

        private void TestButton_Click(object sender, RoutedEventArgs e)
        {
            if (!OpenPort())
            {
                return;
            }

            ShowStatus("", false);

            try
            {
                Send("SCAN 40 70");
                Send("STREAM ON");   // then one "P <angle> <mm>" line per point
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is TimeoutException)
            {
                ShowStatus(ex.Message, true);
                ClosePort();
            }
        }

        // Runs on a thread pool thread: collects bytes into lines, shows them on the UI thread
        private void Port_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            string chunk;
            try
            {
                chunk = ((SerialPort)sender).ReadExisting();
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
                    Dispatcher.BeginInvoke(new Action(() => ShowReply(line)));
                }
                else if (c != '\r')
                {
                    rxLine.Append(c);
                }
            }
        }

        /// <summary>
        /// Shows one reply line from the radar (format: cmd.h).
        /// Until the beam is drawn on the radar screen, the last line is shown as text.
        /// </summary>
        private void ShowReply(string line)
        {
            line = line.Trim();
            ShowStatus(line, line.StartsWith("ERR"));
        }

        private void ShowStatus(string text, bool isError)
        {
            StatusText.Text = text;
            StatusText.Foreground = isError ? Brushes.Firebrick : Brushes.Gray;
        }
    }
}
