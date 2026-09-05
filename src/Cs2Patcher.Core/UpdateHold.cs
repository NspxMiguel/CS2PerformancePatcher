using System.Text.RegularExpressions;

namespace Cs2Patcher.Core;

/// <summary>What Steam is currently set to do about updating this game.</summary>
public enum UpdatePolicy
{
    /// <summary>Steam updates the game in the background whenever it likes. Its default.</summary>
    Always,

    /// <summary>Steam only updates when the game is launched. What this tool sets.</summary>
    OnLaunch,

    /// <summary>Steam prioritises this game's updates over others.</summary>
    HighPriority,

    /// <summary>The manifest was not found or does not say.</summary>
    Unknown,
}

public sealed record UpdateHoldResult(bool Success, string Message, UpdatePolicy Policy);

/// <summary>
/// Stops Steam updating the game out from under a patched install.
///
/// <para><b>Why this belongs in a patcher.</b> Everything this tool does is pinned to one build:
/// the profiles are validated against ranges read out of that build's Game.dll, and the mod is
/// compiled against its assemblies. A background update replaces both, and the first anybody
/// knows about it is the mod failing to load. Steam already has a per-game setting for this; it
/// is just buried, and nothing in the game hints that it matters.</para>
///
/// <para><b>What it changes.</b> One field in <c>appmanifest_949230.acf</c>, which is Steam's own
/// record of this game on this machine. <c>AutoUpdateBehavior</c> goes from 0 — update whenever —
/// to 1, update only when launched. That is a setting Steam offers in its own right-click menu;
/// this writes the same value without making anybody find it. Nothing else in the file is
/// touched, and putting it back is the same edit in reverse.</para>
///
/// <para><b>The one thing to get right.</b> Steam holds this file open and rewrites it from
/// memory, so an edit made while Steam is running is usually lost and occasionally corrupts the
/// entry. This refuses to write while Steam is running rather than silently losing the change.</para>
/// </summary>
public static class UpdateHold
{
    public const string AppId = "949230";

    private static readonly Regex Behaviour =
        new(@"""AutoUpdateBehavior""\s*""(\d+)""", RegexOptions.Compiled);

    /// <summary>
    /// Steam keeps the manifest one level above <c>common</c>, alongside the other installed
    /// games, so it is found from the game directory rather than by guessing at a Steam install.
    /// </summary>
    public static string? ManifestPath(string? installDir)
    {
        if (string.IsNullOrWhiteSpace(installDir)) return null;

        var common = Directory.GetParent(installDir);          // ...\steamapps\common
        var steamapps = common?.Parent;                        // ...\steamapps
        if (steamapps is null) return null;

        var path = Path.Combine(steamapps.FullName, $"appmanifest_{AppId}.acf");
        return File.Exists(path) ? path : null;
    }

    public static UpdatePolicy Read(string? installDir)
    {
        var path = ManifestPath(installDir);
        if (path is null) return UpdatePolicy.Unknown;

        try
        {
            var match = Behaviour.Match(File.ReadAllText(path));
            if (!match.Success) return UpdatePolicy.Unknown;

            return match.Groups[1].Value switch
            {
                "0" => UpdatePolicy.Always,
                "1" => UpdatePolicy.OnLaunch,
                "2" => UpdatePolicy.HighPriority,
                _ => UpdatePolicy.Unknown,
            };
        }
        catch
        {
            return UpdatePolicy.Unknown;
        }
    }

    public static bool IsSteamRunning() =>
        System.Diagnostics.Process.GetProcessesByName("steam").Length > 0;

    /// <summary>Set the policy. <paramref name="hold"/> false puts Steam's own default back.</summary>
    public static UpdateHoldResult Set(string? installDir, bool hold)
    {
        var path = ManifestPath(installDir);
        if (path is null)
            return new UpdateHoldResult(false,
                "Steam's manifest for this game was not found, so its update policy cannot be read "
                + "or changed. A non-Steam copy has nothing to hold back.", UpdatePolicy.Unknown);

        if (IsSteamRunning())
            return new UpdateHoldResult(false,
                "Steam is running. It keeps this file open and rewrites it from memory on exit, so "
                + "the change would be lost. Close Steam completely and run this again.",
                Read(installDir));

        try
        {
            var text = File.ReadAllText(path);
            var match = Behaviour.Match(text);
            if (!match.Success)
                return new UpdateHoldResult(false,
                    "Steam's manifest has no AutoUpdateBehavior line, so its shape has changed and "
                    + "this tool will not guess at it.", UpdatePolicy.Unknown);

            var want = hold ? "1" : "0";
            if (match.Groups[1].Value == want)
                return new UpdateHoldResult(true,
                    hold ? "Already held: Steam updates this game only when you launch it."
                         : "Already Steam's default: it updates this game whenever it likes.",
                    hold ? UpdatePolicy.OnLaunch : UpdatePolicy.Always);

            // Rewrite only the digit, leaving every byte of Steam's formatting alone. This file
            // belongs to Steam and it is fussy about it.
            var updated = text[..match.Groups[1].Index] + want + text[(match.Groups[1].Index + match.Groups[1].Length)..];
            File.WriteAllText(path, updated);

            return new UpdateHoldResult(true,
                hold ? "Held. Steam will now only update this game when you launch it, so a patched "
                     + "install and the mod survive until you choose otherwise."
                     : "Released. Steam updates this game whenever it likes again, as it does by default.",
                hold ? UpdatePolicy.OnLaunch : UpdatePolicy.Always);
        }
        catch (Exception ex)
        {
            return new UpdateHoldResult(false, $"Could not write Steam's manifest: {ex.Message}",
                UpdatePolicy.Unknown);
        }
    }
}
