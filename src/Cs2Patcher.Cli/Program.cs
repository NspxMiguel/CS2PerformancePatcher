using System.Globalization;
using System.Text.Json.Nodes;
using Cs2Patcher.Core;

var command = FirstCommand(args);
var explicitPath = GetOption(args, "--path");
Text.TrySet(GetOption(args, "--lang"));

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
        case "recommend": return Recommend();
        case "hold-update": return HoldUpdate();
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
    Console.WriteLine(Text.Installation);
    Console.WriteLine($"  {Text.Path,-13} {install.InstallDir}");
    Console.WriteLine($"  {Text.UserData,-13} {install.UserDataDir}");
    Console.WriteLine($"  {Text.GameVersion,-13} {install.GameVersion ?? "?"}");
    if (install.BuildId is not null) Console.WriteLine($"  Steam build   {install.BuildId}");

    if (hardware is not null)
    {
        Console.WriteLine();
        Console.WriteLine(Text.HardwareHeading);
        Console.WriteLine($"  GPU           {hardware.Gpu ?? "?"}{(hardware.HasVram ? $" — {hardware.VramMegabytes} MB" : "")}");
        Console.WriteLine($"  CPU           {hardware.Cpu ?? "?"} ({hardware.CoreCount} threads)");
        Console.WriteLine($"  System RAM    {hardware.SystemMemoryGb:N1} GB");
        Console.WriteLine($"  Unity         {hardware.UnityVersion ?? "?"}");
    }

    Console.WriteLine();
    var manifest = engine.ReadManifest();
    if (manifest is null)
    {
        Console.WriteLine($"{Text.PatchStatus}:  {Text.NotPatched}");
        Console.WriteLine("  Apply one with:  cs2patch apply traffic");
    }
    else
    {
        Console.WriteLine($"{Text.PatchStatus}:  {Text.Patched} - '{manifest.ProfileName}'");
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
    // Ordered by how good each tier looks, which is not the same as how fast it is: the bottom
    // three are within noise of each other on the machine they were measured on.
    Console.WriteLine(Text.ProfilesHeading);
    Console.WriteLine();
    Console.WriteLine($"  {Text.TestedOn,-11} {ProfileAdvisor.ReferenceMachine}");
    Console.WriteLine($"  {Text.MeasuredOn,-11} {Text.MeasuredHow}");
    Console.WriteLine($"  {Text.UntouchedIs,-11} {Text.UntouchedNumbers}");
    Console.WriteLine();
    Console.WriteLine($"  {Text.YoursWillDiffer}");
    Console.WriteLine();
    Console.WriteLine($"  {"",-14} {"",-24}{"fps",6}{"1% low",8}{"at 60",7}{"gain",8}");

    foreach (var p in Profiles.All)
    {
        var m = p.Measured;
        var numbers = m is null
            ? $"{"—",6}{"—",8}{"—",7}{"—",8}"
            : $"{m.Fps,6:N1}{m.OnePercentLow,8:N1}{m.ShareAtSixty,6}%{"+" + m.GainPercent + "%",8}";

        Console.WriteLine($"  {p.Id,-14} {Truncate(Text.ProfileName(p), 23),-24}{numbers}");
        Console.WriteLine($"                 {Text.ProfileDescription(p)}");
        Console.WriteLine();
    }

    Console.WriteLine($"  {Text.NotSure + ":",-12} cs2patch recommend --targets");
    Console.WriteLine($"  {Text.ApplyWith + ":",-12} cs2patch apply <id>        {Text.UndoWith}:  cs2patch revert");
    return 0;
}

int Recommend()
{
    if (args.Contains("--targets")) return Targets();

    var wantMax = args.Contains("--max");
    var stable = args.Contains("--stable");

    var target = double.TryParse(GetOption(args, "--fps"), NumberStyles.Float,
        CultureInfo.InvariantCulture, out var parsed) && parsed > 0 ? parsed : (double?)null;

    var pick = wantMax
        ? ProfileAdvisor.Fastest(hardware)
        : ProfileAdvisor.Recommend(hardware, target, stable ? Criterion.Stable : Criterion.Average);

    Console.WriteLine($"{Text.Recommended}:  {Text.ProfileName(pick.Profile)}   (cs2patch apply {pick.Profile.Id})");
    Console.WriteLine();
    Console.WriteLine($"  {Text.Why,-8} {pick.Because}");
    Console.WriteLine($"  {Text.Expect,-8} {Text.ExpectLine(pick.ExpectedFps, pick.ExpectedLow)}");
    Console.WriteLine($"  {Text.Costs,-8} {Text.ProfileDescription(pick.Profile)}");
    Console.WriteLine();

    // Said plainly, because a projection presented as a measurement is worse than no number.
    Console.WriteLine(Text.EstimateWarning);

    if (pick.Profile.Id is "handsome" or "skyline" or "potato" or "super-potato" or "mega-potato")
    {
        Console.WriteLine();
        Console.WriteLine(Text.InstallModToo);
        Console.WriteLine("    cs2patch install-mod");
    }

    return 0;
}

int Targets()
{
    Console.WriteLine(Text.TargetsHeading);
    Console.WriteLine();
    Console.WriteLine(Text.TargetsExplainer);
    Console.WriteLine();
    Console.WriteLine($"  {Text.ColTarget,-10}{Text.ColOnAverage,-26}{Text.ColNeverDropping,-26}");
    Console.WriteLine();

    foreach (var option in ProfileAdvisor.Survey(hardware))
    {
        var average = option.OnAverage is { } a
            ? $"{Truncate(Text.ProfileName(a.Profile), 16),-17}{a.ExpectedFps,5:N0}fps"
            : Text.OutOfReach;

        var stable = option.Stable is { } s
            ? $"{Truncate(Text.ProfileName(s.Profile), 16),-17}{s.ExpectedLow,5:N0}low"
            : Text.OutOfReach;

        Console.WriteLine($"  {option.Label,-10}{average,-26}{stable,-26}");
    }

    var fastest = ProfileAdvisor.Fastest(hardware);
    var most = $"{Truncate(Text.ProfileName(fastest.Profile), 16),-17}{fastest.ExpectedFps,5:N0}fps";
    var mostLow = $"{Truncate(Text.ProfileName(fastest.Profile), 16),-17}{fastest.ExpectedLow,5:N0}low";
    Console.WriteLine($"  {Text.Most,-10}{most,-26}{mostLow,-26}");
    Console.WriteLine();
    // Worth saying here rather than only in the mod, because the table above makes sixty look
    // like a much bigger jump than it is: Handsome misses it by two frames and both of them are
    // in the trees.
    Console.WriteLine(Text.FoliageNote);
    Console.WriteLine();
    Console.WriteLine($"  {Text.ApplyWith}:  cs2patch apply <id>       {Text.SeeIdsWith}:  cs2patch list");
    Console.WriteLine($"  {Text.ProjectedFrom} {ProfileAdvisor.ReferenceMachine}. {Text.YoursWillDifferShort}");

    return 0;
}

int HoldUpdate()
{
    var release = args.Contains("--release");
    var result = UpdateHold.Set(install.InstallDir, hold: !release);

    Console.WriteLine(result.Success ? $"OK — {result.Message}" : $"FAILED — {result.Message}");

    if (result.Success && !release)
    {
        Console.WriteLine();
        Console.WriteLine("  The game still updates when you press Play, which is how Steam works —");
        Console.WriteLine("  there is no setting that refuses an update outright. What this buys is");
        Console.WriteLine("  that it will not happen behind your back while the machine is idle.");
        Console.WriteLine("  Undo with:  cs2patch hold-update --release");
    }

    return result.Success ? 0 : 1;
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

    // Two settings depend on the machine rather than on the profile: how much mesh memory there
    // is to work with, and what "two thirds of the resolution" actually means on this display.
    var extras = new List<Tweak>();
    if (hardware?.HasVram == true)
        extras.Add(Profiles.MeshBudgetFor(hardware.VramMegabytes));

    if (profile.ScreenScale is { } scale)
    {
        var display = DisplayProbe.Current();
        var resolution = Profiles.ResolutionFor(scale, display);

        if (resolution is not null) extras.Add(resolution);
        else Warn("The desktop resolution could not be read, so this tier's resolution cut was skipped.");
    }

    extras.AddRange(ParseOverrides(args));

    var result = engine.Apply(profile, extras);

    Console.WriteLine(result.Success ? $"OK — {result.Message}" : $"FAILED — {result.Message}");
    Console.WriteLine();

    foreach (var line in result.Applied) Console.WriteLine($"  + {line}");
    foreach (var line in result.Skipped) Console.WriteLine($"  ! {line}");

    // The one thing a profile can want from the mod. Silent when there is nothing to say, which
    // is every profile that does not turn the sun's shadow on and every player without the mod.
    if (result.Success && install.UserDataDir is not null)
    {
        var mod = ModTuning.Apply(install.UserDataDir, profile);
        if (mod.Message is not null) Console.WriteLine($"  {(mod.Written ? "+" : "!")} {mod.Message}");
    }

    if (result.Success)
    {
        Console.WriteLine();
        Console.WriteLine($"  Original saved to  {Path.Combine(install.UserDataDir!, PatchEngine.BackupFolderName)}");
        Console.WriteLine("  Undo any time with:  cs2patch revert");

        // The lower tiers are the ones that end up looking bleached rather than merely simpler,
        // and the mod's grading costs nothing, so it is worth saying out loud at the moment
        // somebody has just chosen one.
        if (profile.Id is "skyline" or "potato" or "super-potato")
        {
            Console.WriteLine();
            Console.WriteLine("  This tier trades looks for frames. The mod's 'Cel' look puts a deliberate");
            Console.WriteLine("  style back on top for free — turn it on in its options page.");
        }
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
        Console.WriteLine("  It starts inert. Enable it in a playset, then pick a preset on its options page.");
        Console.WriteLine("  Declutter is what the 'skyline' profile was measured with.");
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

    Console.WriteLine($"  {"run",-26}{"frames",7}{"avg fps",9}{"1% low",8}{"0.1% low",10}{"gpu ms",8}{"render ms",10}{"gpu-bound",10}{"worst",8}{">50ms",7}");
    foreach (var run in runs)
    {
        var e = run.Effective;
        Console.WriteLine($"  {Truncate(run.Label, 25),-26}{e.Frames,7}{e.AvgFps,9:N1}{e.Low1PctFps,8:N1}"
                          + $"{e.Low01PctFps,10:N1}{run.GpuOnly.AvgMs,8:N1}{run.CpuRender.AvgMs,10:N1}{run.GpuBoundPercent,9:N0}%"
                          + $"{run.WorstMs,7:N0}ms{run.StallsOver(50),7}");
    }

    if (runs.Count > 1)
    {
        Console.WriteLine();
        Console.WriteLine($"  Against '{first.Label}'");
        var baseline = first.Effective;
        foreach (var run in runs.Skip(1))
        {
            var e = run.Effective;
            Console.WriteLine($"  {Truncate(run.Label, 25),-26}{"",7}{Delta(baseline.AvgFps, e.AvgFps),9}"
                              + $"{Delta(baseline.Low1PctFps, e.Low1PctFps),8}{Delta(baseline.Low01PctFps, e.Low01PctFps),10}"
                              + $"{Delta(first.GpuOnly.AvgMs, run.GpuOnly.AvgMs, lowerIsBetter: true),8}"
                              + $"{Delta(first.CpuRender.AvgMs, run.CpuRender.AvgMs, lowerIsBetter: true),10}");
        }
    }

    if (GetOption(args, "--phases") is not null || args.Contains("--phases"))
    {
        // "Sixty stable" is a claim about the distribution, not about its average, so the phase
        // view carries the share of frames that actually met a rate. Everything else here can
        // read well while a third of the frames miss.
        var targetFps = double.TryParse(GetOption(args, "--target"), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var parsed) && parsed > 0 ? parsed : 60;

        Console.WriteLine();
        Console.WriteLine("  By phase. Normal play is 'speed 1'; 'speed 3' is fast-forward, which");
        Console.WriteLine("  no one plays at and which drags every whole-run average down.");
        Console.WriteLine($"    {"",-10}{"frames",7}{"avg fps",9}{"1% low",8}{"0.1% low",10}{$"at {targetFps:N0}",8}{"at 30",8}");

        foreach (var run in runs)
        {
            Console.WriteLine($"  {run.Label}");
            foreach (var (name, frames) in run.PhaseSlices())
            {
                var stats = FrameStats.From(frames);
                Console.WriteLine($"    {name,-10}{stats.Frames,7}{stats.AvgFps,9:N1}{stats.Low1PctFps,8:N1}"
                                  + $"{stats.Low01PctFps,10:N1}"
                                  + $"{BenchmarkRun.ShareMeeting(frames, targetFps),7:N0}%"
                                  + $"{BenchmarkRun.ShareMeeting(frames, 30),7:N0}%");
            }
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
    Console.WriteLine("  cs2patch list                Show available profiles, what they cost and what they gave");
    Console.WriteLine("  cs2patch recommend           Pick a profile for this machine");
    Console.WriteLine("  cs2patch recommend --targets What frame rates this machine can hold");
    Console.WriteLine("  cs2patch apply <profile>     Apply a profile (default: traffic)");
    Console.WriteLine("  cs2patch revert              Restore the original settings");
    Console.WriteLine("  cs2patch bench [path]        Compare benchmark runs (default: .research/bench)");
    Console.WriteLine("  cs2patch tune-pc             Report what outside the game is costing you frames");
    Console.WriteLine("  cs2patch hold-update         Stop Steam updating the game in the background");
    Console.WriteLine();
    Console.WriteLine("The code mod — it loads and runs, but has not yet been shown to pay for itself");
    Console.WriteLine("  cs2patch install-mod         Install the Cs2Saver mod into the local mods folder");
    Console.WriteLine("  cs2patch uninstall-mod       Remove it again");
    Console.WriteLine();
    Console.WriteLine("Options");
    Console.WriteLine("  --path <dir>                 Point at the game install explicitly");
    Console.WriteLine("  --lang pt | --lang en         Force a language (defaults to the system one)");
    Console.WriteLine("  --dll <file>                 Use a specific Cs2Saver.dll build");
    Console.WriteLine("  --set Type.property=value    Push one setting past the profile (repeatable)");
    Console.WriteLine("  --phases                     bench: split each run into paused / speed 1 / speed 3");
    Console.WriteLine("  --spikes                     bench: take the slowest 1% of frames apart");
    Console.WriteLine("  --target <fps>                bench --phases: what share of frames met it (default 60)");
    Console.WriteLine("  --fps <n> --stable --max      recommend: aim at a number, or at the most frames");
    return 0;
}

static bool IsGameRunning() =>
    System.Diagnostics.Process.GetProcessesByName("Cities2").Length > 0;

// The command is the first argument that is not an option. The help text presents
// --lang and --path as options rather than as things that must follow the command,
// so `cs2patch --lang en list` has to work; before this it read "--lang" as the
// command and failed on it.
static string FirstCommand(string[] argv)
{
    var valued = new[] { "--path", "--lang", "--dll", "--set", "--target", "--fps" };
    for (var i = 0; i < argv.Length; i++)
    {
        var a = argv[i];
        if (string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "-h", StringComparison.OrdinalIgnoreCase)) return "--help";
        if (!a.StartsWith('-')) return a.ToLowerInvariant();
        if (valued.Contains(a, StringComparer.OrdinalIgnoreCase)) i++;
    }
    return "status";
}

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
