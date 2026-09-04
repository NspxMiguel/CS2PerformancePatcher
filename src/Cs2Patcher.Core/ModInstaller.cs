namespace Cs2Patcher.Core;

/// <summary>
/// Installs and removes the Cs2Saver code mod.
///
/// This is kept strictly separate from <see cref="PatchEngine"/>, and for a reason: the settings
/// patch is verified end to end, while mod installation is not. Someone should be able to take
/// the proven half without the unproven half, and be told clearly which is which.
///
/// A local mod in Cities: Skylines II is a folder under the user's local mod root containing the
/// managed assembly. The game discovers it through its asset database and instantiates every
/// type deriving from <c>IMod</c>.
/// </summary>
public static class ModInstaller
{
    public const string ModName = "Cs2Saver";
    private const string ModAssembly = "Cs2Saver.dll";

    public sealed record InstallResult(bool Success, string Message, string? InstalledPath);

    /// <summary>
    /// Where the game looks for locally installed mods, as declared by its own
    /// <c>.cache/Mods/mod_directory.json</c>.
    /// </summary>
    public static string LocalModsRoot(string userDataDir) =>
        Path.Combine(userDataDir, ".cache", "Mods", "local");

    public static string ModDirectory(string userDataDir) =>
        Path.Combine(LocalModsRoot(userDataDir), ModName);

    public static bool IsInstalled(string userDataDir) =>
        File.Exists(Path.Combine(ModDirectory(userDataDir), ModAssembly));

    /// <summary>
    /// Copies the built mod assembly into the local mods folder.
    ///
    /// Only files this installer owns are ever written or removed, so a hand-installed mod of
    /// the same name is refused rather than silently overwritten.
    /// </summary>
    public static InstallResult Install(string userDataDir, string builtAssemblyPath)
    {
        if (!File.Exists(builtAssemblyPath))
            return new InstallResult(false,
                $"Built mod not found at '{builtAssemblyPath}'. Build Cs2Saver.Mod first.", null);

        var localRoot = LocalModsRoot(userDataDir);
        if (!Directory.Exists(localRoot))
            return new InstallResult(false,
                $"The game's local mods folder does not exist yet ({localRoot}). " +
                "Run the game once so it creates one.", null);

        try
        {
            var target = ModDirectory(userDataDir);
            Directory.CreateDirectory(target);

            var destination = Path.Combine(target, ModAssembly);
            File.Copy(builtAssemblyPath, destination, overwrite: true);

            return new InstallResult(true,
                $"Installed {ModName}. Restart the game and enable it in the mods menu.",
                destination);
        }
        catch (Exception ex)
        {
            return new InstallResult(false, $"Could not install: {ex.Message}", null);
        }
    }

    /// <summary>Removes the mod folder, but only if it contains nothing we did not put there.</summary>
    public static InstallResult Uninstall(string userDataDir)
    {
        var target = ModDirectory(userDataDir);
        if (!Directory.Exists(target))
            return new InstallResult(true, $"{ModName} was not installed.", null);

        try
        {
            var unexpected = Directory.EnumerateFileSystemEntries(target)
                .Select(Path.GetFileName)
                .Where(name => !string.Equals(name, ModAssembly, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (unexpected.Count > 0)
                return new InstallResult(false,
                    $"'{target}' holds files this installer did not create " +
                    $"({string.Join(", ", unexpected)}). Remove it by hand.", target);

            Directory.Delete(target, recursive: true);
            return new InstallResult(true, $"Removed {ModName}.", null);
        }
        catch (Exception ex)
        {
            return new InstallResult(false, $"Could not uninstall: {ex.Message}", target);
        }
    }
}
