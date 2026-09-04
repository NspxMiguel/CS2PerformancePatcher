using System.Text.RegularExpressions;

namespace Cs2Patcher.Core;

/// <summary>
/// Locates a Cities: Skylines II installation and its user-data directory.
/// Everything here is read-only discovery; nothing is modified.
/// </summary>
public static class GameLocator
{
    public const string SteamAppId = "949230";

    /// <summary>Result of a discovery attempt. <see cref="Found"/> tells you whether the rest is meaningful.</summary>
    public sealed record GameInstall(
        bool Found,
        string? InstallDir,
        string? UserDataDir,
        string? SettingsFile,
        string? GameVersion,
        string? BuildId,
        string? Diagnostic);

    /// <summary>
    /// Finds the install by asking Steam, then falls back to the well-known default path.
    /// Never throws: a failed probe comes back as <c>Found = false</c> with a diagnostic.
    /// </summary>
    public static GameInstall Locate(string? explicitInstallDir = null)
    {
        try
        {
            var installDir = explicitInstallDir ?? FindInstallDir(out var buildId) ;
            var build = explicitInstallDir is null ? FindBuildIdSafe() : null;

            if (string.IsNullOrEmpty(installDir) || !Directory.Exists(installDir))
                return new GameInstall(false, null, null, null, null, null,
                    "Cities: Skylines II install not found. Pass the path explicitly.");

            if (!File.Exists(Path.Combine(installDir, "Cities2.exe")))
                return new GameInstall(false, installDir, null, null, null, null,
                    $"'{installDir}' exists but contains no Cities2.exe.");

            var userDir = FindUserDataDir();
            var settings = userDir is null ? null : Path.Combine(userDir, "Settings.coc");
            var version = ReadGameVersion(userDir);

            return new GameInstall(true, installDir, userDir, settings, version, build, null);
        }
        catch (Exception ex)
        {
            return new GameInstall(false, null, null, null, null, null, $"Discovery failed: {ex.Message}");
        }
    }

    /// <summary>The per-user data directory, which is where Settings.coc lives.</summary>
    public static string? FindUserDataDir()
    {
        var localLow = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "AppData", "LocalLow", "Colossal Order", "Cities Skylines II");
        return Directory.Exists(localLow) ? localLow : null;
    }

    /// <summary>Reads the version string the game writes next to its settings ("1.6.0f1 (419.d6c6) [6216.19404]").</summary>
    public static string? ReadGameVersion(string? userDataDir)
    {
        if (userDataDir is null) return null;
        var f = Path.Combine(userDataDir, "version");
        return File.Exists(f) ? File.ReadAllText(f).Trim() : null;
    }

    private static string? FindInstallDir(out string? buildId)
    {
        buildId = null;
        foreach (var library in EnumerateSteamLibraries())
        {
            var manifest = Path.Combine(library, "steamapps", $"appmanifest_{SteamAppId}.acf");
            if (!File.Exists(manifest)) continue;

            var acf = File.ReadAllText(manifest);
            buildId = MatchAcf(acf, "buildid");
            var folder = MatchAcf(acf, "installdir") ?? "Cities Skylines II";
            var candidate = Path.Combine(library, "steamapps", "common", folder);
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string? FindBuildIdSafe()
    {
        try
        {
            foreach (var library in EnumerateSteamLibraries())
            {
                var manifest = Path.Combine(library, "steamapps", $"appmanifest_{SteamAppId}.acf");
                if (File.Exists(manifest)) return MatchAcf(File.ReadAllText(manifest), "buildid");
            }
        }
        catch { /* discovery is best-effort */ }
        return null;
    }

    /// <summary>Steam's own library roots, from the registry plus libraryfolders.vdf.</summary>
    public static IEnumerable<string> EnumerateSteamLibraries()
    {
        var steam = FindSteamRoot();
        if (steam is null) yield break;

        yield return steam;

        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) yield break;

        foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"(.+?)\""))
        {
            var path = m.Groups[1].Value.Replace("\\\\", "\\");
            if (!string.Equals(path, steam, StringComparison.OrdinalIgnoreCase) && Directory.Exists(path))
                yield return path;
        }
    }

    private static string? FindSteamRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var (hive, key, value) in new[]
                     {
                         ("HKCU", @"Software\Valve\Steam", "SteamPath"),
                         ("HKLM", @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
                     })
            {
                var path = ReadRegistry(hive, key, value);
                if (path is not null)
                {
                    path = path.Replace('/', '\\');
                    if (Directory.Exists(path)) return path;
                }
            }
        }

        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        return Directory.Exists(fallback) ? fallback : null;
    }

    private static string? ReadRegistry(string hive, string key, string value)
    {
        if (!OperatingSystem.IsWindows()) return null;
#pragma warning disable CA1416 // guarded by OperatingSystem.IsWindows above
        using var root = hive == "HKCU"
            ? Microsoft.Win32.Registry.CurrentUser.OpenSubKey(key)
            : Microsoft.Win32.Registry.LocalMachine.OpenSubKey(key);
        return root?.GetValue(value) as string;
#pragma warning restore CA1416
    }

    private static string? MatchAcf(string acf, string key)
    {
        var m = Regex.Match(acf, $"\"{Regex.Escape(key)}\"\\s+\"(.*?)\"");
        return m.Success ? m.Groups[1].Value : null;
    }
}
