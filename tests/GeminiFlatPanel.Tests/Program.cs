using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.IO;
using System.Threading;
using GeminiFlatPanel.Core;
using GeminiFlatPanel.Server;

class Program
{
    static int assertions;
    static void Check(bool condition, string message) { assertions++; if(!condition) throw new Exception(message); }
    static void Throws(Action action, string message) { try { action(); } catch { assertions++; return; } throw new Exception(message); }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            if(args.Length > 0 && args[0] == "--render-ui")
            {
                Settings.TestDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-settings");
                System.Windows.Forms.Application.EnableVisualStyles();
                using(var form = new SetupForm())
                {
                    form.ShowInTaskbar = false; form.StartPosition = System.Windows.Forms.FormStartPosition.Manual; form.Location = new System.Drawing.Point(-32000, -32000); form.Show(); System.Windows.Forms.Application.DoEvents(); form.PerformLayout();
                    using(var bitmap = new System.Drawing.Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                        bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"setup-preview.png"));
                    }
                }
                return 0;
            }
            var framer = new ReplyFramer();
            Check(!framer.Feed("noise*HGeminiFlat").Any(), "partial reply emitted");
            Check(framer.Feed("PanelPro#*J100#").SequenceEqual(new[]{"*HGeminiFlatPanelPro#","*J100#"}), "split/coalesced replies");
            Check(framer.Feed("*bad*J0#").Single() == "*J0#", "resynchronization");
            Throws(() => framer.Feed("*" + new string('x', 257)).ToArray(), "oversized frame accepted");
            Check(DeviceStatus.Parse("*S0M1L2C30D37C333O#").HeaterPercent == 30, "heater field");
            Check(DeviceStatus.Parse("*S0M1L2C30D37C333O#").LightOn, "light field");
            Throws(() => DeviceStatus.Parse("*S0M0L2C130D37C333O#"), "invalid heater accepted");
            Check(GeminiProtocol.CloseCover == ">COOO#\n", "close framing");
            Throws(() => GeminiProtocol.SetHeater(101), "heater bounds");
            Throws(() => GeminiProtocol.SetBrightness(-1), "brightness bounds");
            var tracker = new CoverTracker { ClosedPosition = 48, OpenPosition = 343 };
            tracker.ObservePosition(238); Check(tracker.State == PhysicalCoverState.Unknown, "midway must be unknown");
            tracker.Begin(false, DateTime.UtcNow); tracker.Halt(); tracker.Complete('C', 239); tracker.ObservePosition(238);
            Check(tracker.State == PhysicalCoverState.Unknown, "late completion after halt");
            tracker.Begin(true, DateTime.UtcNow); tracker.Complete('C', 48); Check(tracker.IsMoving, "wrong reply ended move");
            tracker.Complete('O',343); Check(tracker.State == PhysicalCoverState.Open, "verified open");
            tracker.Begin(false, DateTime.UtcNow.AddSeconds(-61)); tracker.CheckTimeout(DateTime.UtcNow); Check(tracker.State == PhysicalCoverState.Error,"movement timeout");
            Settings.TestDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-settings");
            new Settings { MotionSafetyLock = false, VerifiedClosedLimit = 37, VerifiedOpenLimit = 333, VerifiedFirmware = "*V103#", ClosedPosition = 48, OpenPosition = 343 }.Save();
            var channel = new FakeChannel(); Hardware.ChannelFactory = name => channel; Hardware.StartupDelayMilliseconds = 0;
            channel.InvalidJ = true; Hardware.Acquire(); Hardware.Acquire();
            Check(Hardware.ReadBrightness() == 0, "D fallback for invalid startup J");
            channel.InvalidD = true; Check(Hardware.ReadBrightness() == 0, "off state with invalid readbacks");
            channel.Brightness = 100; Check(Hardware.RefreshBrightness() == -1, "invalid on brightness must remain unknown");
            Check(Hardware.Healthy, "invalid value should not disconnect device");
            Hardware.Brightness(0); channel.InvalidJ = false; channel.InvalidD = false;
            Check(channel.Opens == 1, "clients did not share physical connection");
            Check(channel.Commands.All(c => !c.StartsWith(">W") && !c.StartsWith(">Y") && !c.StartsWith(">T") && !c.StartsWith(">X")), "connect changed configuration");
            Hardware.Heater(30); Check(Hardware.ReadStatus().HeaterPercent == 30, "heater verification");
            Hardware.Brightness(100); Check(channel.Commands.Skip(channel.Commands.Count - 2).SequenceEqual(new[] { ">L#\n", ">B100#\n" }), "light-on preparation missing"); Check(Hardware.ReadBrightness() == 100, "brightness verification");
            var lockedSettings = Settings.Load(); lockedSettings.MotionSafetyLock = true; lockedSettings.Save();
            int commandCount = channel.Commands.Count; Throws(() => Hardware.Move(false), "safety lock allowed motion"); Check(channel.Commands.Count == commandCount, "safety lock transmitted a command"); lockedSettings.MotionSafetyLock = false; lockedSettings.Save();
            Hardware.Move(false); Check(Hardware.CoverState() == PhysicalCoverState.Moving, "asynchronous movement");
            Throws(() => Hardware.Heater(20), "output allowed during unverified overlap");
            using(var controls = new Switch())
            {
                controls.Connected = true;
                Throws(() => controls.SetSwitch(1, true), "mode command allowed during motion");
                Throws(() => controls.SetSwitch(2, true), "beep command allowed during motion");
                Check(!controls.GetSwitch(1) && !controls.GetSwitch(2), "rejected command acknowledged");
                controls.SetSwitch(3, true);
                Check(channel.Commands.Last() == ">K#\n", "Switch HALT blocked during motion");
                Check(Hardware.CoverState() == PhysicalCoverState.Unknown, "HALT returned before stop completion");
                Hardware.Brightness(63);
                Check(Hardware.ReadBrightness() == 63, "Conform brightness immediately after HALT blocked");
            }
            Hardware.Halt(); channel.Emit("*C239#"); Thread.Sleep(50);
            Check(Hardware.CoverState() == PhysicalCoverState.Unknown, "halt misreported closed");
            Hardware.Move(true); channel.Position = 343; channel.Emit("*O343#"); Thread.Sleep(50);
            Check(Hardware.CoverState() == PhysicalCoverState.Open, "open completion");
            Hardware.Halt();
            Check(Hardware.CoverState() == PhysicalCoverState.Open, "idle HALT lost known endpoint");
            Hardware.Move(false); channel.SuppressHaltReply = true; channel.HaltSent.Reset();
            var haltTask = System.Threading.Tasks.Task.Run(() => Hardware.Halt());
            Check(channel.HaltSent.Wait(1000), "HALT was not transmitted");
            Check(Hardware.CoverState() == PhysicalCoverState.Moving, "unconfirmed HALT reported stationary");
            channel.Emit("*O239#"); Thread.Sleep(50);
            Check(!haltTask.IsCompleted, "wrong-direction completion confirmed HALT");
            channel.Emit("*C239#");
            Check(haltTask.Wait(2000), "matching completion did not release HALT");
            Check(Hardware.CoverState() == PhysicalCoverState.Unknown, "midway stop became endpoint");
            Hardware.Brightness(0);
            Hardware.Move(true); channel.Emit("*O343#"); Thread.Sleep(50);
            Check(Hardware.CoverState() == PhysicalCoverState.Open, "move after confirmed stop blocked");
            channel.SuppressHaltReply = false;
            channel.ClosedLimit = 0; channel.OpenLimit = 3000;
            int beforeGuard = channel.Commands.Count;
            Throws(() => Hardware.Move(false), "changed calibration allowed motion");
            Check(!channel.Commands.Skip(beforeGuard).Any(c => c == ">O#\n" || c == ">COOO#\n"), "changed calibration sent motion");
            Check(Settings.Load().MotionSafetyLock, "calibration mismatch did not latch lock");
            channel.ClosedLimit = 37; channel.OpenLimit = 333;
            Throws(() => Hardware.Move(true), "latched lock cleared automatically");
            Hardware.Halt(); Check(channel.Commands.Last() == ">K#\n", "HALT unavailable with motion lock");
            Hardware.Release(); Check(channel.IsOpen, "first disconnect closed shared port");
            Hardware.Release(); Check(!channel.IsOpen && channel.Heater == 30, "disconnect changed heater or leaked port");
            var haltSettings = Settings.Load(); haltSettings.MotionSafetyLock = false; haltSettings.Save();
            Hardware.Acquire(); Hardware.Move(false);
            channel.SuppressHaltReply = true; Hardware.HaltTimeoutMilliseconds = 100;
            Throws(() => Hardware.Halt(), "missing HALT completion accepted");
            Check(!Hardware.Healthy, "unconfirmed stop did not invalidate connection");
            Throws(() => Hardware.Brightness(10), "output allowed after unconfirmed stop");
            Throws(() => Hardware.Move(true), "movement allowed after unconfirmed stop");
            Hardware.Release(); channel.SuppressHaltReply = false; Hardware.HaltTimeoutMilliseconds = 3000;
            Hardware.Acquire(); Hardware.Move(false);
            channel.SuppressHaltReply = true; channel.Position = 239;
            Hardware.Halt();
            Check(Hardware.CoverState() == PhysicalCoverState.Unknown, "silent HALT did not remain unknown mid-travel");
            Hardware.Brightness(63); Check(Hardware.ReadBrightness() == 63, "silent HALT blocked next brightness command");
            Hardware.Move(true); channel.Position = 333; channel.Emit("*O333#"); Thread.Sleep(50);
            Check(Hardware.CoverState() == PhysicalCoverState.Open, "movement after stable-position HALT blocked");
            Hardware.Move(false); channel.PositionStep = 10; Hardware.HaltTimeoutMilliseconds = 1100;
            Throws(() => Hardware.Halt(), "still-changing position accepted as stopped");
            Check(!Hardware.Healthy, "unsettled HALT left connection usable");
            Hardware.Release(); channel.PositionStep = 0; Hardware.HaltTimeoutMilliseconds = 3000;
            Hardware.Acquire(); Hardware.Move(false); channel.SuppressG = true;
            Throws(() => Hardware.Halt(), "missing position readback accepted as stopped");
            Check(!Hardware.Healthy, "missing position readback left connection usable");
            Hardware.Release(); channel.SuppressG = false; channel.SuppressHaltReply = false;
            haltSettings.MotionSafetyLock = true; haltSettings.Save();
            Hardware.Acquire(); channel.SuppressJ = true;
            Throws(() => Hardware.ReadBrightness(), "missing reply did not time out");
            Check(!Hardware.Healthy, "timeout did not invalidate connection"); Hardware.Release();
            channel.SuppressJ = false; channel.Identity = "*HGeminiFlatPanel#";
            using(var panel = new CoverCalibrator())
            using(var dew = new Switch())
            {
                panel.Connected = true; dew.Connected = true;
                dew.SetSwitchValue(0, 20); Check(dew.GetSwitchValue(0) == 20, "Switch interface readback");
                int beforeHeaterPoll = channel.Commands.Count;
                for(int i = 0; i < 10; i++) Check(dew.GetSwitchValue(0) == 20, "cached heater readback");
                Check(channel.Commands.Count == beforeHeaterPoll, "duplicate heater queries");
                Thread.Sleep(550); channel.Heater = 21;
                Check(dew.GetSwitchValue(0) == 21, "heater cache never expired");
                Throws(() => dew.SetSwitchValue(4, 1), "invalid switch ID accepted");
                Throws(() => dew.SetSwitchValue(-1, 1), "negative switch ID accepted");
                Throws(() => dew.SetSwitchValue(1, 20), "invalid command value accepted");
                Check(dew.MaxSwitch == 4, "missing command switches");
                int beforeCommands = channel.Commands.Count;
                for(short id = 1; id < 4; id++)
                {
                    Check(dew.MinSwitchValue(id) == 0 && dew.MaxSwitchValue(id) == 1 && dew.SwitchStep(id) == 1, "command range");
                    dew.SetSwitch(id, false);
                    Check(!dew.GetSwitch(id), "command switch claimed device state");
                }
                Check(channel.Commands.Skip(beforeCommands).SequenceEqual(new[] { ">Y0#\n", ">T0#\n" }), "OFF must set low brightness and disable beep"); beforeCommands = channel.Commands.Count;
                for(short id = 1; id < 4; id++)
                {
                    dew.SetSwitch(id, true);
                    Check(dew.GetSwitchValue(id) == 1 && dew.GetSwitch(id), "NINA target readback would time out");
                }
                Check(channel.Commands.Skip(beforeCommands).SequenceEqual(new[] { ">Y1#\n", ">T1#\n", ">K#\n" }), "command switch mapping");
                Thread.Sleep(550);
                int beforeReceiptPoll = channel.Commands.Count;
                using(var otherSwitch = new Switch())
                {
                    otherSwitch.Connected = true;
                    for(short id = 1; id < 4; id++)
                    {
                        Check(otherSwitch.GetSwitchValue(id) == 1, "command receipt expired or was not shared");
                        dew.SetSwitchValue(id, 0);
                        Check(otherSwitch.GetSwitchValue(id) == 0, "cleared receipt did not match NINA target");
                    }
                }
                Check(channel.Commands.Skip(beforeReceiptPoll).SequenceEqual(new[] { ">Y0#\n", ">T0#\n" }), "OFF command mapping or stop reset failed"); beforeReceiptPoll = channel.Commands.Count;
                dew.SetSwitchValue(2, 1); dew.SetSwitchValue(2, 1);
                Check(channel.Commands.Skip(beforeReceiptPoll).SequenceEqual(new[] { ">T1#\n", ">T1#\n" }), "explicit repeated command suppressed");
                channel.FailCommand = ">Y1#\n";
                Throws(() => dew.SetSwitchValue(1, 1), "write failure swallowed");
                Check(dew.GetSwitchValue(1) == 0, "failed write acknowledged");
                channel.FailCommand = null;
                Hardware.Mode(true); Check(dew.GetSwitch(1), "setup high mode not shared");
                Hardware.Mode(false); Check(!dew.GetSwitch(1), "setup low mode not shared");
                Hardware.Beep(false); Check(!dew.GetSwitch(2), "setup beep not shared");
                Throws(() => dew.SetSwitchValue(0, double.NaN), "NaN accepted");
                foreach(short id in new short[] {0, 1, 2, 3})
                {
                    foreach(double invalid in new[] {double.PositiveInfinity, double.NegativeInfinity, -0.01, id == 0 ? 100.01 : 1.01})
                        Throws(() => dew.SetSwitchValue(id, invalid), "out-of-range input rounded into range");
                    foreach(double fraction in new[] {0.25, 0.499999, 0.5, 0.75})
                    {
                        dew.SetSwitchValue(id, fraction);
                        Check(dew.GetSwitchValue(id) == (fraction < 0.5 ? 0 : 1), "fractional switch rounding/readback mismatch");
                    }
                }
                dew.SetSwitchValue(0, 10.5); Check(dew.GetSwitchValue(0) == 11, "midpoint rounded to even");
                dew.SetSwitchValue(0, 99.75); Check(dew.GetSwitchValue(0) == 100, "upper-bound rounding failed");
                Throws(() => panel.CalibratorOn(256), "invalid ASCOM brightness accepted");
                panel.CalibratorOn(0);
                Check(panel.Brightness == 0 && panel.CalibratorState == ASCOM.DeviceInterface.CalibratorStatus.Ready, "CalibratorOn(0) must be Ready");
                Thread.Sleep(550);
                Check(panel.CalibratorState == ASCOM.DeviceInterface.CalibratorStatus.Ready, "zero-enabled state lost on fresh readback");
                using(var secondPanel = new CoverCalibrator())
                {
                    secondPanel.Connected = true;
                    Check(secondPanel.CalibratorState == ASCOM.DeviceInterface.CalibratorStatus.Ready, "zero-enabled state not shared across clients");
                    secondPanel.CalibratorOff();
                    Check(panel.CalibratorState == ASCOM.DeviceInterface.CalibratorStatus.Off, "CalibratorOff did not clear shared enabled state");
                }
                panel.CalibratorOn(0); Hardware.Brightness(0);
                Check(panel.CalibratorState == ASCOM.DeviceInterface.CalibratorStatus.Off, "setup light-off did not clear enabled state");
                panel.CalibratorOn(0);
                channel.FailCommand = ">B100#\n";
                Throws(() => panel.CalibratorOn(100), "failed brightness write accepted");
                Check(panel.CalibratorState == ASCOM.DeviceInterface.CalibratorStatus.Unknown, "faulted calibrator reported ready");
                channel.FailCommand = null;
                panel.Connected = false; dew.Connected = false;
                panel.Connected = true; dew.Connected = true;
                Check(panel.CalibratorState == ASCOM.DeviceInterface.CalibratorStatus.Off, "reconnect retained zero-enabled state");
                panel.CalibratorOn(10); Check(panel.Brightness == 10, "CoverCalibrator brightness");
                int beforePoll = channel.Commands.Count;
                for(int i = 0; i < 10; i++) { Check(panel.Brightness == 10, "cached brightness"); Check(panel.CalibratorState == ASCOM.DeviceInterface.CalibratorStatus.Ready, "cached light state"); }
                Check(channel.Commands.Count == beforePoll, "duplicate brightness queries");
                Thread.Sleep(550); channel.Brightness = 20;
                Check(panel.Brightness == 20, "brightness cache never expired");
                panel.Connected = false; Check(dew.Connected, "panel disconnect broke Switch");
                IntPtr dispatch = System.Runtime.InteropServices.Marshal.GetIDispatchForObject(dew);
                Check(dispatch != IntPtr.Zero, "Switch COM dispatch unavailable"); System.Runtime.InteropServices.Marshal.Release(dispatch);
                dispatch = System.Runtime.InteropServices.Marshal.GetIDispatchForObject(panel);
                Check(dispatch != IntPtr.Zero, "CoverCalibrator COM dispatch unavailable"); System.Runtime.InteropServices.Marshal.Release(dispatch);
            }
            Check(!channel.IsOpen, "driver disposal leaked shared connection");
            using(var controls = new Switch())
            {
                controls.Connected = true;
                for(short id = 1; id < 4; id++) Check(!controls.GetSwitch(id), "receipt survived physical reconnect");
            }
            new Settings { VerifiedClosedLimit = 52, VerifiedOpenLimit = 801, VerifiedFirmware = "*V107#" }.Save();
            channel.Firmware = "*V107#"; channel.ClosedLimit = 52; channel.OpenLimit = 801; channel.Position = 44;
            using(var panel = new CoverCalibrator())
            {
                int beforeConnect = channel.Commands.Count;
                panel.Connected = true;
                Check(panel.CoverState == ASCOM.DeviceInterface.CoverStatus.Closed, "V107 closed startup at 44");
                Check(channel.Commands.Skip(beforeConnect).All(c => c == ">H#\n" || c == ">V#\n" || c == ">S#\n" || c == ">J#\n" || c == ">G#\n"), "startup changed device configuration");
                int beforePoll = channel.Commands.Count;
                for(int i = 0; i < 10; i++) Check(panel.CoverState == ASCOM.DeviceInterface.CoverStatus.Closed, "cached cover state");
                Check(channel.Commands.Count == beforePoll, "duplicate position queries");
                channel.Position = 400; Thread.Sleep(550);
                Check(panel.CoverState == ASCOM.DeviceInterface.CoverStatus.Unknown, "L1 midway classified as closed");
                panel.Connected = false; channel.Position = 812; panel.Connected = true;
                Check(panel.CoverState == ASCOM.DeviceInterface.CoverStatus.Open, "V107 open startup at 812");
                Throws(() => panel.CloseCover(), "state recognition removed motion lock");
                panel.Connected = false; channel.OpenLimit = 900; panel.Connected = true;
                Check(panel.CoverState == ASCOM.DeviceInterface.CoverStatus.Unknown, "changed limits reused endpoint baseline");
                panel.Connected = false; channel.OpenLimit = 801; channel.Firmware = "*V108#"; panel.Connected = true;
                Check(panel.CoverState == ASCOM.DeviceInterface.CoverStatus.Unknown, "changed firmware reused endpoint baseline");
            }
            new Settings { MotionSafetyLock = false, VerifiedClosedLimit = 52, VerifiedOpenLimit = 801, VerifiedFirmware = "*V107#" }.Save();
            channel.Firmware = "*V107#"; channel.Position = 44;
            Hardware.Acquire(); Hardware.Move(true); channel.SuppressHaltReply = true;
            channel.PositionReadings = new Queue<int>(new[] {453, 455, 453, 455});
            int beforeStop = channel.Commands.Count;
            Hardware.Halt();
            Check(channel.Commands.Skip(beforeStop).Count(c => c == ">G#\n") == 4, "V107 stop confirmation must use four fresh samples");
            Check(Hardware.CoverState() == PhysicalCoverState.Unknown, "two-count jitter misclassified stopped cover");
            Hardware.Brightness(0);
            Hardware.Move(false); channel.PositionReadings = null; channel.PositionStep = 1; Hardware.HaltTimeoutMilliseconds = 1300;
            Throws(() => Hardware.Halt(), "one-count-per-sample drift accepted as stopped");
            Check(!Hardware.Healthy, "slow drift did not invalidate connection");
            Hardware.Release(); channel.PositionStep = 0; Hardware.HaltTimeoutMilliseconds = 3000;
            channel.SuppressHaltReply = false;
            int offIndex = channel.Commands.LastIndexOf(">B0#\n"); Check(offIndex > 0 && channel.Commands[offIndex - 1] == ">D#\n", "light-off preparation missing");
            ExternalSwitchTests.Run(Check, Throws);
            Console.WriteLine("PASS: " + assertions + " assertions; no physical serial port opened."); return 0;
        }
        catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
sealed class FakeChannel : ISerialChannel
{
    readonly BlockingCollection<char> input = new BlockingCollection<char>();
    public readonly List<string> Commands = new List<string>();
    public bool IsOpen {get; private set;}
    public int Opens, Heater, Brightness, Position = 343; public int ClosedLimit = 37, OpenLimit = 333;
    public bool InvalidJ, InvalidD; public bool SuppressJ; public string Identity = "*HGeminiFlatPanelPro#", Firmware = "*V103#";
    public string FailCommand;
    public bool SuppressHaltReply;
    public bool SuppressG;
    public int PositionStep;
    public Queue<int> PositionReadings;
    public readonly ManualResetEventSlim HaltSent = new ManualResetEventSlim();
    private char? pendingMove;
    private readonly object emitGate = new object();
    public void Open() { IsOpen = true; Opens++; }
    public void Close() { IsOpen = false; }
    public void Dispose() { }
    public int ReadChar() { if(input.TryTake(out char c, 20)) return c; throw new TimeoutException(); }
    public void Emit(string frame) { lock(emitGate) { if(frame.StartsWith("*O") || frame.StartsWith("*C")) pendingMove = null; foreach(char c in frame) input.Add(c); } }
    public void Write(string text)
    {
        if(text == FailCommand) throw new IOException("Simulated serial write failure");
        Commands.Add(text);
        if(text == ">K#\n") HaltSent.Set();
        if(text == ">O#\n") pendingMove = 'O';
        else if(text == ">COOO#\n") pendingMove = 'C';
        else if(text == ">K#\n" && pendingMove.HasValue && !SuppressHaltReply)
        {
            char operation = pendingMove.Value;
            pendingMove = null;
            ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(100); Emit("*" + operation + Position + "#"); });
        }
        if(text == ">H#\n") Emit(Identity);
        else if(text == ">V#\n") Emit(Firmware);
        else if(text == ">S#\n") Emit("*S0M" + (Brightness > 0 ? 1 : 0) + "L1C" + Heater + "D" + ClosedLimit + "C" + OpenLimit + "O#");
        else if(text == ">G#\n") { Position = PositionReadings != null && PositionReadings.Count > 0 ? PositionReadings.Dequeue() : Position + PositionStep; if(!SuppressG) Emit("*G" + Position + "#"); }
        else if(text == ">J#\n") { if(!SuppressJ) Emit("*J" + (InvalidJ ? 1281 : Brightness) + "#"); }
        else if(text == ">D#\n") Emit("*D" + (InvalidD ? 1281 : Brightness) + "#");
        else if(text == ">L#\n") Emit("*L" + Brightness + "#");
        else if(text.StartsWith(">B")) { Brightness = int.Parse(text.Substring(2).Trim('#','\n')); Emit("*B" + Brightness + "#"); }
        else if(text.StartsWith(">W")) Heater = int.Parse(text.Substring(2).Trim('#','\n'));
    }
}
