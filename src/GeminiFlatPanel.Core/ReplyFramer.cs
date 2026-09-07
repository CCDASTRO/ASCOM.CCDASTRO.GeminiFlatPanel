using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;

namespace GeminiFlatPanel.Core;

/// <summary>Extracts complete replies independently of Windows serial read boundaries.</summary>
public sealed class ReplyFramer
{
    private readonly StringBuilder buffer = new StringBuilder();
    public IEnumerable<string> Feed(string chunk)
    {
        var frames = new List<string>();
        foreach (char c in chunk)
        {
            if (c == '*') { buffer.Clear(); buffer.Append(c); }
            else if (buffer.Length > 0)
            {
                buffer.Append(c);
                if (c == '#') { frames.Add(buffer.ToString()); buffer.Clear(); }
                else if (buffer.Length > 256) { buffer.Clear(); throw new FormatException("Oversized Gemini reply."); }
            }
        }
        return frames;
    }
}

public sealed class DeviceStatus
{
    public bool LightOn { get; private set; }
    public int CoverFlag { get; private set; }
    public int HeaterPercent { get; private set; }
    public int ClosedLimit { get; private set; }
    public int OpenLimit { get; private set; }
    public static DeviceStatus Parse(string frame)
    {
        var match = Regex.Match(frame, @"^\*S\d+M([01])L(\d+)C(\d+)D(\d+)C(\d+)O#$", RegexOptions.CultureInvariant);
        if (!match.Success) throw new FormatException("Unrecognized Gemini status: " + frame);
        int heater = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        if (heater > 100) throw new FormatException("Heater readback outside 0-100: " + frame);
        return new DeviceStatus { LightOn = match.Groups[1].Value == "1", CoverFlag = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture), HeaterPercent = heater, ClosedLimit = int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture), OpenLimit = int.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture) };
    }
}

public enum PhysicalCoverState { Unknown, Moving, Open, Closed, Error }

/// <summary>Uses explicitly verified endpoint observations, never the unreliable L flag.</summary>
public sealed class CoverTracker
{
    public int? ClosedPosition { get; set; }
    public int? OpenPosition { get; set; }
    public int Tolerance { get; set; } = 5;
    public PhysicalCoverState State { get; private set; } = PhysicalCoverState.Unknown;
    private char? movement;
    private bool halted;
    private DateTime deadline;
    public bool IsMoving => State == PhysicalCoverState.Moving;
    public void Begin(bool open, DateTime now)
    {
        if (IsMoving) throw new InvalidOperationException("Cover is already moving.");
        movement = open ? 'O' : 'C'; halted = false;
        deadline = now.AddSeconds(60); State = PhysicalCoverState.Moving;
    }
    public void Halt() { halted = true; movement = null; State = PhysicalCoverState.Unknown; }
    public void Fail() { movement = null; State = PhysicalCoverState.Error; }
    public void Reset() { movement = null; halted = false; State = PhysicalCoverState.Unknown; }
    public void CheckTimeout(DateTime now) { if (IsMoving && now >= deadline) Fail(); }
    public void ObservePosition(int position)
    {
        if (IsMoving || halted || State == PhysicalCoverState.Error) return;
        State = Classify(position);
    }
    public void Complete(char operation, int position)
    {
        if (movement != operation || halted) return;
        movement = null;
        State = Classify(position);
    }
    private PhysicalCoverState Classify(int position)
    {
        bool closed = ClosedPosition.HasValue && Math.Abs((long)position - ClosedPosition.Value) <= Tolerance;
        bool open = OpenPosition.HasValue && Math.Abs((long)position - OpenPosition.Value) <= Tolerance;
        if (closed == open) return PhysicalCoverState.Unknown;
        return closed ? PhysicalCoverState.Closed : PhysicalCoverState.Open;
    }
}

