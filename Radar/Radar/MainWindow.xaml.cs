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
using Radar.Device;

namespace Radar
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // USART1 of the radar, 115200 8N1 (see setup.md)
        private const int BaudRate = 115200;

        // SCAN_ANGLE_MAX in the firmware
        private const int ScanAngleMax = 180;

        private SerialPort port;
        private readonly StringBuilder rxLine = new StringBuilder();
        private readonly RadarSimulator simulator = new RadarSimulator();

        public MainWindow()
        {
            InitializeComponent();
            RefreshPorts();
            simulator.LineReceived += ShowReply;
            Closed += (s, e) =>
            {
                ClosePort();
                simulator.Stop();
            };
        }

        private bool IsDemo => DemoBox.IsChecked == true;

        private void DemoBox_Checked(object sender, RoutedEventArgs e)
        {
            ClosePort();
            ResetRadarScreen();
            simulator.Start();
            ShowStatus("demo: simulated radar, press Start", false);
        }

        private void DemoBox_Unchecked(object sender, RoutedEventArgs e)
        {
            simulator.Stop();
            ResetRadarScreen();
            ShowStatus("", false);
        }

        /// <summary>Back to what the firmware has after power-up: idle, sector 0..180, nothing measured.</summary>
        private void ResetRadarScreen()
        {
            RadarScreen.SectorFrom = 0;
            RadarScreen.SectorTo = ScanAngleMax;
            RadarScreen.BeamAngle = double.NaN;
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
            if (IsDemo)
            {
                simulator.Send(command);
            }
            else
            {
                port.WriteLine(command);
            }
        }

        private void AngleBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Digits only, the range is checked on Start
            e.Handled = !e.Text.All(c => c >= '0' && c <= '9');
        }

        private static bool TryReadAngle(TextBox box, out int angle)
        {
            return int.TryParse(box.Text, out angle) && angle >= 0 && angle <= ScanAngleMax;
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            // The firmware accepts the angles in any order
            SendCommand(TryReadAngle(FromBox, out int from) && TryReadAngle(ToBox, out int to)
                ? $"SCAN {from} {to}"
                : null);
        }

        private void GoToButton_Click(object sender, RoutedEventArgs e)
        {
            SendCommand(TryReadAngle(GoToBox, out int target) ? $"GOTO {target}" : null);
        }

        private void GoToBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;   // otherwise the default button (Start) gets it
                GoToButton_Click(sender, e);
            }
        }

        /// <summary>Sends the command and turns streaming on. null = an angle is not 0..180.</summary>
        private void SendCommand(string command)
        {
            if (command == null)
            {
                ShowStatus($"angles must be 0..{ScanAngleMax}", true);
                return;
            }

            if (!IsDemo && !OpenPort())
            {
                return;
            }

            ShowStatus("", false);

            try
            {
                Send(command);
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
        /// Points go to the beam, every other line to the status text.
        /// </summary>
        private void ShowReply(string line)
        {
            line = line.Trim();

            // "P 45 523": the point just measured, 0 mm = no echo
            if (line.StartsWith("P "))
            {
                string[] point = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (point.Length == 3
                    && int.TryParse(point[1], out int angle) && angle >= 0 && angle <= ScanAngleMax
                    && ushort.TryParse(point[2], out ushort mm))
                {
                    RadarScreen.BeamAngle = angle;
                    RadarScreen.BeamDistanceMm = mm;
                }
                return;
            }

            ShowStatus(line, line.StartsWith("ERR"));

            // "OK SCAN 40 70": the sector as the firmware applied it (from <= to)
            if (line.StartsWith("OK SCAN "))
            {
                string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 4
                    && int.TryParse(parts[2], out int from) && from >= 0 && from <= ScanAngleMax
                    && int.TryParse(parts[3], out int to) && to >= 0 && to <= ScanAngleMax)
                {
                    RadarScreen.SectorFrom = from;
                    RadarScreen.SectorTo = to;
                }
            }

            // "OK GOTO 120": the target is shown as a single edge line, the beam walks to it and stays
            if (line.StartsWith("OK GOTO "))
            {
                string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 3
                    && int.TryParse(parts[2], out int target) && target >= 0 && target <= ScanAngleMax)
                {
                    RadarScreen.SectorFrom = target;
                    RadarScreen.SectorTo = target;
                }
            }
        }

        private void ShowStatus(string text, bool isError)
        {
            StatusText.Text = text;
            StatusText.Foreground = isError ? Brushes.Firebrick : Brushes.Gray;
        }
    }
}
