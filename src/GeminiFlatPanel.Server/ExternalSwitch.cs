using System;

namespace GeminiFlatPanel.Server
{
    // One vendor connection shared by all Gemini Switch clients. CoverCalibrator
    // never acquires this connection. Calls are serialized with lifecycle changes.
    internal static class ExternalSwitch
    {
        private static readonly object gate = new object();
        private static IExternalSwitch device;
        private static int clients;
        private static short count;
        private static bool connecting;
        internal static Func<string, IExternalSwitch> Factory = id => new AscomExternalSwitch(id);
        internal static bool InUse { get { lock(gate) return clients > 0 || connecting; } }
        internal static short Count
        {
            get
            {
                lock(gate)
                {
                    if(clients > 0) return count;
                    var settings = Settings.Load();
                    if(string.IsNullOrWhiteSpace(settings.ExternalSwitchProgId)) return 0;
                    if(settings.ExternalSwitchCount < 0 || settings.ExternalSwitchCount > short.MaxValue - 4)
                        throw new ASCOM.DriverException("Invalid saved external channel count. Disable and reselect the external Switch driver in setup.");
                    return settings.ExternalSwitchCount;
                }
            }
        }

        internal static void Configure(string progId)
        {
            lock(gate)
            {
                if(clients > 0 || connecting) throw new InvalidOperationException("Disconnect all Gemini Switch clients before changing the external driver.");
                progId = (progId ?? "").Trim();
                ValidateDriver(progId);
                var settings = Settings.Load();
                if(!string.Equals(settings.ExternalSwitchProgId, progId, StringComparison.OrdinalIgnoreCase)) settings.ExternalSwitchCount = 0;
                settings.ExternalSwitchProgId = progId;
                settings.Save();
            }
        }

        private static void ValidateDriver(string progId)
        {
            if(progId.StartsWith("ASCOM.CCDASTRO.GeminiFlatPanel.", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Select the SVBONY Switch driver, not a Gemini driver. A combined driver cannot select itself.");
        }

        internal static void Acquire()
        {
            lock(gate)
            {
                if(connecting) throw new ASCOM.DriverException("Recursive external Switch connection. Select SVBONY directly, not another combined driver.");
                if(clients > 0)
                {
                    if(device != null && !device.Connected) throw new ASCOM.NotConnectedException("External Switch disconnected. Disconnect and reconnect all Gemini Switch clients.");
                    clients++;
                    return;
                }
                var settings = Settings.Load();
                string progId = settings.ExternalSwitchProgId ?? "";
                ValidateDriver(progId);
                if(string.IsNullOrWhiteSpace(progId)) { count = 0; clients = 1; return; }
                IExternalSwitch candidate = null;
                connecting = true;
                try
                {
                    candidate = Factory(progId);
                    if(string.Equals(progId, "ASCOM.SVBONY.Switch", StringComparison.OrdinalIgnoreCase))
                        candidate = new PercentageExternalSwitch(candidate);
                    candidate.Connected = true;
                    if(!candidate.Connected) throw new ASCOM.NotConnectedException("The external driver did not connect.");
                    short discovered = candidate.MaxSwitch;
                    if(discovered < 1 || discovered > short.MaxValue - 4) throw new ASCOM.DriverException("Invalid external Switch channel count.");
                    settings.ExternalSwitchCount = discovered;
                    settings.Save();
                    count = discovered;
                    device = candidate;
                    clients = 1;
                }
                catch(Exception ex)
                {
                    Close(candidate);
                    throw new ASCOM.DriverException("Cannot connect external Switch '" + progId + "'. Check its setup and USB connection. " + ex.Message, ex);
                }
                finally { connecting = false; }
            }
        }

        internal static void Release()
        {
            lock(gate)
            {
                if(clients == 0 || --clients != 0) return;
                var previous = device;
                device = null;
                count = 0;
                Close(previous);
            }
        }

        private static void Close(IExternalSwitch previous)
        {
            if(previous == null) return;
            try { previous.Connected = false; } catch(Exception ex) { Hardware.Trace("External Switch disconnect: " + ex.Message); }
            try { previous.Dispose(); } catch(Exception ex) { Hardware.Trace("External Switch dispose: " + ex.Message); }
        }

        internal static T Call<T>(short id, Func<IExternalSwitch, short, T> action)
        {
            lock(gate)
            {
                if(device == null || !device.Connected) throw new ASCOM.NotConnectedException("SVBONY/external Switch is unavailable. Disconnect and reconnect all Gemini Switch clients; Gemini cover and light remain independent.");
                if(device.MaxSwitch != count) throw new ASCOM.DriverException("External channel count changed. Reconnect and review sequence channel assignments.");
                short local = (short)(id - 4);
                if(local < 0 || local >= count) throw new ASCOM.InvalidValueException(nameof(id), id.ToString(), "External channel ID");
                return action(device, local);
            }
        }

        internal static void Call(short id, Action<IExternalSwitch, short> action)
            => Call(id, (driver, local) => { action(driver, local); return true; });
    }

    internal interface IExternalSwitch : IDisposable
    {
        bool Connected { get; set; }
        short MaxSwitch { get; }
        bool CanWrite(short id);
        string GetSwitchName(short id);
        void SetSwitchName(short id, string name);
        string GetSwitchDescription(short id);
        bool GetSwitch(short id);
        void SetSwitch(short id, bool state);
        double MinSwitchValue(short id);
        double MaxSwitchValue(short id);
        double SwitchStep(short id);
        double GetSwitchValue(short id);
        void SetSwitchValue(short id, double value);
    }

    // SVBONY channel 13 is voltage; only 14 and 15 are heater PWM.
    // IDs remain reliable when the user customizes channel names.
    internal sealed class PercentageExternalSwitch : IExternalSwitch
    {
        private readonly IExternalSwitch driver;
        internal PercentageExternalSwitch(IExternalSwitch driver) { this.driver = driver; }
        private bool IsPwm(short id) => driver.MaxSwitch == 17 && (id == 14 || id == 15)
            && (driver.MinSwitchValue(id) == 0 || (id == 14 && driver.MinSwitchValue(id) == -1)) && driver.MaxSwitchValue(id) > 100 && driver.SwitchStep(id) == 1;
        public bool Connected { get => driver.Connected; set => driver.Connected = value; }
        public short MaxSwitch => driver.MaxSwitch;
        public bool CanWrite(short id) => driver.CanWrite(id);
        public string GetSwitchName(short id) => IsPwm(id) ? "Dew heater " + (id - 13) + " / " + driver.GetSwitchName(id) + " (%)" : driver.GetSwitchName(id);
        public void SetSwitchName(short id, string name) => driver.SetSwitchName(id, name);
        public string GetSwitchDescription(short id) => IsPwm(id) ? "SVBONY " + (id == 14 ? "automatic" : "manual") + " heater control: 0-100%. 0 = off; 100 = maximum. Converted to vendor PWM units." : driver.GetSwitchDescription(id);
        public bool GetSwitch(short id) => IsPwm(id) ? driver.GetSwitchValue(id) > 0 : driver.GetSwitch(id);
        public void SetSwitch(short id, bool state)
        {
            if(IsPwm(id)) SetSwitchValue(id, state ? 100 : 0);
            else driver.SetSwitch(id, state);
        }
        public double MinSwitchValue(short id) => IsPwm(id) ? 0 : driver.MinSwitchValue(id);
        public double MaxSwitchValue(short id) => IsPwm(id) ? 100 : driver.MaxSwitchValue(id);
        public double SwitchStep(short id) => IsPwm(id) ? 1 : driver.SwitchStep(id);
        public double GetSwitchValue(short id) => IsPwm(id)
            ? Math.Round(driver.GetSwitchValue(id) * 100 / driver.MaxSwitchValue(id), MidpointRounding.AwayFromZero)
            : driver.GetSwitchValue(id);
        public void SetSwitchValue(short id, double value)
        {
            if(IsPwm(id))
            {
                if(double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > 100)
                    throw new ASCOM.InvalidValueException(nameof(value), value.ToString(), "0 to 100 percent");
                value = Math.Round(Math.Round(value, MidpointRounding.AwayFromZero)
                    * driver.MaxSwitchValue(id) / 100, MidpointRounding.AwayFromZero);
            }
            driver.SetSwitchValue(id, value);
        }
        public void Dispose() => driver.Dispose();
    }

    internal sealed class AscomExternalSwitch : IExternalSwitch
    {
        private readonly ASCOM.DriverAccess.Switch driver;
        internal AscomExternalSwitch(string progId) { driver = new ASCOM.DriverAccess.Switch(progId); }
        public bool Connected { get => driver.Connected; set => driver.Connected = value; }
        public short MaxSwitch => driver.MaxSwitch;
        public bool CanWrite(short id) => driver.CanWrite(id);
        public string GetSwitchName(short id) => driver.GetSwitchName(id);
        public void SetSwitchName(short id, string name) => driver.SetSwitchName(id, name);
        public string GetSwitchDescription(short id) => driver.GetSwitchDescription(id);
        public bool GetSwitch(short id) => driver.GetSwitch(id);
        public void SetSwitch(short id, bool state) => driver.SetSwitch(id, state);
        public double MinSwitchValue(short id) => driver.MinSwitchValue(id);
        public double MaxSwitchValue(short id) => driver.MaxSwitchValue(id);
        public double SwitchStep(short id) => driver.SwitchStep(id);
        public double GetSwitchValue(short id) => driver.GetSwitchValue(id);
        public void SetSwitchValue(short id, double value) => driver.SetSwitchValue(id, value);
        public void Dispose() => driver.Dispose();
    }
}
