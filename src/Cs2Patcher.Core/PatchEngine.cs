using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Cs2Patcher.Core;

/// <summary>Records what a patch did, so it can be undone exactly and audited later.</summary>
public sealed class PatchManifest
{
    public string ProfileId { get; set; } = "";
    public string ProfileName { get; set; } = "";
    public DateTimeOffset PatchedAtUtc { get; set; }
    public string GameVersion { get; set; } = "";
    public string OriginalSha256 { get; set; } = "";
    public string PatchedSha256 { get; set; } = "";
    public List<string> Applied { get; set; } = [];
    public List<string> Skipped { get; set; } = [];
}

public sealed record PatchResult(
    bool Success,
    string Message,
    IReadOnlyList<string> Applied,
    IReadOnlyList<string> Skipped);

/// <summary>
/// Applies and reverts a <see cref="TuningProfile"/> against the game's Settings.coc.
///
/// Two rules this type will not break:
///  1. It never writes without first taking a byte-exact backup of the original.
///  2. It never writes a value outside the range the game itself declares.
/// </summary>
public sealed class PatchEngine(string settingsFilePath, string backupDirectory)
{
    public const string BackupFolderName = "CS2PerformancePatcher_Backup";
    private const string OriginalName = "Settings.coc.original";
    private const string ManifestName = "patch-manifest.json";

    private static readonly JsonSerializerOptions ManifestJson = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private string OriginalPath => Path.Combine(backupDirectory, OriginalName);
    private string ManifestPath => Path.Combine(backupDirectory, ManifestName);

    public static PatchEngine For(GameLocator.GameInstall install)
    {
        if (!install.Found || install.SettingsFile is null || install.UserDataDir is null)
            throw new InvalidOperationException("Game install was not located.");

        return new PatchEngine(
            install.SettingsFile,
            Path.Combine(install.UserDataDir, BackupFolderName));
    }

    public bool IsPatched => File.Exists(ManifestPath) && File.Exists(OriginalPath);

    public PatchManifest? ReadManifest()
    {
        if (!File.Exists(ManifestPath)) return null;
        try
        {
            return JsonSerializer.Deserialize<PatchManifest>(File.ReadAllText(ManifestPath), ManifestJson);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Applies <paramref name="profile"/>. Re-applying over an existing patch always starts
    /// from the pristine backup, so profiles never compound on top of each other.
    /// </summary>
    public PatchResult Apply(TuningProfile profile, IEnumerable<Tweak>? extraTweaks = null)
    {
        if (!File.Exists(settingsFilePath))
            return new PatchResult(false,
                $"Settings.coc not found at '{settingsFilePath}'. Run the game once so it writes one.", [], []);

        Directory.CreateDirectory(backupDirectory);

        // Always patch the pristine original, never a previously patched file.
        if (!File.Exists(OriginalPath))
            File.Copy(settingsFilePath, OriginalPath, overwrite: false);

        var originalText = File.ReadAllText(OriginalPath);
        var originalHash = Sha256(originalText);

        CocDocument doc;
        try
        {
            doc = CocDocument.Parse(originalText);
        }
        catch (Exception ex)
        {
            return new PatchResult(false, $"Could not parse Settings.coc: {ex.Message}", [], []);
        }

        var applied = new List<string>();
        var skipped = new List<string>();

        // Profiles are built by stacking tiers, so a stricter tier can restate a setting the
        // tier below it already set. Keep only the last word on each setting.
        var effective = profile.Tweaks
            .Concat(extraTweaks ?? [])
            .GroupBy(t => (t.TypeName, t.Property))
            .Select(g => g.Last());

        foreach (var tweak in effective)
        {
            if (!tweak.IsInRange())
            {
                skipped.Add($"{Describe(tweak)} — value outside the game's declared range " +
                            $"[{tweak.Min?.ToString() ?? "-"}, {tweak.Max?.ToString() ?? "-"}]");
                continue;
            }

            try
            {
                var block = doc.GetOrAddQualitySetting(tweak.TypeName);
                var before = block[tweak.Property]?.ToJsonString() ?? "unset";
                var after = tweak.Value.ToJsonString();
                block[tweak.Property] = tweak.Value.DeepClone();

                applied.Add(before == after
                    ? $"{Describe(tweak)}  (already {after})"
                    : $"{Describe(tweak)}  ({before} -> {after})");
            }
            catch (Exception ex)
            {
                skipped.Add($"{Describe(tweak)} — {ex.Message}");
            }
        }

        if (applied.Count == 0)
            return new PatchResult(false, "Nothing was applied.", applied, skipped);

        var patchedText = doc.Serialize();

        // Write through a temp file so an interrupted write cannot truncate the real one.
        var temp = settingsFilePath + ".tmp";
        File.WriteAllText(temp, patchedText, new UTF8Encoding(false));
        File.Move(temp, settingsFilePath, overwrite: true);

        var manifest = new PatchManifest
        {
            ProfileId = profile.Id,
            ProfileName = profile.Name,
            PatchedAtUtc = DateTimeOffset.UtcNow,
            GameVersion = GameLocator.ReadGameVersion(Path.GetDirectoryName(settingsFilePath)) ?? "unknown",
            OriginalSha256 = originalHash,
            PatchedSha256 = Sha256(patchedText),
            Applied = applied,
            Skipped = skipped,
        };
        File.WriteAllText(ManifestPath, JsonSerializer.Serialize(manifest, ManifestJson));

        return new PatchResult(true,
            $"Applied '{profile.Name}': {applied.Count} settings changed" +
            (skipped.Count > 0 ? $", {skipped.Count} skipped." : "."),
            applied, skipped);
    }

    /// <summary>Restores the pristine Settings.coc and removes the patch record.</summary>
    public PatchResult Revert()
    {
        if (!File.Exists(OriginalPath))
            return new PatchResult(false, "No backup found — nothing to revert.", [], []);

        File.Copy(OriginalPath, settingsFilePath, overwrite: true);
        File.Delete(OriginalPath);
        if (File.Exists(ManifestPath)) File.Delete(ManifestPath);

        return new PatchResult(true, "Reverted to the original Settings.coc.", [], []);
    }

    /// <summary>
    /// True when the live file no longer matches what we wrote — meaning the game or the
    /// player changed settings since. Re-applying is safe; it restarts from the backup.
    /// </summary>
    public bool HasDriftedSincePatch()
    {
        var manifest = ReadManifest();
        if (manifest is null || !File.Exists(settingsFilePath)) return false;
        return Sha256(File.ReadAllText(settingsFilePath)) != manifest.PatchedSha256;
    }

    private static string Describe(Tweak t) =>
        $"[{t.Cost}] {t.TypeName.Replace("Game.Settings.", "")}.{t.Property}";

    private static string Sha256(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
