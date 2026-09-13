using ASCOM;
using System;
using System.Collections;
using System.Runtime.InteropServices;
using ASCOM.DeviceInterface;
using ASCOM.Utilities;

namespace GeminiFlatPanel.Server
{
    [ComVisible(false)]
    public abstract class DriverBase : ASCOM.LocalServer.ReferenceCountedObjectBase, IDisposable
    {
        private bool connected, disposed;
        private readonly object connectionGate = new object();
        protected virtual void AcquireAdditional() { }
        protected virtual void ReleaseAdditional() { }
        public bool Connected
        {
            get => connected && Hardware.Healthy;
            set
            {
                lock(connectionGate) {
                if(disposed) throw new ObjectDisposedException(GetType().Name);
                if(value == connected) { if(value && !Hardware.Healthy) throw new ASCOM.NotConnectedException("Disconnect and reconnect all Gemini clients."); return; }
                if(value) { Hardware.Acquire(); try { AcquireAdditional(); connected = true; } catch { Hardware.Release(); throw; } }
                else { connected = false; try { ReleaseAdditional(); } finally { Hardware.Release(); } }
                }
            }
        }
        public abstract short InterfaceVersion { get; }
        public abstract string Name { get; }
        public string Description => Name;
        public string DriverInfo => "CCDASTRO Gemini FlatPanel Pro and dew control prototype 0.16 with optional external ASCOM Switch, motion locked pending calibration investigation.";
        public string DriverVersion => "0.16";
        public ArrayList SupportedActions => new ArrayList();
        public string Action(string actionName, string actionParameters) => throw new ASCOM.ActionNotImplementedException(actionName);
        public void CommandBlind(string command, bool raw) => throw new ASCOM.MethodNotImplementedException(nameof(CommandBlind));
        public bool CommandBool(string command, bool raw) => throw new ASCOM.MethodNotImplementedException(nameof(CommandBool));
        public string CommandString(string command, bool raw) => throw new ASCOM.MethodNotImplementedException(nameof(CommandString));
        protected void Check() { if(!Connected) throw new ASCOM.NotConnectedException("Connect this Gemini driver first."); }
        public void SetupDialog() { using(var form = new SetupForm()) form.ShowDialog(); }
        public void Dispose() { lock(connectionGate) { if(disposed) return; try { if(connected) Connected = false; } finally { disposed = true; /* Base finalizer must release the COM server object count after clients release this object. */ } } }
        ~DriverBase() { try { Dispose(); } catch { } }
    }

    [ComVisible(true), Guid("06F25190-B598-42D5-8207-36754DCD8C2B"), ProgId("ASCOM.CCDASTRO.GeminiFlatPanel.CoverCalibrator"), ClassInterface(ClassInterfaceType.None), ComDefaultInterface(typeof(ICoverCalibratorV1))]
    [ServedClassName("CCDASTRO Gemini FlatPanel")]
    public sealed class CoverCalibrator : DriverBase, ICoverCalibratorV1
    {
        public override short InterfaceVersion => 1;
        public override string Name => "CCDASTRO Gemini FlatPanel";
        public CoverStatus CoverState
        {
            get
            {
                if(!Connected) return CoverStatus.Unknown;
                switch(Hardware.CoverState())
                {
                    case GeminiFlatPanel.Core.PhysicalCoverState.Moving: return CoverStatus.Moving;
                    case GeminiFlatPanel.Core.PhysicalCoverState.Open: return CoverStatus.Open;
                    case GeminiFlatPanel.Core.PhysicalCoverState.Closed: return CoverStatus.Closed;
                    case GeminiFlatPanel.Core.PhysicalCoverState.Error: return CoverStatus.Error;
                    default: return CoverStatus.Unknown;
                }
            }
        }
        public CalibratorStatus CalibratorState { get { if(!Connected) return CalibratorStatus.Unknown; return Hardware.CalibratorState(); } }
        public int Brightness { get { Check(); return Hardware.ReadBrightness(false); } }
        public int MaxBrightness => 255;
        public void OpenCover() { Check(); Hardware.Move(true); }
        public void CloseCover() { Check(); Hardware.Move(false); }
        public void HaltCover() { Check(); Hardware.Halt(); }
        public void CalibratorOff() { Check(); Hardware.Brightness(0); }
        public void CalibratorOn(int brightness) { Check(); if(brightness < 0 || brightness > 255) throw new ASCOM.InvalidValueException(nameof(brightness), brightness.ToString(), "0..255"); Hardware.Brightness(brightness, true); }
    }

    [ComVisible(true), Guid("328C89C6-68E1-4A30-B696-57E00E05F975"), ProgId("ASCOM.CCDASTRO.GeminiFlatPanel.Switch"), ClassInterface(ClassInterfaceType.None), ComDefaultInterface(typeof(ISwitchV2))]
    [ServedClassName("CCDASTRO Gemini Dew Heater")]
    public sealed class Switch : DriverBase, ISwitchV2
    {
        public override short InterfaceVersion => 2;
        public override string Name => "CCDASTRO Gemini Dew Heater";
        protected override void AcquireAdditional() => ExternalSwitch.Acquire();
        protected override void ReleaseAdditional() => ExternalSwitch.Release();
        public short MaxSwitch => checked((short)(4 + ExternalSwitch.Count));
        private static readonly string[] commandNames = { "", "High brightness", "Beep", "Stop cover motion" };
        private void Validate(short id) { Check(); if(id < 0 || id >= MaxSwitch) throw new ASCOM.InvalidValueException(nameof(id), id.ToString(), "0.." + (MaxSwitch - 1)); }
        public bool CanWrite(short id) { Validate(id); return id < 4 || ExternalSwitch.Call(id, (d, i) => d.CanWrite(i)); }
        public string GetSwitchName(short id) { Validate(id); return id >= 4 ? "SVBONY / " + ExternalSwitch.Call(id, (d, i) => d.GetSwitchName(i)) : "Gemini / " + (id == 0 ? Settings.Load().HeaterName : commandNames[id]); }
        public void SetSwitchName(short id, string name) { Validate(id); if(id >= 4) { ExternalSwitch.Call(id, (d, i) => d.SetSwitchName(i, name)); return; } if(id != 0) throw new ASCOM.MethodNotImplementedException(nameof(SetSwitchName)); if(string.IsNullOrWhiteSpace(name)) throw new ASCOM.InvalidValueException("name", name ?? "null", "Nonempty name"); var settings = Settings.Load(); settings.HeaterName = name; settings.Save(); }
        public string GetSwitchDescription(short id)
        {
            Validate(id);
            if(id >= 4) return ExternalSwitch.Call(id, (d, i) => d.GetSwitchDescription(i));
            switch(id)
            {
                case 0: return "Gemini native dew command (legacy 0-100 range). Hardware may be on/off only; use an appended SVBONY PWM channel for proportional heating.";
                case 1: return "ON = high, OFF = low. Shows last requested setting; initial OFF is unverified until set.";
                case 2: return "ON = beep enabled, OFF = disabled. Shows last requested setting; initial OFF is unverified until set.";
                default: return "ON stops cover movement. OFF resets this indicator; it does not resume movement. Turn off then on to stop again.";
            }
        }
        public bool GetSwitch(short id) { Validate(id); return id >= 4 ? ExternalSwitch.Call(id, (d, i) => d.GetSwitch(i)) : GetSwitchValue(id) > 0; }
        public void SetSwitch(short id, bool state) { Validate(id); if(id >= 4) { ExternalSwitch.Call(id, (d, i) => d.SetSwitch(i, state)); return; } SetSwitchValue(id, state ? MaxSwitchValue(id) : 0); }
        public double MinSwitchValue(short id) { Validate(id); return id >= 4 ? ExternalSwitch.Call(id, (d, i) => d.MinSwitchValue(i)) : 0; }
        public double MaxSwitchValue(short id) { Validate(id); return id >= 4 ? ExternalSwitch.Call(id, (d, i) => d.MaxSwitchValue(i)) : id == 0 ? 100 : 1; }
        public double SwitchStep(short id) { Validate(id); return id >= 4 ? ExternalSwitch.Call(id, (d, i) => d.SwitchStep(i)) : 1; }
        public double GetSwitchValue(short id) { Validate(id); return id >= 4 ? ExternalSwitch.Call(id, (d, i) => d.GetSwitchValue(i)) : id == 0 ? Hardware.ReadStatus(false).HeaterPercent : Hardware.CommandReceipt(id) ? 1 : 0; }
        public void SetSwitchValue(short id, double value)
        {
            Validate(id);
            if(id >= 4) { ExternalSwitch.Call(id, (d, i) => d.SetSwitchValue(i, value)); return; }
            double maximum = MaxSwitchValue(id);
            if(double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > maximum)
                throw new ASCOM.InvalidValueException(nameof(value), value.ToString(), "0.." + maximum);
            // ASCOM permits requests between steps: use the nearest supported value,
            // with midpoint ties rounded upward. Validate before rounding.
            value = Math.Round(value, MidpointRounding.AwayFromZero);
            if(id == 0) { Hardware.Heater((int)value); return; }
            Hardware.SetCommandSwitch(id, value == 1);
        }
    }
}
