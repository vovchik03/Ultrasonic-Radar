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

        // One pass over 40..70 takes ~2 s (~60 ms per degree), GET is sent after it
        private const int ScanPassMs = 3000;

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
                ErrorBox.Text = "no COM port selected";
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
                ErrorBox.Text = ex.Message;
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

        private async void TestButton_Click(object sender, RoutedEventArgs e)
        {
            if (!OpenPort())
            {
                return;
            }

            ErrorBox.Text = "";
            DataBox.Text = "";
            TestButton.IsEnabled = false;

            try
            {
                Send("SCAN 40 70");
                Send("STREAM ON");   // live points go to the P field
                await Task.Delay(ScanPassMs);
                Send("GET");         // the whole sector goes to the DATA field
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is TimeoutException)
            {
                ErrorBox.Text = ex.Message;
                ClosePort();
            }
            finally
            {
                TestButton.IsEnabled = true;
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
        /// Shows one reply line from the radar in the matching field (format: cmd.h).
        /// </summary>
        public void ShowReply(string line)
        {
            line = line.Trim();

            if (line.StartsWith("OK STREAM "))
            {
                StreamBox.Text = line.Substring("OK STREAM ".Length);
            }
            else if (line.StartsWith("P "))
            {
                string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 3)
                {
                    PointAngleBox.Text = parts[1];
                    PointDistanceBox.Text = parts[2];
                }
            }
            else if (line.StartsWith("DATA"))
            {
                DataBox.Text = line.Substring("DATA".Length).Trim();
            }
            else if (line.StartsWith("ERR"))
            {
                ErrorBox.Text = line.Substring("ERR".Length).Trim();
            }
        }
    }
}
