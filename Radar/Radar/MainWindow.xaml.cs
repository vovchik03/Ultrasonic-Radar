using System;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Radar.Device;

namespace Radar
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // The slider sends SPEED only after it stopped moving for this long
        private const int SpeedSendDelayMs = 300;

        private static readonly Brush LampOff = Frozen(Color.FromRgb(0xE2, 0x4B, 0x4A));
        private static readonly Brush LampOn = Frozen(Color.FromRgb(0x6C, 0xCB, 0x5F));

        private readonly RadarConnection connection = new RadarConnection();
        private readonly DispatcherTimer speedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SpeedSendDelayMs) };
        private bool showingSpeedReply;     // the slider is moved by OK SPEED, not by the user
        private string connectedPort;       // port of the current serial link, null = none or demo

        public MainWindow()
        {
            InitializeComponent();

            connection.ReplyReceived += ShowReply;
            connection.AliveChanged += alive => UpdateLinkLamp();
            speedTimer.Tick += SpeedTimer_Tick;
            RadarScreen.AngleClicked += RadarScreen_AngleClicked;
            RadarScreen.SectorSelected += RadarScreen_SectorSelected;
            Closed += (s, e) => connection.Disconnect();

            RefreshPorts();     // selects the first port, which connects to it
            UpdateLinkLamp();
        }

        private static Brush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private bool IsDemo => DemoBox.IsChecked == true;

        // ---- Connection --------------------------------------------------------------------

        /// <summary>Connects to the simulator or the selected port. Returns the error, or null.</summary>
        private string Reconnect()
        {
            ResetRadarScreen();
            connectedPort = null;

            if (IsDemo)
            {
                return connection.Connect(() =>
                {
                    var simulator = new RadarSimulator();
                    simulator.Start();
                    return simulator;
                });
            }

            string name = PortBox.SelectedItem as string;
            if (name == null)
            {
                connection.Disconnect();
                UpdateLinkLamp();
                return "no COM port selected";
            }
            connectedPort = name;
            return connection.Connect(() => new SerialRadarLink(name, Dispatcher));
        }

        private void UpdateLinkLamp()
        {
            bool alive = connection.IsAlive;
            Brush brush = alive ? LampOn : LampOff;
            LinkLamp.Fill = brush;
            LinkGlow.Color = ((SolidColorBrush)brush).Color;
            LinkText.Text = !alive ? "no link"
                          : IsDemo ? "demo"
                          : "connected " + PortBox.SelectedItem;
        }

        private void DemoBox_Checked(object sender, RoutedEventArgs e)
        {
            Reconnect();
            ShowStatus("demo: simulated radar, press Start", false);
        }

        private void DemoBox_Unchecked(object sender, RoutedEventArgs e)
        {
            ShowStatus(Reconnect() ?? "", false);
        }

        /// <summary>Back to what the firmware has after power-up: idle, sector 0..180, nothing measured.</summary>
        private void ResetRadarScreen()
        {
            RadarScreen.SectorFrom = 0;
            RadarScreen.SectorTo = RadarProtocol.AngleMax;
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
            // Choosing a port connects to it; refreshing the list keeps the same port selected
            string name = PortBox.SelectedItem as string;
            if (!IsDemo && name != null && name != connectedPort)
            {
                string error = Reconnect();
                if (error != null)
                {
                    ShowStatus(error, true);
                }
            }
        }

        // ---- Commands ----------------------------------------------------------------------

        private void AngleBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Digits only, the range is checked on Start
            e.Handled = !e.Text.All(c => c >= '0' && c <= '9');
        }

        private static bool TryReadAngle(TextBox box, out int angle)
        {
            return int.TryParse(box.Text, out angle) && angle >= 0 && angle <= RadarProtocol.AngleMax;
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            // The firmware accepts the angles in any order
            SendCommand(TryReadAngle(FromBox, out int from) && TryReadAngle(ToBox, out int to)
                ? RadarProtocol.Scan(from, to)
                : null);
        }

        private void GoToButton_Click(object sender, RoutedEventArgs e)
        {
            SendCommand(TryReadAngle(GoToBox, out int target) ? RadarProtocol.GoTo(target) : null);
        }

        private void GoToBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;   // otherwise the default button (Start) gets it
                GoToButton_Click(sender, e);
            }
        }

        // Click on the radar = GoTo that angle, drag = Start on that sector; the fields follow
        private void RadarScreen_AngleClicked(int angle)
        {
            GoToBox.Text = angle.ToString();
            SendCommand(RadarProtocol.GoTo(angle));
        }

        private void RadarScreen_SectorSelected(int from, int to)
        {
            FromBox.Text = from.ToString();
            ToBox.Text = to.ToString();
            SendCommand(RadarProtocol.Scan(from, to));
        }

        /// <summary>Sends the command with the slider speed and turns streaming on.
        /// null = an angle is not 0..180.</summary>
        private void SendCommand(string command)
        {
            if (command == null)
            {
                ShowStatus($"angles must be 0..{RadarProtocol.AngleMax}", true);
                return;
            }

            if (!connection.IsOpen)
            {
                string error = Reconnect();
                if (error != null)
                {
                    ShowStatus(error, true);
                    return;
                }
            }

            ShowStatus("", false);
            speedTimer.Stop();

            try
            {
                // The board may have been reset since the last SPEED/STEP, so they go with every command
                SendSpeedAndStep();
                connection.Send(command);
                connection.Send(RadarProtocol.StreamOn);   // then one "P <angle> <mm>" line per point
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is TimeoutException)
            {
                ShowStatus(ex.Message, true);
            }
        }

        // ---- Servo speed -------------------------------------------------------------------

        private int SliderSpeed => (int)Math.Round(SpeedSlider.Value);

        // Grid step that goes with the speed: slower servo = denser grid.
        // A separate step control can replace this rule later, everything else reads ScanStep.
        private int ScanStep => RadarProtocol.StepForSpeed(SliderSpeed);

        /// <summary>SPEED and the matching STEP, always together. Throws like RadarConnection.Send.</summary>
        private void SendSpeedAndStep()
        {
            connection.Send(RadarProtocol.SetSpeed(SliderSpeed));
            connection.Send(RadarProtocol.SetStep(ScanStep));
        }

        private void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (SpeedText == null)
            {
                return;     // still in InitializeComponent
            }

            SpeedText.Text = $"{SliderSpeed} °/s · step {ScanStep}°";

            if (!showingSpeedReply)
            {
                // Restart the delay: one SPEED when the user stops dragging, not one per pixel
                speedTimer.Stop();
                speedTimer.Start();
            }
        }

        private void SpeedTimer_Tick(object sender, EventArgs e)
        {
            speedTimer.Stop();
            if (!connection.IsOpen)
            {
                return;     // sent with the next command
            }

            try
            {
                SendSpeedAndStep();
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is TimeoutException)
            {
                ShowStatus(ex.Message, true);
            }
        }

        // ---- Replies -----------------------------------------------------------------------

        /// <summary>
        /// Shows one reply from the radar (format: cmd.h).
        /// Points go to the beam, every other line to the status text.
        /// </summary>
        private void ShowReply(RadarReply reply)
        {
            switch (reply.Kind)
            {
                case RadarReplyKind.Point:
                    RadarScreen.BeamAngle = reply.Angle;
                    RadarScreen.BeamDistanceMm = reply.DistanceMm;
                    return;

                case RadarReplyKind.Scan:
                    // The sector as the firmware applied it (from <= to)
                    RadarScreen.SectorFrom = reply.From;
                    RadarScreen.SectorTo = reply.To;
                    break;

                case RadarReplyKind.GoTo:
                    // The target is shown as a single edge line, the beam walks to it and stays
                    RadarScreen.SectorFrom = reply.Angle;
                    RadarScreen.SectorTo = reply.Angle;
                    break;

                case RadarReplyKind.Speed:
                    // What the firmware applied, without sending it back
                    if (!speedTimer.IsEnabled)
                    {
                        showingSpeedReply = true;
                        SpeedSlider.Value = reply.Speed;
                        showingSpeedReply = false;
                    }
                    break;
            }

            ShowStatus(reply.Line, reply.Kind == RadarReplyKind.Error);
        }

        private void ShowStatus(string text, bool isError)
        {
            StatusText.Text = text;
            StatusText.Foreground = isError ? Brushes.Firebrick : Brushes.Gray;
        }
    }
}
