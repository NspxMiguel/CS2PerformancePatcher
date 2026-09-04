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

        Console.WriteLine($"  {p.Id,-8} {p.Name}");
        Console.WriteLine($"           {p.Description}");
        Console.WriteLine($"           {p.Tweaks.Count} changes — {free} free, {cheap} cheap, {visible} visible");
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

int Help()
{
    Console.WriteLine("Usage");
    Console.WriteLine("  cs2patch status              Show the install, your hardware, and patch state");
    Console.WriteLine("  cs2patch list                Show available profiles and what they cost you");
    Console.WriteLine("  cs2patch apply <profile>     Apply a profile (default: traffic)");
    Console.WriteLine("  cs2patch revert              Restore the original settings");
    Console.WriteLine();
    Console.WriteLine("Options");
    Console.WriteLine("  --path <dir>                 Point at the game install explicitly");
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
