using System.Globalization;
using System.Text.Json.Nodes;
using Cs2Patcher.Core;

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "status";
var explicitPath = GetOption(args, "--path");

Banner();

var install = GameLocator.Locate(explicitPath);
if (!install.Found)
{
    Fail(install.Diagnostic ?? "Cities: Skylines II was not found.");
    Console.WriteLine("  Point at it directly:  cs2patch status --path \"D:\\Games\\Cities Skylines II\"");
    return 1;
}

var hardware = HardwareProbe.FromPlayerLog(install.UserDataDir);
var engine = PatchEngine.For(install);

try
{
    switch (command)
    {
        case "status": return Status();
        case "list": return List();
        case "apply": return Apply();
        case "revert": return Revert();
        case "install-mod": return InstallMod();
        case "uninstall-mod": return UninstallMod();
        case "bench": return Bench();
        case "tune-pc": return TunePc();
        case "help" or "--help" or "-h": return Help();
        default:
            Fail($"Unknown command '{command}'.");
            return Help();
    }
}
catch (Exception ex)
{
    // Nothing this tool does is worth showing a stack trace over. The backup is always
    // on disk by this point, so recovery is a `cs2patch revert` away.
    Fail(ex.Message);
    Console.WriteLine("  Your original settings are backed up. Run 'cs2patch revert' to restore them.");
    return 1;
}

int Status()
{
    Console.WriteLine("Installation");
    Console.WriteLine($"  Path          {install.InstallDir}");
    Console.WriteLine($"  User data     {install.UserDataDir}");
    Console.WriteLine($"  Game version  {install.GameVersion ?? "unknown"}");
    if (install.BuildId is not null) Console.WriteLine($"  Steam build   {install.BuildId}");

    if (hardware is not null)
    {
        Console.WriteLine();
        Console.WriteLine("Hardware (as the game itself reported it)");
        Console.WriteLine($"  GPU           {hardware.Gpu ?? "?"}{(hardware.HasVram ? $" — {hardware.VramMegabytes} MB" : "")}");
        Console.WriteLine($"  CPU           {hardware.Cpu ?? "?"} ({hardware.CoreCount} threads)");
        Console.WriteLine($"  System RAM    {hardware.SystemMemoryGb:N1} GB");
        Console.WriteLine($"  Unity         {hardware.UnityVersion ?? "?"}");
    }

    Console.WriteLine();
    var manifest = engine.ReadManifest();
    if (manifest is null)
    {
        Console.WriteLine("Patch status:  NOT PATCHED");
        Console.WriteLine("  Apply one with:  cs2patch apply traffic");
    }
    else
    {
        Console.WriteLine($"Patch status:  PATCHED — '{manifest.ProfileName}'");
        Console.WriteLine($"  Applied at    {manifest.PatchedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}");
        Console.WriteLine($"  Game version  {manifest.GameVersion}");
        Console.WriteLine($"  Changes       {manifest.Applied.Count}");

        if (!string.Equals(manifest.GameVersion, install.GameVersion, StringComparison.Ordinal))
            Warn($"The game updated since this patch ({manifest.GameVersion} -> {install.GameVersion}). Re-apply to be safe.");

        if (engine.HasDriftedSincePatch())
            Warn("Settings.coc changed since the patch — you or the game edited settings. Re-apply to restore the profile.");
    }

    return 0;
}

int List()
{
    Console.WriteLine("Profiles");
    Console.WriteLine();
    foreach (var p in Profiles.All)
    {
        var free = p.Tweaks.Count(t => t.Cost == Cost.Free);
        var cheap = p.Tweaks.Count(t => t.Cost == Cost.Cheap);
        var visible = p.Tweaks.Count(t => t.Cost == Cost.Visible);

        Console.WriteLine($"  {p.Id,-14} {p.Name}");
        Console.WriteLine($"                 {p.Description}");
        Console.WriteLine($"                 {p.Tweaks.Count} changes — {free} free, {cheap} cheap, {visible} visible");
        Console.WriteLine();
    }
    Console.WriteLine("  Apply with:  cs2patch apply <id>        Undo with:  cs2patch revert");
    return 0;
}

int Apply()
{
    var id = args.Length > 1 && !args[1].StartsWith("--") ? args[1] : "traffic";
    var profile = Profiles.ById(id);
    if (profile is null)
    {
        Fail($"No profile called '{id}'.");
        return List();
    }

    if (IsGameRunning())
    {
        Fail("Cities: Skylines II is running. Close it first — it overwrites Settings.coc when it exits.");
        return 1;
    }

    // Mesh budget is the one setting that depends on the machine rather than the profile.
    var extras = new List<Tweak>();
    if (hardware?.HasVram == true)
        extras.Add(Profiles.MeshBudgetFor(hardware.VramMegabytes));

    extras.AddRange(ParseOverrides(args));

    var result = engine.Apply(profile, extras);

    Console.WriteLine(result.Success ? $"OK — {result.Message}" : $"FAILED — {result.Message}");
    Console.WriteLine();

    foreach (var line in result.Applied) Console.WriteLine($"  + {line}");
    foreach (var line in result.Skipped) Console.WriteLine($"  ! {line}");

    if (result.Success)
    {
        Console.WriteLine();
        Console.WriteLine($"  Original saved to  {Path.Combine(install.UserDataDir!, PatchEngine.BackupFolderName)}");
        Console.WriteLine("  Undo any time with:  cs2patch revert");
    }

    return result.Success ? 0 : 1;
}

int Revert()
{
    if (IsGameRunning())
    {
        Fail("Cities: Skylines II is running. Close it first.");
        return 1;
    }

    var result = engine.Revert();
    Console.WriteLine(result.Success ? $"OK — {result.Message}" : $"FAILED — {result.Message}");
    return result.Success ? 0 : 1;
}

int InstallMod()
{
    if (IsGameRunning())
    {
        Fail("Cities: Skylines II is running. Close it first.");
        return 1;
    }

    var dll = GetOption(args, "--dll") ?? DefaultModPath();
    var result = ModInstaller.Install(install.UserDataDir!, dll);

    Console.WriteLine(result.Success ? $"OK — {result.Message}" : $"FAILED — {result.Message}");
    if (result.Success)
    {
        Console.WriteLine($"  {result.InstalledPath}");
        Console.WriteLine();
        Warn("The mod is UNTESTED at runtime. It starts inert and changes nothing until configured.");
    }
    return result.Success ? 0 : 1;
}

int UninstallMod()
{
    if (IsGameRunning())
    {
        Fail("Cities: Skylines II is running. Close it first.");
        return 1;
    }

    var result = ModInstaller.Uninstall(install.UserDataDir!);
    Console.WriteLine(result.Success ? $"OK — {result.Message}" : $"FAILED — {result.Message}");
    return result.Success ? 0 : 1;
}

// Built next to this executable when the whole solution is published together; otherwise
// falls back to the source-tree build output so it works from a dev checkout.
static string DefaultModPath()
{
    var beside = Path.Combine(AppContext.BaseDirectory, "Cs2Saver.dll");
    if (File.Exists(beside)) return beside;

    return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
        "..", "..", "..", "..", "Cs2Saver.Mod", "bin", "Release", "netstandard2.1", "Cs2Saver.dll"));
}

int TunePc()
{
    var exe = install.InstallDir is null ? null : Path.Combine(install.InstallDir, "Cities2.exe");
    var findings = HostTuning.Inspect(File.Exists(exe) ? exe : null);
    var state = HostTuning.StateFile(install.UserDataDir!);

    if (args.Contains("--revert"))
    {
        var undone = HostTuning.Undo(findings, state);
        Console.WriteLine(undone.Count == 0
            ? "Nothing to undo — this tool has not changed anything on this machine."
            : $"Restored {undone.Count}:");
        foreach (var line in undone) Console.WriteLine($"  - {line}");
        return 0;
    }

    Console.WriteLine("Outside the game");
    Console.WriteLine();

    foreach (var f in findings)
    {
        var mark = f.Severity switch { Severity.Ok => "ok  ", Severity.Actionable => "FIX ", _ => "note" };
        Console.WriteLine($"  [{mark}] {f.Name}");
        Console.WriteLine($"         now: {f.Current}");
        if (f.Severity != Severity.Ok) Console.WriteLine($"         want: {f.Wanted}");
        Console.WriteLine($"         {f.Why}");
        Console.WriteLine();
    }

    var fixable = findings.Count(f => f.Severity == Severity.Actionable);
    var advisory = findings.Count(f => f.Severity == Severity.Advisory);

    if (!args.Contains("--apply"))
    {
        Console.WriteLine(fixable == 0
            ? "Nothing here this tool can change."
            : $"{fixable} of these can be changed from here, reversibly:  cs2patch tune-pc --apply");

        if (advisory > 0)
            Console.WriteLine($"{advisory} need a BIOS, a reboot, or different hardware. They are yours to make.");
        return 0;
    }

    var applied = HostTuning.Apply(findings, state);
    Console.WriteLine(applied.Count == 0 ? "Nothing to change." : $"Changed {applied.Count}:");
    foreach (var line in applied) Console.WriteLine($"  + {line}");

    if (applied.Count > 0)
        Console.WriteLine("  Undo with:  cs2patch tune-pc --revert");

    return 0;
}

int Bench()
{
    // Default to wherever the runner script files its results, then fall back to the live
    // file the game just wrote, so `cs2patch bench` says something useful either way.
    var target = args.Length > 1 && !args[1].StartsWith("--") ? args[1] : null;
    var files = ResolveBenchFiles(target);

    if (files.Count == 0)
    {
        Fail("No benchmark results found.");
        Console.WriteLine("  Produce one with:  scripts\\Run-Benchmark.ps1 -Label baseline");
        return 1;
    }

    var runs = new List<BenchmarkRun>();
    foreach (var file in files)
    {
        try { runs.Add(BenchmarkRun.Load(file)); }
        catch (Exception ex) { Warn($"Skipping {Path.GetFileName(file)}: {ex.Message}"); }
    }

    if (runs.Count == 0) { Fail("Every result found was unreadable."); return 1; }

    // A run is only comparable to another run of the same game build at the same resolution.
    // The quality level deliberately differs — every patched run reports Custom, because that
    // is what writing past the presets produces — so it is not checked here.
    foreach (var key in new[] { "game version", "resolution" })
    {
        var values = runs
            .Select(r => key == "game version" ? r.GameVersion : r.ScreenResolution)
            .Distinct()
            .ToList();

        if (values.Count > 1)
            Warn($"These runs do not share the same {key} ({string.Join(" vs ", values)}) — they are not comparable.");
    }

    var first = runs[0];
    Console.WriteLine($"Benchmark — {runs.Count} run{(runs.Count == 1 ? "" : "s")}");
    Console.WriteLine($"  {first.Gpu}  {first.Cpu}");
    Console.WriteLine($"  {first.ScreenResolution}  {first.GameVersion}");
    Console.WriteLine($"  '{first.Label}' ran at {first.GraphicsQuality}. Patched runs report Custom by design.");
    Console.WriteLine();
    Console.WriteLine("  Frame cost is whichever of CPU and GPU finished last, per frame.");
    Console.WriteLine("  1% low is the mean of the slowest 1% of frames, not the 99th percentile.");
    Console.WriteLine();

    Console.WriteLine($"  {"run",-20}{"frames",7}{"avg fps",9}{"1% low",8}{"0.1% low",10}{"gpu ms",8}{"render ms",10}{"gpu-bound",10}");
    foreach (var run in runs)
    {
        var e = run.Effective;
        Console.WriteLine($"  {Truncate(run.Label, 19),-20}{e.Frames,7}{e.AvgFps,9:N1}{e.Low1PctFps,8:N1}"
                          + $"{e.Low01PctFps,10:N1}{run.GpuOnly.AvgMs,8:N1}{run.CpuRender.AvgMs,10:N1}{run.GpuBoundPercent,9:N0}%");
    }

    if (runs.Count > 1)
    {
        Console.WriteLine();
        Console.WriteLine($"  Against '{first.Label}'");
        var baseline = first.Effective;
        foreach (var run in runs.Skip(1))
        {
            var e = run.Effective;
            Console.WriteLine($"  {Truncate(run.Label, 19),-20}{"",7}{Delta(baseline.AvgFps, e.AvgFps),9}"
                              + $"{Delta(baseline.Low1PctFps, e.Low1PctFps),8}{Delta(baseline.Low01PctFps, e.Low01PctFps),10}"
                              + $"{Delta(first.GpuOnly.AvgMs, run.GpuOnly.AvgMs, lowerIsBetter: true),8}"
                              + $"{Delta(first.CpuRender.AvgMs, run.CpuRender.AvgMs, lowerIsBetter: true),10}");
        }
    }

    if (GetOption(args, "--phases") is not null || args.Contains("--phases"))
    {
        Console.WriteLine();
        Console.WriteLine("  By phase (paused is the cleanest read on rendering alone)");
        foreach (var run in runs)
        {
            Console.WriteLine($"  {run.Label}");
            foreach (var (name, stats) in run.Phases())
                Console.WriteLine($"    {name,-10}{stats.Frames,7}{stats.AvgFps,9:N1}{stats.Low1PctFps,8:N1}{stats.Low01PctFps,10:N1}");
        }
    }

    if (args.Contains("--spikes"))
    {
        Console.WriteLine();
        Console.WriteLine("  Where the lows come from (the slowest 1% of frames, taken apart)");
        Console.WriteLine("  A setting can only fix a spike the GPU row explains.");

        foreach (var run in runs)
        {
            var s = SpikeReport.From(run);
            if (s is null) continue;

            Console.WriteLine();
            Console.WriteLine($"  {run.Label}  ({s.SlowFrames} of {s.TotalFrames} frames over {s.SlowThresholdMs:N1}ms)");

            foreach (var series in new[] { s.Effective, s.Gpu, s.CpuGame, s.CpuRender })
                Console.WriteLine($"    {series.Name,-12}{series.TypicalMs,7:N1} ->{series.SlowMs,7:N1} ms   {series.Ratio,5:N1}x");

            Console.WriteLine($"    CPU was the wall in {s.CpuBlamed} of {s.SlowFrames}"
                              + $"; {s.Bursts} burst{(s.Bursts == 1 ? "" : "s")}, longest {s.LongestBurst}"
                              + $"; {string.Join(", ", s.ByPhase.Select(p => $"{p.Key} {p.Value}"))}");
            Console.WriteLine($"    -> {s.Culprit.Name} inflates most ({s.Culprit.Ratio:N1}x)"
                              + (s.IsBursty ? ", and they arrive in clumps" : ", scattered"));
        }
    }

    Console.WriteLine();
    var best = runs.MaxBy(r => r.Effective.Low1PctFps)!;
    Console.WriteLine($"Best 1% low: '{best.Label}' at {best.Effective.Low1PctFps:N1} fps"
                      + $" (target is 60).");

    return 0;
}

List<string> ResolveBenchFiles(string? target)
{
    if (target is not null)
    {
        if (Directory.Exists(target))
            return [.. Directory.GetFiles(target, "Benchmark*.coc").OrderBy(f => f)];
        if (File.Exists(target)) return [target];
        throw new FileNotFoundException($"No such file or directory: {target}");
    }

    var filed = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
        "..", "..", "..", "..", "..", ".research", "bench"));
    if (Directory.Exists(filed))
    {
        var found = Directory.GetFiles(filed, "Benchmark*.coc").OrderBy(f => f).ToList();
        if (found.Count > 0) return found;
    }

    if (install.UserDataDir is null) return [];
    var live = Path.Combine(install.UserDataDir, "Benchmark.coc");
    return File.Exists(live) ? [live] : [];
}

static string Delta(double from, double to, bool lowerIsBetter = false)
{
    if (from <= 0) return "-";
    var change = (to - from) / from * 100;
    var good = lowerIsBetter ? change < 0 : change > 0;
    return $"{(change >= 0 ? "+" : "")}{change:N0}%{(good ? "" : "")}";
}

static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

/// <summary>
/// Ad-hoc setting overrides, applied on top of a profile:
/// <c>--set ExtraQualitySettings.cascadeShadowSplitCount=0</c>.
///
/// This is how a knob gets tried before anyone decides it belongs in a profile. Overrides go
/// through the same range check, backup and manifest as everything else, so a bad one is
/// rejected rather than written, and `revert` still undoes it.
/// </summary>
static IEnumerable<Tweak> ParseOverrides(string[] argv)
{
    for (var i = 0; i < argv.Length - 1; i++)
    {
        if (!string.Equals(argv[i], "--set", StringComparison.OrdinalIgnoreCase)) continue;
        yield return ParseOverride(argv[i + 1]);
    }
}

static Tweak ParseOverride(string spec)
{
    var equals = spec.IndexOf('=');
    if (equals <= 0)
        throw new ArgumentException($"--set wants Type.property=value, got '{spec}'.");

    var path = spec[..equals];
    var raw = spec[(equals + 1)..];

    var dot = path.LastIndexOf('.');
    if (dot <= 0)
        throw new ArgumentException($"--set wants Type.property=value, got '{spec}'.");

    // Both the short and fully-qualified type names work, since nobody wants to type the
    // namespace: ShadowsQualitySettings and Game.Settings.ShadowsQualitySettings are the same.
    var type = path[..dot];
    if (!type.StartsWith("Game.Settings.", StringComparison.Ordinal)) type = "Game.Settings." + type;

    JsonNode value =
        bool.TryParse(raw, out var b) ? JsonValue.Create(b)
        : int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? JsonValue.Create(i)
        : double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? JsonValue.Create(d)
        : JsonValue.Create(raw);

    // No declared range: this is the explicit escape hatch from the profiles' safety rails,
    // so the cost is stated as Visible rather than guessed at.
    return new Tweak(type, path[(dot + 1)..], value, Cost.Visible, "set by hand with --set");
}

int Help()
{
    Console.WriteLine("Usage");
    Console.WriteLine("  cs2patch status              Show the install, your hardware, and patch state");
    Console.WriteLine("  cs2patch list                Show available profiles and what they cost you");
    Console.WriteLine("  cs2patch apply <profile>     Apply a profile (default: traffic)");
    Console.WriteLine("  cs2patch revert              Restore the original settings");
    Console.WriteLine("  cs2patch bench [path]        Compare benchmark runs (default: .research/bench)");
    Console.WriteLine("  cs2patch tune-pc             Report what outside the game is costing you frames");
    Console.WriteLine();
    Console.WriteLine("The code mod — it loads and runs, but has not yet been shown to pay for itself");
    Console.WriteLine("  cs2patch install-mod         Install the Cs2Saver mod into the local mods folder");
    Console.WriteLine("  cs2patch uninstall-mod       Remove it again");
    Console.WriteLine();
    Console.WriteLine("Options");
    Console.WriteLine("  --path <dir>                 Point at the game install explicitly");
    Console.WriteLine("  --dll <file>                 Use a specific Cs2Saver.dll build");
    Console.WriteLine("  --set Type.property=value    Push one setting past the profile (repeatable)");
    Console.WriteLine("  --phases                     bench: split each run into paused / speed 1 / speed 3");
    Console.WriteLine("  --spikes                     bench: take the slowest 1% of frames apart");
    return 0;
}

static bool IsGameRunning() =>
    System.Diagnostics.Process.GetProcessesByName("Cities2").Length > 0;

static string? GetOption(string[] argv, string name)
{
    var i = Array.FindIndex(argv, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
    return i >= 0 && i + 1 < argv.Length ? argv[i + 1] : null;
}

static void Banner()
{
    Console.WriteLine("CS2 Performance Patcher");
    Console.WriteLine("-----------------------");
}

static void Fail(string message)
{
    var prior = Console.ForegroundColor;
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"ERROR: {message}");
    Console.ForegroundColor = prior;
}

static void Warn(string message)
{
    var prior = Console.ForegroundColor;
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"  WARNING: {message}");
    Console.ForegroundColor = prior;
}
