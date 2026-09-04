using System.Text.Json.Nodes;

namespace Cs2Patcher.Core;

/// <summary>How much a tweak costs you visually. Profiles are built by stacking tiers.</summary>
public enum Cost
{
    /// <summary>No perceptible difference at normal play distance. Pure waste removal.</summary>
    Free,

    /// <summary>Detectable only if you go looking for it — a pixel-peeping difference.</summary>
    Cheap,

    /// <summary>You will see this. Reserved for profiles that explicitly trade looks for frames.</summary>
    Visible,
}

/// <summary>
/// One setting change. <see cref="Min"/>/<see cref="Max"/> mirror the ranges declared by the
/// game's own <c>[SettingsUISlider]</c> attributes, read out of Game.dll — a value outside
/// them is rejected before it ever reaches disk.
/// </summary>
public sealed record Tweak(
    string TypeName,
    string Property,
    JsonNode Value,
    Cost Cost,
    string Why,
    double? Min = null,
    double? Max = null)
{
    public bool IsInRange()
    {
        if (Min is null && Max is null) return true;
        if (!TryGetNumber(out var v)) return true; // non-numeric settings carry no range

        return (Min is null || v >= Min.Value) && (Max is null || v <= Max.Value);
    }

    /// <summary>
    /// A JsonValue is bound to the CLR type it was built from, so one created from an int
    /// refuses GetValue&lt;double&gt;() even though its kind is Number. Try each numeric
    /// type rather than assuming.
    /// </summary>
    private bool TryGetNumber(out double value)
    {
        value = 0;
        if (Value is not JsonValue jv) return false;

        if (jv.TryGetValue<double>(out var d)) { value = d; return true; }
        if (jv.TryGetValue<int>(out var i)) { value = i; return true; }
        if (jv.TryGetValue<long>(out var l)) { value = l; return true; }
        if (jv.TryGetValue<float>(out var f)) { value = f; return true; }
        if (jv.TryGetValue<decimal>(out var m)) { value = (double)m; return true; }

        return false;
    }
}

public sealed record TuningProfile(string Id, string Name, string Description, IReadOnlyList<Tweak> Tweaks);

/// <summary>
/// The built-in profiles.
///
/// Design rule: every entry must justify itself. A frame costs what it draws, so we cut
/// work that produces no pixels before we cut anything you can see. The order below is
/// the order of the evidence — the shadow pass was measured at ~40ms of the frame and
/// ~72% of all draw calls, so it is attacked first and hardest.
/// </summary>
public static class Profiles
{
    // Type discriminators as they appear in Settings.coc.
    private const string Shadows      = "Game.Settings.ShadowsQualitySettings";
    private const string Lod          = "Game.Settings.LevelOfDetailQualitySettings";
    private const string Extra        = "Game.Settings.ExtraQualitySettings";
    private const string Dof          = "Game.Settings.DepthOfFieldQualitySettings";
    private const string MotionBlur   = "Game.Settings.MotionBlurQualitySettings";
    private const string Volumetrics  = "Game.Settings.VolumetricsQualitySettings";
    private const string Ssao         = "Game.Settings.SSAOQualitySettings";
    private const string Ssr          = "Game.Settings.SSRQualitySettings";
    private const string Ssgi         = "Game.Settings.SSGIQualitySettings";
    private const string Clouds       = "Game.Settings.CloudsQualitySettings";
    private const string Water        = "Game.Settings.WaterQualitySettings";
    private const string Terrain      = "Game.Settings.TerrainQualitySettings";
    private const string Texture      = "Game.Settings.TextureQualitySettings";

    /// <summary>
    /// Costs nothing you can see. Every entry here removes work that lands on zero or
    /// near-zero pixels. This is the profile to recommend to someone who does not want
    /// to think about it.
    /// </summary>
    public static readonly TuningProfile FreeWins = new(
        "free", "Free Wins",
        "Removes rendering work that produces no visible pixels. No perceptible visual change.",
        [
            // Depth of field on a camera that mostly looks at a city from above blurs
            // almost nothing, and costs a full-screen pass to do it.
            new(Dof, "enabled", false, Cost.Free,
                "Depth of field blurs a top-down strategy view that has nothing to defocus."),

            // The game's motion vectors are known-broken for vertex-animated geometry,
            // so blur produces ghosting artifacts on top of costing frame time.
            new(MotionBlur, "enabled", false, Cost.Free,
                "Motion blur costs a pass and rides on motion vectors that ghost on animated meshes."),

            // These two are the game's own shadow-caster rejection thresholds. They have
            // no UI slider, so a player can only reach them through the Low preset (2.0).
            new(Shadows, "shadowCullingThresholdHeight", 3.0, Cost.Free,
                "Drops shadow casters too short to read as a shadow. Game presets stop at 2.0."),
            new(Shadows, "shadowCullingThresholdVolume", 4.0, Cost.Free,
                "Drops shadow casters too small to read as a shadow. Game presets stop at 2.0."),

            // Distant clouds casting shadows costs a shadow pass for a soft gradient
            // nobody attributes to clouds.
            new(Clouds, "distanceCloudsShadows", false, Cost.Free,
                "Distant cloud shadows cost a pass for an effect that reads as ambient shading."),
        ]);

    /// <summary>
    /// The city stays sharp and legible; the things you never zoom into stop being
    /// rendered as if you might. This is the default recommendation.
    /// </summary>
    public static readonly TuningProfile TrafficSim = new(
        "traffic", "Traffic Sim",
        "Free Wins, plus cuts to effects you do not look at. City and traffic stay sharp.",
        [
            .. FreeWins.Tweaks,

            // Four cascades means the scene is re-rasterized into four shadow maps.
            // Two is the game's own Low preset and the single biggest lever available.
            new(Extra, "enabled", true, Cost.Cheap, "Required for the cascade count below to apply."),
            new(Extra, "cascadeShadowSplitCount", 2, Cost.Cheap,
                "Halves directional shadow cascades. Game's own Low preset value.", 0, 4),

            new(Shadows, "shadowCullingThresholdHeight", 4.0, Cost.Cheap,
                "Pushes shadow-caster rejection past the Free tier."),
            new(Shadows, "shadowCullingThresholdVolume", 6.0, Cost.Cheap,
                "Pushes shadow-caster rejection past the Free tier."),

            // Screen-space effects are half-res ray marches; step count is a near-linear cost.
            new(Ssr, "maxRaySteps", 12, Cost.Cheap,
                "Reflection ray steps cut from 32. Reflections stay, they just resolve shorter.", 1, 128),
            new(Ssao, "stepCount", 4, Cost.Cheap, "Ambient occlusion sample steps.", 2, 32),
            new(Ssao, "maxPixelRadius", 24, Cost.Cheap, "Ambient occlusion search radius.", 16, 256),

            new(Volumetrics, "budget", 0.15, Cost.Cheap,
                "Volumetric fog resolution budget, from 0.33.", 0.001, 1),
            new(Volumetrics, "resolutionDepthRatio", 0.5, Cost.Cheap,
                "Volumetric depth slices, from 0.666.", 0.001, 1),

            // Global illumination is the single most expensive optional pass in HDRP.
            new(Ssgi, "enabled", false, Cost.Cheap,
                "Screen-space global illumination: highest-cost optional pass in the pipeline."),

            new(Water, "maxTessellationFactor", 4.0, Cost.Cheap, "Water tessellation.", 0, 15),
            new(Water, "tessellationFactorFadeStart", 80.0, Cost.Cheap, "Water detail fades sooner.", 0, 4000),
            new(Water, "tessellationFactorFadeRange", 600.0, Cost.Cheap, "Water detail fades faster.", 10, 4000),
            new(Terrain, "finalTessellation", 2, Cost.Cheap, "Terrain tessellation."),
        ]);

    /// <summary>
    /// For hardware that has no business running this game. Trades looks for frames,
    /// deliberately and visibly.
    /// </summary>
    public static readonly TuningProfile Potato = new(
        "potato", "Potato",
        "Everything above, plus visible cuts. For hardware well under the game's requirements.",
        [
            .. TrafficSim.Tweaks,

            new(Lod, "levelOfDetail", 0.2, Cost.Visible,
                "Global LOD scalar. Game's own Very Low preset is 0.25; the UI floor is 0.10.", 0.1, 1.0),
            new(Lod, "maxLightCount", 1024, Cost.Visible, "Concurrent light cap.", 512, 16384),
            new(Lod, "lodCrossFade", false, Cost.Visible,
                "Disables LOD blending. Cheaper, but you will see models pop in."),

            new(Extra, "cascadeShadowSplitCount", 1, Cost.Visible, "Single shadow cascade.", 0, 4),
            new(Extra, "shaderQualityTier", "Low", Cost.Visible,
                "Switches the game to its COLOSSAL_QUALITY_TIER_LOW shader variants."),
            new(Shadows, "directionalShadowResolution", 1024, Cost.Visible, "Sun shadow map resolution."),
            new(Shadows, "terrainCastShadows", false, Cost.Visible, "Terrain stops casting shadows."),

            new(Ssao, "enabled", false, Cost.Visible, "No ambient occlusion."),
            new(Ssr, "enabled", false, Cost.Visible, "No screen-space reflections."),
            new(Volumetrics, "enabled", false, Cost.Visible, "No volumetric fog."),
            new(Clouds, "volumetricCloudsEnabled", false, Cost.Visible, "Flat clouds instead of volumetric."),

            new(Texture, "mipbias", 2, Cost.Visible, "Biases toward smaller mips. Softer textures, less VRAM.", 0, 3),
            new(Terrain, "finalTessellation", 1, Cost.Visible, "Minimum terrain tessellation."),
        ]);

    public static readonly IReadOnlyList<TuningProfile> All = [FreeWins, TrafficSim, Potato];

    public static TuningProfile? ById(string id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Mesh streaming budget is the one knob that should go UP, not down: a budget that
    /// undershoots available VRAM makes the game evict and re-upload meshes it is about
    /// to need again. Sized off reported VRAM, clamped to the game's own 128–4096 MB range.
    /// </summary>
    public static Tweak MeshBudgetFor(int vramMegabytes)
    {
        var budget = vramMegabytes switch
        {
            >= 12000 => 4096,
            >= 8000  => 2048,
            >= 6000  => 1536,
            >= 4000  => 768,
            _        => 384,
        };

        return new Tweak(Lod, "meshMemoryBudget", budget, Cost.Free,
            $"Mesh streaming budget sized for {vramMegabytes} MB of VRAM. Too small causes re-upload stalls.",
            128, 4096);
    }
}
