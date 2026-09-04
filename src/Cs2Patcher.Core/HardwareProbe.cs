using System.Globalization;
using System.Text.RegularExpressions;

namespace Cs2Patcher.Core;

/// <summary>
/// Reads hardware facts out of the game's own Player.log.
///
/// This deliberately avoids WMI's Win32_VideoController.AdapterRAM, which is a uint32 and
/// silently saturates at 4096 MB — an 8 GB card reports 4 GB. The game asks the graphics
/// driver directly and logs the true figure, so the log is the more trustworthy source.
/// </summary>
public sealed record HardwareInfo(
    string? Gpu,
    int VramMegabytes,
    string? Cpu,
    int CoreCount,
    double SystemMemoryGb,
    string? GameVersion,
    string? UnityVersion)
{
    public bool HasVram => VramMegabytes > 0;
}

public static class HardwareProbe
{
    /// <summary>Parses the newest Player.log in the user data directory. Returns null when absent.</summary>
    public static HardwareInfo? FromPlayerLog(string? userDataDir)
    {
        if (userDataDir is null) return null;

        var log = Path.Combine(userDataDir, "Player.log");
        if (!File.Exists(log)) return null;

        string text;
        try
        {
            // The game holds this open while running, so share everything.
            using var stream = new FileStream(log, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            text = reader.ReadToEnd();
        }
        catch
        {
            return null;
        }

        return new HardwareInfo(
            Gpu: Capture(text, @"Graphics device:\s*(.+?)\s*\(Version"),
            VramMegabytes: ParseVram(text),
            Cpu: Capture(text, @"^CPU:\s*(.+)$"),
            // No '$' anchors on numeric captures: the log uses CRLF, and a trailing \r
            // sits between the digits and the line end, so '$' would never match.
            CoreCount: ParseInt(text, @"^Core count:\s*(\d+)"),
            SystemMemoryGb: ParseDouble(text, @"System memory:\s*([\d.]+)\s*GB"),
            GameVersion: Capture(text, @"^Game version:\s*(.+)$"),
            UnityVersion: Capture(text, @"Initialize engine version:\s*(\S+)"));
    }

    private static int ParseVram(string text)
    {
        // "Graphics memory: 7.854 GB" is the accurate line; "VRAM: 8042 MB" is the D3D block.
        var mb = ParseInt(text, @"VRAM:\s*(\d+)\s*MB");
        if (mb > 0) return mb;

        var gb = ParseDouble(text, @"Graphics memory:\s*([\d.]+)\s*GB");
        return gb > 0 ? (int)Math.Round(gb * 1024) : 0;
    }

    private static string? Capture(string text, string pattern)
    {
        var m = Regex.Match(text, pattern, RegexOptions.Multiline);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    private static int ParseInt(string text, string pattern) =>
        int.TryParse(Capture(text, pattern), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static double ParseDouble(string text, string pattern) =>
        double.TryParse(Capture(text, pattern), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
