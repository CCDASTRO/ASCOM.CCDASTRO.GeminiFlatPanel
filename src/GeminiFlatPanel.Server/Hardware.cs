using System;
using System.IO;
using System.IO.Ports;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using GeminiFlatPanel.Core;

namespace GeminiFlatPanel.Server
{
    internal static class Hardware
    {
        private static readonly object commandGate = new object(), stateGate = new object();
        private static ISerialChannel port;
        internal static Func<string, ISerialChannel> ChannelFactory = name => new SerialChannel(name);
        internal static int StartupDelayMilliseconds = 2000;
        internal static int HaltTimeoutMilliseconds = 3000;
        private static Thread reader;
        private static volatile bool stop;
        private static int clients;
        private static TaskCompletionSource<string> pending;
        private static char expected;
        private static Exception fault;
        private static char? outstandingMove; private static DeviceStatus cachedStatus; private static int cachedPosition, cachedBrightness;
        private static bool enabledAtZero;
        private static DateTime moveDeadline;
        private static DateTime brightnessReadAt, positionReadAt, statusReadAt;
        private static readonly bool[] commandReceipts = new bool[4];
        private static readonly TimeSpan PollCache = TimeSpan.FromMilliseconds(500);
        private static readonly CoverTracker cover = new CoverTracker();
        internal static bool InUse { get { lock(commandGate) return clients > 0; } }
        internal static bool Healthy { get { lock(stateGate) return fault == null && port != null && port.IsOpen; } }
        internal static string Firmware { get; private set; }
        internal static string EndpointSummary
        {
            get { lock(stateGate) return "State endpoints: closed " + (cover.ClosedPosition?.ToString() ?? "unknown") +
                ", open " + (cover.OpenPosition?.ToString() ?? "unknown") + "; tolerance " + cover.Tolerance + " raw counts."; }
        }
        internal static void Trace(string text)
        {
            try { Directory.CreateDirectory(Settings.DirectoryPath); File.AppendAllText(Path.Combine(Settings.DirectoryPath, "serial.log"), DateTime.UtcNow.ToString("O") + " " + text + Environment.NewLine); } catch { }
        }
        internal static void Acquire()
        {
            lock(commandGate)
            {
                if (clients > 0) { Ensure(); clients++; return; }
                var settings = Settings.Load();
                enabledAtZero = false;
                brightnessReadAt = positionReadAt = statusReadAt = DateTime.MinValue;
                Array.Clear(commandReceipts, 0, commandReceipts.Length);
                lock(stateGate) { fault = null; outstandingMove = null; cover.Reset(); cover.ClosedPosition = settings.ClosedPosition; cover.OpenPosition = settings.OpenPosition; cover.Tolerance = settings.Tolerance; }
                try
                {
                    port = ChannelFactory(settings.Port);
                    port.Open(); stop = false;
                    reader = new Thread(ReadLoop) { IsBackground = true, Name = "Gemini serial reader" }; reader.Start();
                    Thread.Sleep(StartupDelayMilliseconds);
                    string id = Query('H', GeminiProtocol.Identify);
                    if (id != "*HGeminiFlatPanelPro#" && id != "*HGeminiFlatPanel#") throw new ASCOM.DriverException("Unexpected device on " + settings.Port + ": " + id);
                    Firmware = Query('V', GeminiProtocol.Version);
                    var status = ReadStatus();
                    ConfigureEndpoints(settings, status);
                    RefreshBrightness();
                    int position = ReadPosition(); lock(stateGate) cover.ObservePosition(position);
                    clients = 1;
                }
                catch(Exception ex) { ClosePort(); throw new ASCOM.DriverException("Cannot connect Gemini on " + settings.Port + ". Check the port and disconnect other applications. " + ex.Message, ex); }
            }
        }
        internal static void Release()
        {
            lock(commandGate) { if (clients > 0 && --clients == 0) ClosePort(); }
        }
        private static void ConfigureEndpoints(Settings settings, DeviceStatus status)
        {
            lock(stateGate)
            {
                // Only use controller limits when they match the physically verified baseline.
                bool verified = settings.VerifiedClosedLimit == status.ClosedLimit &&
                    settings.VerifiedOpenLimit == status.OpenLimit && settings.VerifiedFirmware == Firmware;
                if (!verified)
                {
                    cover.ClosedPosition = cover.OpenPosition = null;
                    return;
                }
                cover.ClosedPosition = settings.ClosedPosition ?? status.ClosedLimit;
                cover.OpenPosition = settings.OpenPosition ?? status.OpenLimit;
                // V107 observed stops: closed 44 vs limit 52; open 812 vs limit 801.
                // Keep this fallback specific to the verified firmware; never infer from L1.
                cover.Tolerance = (!settings.ClosedPosition.HasValue || !settings.OpenPosition.HasValue) && Firmware == "*V107#"
                    ? Math.Max(settings.Tolerance, 12) : settings.Tolerance;
            }
        }
        private static void ClosePort()
        {
            stop = true;
            try { port?.Close(); } catch { }
            reader?.Join(1000);
            port?.Dispose(); port = null; reader = null;
            lock(stateGate) { pending?.TrySetException(new IOException("Connection closed.")); pending = null; cover.Reset(); outstandingMove = null; }
        }
        private static void Ensure()
        {
            lock(stateGate)
            {
                if (fault != null) throw new ASCOM.DriverException("Serial connection failed; disconnect all Gemini clients and reconnect. " + fault.Message, fault);
                if (port == null || !port.IsOpen) throw new ASCOM.NotConnectedException("Gemini is disconnected.");
            }
        }
        private static void ReadLoop()
        {
            var framer = new ReplyFramer();
            while(!stop)
            {
                try
                {
                    string chunk = ((char)port.ReadChar()).ToString();
                    foreach(string frame in framer.Feed(chunk))
                    {
                        Trace("RX " + frame);
                        lock(stateGate)
                        {
                            if (frame.Length > 3 && (frame[1] == 'O' || frame[1] == 'C') && int.TryParse(frame.Substring(2, frame.Length - 3), out int p))
                            {
                                if (outstandingMove == frame[1]) { outstandingMove = null; cachedPosition = p; cover.Complete(frame[1], p); Monitor.PulseAll(stateGate); }
                            }
                            if (pending != null && frame.Length > 2 && frame[1] == expected) pending.TrySetResult(frame);
                        }
                    }
                }
                catch(TimeoutException) { lock(stateGate) { cover.CheckTimeout(DateTime.UtcNow); if (outstandingMove.HasValue && DateTime.UtcNow >= moveDeadline) { fault = new TimeoutException("Cover completion missing; reconnect before another move."); pending?.TrySetException(fault); } } }
                catch(Exception ex) { if (!stop) lock(stateGate) { fault = ex; cover.Fail(); pending?.TrySetException(ex); } return; }
            }
        }
        private static void Write(string command)
        {
            Ensure(); Trace("TX " + command.TrimEnd('\n')); port.Write(command);
        }
        private static string Query(char reply, string command)
        {
            lock(commandGate)
            {
                Ensure(); var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                lock(stateGate) { pending = completion; expected = reply; }
                try
                {
                    Write(command);
                    if (!completion.Task.Wait(3000)) throw new TimeoutException("No reply to " + command.Trim());
                    return completion.Task.GetAwaiter().GetResult();
                }
                catch(Exception ex) { lock(stateGate) { fault = ex; cover.Fail(); } throw new ASCOM.DriverException("Gemini request failed: " + ex.Message, ex); }
                finally { lock(stateGate) pending = null; }
            }
        }
        private static int Number(string frame) => int.Parse(frame.Substring(2, frame.Length - 3), CultureInfo.InvariantCulture);
        internal static DeviceStatus ReadStatus(bool force = true)
        {
            lock(commandGate)
            {
                Ensure();
                lock(stateGate) if(outstandingMove.HasValue) return cachedStatus;
                if (!force && DateTime.UtcNow - statusReadAt < PollCache) return cachedStatus;
                cachedStatus = DeviceStatus.Parse(Query('S', GeminiProtocol.Status));
                statusReadAt = DateTime.UtcNow;
                return cachedStatus;
            }
        }
        // These values acknowledge a command sent by this server, not device state.
        // NINA requires a stable readback matching SetSwitchValue before it completes.
        internal static bool CommandReceipt(short id)
        {
            lock(commandGate) { Ensure(); return commandReceipts[id]; }
        }
        internal static void SetCommandSwitch(short id, bool value)
        {
            lock(commandGate)
            {
                Ensure();
                switch(id)
                {
                    case 1: Mode(value); break;
                    case 2: Beep(value); break;
                    case 3: if(value) Halt(); break;
                    default: throw new ArgumentOutOfRangeException(nameof(id));
                }
                // Update only after a successful write. Stop OFF clears its indicator.
                commandReceipts[id] = value;
            }
        }
        internal static int ReadPosition(bool force = true)
        {
            lock(commandGate)
            {
                Ensure();
                lock(stateGate) if(outstandingMove.HasValue) return cachedPosition;
                if (!force && DateTime.UtcNow - positionReadAt < PollCache) return cachedPosition;
                cachedPosition = Number(Query('G', GeminiProtocol.Position));
                positionReadAt = DateTime.UtcNow;
                return cachedPosition;
            }
        }
        internal static int RefreshBrightness(bool force = true)
        {
            lock(commandGate)
            {
                Ensure();
                lock(stateGate) if(outstandingMove.HasValue) return cachedBrightness;
                if (!force && DateTime.UtcNow - brightnessReadAt < PollCache) return cachedBrightness;
                int n = Number(Query('J', GeminiProtocol.Brightness));
                if(n < 0 || n > 255)
                {
                    Trace("Invalid J brightness " + n + "; trying D readback.");
                    n = Number(Query('D', ">D#\n"));
                    if(n < 0 || n > 255)
                    {
                        // A confirmed off state has effective brightness zero; never clamp an unknown on value.
                        n = ReadStatus().LightOn ? -1 : 0;
                        Trace("Brightness unresolved; effective value " + n);
                    }
                }
                brightnessReadAt = DateTime.UtcNow;
                if(n > 0) enabledAtZero = false;
                return cachedBrightness = n;
            }
        }
        internal static int ReadBrightness(bool force = true)
        {
            int n = RefreshBrightness(force);
            if(n < 0) throw new ASCOM.InvalidOperationException("Panel reports an invalid brightness. Use Light off in driver setup or set a brightness explicitly.");
            return n;
        }
        internal static ASCOM.DeviceInterface.CalibratorStatus CalibratorState()
        {
            lock(commandGate)
            {
                int value = RefreshBrightness(false);
                if(value < 0) return ASCOM.DeviceInterface.CalibratorStatus.Unknown;
                return value > 0 || enabledAtZero ? ASCOM.DeviceInterface.CalibratorStatus.Ready : ASCOM.DeviceInterface.CalibratorStatus.Off;
            }
        }
        internal static void Brightness(int value, bool enabled = false)
        {
            string command = GeminiProtocol.SetBrightness(value);
            lock(commandGate)
            {
                RequireStationary();
                // Reproduce the observed manufacturer sequence. L's internal side effects remain unverified.
                Number(value == 0 ? Query('D', ">D#\n") : Query('L', ">L#\n"));
                // Captures show roughly 240 ms between the preparatory reply and the brightness write.
                Thread.Sleep(250);
                string response = Query('B', command);
                if(Number(response) != value) throw new ASCOM.DriverException("Brightness acknowledgment mismatch.");
                cachedBrightness = value;
                // B0 emits no light in either case. Preserve the ASCOM distinction
                // between CalibratorOn(0) and an explicit CalibratorOff request.
                enabledAtZero = enabled && value == 0;
                brightnessReadAt = DateTime.UtcNow;
            }
        }
        internal static void Heater(int value)
        {
            lock(commandGate)
            {
                RequireStationary(); Write(GeminiProtocol.SetHeater(value));
                if(ReadStatus().HeaterPercent != value) throw new ASCOM.DriverException("Heater readback did not match requested power.");
            }
        }
        internal static void Mode(bool high) { lock(commandGate) { RequireStationary(); Write(GeminiProtocol.SetHighBrightnessMode(high)); commandReceipts[1] = high; } }
        internal static void Beep(bool enabled) { lock(commandGate) { RequireStationary(); Write(GeminiProtocol.SetBeep(enabled)); commandReceipts[2] = enabled; } }
        private static void RequireStationary() { Ensure(); lock(stateGate) if(outstandingMove.HasValue) throw new ASCOM.InvalidOperationException("Wait for cover movement to complete before changing outputs."); } internal static void Move(bool open)
        {
            if(Settings.Load().MotionSafetyLock) throw new ASCOM.InvalidOperationException("Cover movement disabled pending calibration-reset investigation. HALT remains available.");
            lock(commandGate)
            {
                Ensure();
                RequireStationary();
                CheckMotionBaseline();
                positionReadAt = brightnessReadAt = DateTime.MinValue;
                lock(stateGate) { if(outstandingMove.HasValue) throw new ASCOM.InvalidOperationException("Wait for the previous cover operation to finish."); cover.Begin(open, DateTime.UtcNow); outstandingMove = open ? 'O' : 'C'; moveDeadline = DateTime.UtcNow.AddSeconds(60); }
                try { Write(open ? GeminiProtocol.OpenCover : GeminiProtocol.CloseCover); }
                catch { lock(stateGate) { cover.Fail(); outstandingMove = null; } throw; }
            }
        }
        private static void CheckMotionBaseline()
        {
            var settings = Settings.Load();
            if(settings.MotionSafetyLock || !settings.VerifiedClosedLimit.HasValue || !settings.VerifiedOpenLimit.HasValue)
                throw new ASCOM.InvalidOperationException("Movement locked: controller calibration has not been explicitly verified.");
            var actual = ReadStatus(); // Fresh query under commandGate immediately before every movement.
            if(actual.ClosedLimit != settings.VerifiedClosedLimit.Value || actual.OpenLimit != settings.VerifiedOpenLimit.Value || Firmware != settings.VerifiedFirmware)
            {
                settings.MotionSafetyLock = true;
                settings.ClosedPosition = null; settings.OpenPosition = null;
                settings.Save();
                lock(stateGate) { cover.Reset(); cover.ClosedPosition = null; cover.OpenPosition = null; }
                Trace("MOTION BLOCKED: controller calibration or firmware changed.");
                throw new ASCOM.InvalidOperationException("Movement blocked: controller limits changed. Expected " + settings.VerifiedClosedLimit + "/" + settings.VerifiedOpenLimit + ", received " + actual.ClosedLimit + "/" + actual.OpenLimit + ". Inspect Gemini calibration. No motion command sent.");
            }
        }
        internal static void VerifyMotionBaseline(int expectedClosed, int expectedOpen)
        {
            lock(commandGate)
            {
                RequireStationary();
                if(expectedClosed < 0 || expectedOpen < 0 || expectedClosed == expectedOpen) throw new ArgumentException("Enter distinct nonnegative raw limits verified against Gemini calibration.");
                var actual = ReadStatus();
                if(actual.ClosedLimit != expectedClosed || actual.OpenLimit != expectedOpen) throw new ASCOM.InvalidOperationException("Entered limits do not match controller readback; movement remains locked.");
                var settings = Settings.Load();
                settings.VerifiedClosedLimit = expectedClosed; settings.VerifiedOpenLimit = expectedOpen; settings.VerifiedFirmware = Firmware;
                // Recording a baseline does not remove the investigation lock.
                settings.Save();
                Trace("Verified baseline recorded; motion investigation lock remains " + settings.MotionSafetyLock);
            }
        }
        internal static void Halt()
        {
            lock(commandGate)
            {
                Ensure();
                lock(stateGate) if(outstandingMove.HasValue) cover.Halt();
                try
                {
                    Write(GeminiProtocol.HaltCover);
                    var timer = System.Diagnostics.Stopwatch.StartNew();
                    int? minimum = null, maximum = null;
                    int stableSamples = 0;
                    // V107 captures show stopped readback alternating 453/455.
                    // Bound the entire sample window, not each adjacent difference.
                    int positionSpread = Firmware == "*V107#" ? 2 : 1;
                    while(true)
                    {
                        lock(stateGate)
                        {
                            if(fault != null) throw new ASCOM.DriverException("HALT could not be confirmed.", fault);
                            if(!outstandingMove.HasValue) break;
                            // Allow a normal completion reply, while keeping serial output serialized.
                            Monitor.Wait(stateGate, 200);
                            if(fault != null) throw new ASCOM.DriverException("HALT could not be confirmed.", fault);
                            if(!outstandingMove.HasValue) break;
                        }
                        if(timer.ElapsedMilliseconds >= HaltTimeoutMilliseconds)
                            throw new TimeoutException("Cover position did not settle after HALT. Reconnect before another operation.");
                        // V107 may stop without an O/C completion. Query G directly: ReadPosition
                        // intentionally returns cached data while an operation is outstanding.
                        int position = Number(Query('G', GeminiProtocol.Position));
                        if(position < 0) throw new FormatException("Invalid position after HALT.");
                        int low = minimum.HasValue ? Math.Min(minimum.Value, position) : position;
                        int high = maximum.HasValue ? Math.Max(maximum.Value, position) : position;
                        if((long)high - low > positionSpread) { minimum = maximum = position; stableSamples = 1; }
                        else { minimum = low; maximum = high; stableSamples++; }
                        if(stableSamples >= 4)
                        {
                            lock(stateGate) { outstandingMove = null; cachedPosition = position; }
                            Trace("HALT confirmed by four fresh positions within " + positionSpread + " counts, sampled at least 200 ms apart.");
                            break;
                        }
                    }
                    positionReadAt = brightnessReadAt = DateTime.MinValue;
                }
                catch(Exception ex)
                {
                    lock(stateGate) { fault = ex; cover.Fail(); }
                    throw new ASCOM.DriverException("HALT failed: " + ex.Message, ex);
                }
            }
        }
        internal static PhysicalCoverState CoverState()
        {
            Ensure(); lock(stateGate) { cover.CheckTimeout(DateTime.UtcNow); if(outstandingMove.HasValue) return cover.State == PhysicalCoverState.Error ? PhysicalCoverState.Error : PhysicalCoverState.Moving; if(cover.IsMoving) return cover.State; }
            int position = ReadPosition(false); lock(stateGate) { cover.ObservePosition(position); return cover.State; }
        }
        internal static void RememberEndpoint(bool open)
        {
            lock(commandGate)
            {
                lock(stateGate) if(outstandingMove.HasValue) throw new ASCOM.InvalidOperationException("Wait until movement completes.");
                int position = ReadPosition(); var settings = Settings.Load();
                if(open) settings.OpenPosition = position; else settings.ClosedPosition = position;
                if(settings.OpenPosition.HasValue && settings.ClosedPosition.HasValue && Math.Abs(settings.OpenPosition.Value - settings.ClosedPosition.Value) <= 2 * settings.Tolerance) throw new ASCOM.InvalidOperationException("Endpoints overlap. Verify the physical cover position.");
                settings.Save();
                ConfigureEndpoints(settings, ReadStatus());
                lock(stateGate) { cover.Reset(); cover.ObservePosition(position); }
            }
        }
    }
}







