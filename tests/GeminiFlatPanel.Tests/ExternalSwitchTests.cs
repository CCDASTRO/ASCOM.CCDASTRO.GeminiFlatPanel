using System;
using GeminiFlatPanel.Server;

internal static class ExternalSwitchTests
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference CreateDisposedDriver()
    {
        var driver = new Switch();
        driver.Dispose();
        driver.Dispose();
        return new WeakReference(driver);
    }

    private static void CheckObjectLifetime(Action<bool, string> check)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        int before = ASCOM.LocalServer.Server.ObjectCount;
        var released = CreateDisposedDriver();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        check(!released.IsAlive, "disposed driver remains rooted");
        check(ASCOM.LocalServer.Server.ObjectCount == before, "Dispose leaked COM server object count");
    }
    internal static void Run(Action<bool, string> check, Action<Action, string> throws)
    {
        CheckObjectLifetime(check);
        foreach(double maximum in new[] { 249.0, 253.0 })
        {
            var raw = new FakeExternalSwitch { Count = 17, PwmMaximum = maximum };
            var percent = new PercentageExternalSwitch(raw);
            check(percent.MinSwitchValue(14) == 0 && percent.MaxSwitchValue(14) == 100 && percent.SwitchStep(14) == 1, "PWM percentage range");
            check(percent.MaxSwitchValue(13) == 90, "voltage channel changed");
            for(int level = 0; level <= 100; level++)
            {
                percent.SetSwitchValue(14, level);
                check(raw.Value == Math.Round(level * maximum / 100, MidpointRounding.AwayFromZero), "PWM raw conversion");
                check(percent.GetSwitchValue(14) == level, "PWM percentage readback");
            }
            percent.SetSwitchName(15, "Dew strap");
            check(percent.GetSwitchName(15) == "Dew heater 2 / Dew strap (%)", "PWM rename mapping");
            percent.SetSwitch(15, false);
            check(raw.Value == 0 && !percent.GetSwitch(15), "PWM off");
            percent.SetSwitch(15, true);
            check(raw.Value == maximum && percent.GetSwitch(15), "PWM on");
            int writes = raw.Writes;
            foreach(double invalid in new[] { -1.0, 101.0, double.NaN, double.PositiveInfinity })
                throws(() => percent.SetSwitchValue(14, invalid), "invalid percentage accepted");
            check(raw.Writes == writes, "invalid percentage wrote output");
        }
        new Settings().Save();
        var serial = new FakeChannel();
        Hardware.ChannelFactory = _ => serial;
        int factories = 0;
        var vendor = new FakeExternalSwitch();
        ExternalSwitch.Factory = _ => { factories++; return vendor; };
        using(var legacy = new Switch())
        {
            legacy.Connected = true;
            check(legacy.MaxSwitch == 4 && factories == 0, "disabled bridge created vendor driver");
            throws(() => ExternalSwitch.Configure("ASCOM.SVBONY.Switch"), "configuration changed during disabled session");
        }
        throws(() => ExternalSwitch.Configure("ASCOM.CCDASTRO.GeminiFlatPanel.Switch"), "self reference accepted");
        check(Settings.Load().ExternalSwitchProgId == "", "failed selection altered settings");
        ExternalSwitch.Configure("ASCOM.SVBONY.Switch");
        using(var cover = new CoverCalibrator())
        using(var first = new Switch())
        using(var second = new Switch())
        {
            cover.Connected = true;
            check(factories == 0, "cover opened vendor driver");
            first.Connected = true;
            second.Connected = true;
            check(factories == 1 && vendor.Connects == 1, "vendor connection not shared");
            check(first.MaxSwitch == 6 && Settings.Load().ExternalSwitchCount == 2, "appended count incorrect");
            check(first.GetSwitchName(1) == "Gemini / High brightness", "Gemini ID 1 shifted");
            check(first.GetSwitchName(4) == "SVBONY / Heater PWM", "vendor names not prefixed");
            check(first.GetSwitchDescription(4) == "Vendor channel 0", "description mapping");
            check(first.CanWrite(4) && !first.CanWrite(5), "read only flag not forwarded");
            check(first.MinSwitchValue(4) == -10 && first.MaxSwitchValue(4) == 90 && first.SwitchStep(4) == .25, "vendor range changed");
            check(!first.GetSwitch(4) && first.GetSwitchValue(4) == 10, "boolean read inferred from numeric value");
            check(vendor.Writes == 0, "connection changed vendor output");
            first.SetSwitchValue(4, 25.25);
            check(vendor.Value == 25.25 && vendor.LastId == 0, "fractional value or ID altered");
            first.SetSwitch(4, false);
            check(vendor.BooleanCalls == 1 && !vendor.BooleanValue && vendor.Value == 25.25, "boolean call converted to numeric command");
            first.SetSwitchName(4, "My heater");
            check(second.GetSwitchName(4) == "SVBONY / My heater", "rename not forwarded");
            throws(() => first.SetSwitchValue(5, 10), "read only vendor write accepted");
            throws(() => first.SetSwitchValue(4, double.NaN), "vendor validation swallowed");
            throws(() => first.GetSwitchValue(6), "out of range channel accepted");
            throws(() => first.GetSwitchValue(-1), "negative channel accepted");
            throws(() => ExternalSwitch.Configure(""), "active bridge changed");
            int writes = vendor.Writes;
            vendor.Count = 3;
            check(first.MaxSwitch == 6, "count changed during session");
            throws(() => first.SetSwitchValue(4, 50), "changed channel layout accepted");
            check(vendor.Writes == writes, "changed layout wrote hardware");
            vendor.Count = 2;
            vendor.IsConnected = false;
            using(var third = new Switch()) throws(() => third.Connected = true, "new client accepted disconnected vendor");
            throws(() => first.GetSwitchValue(4), "disconnected vendor read accepted");
            first.SetSwitch(1, true);
            check(first.GetSwitch(1) && cover.Connected, "vendor failure broke native controls");
            vendor.IsConnected = true;
            first.Connected = false;
            first.Dispose();
            check(vendor.Disposes == 0 && second.Connected, "first client closed shared driver");
            second.Connected = false;
            check(vendor.Disposes == 1 && vendor.Disconnects == 1, "last client failed to close vendor");
            check(vendor.Writes == writes, "disconnect changed output");
            check(cover.Connected && serial.IsOpen, "Switch disconnect closed CoverCalibrator");
        }
        check(!Hardware.InUse && !ExternalSwitch.InUse, "connection reference leaked");
        vendor = new FakeExternalSwitch { FailConnect = true };
        using(var cover = new CoverCalibrator())
        using(var failed = new Switch())
        {
            cover.Connected = true;
            throws(() => failed.Connected = true, "failed vendor connection accepted");
            check(!failed.Connected && cover.Connected && vendor.Disposes == 1, "failed connection rollback damaged cover");
        }
        check(!Hardware.InUse && !ExternalSwitch.InUse, "failed connection leaked reference");
        vendor = new FakeExternalSwitch { Count = short.MaxValue };
        using(var invalid = new Switch()) throws(() => invalid.Connected = true, "overflow count accepted");
        check(vendor.Disposes == 1, "invalid count leaked vendor");
        ExternalSwitch.Factory = _ => { ExternalSwitch.Acquire(); return new FakeExternalSwitch(); };
        using(var recursive = new Switch()) throws(() => recursive.Connected = true, "recursive bridge accepted");
        check(!Hardware.InUse && !ExternalSwitch.InUse, "recursive connection leaked reference");
        ExternalSwitch.Configure("");
        using(var disabled = new Switch()) check(disabled.MaxSwitch == 4, "disabled bridge retained cached channels");
        ExternalSwitch.Factory = id => new AscomExternalSwitch(id);
    }
}

internal sealed class FakeExternalSwitch : IExternalSwitch
{
    internal bool IsConnected, FailConnect, BooleanValue;
    internal int Connects, Disconnects, Disposes, Writes, BooleanCalls;
    internal short Count = 2, LastId = -1;
    internal double Value = 10, PwmMaximum;
    internal string ChannelName = "Heater PWM";
    public bool Connected
    {
        get => IsConnected;
        set
        {
            if(value) { Connects++; if(FailConnect) throw new InvalidOperationException("USB unavailable"); }
            else Disconnects++;
            IsConnected = value;
        }
    }
    public short MaxSwitch => Count;
    public bool CanWrite(short id) => id == 0 || (PwmMaximum > 0 && (id == 14 || id == 15));
    public string GetSwitchName(short id) => ChannelName;
    public void SetSwitchName(short id, string name) { LastId = id; ChannelName = name; }
    public string GetSwitchDescription(short id) => "Vendor channel " + id;
    public bool GetSwitch(short id) => BooleanValue;
    public void SetSwitch(short id, bool value) { if(!CanWrite(id)) throw new InvalidOperationException(); LastId = id; BooleanValue = value; BooleanCalls++; Writes++; }
    public double MinSwitchValue(short id) => PwmMaximum > 0 && (id == 14 || id == 15) ? (id == 14 ? -1 : 0) : -10;
    public double MaxSwitchValue(short id) => PwmMaximum > 0 && (id == 14 || id == 15) ? PwmMaximum : 90;
    public double SwitchStep(short id) => PwmMaximum > 0 && (id == 14 || id == 15) ? 1 : .25;
    public double GetSwitchValue(short id) => Value;
    public void SetSwitchValue(short id, double value)
    {
        if(!CanWrite(id) || double.IsNaN(value) || double.IsInfinity(value) || value < MinSwitchValue(id) || value > MaxSwitchValue(id)) throw new ArgumentException();
        LastId = id; Value = value; Writes++;
    }
    public void Dispose() { Disposes++; }
}
