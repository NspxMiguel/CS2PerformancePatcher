using System.Text.Json.Nodes;

namespace Cs2Patcher.Core;

/// <summary>
/// Writes the handful of Cs2Saver settings that a tuning profile has an opinion about.
///
/// <para>The patcher and the mod are normally strangers on purpose: one edits the game's settings
/// file, the other runs inside the game, and neither needs the other to be useful. There is
/// exactly one place that separation costs something real.</para>
///
/// <para>Switching the sun's shadow on is the game's setting, so a profile has to do it. Bounding
/// how far that shadow reaches is an HDRP volume parameter the game never exposes, so only the mod
/// can do it. Measured at normal play speed, the same shadows cost six frames per second at the
/// distance the game picks and one at a hundred metres — so a profile that turns shadows on
/// without telling the mod to bound them is handing the player a five-frame bill for nothing they
/// can see. This class is that one sentence of coordination.</para>
///
/// <para>It only ever writes keys a profile named, only to a settings file that already exists,
/// and never creates one: a player who has not installed the mod is left entirely alone.</para>
/// </summary>
public static class ModTuning
{
    /// <summary>The mod's settings file, which lives beside the game's own.</summary>
    public static string SettingsPath(string userDataDir) =>
        Path.Combine(userDataDir, $"{ModInstaller.ModName}.coc");

    public sealed record Result(bool Written, string? Message);

    /// <summary>
    /// Applies whatever <paramref name="profile"/> asks of the mod.
    ///
    /// Returns <c>Written: false</c> with no message when there is nothing to do, so a caller can
    /// stay quiet in the common case rather than reporting a no-op at every player.
    /// </summary>
    public static Result Apply(string userDataDir, TuningProfile profile)
    {
        if (profile.ModShadowReach is null) return new Result(false, null);

        var path = SettingsPath(userDataDir);
        if (!File.Exists(path)) return new Result(false, null); // mod not installed, or never run

        try
        {
            var doc = CocDocument.Load(path);
            var section = doc.GetSection(ModInstaller.ModName);
            if (section is null)
                return new Result(false,
                    $"{ModInstaller.ModName}.coc has no {ModInstaller.ModName} section; left alone.");

            var was = section.Json["SunShadows"]?.GetValue<string>();
            if (was == profile.ModShadowReach) return new Result(false, null);

            section.Json["SunShadows"] = JsonValue.Create(profile.ModShadowReach);
            doc.Save(path);

            return new Result(true,
                $"Cs2Saver sun shadow reach set to {profile.ModShadowReach}"
                + (was is null ? "." : $" (was {was})."));
        }
        catch (Exception ex)
        {
            // The mod's settings are not this tool's to break. A failure here must not fail an
            // otherwise good profile apply, and the game's own settings are already written.
            return new Result(false, $"Could not set the mod's shadow reach: {ex.Message}");
        }
    }
}
