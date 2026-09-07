using System;
using System.Drawing;
using System.IO.Ports;
using System.Windows.Forms;

namespace GeminiFlatPanel.Server
{
    internal sealed class SetupForm : Form
    {
        private readonly ComboBox ports = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 110 };
        private readonly NumericUpDown heater = new NumericUpDown { Minimum = 0, Maximum = 100, Width = 75 };
        private readonly Label status = new Label { AutoSize = true, MaximumSize = new Size(510, 0) };
        private readonly FlowLayoutPanel live = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Enabled = false };
        private bool attached;
        public SetupForm()
        {
            Text = "CCDASTRO Gemini FlatPanel Setup"; ClientSize = new Size(560, 490); Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
            var root = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true }; Controls.Add(root);
            ports.Items.AddRange(SerialPort.GetPortNames()); ports.Text = Settings.Load().Port; ports.Enabled = !Hardware.InUse;
            root.Controls.Add(Row(new Label { Text = "COM port", AutoSize = true }, ports, Button("Save port", SavePort)));
            root.Controls.Add(new Label { Text = "9600 baud / 8N1. Connection preserves device settings.", AutoSize = true });
            root.Controls.Add(Button("Connect controls / refresh", () => { if(!attached) { if(!Hardware.InUse) SavePort(); Hardware.Acquire(); attached = true; } live.Enabled = true; ports.Enabled = false; heater.Value = Hardware.ReadStatus().HeaterPercent; RefreshStatus(); }));
            live.Controls.Add(Row(new Label { Text = "Dew power (%)", AutoSize = true }, heater, Button("Apply", () => { Hardware.Heater((int)heater.Value); RefreshStatus(); })));
            live.Controls.Add(Button("Light off", () => Hardware.Brightness(0)));
            live.Controls.Add(Row(Button("Low brightness", () => Hardware.Mode(false)), Button("High brightness", () => Hardware.Mode(true))));
            live.Controls.Add(Row(Button("Beep off", () => Hardware.Beep(false)), Button("Beep on", () => Hardware.Beep(true))));
            live.Controls.Add(Row(Button("Open cover", () => { Hardware.Move(true); RefreshStatus(); }), Button("Close cover", () => { Hardware.Move(false); RefreshStatus(); }), Button("HALT", () => { Hardware.Halt(); RefreshStatus(); })));
            live.Controls.Add(new Label { Text = "After visually verifying a fully reached endpoint, remember its position.\nThis stores a local observation; it does not alter device calibration.", AutoSize = true });
            live.Controls.Add(Row(Button("Remember fully closed", () => Remember(false)), Button("Remember fully open", () => Remember(true))));
            live.Controls.Add(Button("Refresh state", RefreshStatus)); root.Controls.Add(live); root.Controls.Add(status);
            root.Controls.Add(Button("Done", Close));
            FormClosed += (s,e) => { if(attached) { Hardware.Release(); attached = false; } };
        }
        private static Control Row(params Control[] controls) { var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false }; row.Controls.AddRange(controls); return row; }
        private Button Button(string text, Action action)
        {
            var button = new Button { Text = text, AutoSize = true };
            button.Click += (s,e) => { try { UseWaitCursor = true; action(); } catch(Exception ex) { MessageBox.Show(this, ex.Message, "Gemini", MessageBoxButtons.OK, MessageBoxIcon.Error); } finally { UseWaitCursor = false; } };
            return button;
        }
        private void SavePort()
        {
            if(Hardware.InUse) throw new InvalidOperationException("Disconnect all Gemini clients before changing the port.");
            string value = ports.Text.Trim().ToUpperInvariant(); if(!System.Text.RegularExpressions.Regex.IsMatch(value, @"^COM[1-9]\d*$")) throw new ArgumentException("Enter a COM port such as COM9.");
            var settings = Settings.Load();
            if(settings.Port != value) { settings.ClosedPosition = null; settings.OpenPosition = null; }
            settings.Port = value; settings.Save(); status.Text = "Port saved.";
        }
        private void Remember(bool open)
        {
            if(MessageBox.Show(this, "Is the cover physically fully " + (open ? "open" : "closed") + " and stationary?", "Verify endpoint", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            Hardware.RememberEndpoint(open); RefreshStatus();
        }
        private void RefreshStatus()
        {
            status.Text = "Cover: " + Hardware.CoverState() + "; position: " + Hardware.ReadPosition() + "; heater: " + Hardware.ReadStatus().HeaterPercent + "%\n" + Hardware.EndpointSummary;
        }
    }
}
namespace ASCOM.LocalServer
{
    public sealed class FrmMain : Form
    {
        public FrmMain()
        {
            Text = "CCDASTRO Gemini Local Server"; ShowInTaskbar = false;
            var setup = new Button { Text = "Gemini setup", Dock = DockStyle.Fill };
            setup.Click += (s,e) => { using(var form = new GeminiFlatPanel.Server.SetupForm()) form.ShowDialog(); };
            Controls.Add(setup);
        }
    }
}

