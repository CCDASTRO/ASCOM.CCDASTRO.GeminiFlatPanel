namespace GeminiFlatPanel.Core;

/// <summary>Commands observed from Gemini FlatPanel Pro firmware identifier 103.</summary>
public static class GeminiProtocol
{
    public const int BaudRate = 9600;
    public const string Identify = ">H#\n";
    public const string Version = ">V#\n";
    public const string Status = ">S#\n";
    public const string Position = ">G#\n";
    public const string Brightness = ">J#\n";
    public const string OpenCover = ">O#\n";
    public const string CloseCover = ">COOO#\n";
    public const string HaltCover = ">K#\n";

    public static string SetBrightness(int value)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        if (value > 255) throw new ArgumentOutOfRangeException(nameof(value));
        return FormattableString.Invariant($">B{value}#\n");
    }

    public static string SetHeater(int percent)
    {
        if (percent < 0) throw new ArgumentOutOfRangeException(nameof(percent));
        if (percent > 100) throw new ArgumentOutOfRangeException(nameof(percent));
        return FormattableString.Invariant($">W{percent}#\n");
    }

    public static string SetHighBrightnessMode(bool enabled) => enabled ? ">Y1#\n" : ">Y0#\n";
    public static string SetBeep(bool enabled) => enabled ? ">T1#\n" : ">T0#\n";
}

