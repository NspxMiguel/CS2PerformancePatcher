using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Win32;

namespace Cs2Patcher.Core;

/// <summary>How much a host finding is worth acting on.</summary>
public enum Severity
{
    /// <summary>Already in the state we would ask for.</summary>
    Ok,

    /// <summary>Worth knowing and worth fixing, but not from here — BIOS, hardware, or admin.</summary>
    Advisory,

    /// <summary>This tool can fix it, reversibly.</summary>
    Actionable,
}

/// <param name="Fix">Applies the change and returns what to store in order to undo it.</param>
/// <param name="Undo">Restores the value recorded by <see cref="Fix"/>.</param>
public sealed record HostFinding(
    string Id,
    string Name,
    string Current,
    string Wanted,
    Severity Severity,
    string Why,
    Func<string?>? Fix = null,
    Action<string?>? Undo = null);

/// <summary>
/// Everything outside the game that decides how fast it runs.
///
/// The rule this file follows: only change what is per-application or per-user and can be put
/// back exactly. Anything needing admin, a reboot, or a BIOS visit is reported and explained,
/// never touched — a patcher that quietly rewrites machine-wide state is not a patcher.
/// </summary>
[SupportedOSPlatform("windows")]
public static class HostTuning
{
    /// <summary>Windows' built-in High performance scheme. Stable across installs.</summary>
    private const string HighPerformanceScheme = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    private const string GpuPreferenceKey = @"Software\Microsoft\DirectX\UserGpuPreferences";

    /// <summary>What the Windows Graphics Settings page writes for "High performance".</summary>
    private const string HighPerformanceGpu = "GpuPreference=2;";

    public static string StateFile(string userDataDir) =>
        Path.Combine(userDataDir, PatchEngine.BackupFolderName, "host-tuning.json");

    public static IReadOnlyList<HostFinding> Inspect(string? gameExePath)
    {
        var findings = new List<HostFinding> { PowerPlan() };
        if (gameExePath is not null) findings.Add(GpuPreference(gameExePath));
        findings.Add(MemorySpeed());
        findings.Add(MemoryChannels());
        findings.Add(PcieLink());
        findings.Add(GpuScheduling());
        return findings;
    }

    // ---- things we can change and put back -------------------------------------------------

    private static HostFinding PowerPlan()
    {
        var active = Run("powercfg", "/getactivescheme");
        var isHigh = active.Contains(HighPerformanceScheme, StringComparison.OrdinalIgnoreCase);
        var name = active.Contains('(') ? active[(active.IndexOf('(') + 1)..].TrimEnd(')', '\r', '\n', ' ') : "unknown";

        return new HostFinding(
            "power-plan",
            "Windows power plan",
            isHigh ? "High performance" : name,
            "High performance",
            isHigh ? Severity.Ok : Severity.Actionable,
            "Balanced parks CPU cores and drops clocks between frames. On a frame-paced workload "
            + "that shows up as inconsistent frame times rather than a lower average.",
            Fix: isHigh ? null : () =>
            {
                var previous = CurrentSchemeGuid();
                Run("powercfg", $"/setactive {HighPerformanceScheme}");
                return previous;
            },
            Undo: previous =>
            {
                if (!string.IsNullOrWhiteSpace(previous)) Run("powercfg", $"/setactive {previous}");
            });
    }

    private static HostFinding GpuPreference(string exePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(GpuPreferenceKey);
        var current = key?.GetValue(exePath) as string ?? "";
        var isHigh = current.Contains("GpuPreference=2", StringComparison.OrdinalIgnoreCase);

        return new HostFinding(
            "gpu-preference",
            "GPU preference for Cities2.exe",
            isHigh ? "High performance" : (current.Length == 0 ? "not set" : current),
            "High performance",
            isHigh ? Severity.Ok : Severity.Actionable,
            "Without this Windows picks the adapter itself. On a machine with integrated graphics "
            + "alongside the card — which is every Ryzen G-series desktop — it can pick wrong.",
            Fix: isHigh ? null : () =>
            {
                using var write = Registry.CurrentUser.CreateSubKey(GpuPreferenceKey, writable: true)!;
                var previous = write.GetValue(exePath) as string;
                write.SetValue(exePath, HighPerformanceGpu, RegistryValueKind.String);
                return previous ?? "";
            },
            Undo: previous =>
            {
                using var write = Registry.CurrentUser.CreateSubKey(GpuPreferenceKey, writable: true);
                if (write is null) return;

                if (string.IsNullOrEmpty(previous)) write.DeleteValue(exePath, throwOnMissingValue: false);
                else write.SetValue(exePath, previous, RegistryValueKind.String);
            });
    }

    // ---- things we only report -------------------------------------------------------------

    private static HostFinding MemorySpeed()
    {
        var speeds = WmiValues("Win32_PhysicalMemory", "ConfiguredClockSpeed")
            .Select(v => Convert.ToInt32(v))
            .Where(v => v > 0)
            .ToList();

        if (speeds.Count == 0)
            return new HostFinding("ram-speed", "Memory speed", "unknown", "3200 MT/s or better",
                Severity.Advisory, "Could not read the configured memory clock.");

        var slowest = speeds.Min();
        return new HostFinding(
            "ram-speed",
            "Memory speed",
            $"{slowest} MT/s",
            "3200 MT/s or better",
            slowest >= 3000 ? Severity.Ok : Severity.Advisory,
            slowest >= 3000
                ? "Fast enough that memory is not the limit."
                : "Memory is running at its JEDEC fallback rather than its rated speed, which means "
                + "the XMP/DOCP profile is off in the BIOS. On Ryzen the memory clock also sets the "
                + "Infinity Fabric clock, so this costs CPU throughput everywhere, not just in "
                + "memory-heavy code. It is a BIOS setting; this tool will not touch it.");
    }

    private static HostFinding MemoryChannels()
    {
        var banks = WmiValues("Win32_PhysicalMemory", "BankLabel")
            .Select(v => v?.ToString() ?? "")
            .Where(v => v.Length > 0)
            .Distinct()
            .Count();

        return new HostFinding(
            "ram-channels",
            "Memory channels populated",
            banks == 0 ? "unknown" : banks.ToString(),
            "2 or more",
            banks >= 2 ? Severity.Ok : Severity.Advisory,
            banks >= 2
                ? "Dual channel. Nothing to do."
                : "Single channel halves memory bandwidth. Moving one stick to the other channel "
                + "costs nothing and is the largest free gain available on this class of machine.");
    }

    private static HostFinding PcieLink()
    {
        var csv = Run("nvidia-smi",
            "--query-gpu=pcie.link.gen.current,pcie.link.gen.max,pcie.link.width.current,pcie.link.width.max "
            + "--format=csv,noheader,nounits");

        var parts = csv.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length < 4 || !int.TryParse(parts[0], out var gen) || !int.TryParse(parts[2], out var width))
            return new HostFinding("pcie", "PCIe link", "unknown", "Gen 4 x16", Severity.Advisory,
                "Could not read the link — nvidia-smi is absent or this is not an NVIDIA card.");

        int.TryParse(parts[1], out var genMax);
        int.TryParse(parts[3], out var widthMax);

        // Gen 3 x16 is roughly where streaming stops being the limiting factor for this game.
        var roomy = gen >= 3 && width >= 16;

        return new HostFinding(
            "pcie",
            "PCIe link",
            $"Gen {gen} x{width} (card supports up to Gen {genMax} x{widthMax})",
            "Gen 3 x16 or better",
            roomy ? Severity.Ok : Severity.Advisory,
            roomy
                ? "Wide enough that host-to-card transfers are not the limit."
                : "A narrow link makes every mesh and texture the game re-uploads more expensive, "
                + "which shows up as stutter rather than a lower average. It also means the mesh "
                + "streaming budget matters more here than on a wider link: undershoot it and the "
                + "game pays this cost repeatedly. Fixing it means a different slot or board.");
    }

    private static HostFinding GpuScheduling()
    {
        object? mode = null;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
            mode = key?.GetValue("HwSchMode");
        }
        catch (Exception) { /* reading HKLM can be denied; treat as unknown */ }

        var on = mode is int m && m == 2;
        return new HostFinding(
            "gpu-scheduling",
            "Hardware-accelerated GPU scheduling",
            mode is null ? "not set" : (on ? "on" : "off"),
            "on",
            on ? Severity.Ok : Severity.Advisory,
            on
                ? "Already on."
                : "Letting the GPU manage its own work queue trims render-thread overhead, which is "
                + "where a Direct3D 11 title with thousands of draw calls spends its time. It lives "
                + "in Settings > Display > Graphics, needs administrator rights and a reboot, so "
                + "this tool reports it rather than setting it.");
    }

    // ---- apply / undo bookkeeping -----------------------------------------------------------

    /// <summary>
    /// Applies every actionable finding, recording what each one replaced so it can be undone.
    /// </summary>
    public static IReadOnlyList<string> Apply(IReadOnlyList<HostFinding> findings, string statePath)
    {
        var undo = ReadState(statePath);
        var done = new List<string>();

        foreach (var finding in findings.Where(f => f.Severity == Severity.Actionable && f.Fix is not null))
        {
            var previous = finding.Fix!();
            // Keep the first value we ever replaced. Applying twice must not record our own
            // change as the thing to restore.
            undo.TryAdd(finding.Id, previous ?? "");
            done.Add($"{finding.Name}: {finding.Current} -> {finding.Wanted}");
        }

        if (done.Count > 0) WriteState(statePath, undo);
        return done;
    }

    public static IReadOnlyList<string> Undo(IReadOnlyList<HostFinding> findings, string statePath)
    {
        var undo = ReadState(statePath);
        var done = new List<string>();

        foreach (var (id, previous) in undo)
        {
            var finding = findings.FirstOrDefault(f => f.Id == id);
            if (finding?.Undo is null) continue;

            finding.Undo(previous);
            done.Add(finding.Name);
        }

        if (File.Exists(statePath)) File.Delete(statePath);
        return done;
    }

    private static Dictionary<string, string> ReadState(string path)
    {
        if (!File.Exists(path)) return [];
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static void WriteState(string path, Dictionary<string, string> state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
    }

    // ---- plumbing ---------------------------------------------------------------------------

    private static string CurrentSchemeGuid()
    {
        var line = Run("powercfg", "/getactivescheme");
        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.FirstOrDefault(w => Guid.TryParse(w, out _)) ?? "";
    }

    private static IEnumerable<object?> WmiValues(string className, string property)
    {
        List<object?> values = [];
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT {property} FROM {className}");
            foreach (var item in searcher.Get()) values.Add(item[property]);
        }
        catch (Exception)
        {
            // WMI is routinely broken or restricted. An empty answer becomes "unknown", which is
            // an honest thing for this tool to say.
        }
        return values;
    }

    private static string Run(string exe, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(exe, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process is null) return "";
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(10_000);
            return output.Trim();
        }
        catch (Exception)
        {
            return "";
        }
    }
}
