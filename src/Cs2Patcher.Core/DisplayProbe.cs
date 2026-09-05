using System.Runtime.InteropServices;

namespace Cs2Patcher.Core;

/// <summary>
/// The display mode the desktop is currently in, including the exact refresh rate.
/// </summary>
/// <param name="RefreshNumerator">
/// Refresh expressed as a fraction, because that is how the game stores it and how displays
/// actually work: 59.94 Hz is 60000/1001, not 60, and rounding it produces a mode the driver
/// may refuse.
/// </param>
public sealed record DisplayMode(int Width, int Height, int RefreshNumerator, int RefreshDenominator)
{
    public double RefreshHz => RefreshDenominator == 0 ? 0 : (double)RefreshNumerator / RefreshDenominator;

    public override string ToString() => $"{Width}x{Height} @{RefreshHz:N0}Hz";
}

/// <summary>
/// Reads the current desktop display mode.
///
/// <para>This exists because lowering the game's output resolution is the only lever left below
/// the bottom of the settings ladder, and writing that setting means writing a refresh rate too.
/// Guessing at the refresh rate is not acceptable for a value that reaches
/// <c>Screen.SetResolution</c>: a mode the driver rejects is a black screen on somebody else's
/// computer.</para>
///
/// <para><c>EnumDisplaySettings</c> is used rather than WMI because it is dependency-free, and
/// rather than <c>Screen.PrimaryScreen</c> because that does not report refresh at all.</para>
/// </summary>
public static class DisplayProbe
{
    private const int EnumCurrentSettings = -1;

    public static DisplayMode? Current()
    {
        try
        {
            var mode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            if (!EnumDisplaySettings(null, EnumCurrentSettings, ref mode)) return null;
            if (mode.dmPelsWidth <= 0 || mode.dmPelsHeight <= 0) return null;

            // Windows reports refresh as a whole number, so 59.94 Hz comes back as 59 or 60. The
            // common broadcast rates are recovered exactly; anything else is treated as integral,
            // which is true for every PC monitor mode outside of those two families.
            var (numerator, denominator) = mode.dmDisplayFrequency switch
            {
                59 or 60 => (60000, 1001),
                29 or 30 => (30000, 1001),
                <= 0 => (60, 1),
                var hz => (hz, 1),
            };

            return new DisplayMode(mode.dmPelsWidth, mode.dmPelsHeight, numerator, denominator);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Every mode the driver will accept at the current refresh rate and colour depth, largest
    /// first.
    ///
    /// Needed because a computed resolution is not necessarily a real one. Two thirds of 1920x1080
    /// is 1286x724, and asking a display for that in exclusive fullscreen gets a refusal or a
    /// letterbox rather than a smaller picture. Picking from the list the driver reports means the
    /// mode always exists.
    /// </summary>
    public static IReadOnlyList<DisplayMode> SupportedModes(DisplayMode? matching = null)
    {
        var current = matching ?? Current();
        var modes = new List<DisplayMode>();

        try
        {
            var seen = new HashSet<(int, int)>();

            for (var i = 0; ; i++)
            {
                var mode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
                if (!EnumDisplaySettings(null, i, ref mode)) break;
                if (mode.dmPelsWidth <= 0 || mode.dmPelsHeight <= 0) continue;

                // One entry per resolution. The driver reports the same size at several refresh
                // rates and colour depths, and the refresh rate is carried over from the desktop
                // rather than chosen here.
                if (!seen.Add((mode.dmPelsWidth, mode.dmPelsHeight))) continue;

                modes.Add(new DisplayMode(mode.dmPelsWidth, mode.dmPelsHeight,
                    current?.RefreshNumerator ?? 60, current?.RefreshDenominator ?? 1));
            }
        }
        catch
        {
            return modes;
        }

        return [.. modes.OrderByDescending(m => (long)m.Width * m.Height)];
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }
}
