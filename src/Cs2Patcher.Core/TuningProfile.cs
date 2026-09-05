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

/// <summary>
/// What a profile actually did, on the one machine this project has been able to measure.
///
/// All three figures are the benchmark's speed-1 phase — normal play — with the mod installed at
/// Declutter Max, because that is the configuration the tool recommends and quoting anything else
/// would be quoting a number nobody runs. The whole-run average of the same benchmark reads about
/// fifteen percent lower, because it spends 35 of its 90 seconds at triple simulation speed.
/// </summary>
/// <param name="Fps">Average frames per second.</param>
/// <param name="OnePercentLow">Mean of the slowest 1% of frames, as a rate.</param>
/// <param name="ShareAtSixty">Percentage of frames that met 60 fps.</param>
public sealed record Measured(double Fps, double OnePercentLow, int ShareAtSixty)
{
    /// <summary>Untouched settings with the mod off: 26.1 fps at play speed.</summary>
    public const double StockFps = 26.1;

    public int GainPercent => (int)Math.Round((Fps - StockFps) / StockFps * 100);
}

public sealed record TuningProfile(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<Tweak> Tweaks,
    Measured? Measured = null);

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
    /// <summary>
    /// Settings that live at the root of the Graphics section instead of inside the
    /// qualitySettings array. Never a real <c>@type</c>, so it cannot collide with one.
    /// </summary>
    public const string GraphicsRoot  = "Game.Settings.GraphicsSettings";

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
    private const string Fog          = "Game.Settings.FogQualitySettings";
    private const string Water        = "Game.Settings.WaterQualitySettings";
    private const string Terrain      = "Game.Settings.TerrainQualitySettings";
    private const string Texture      = "Game.Settings.TextureQualitySettings";
    private const string DynamicRes   = "Game.Settings.DynamicResolutionScaleSettings";

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
        ],
        new Measured(36.7, 22.2, 0));

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
            new(Terrain, "finalTessellation", 2, Cost.Cheap, "Terrain tessellation.", 2, 5),
        ],
        new Measured(37.5, 23.0, 0));

    /// <summary>
    /// Everything that is not the city itself.
    ///
    /// The other aggressive tiers buy their frames by making the city cheaper to look at —
    /// lower-detail meshes, blurrier textures. This one refuses to touch either. It removes the
    /// fixed per-frame costs instead: the screen-space effects, the volumetrics, half the shadow
    /// map, the expensive shader variants. Buildings, roads and vehicles keep every triangle and
    /// every texel they started with.
    ///
    /// Measured at +33% average against +126% for super-potato. That gap
    /// is the price of a sharp city, and it is the honest reason this tier exists rather than
    /// being the default: it costs real frames to look right.
    /// </summary>
    public static readonly TuningProfile SharpCity = new(
        "sharp", "Sharp City",
        "Cuts everything except the city. Geometry and textures untouched. Measured +33% average, +47% with the harshest upscaler.",
        [
            .. TrafficSim.Tweaks,

            // Cheaper shader variants. Changes shading, not shapes or textures.
            new(Extra, "shaderQualityTier", "Low", Cost.Cheap,
                "Switches to the game's COLOSSAL_QUALITY_TIER_LOW shader variants."),
            new(Extra, "cascadeShadowSplitCount", 0, Cost.Cheap,
                "No cascade splits. Shadows remain; the sun stops being rasterised four times.", 0, 4),

            // Screen-space effects are full passes over a strategy view that rarely shows them off.
            new(Ssao, "enabled", false, Cost.Cheap, "No ambient occlusion."),
            new(Ssr, "enabled", false, Cost.Cheap, "No screen-space reflections."),
            new(Volumetrics, "enabled", false, Cost.Cheap, "No volumetric fog."),
            new(Clouds, "volumetricCloudsEnabled", false, Cost.Cheap, "Flat clouds instead of volumetric."),

            new(Shadows, "directionalShadowResolution", 1024, Cost.Cheap, "Sun shadow map resolution."),
            new(Shadows, "terrainCastShadows", false, Cost.Cheap, "Terrain stops casting shadows."),
            new(Lod, "maxLightCount", 1024, Cost.Cheap, "Concurrent light cap, from 4096.", 512, 16384),

            // Balanced rather than UltraPerformance on purpose. With geometry left at stock this
            // configuration is limited by triangles, not pixels — the most aggressive upscaler
            // measured only ten percent above the least aggressive one, so there is nothing to
            // buy by making the image worse.
            new(GraphicsRoot, "dlssQuality", "Balanced", Cost.Cheap,
                "Forces DLSS past the level the game picks for itself. Ignored on cards without it."),
        ],
        new Measured(48.4, 27.8, 4));

    /// <summary>
    /// A sharp city that actually runs. The tier this project was aiming at.
    ///
    /// <para><c>sharp</c> proved that turning off every screen-space effect in the pipeline is
    /// not enough: it still cost 32.5ms of GPU because, with geometry left at stock, the frame
    /// is bound by triangles and draw calls rather than by pixels. Quartering the pixel count
    /// on that configuration was worth three percent, which is what being triangle-bound looks
    /// like from the outside.</para>
    ///
    /// <para>So this tier gives up the two things that buy geometry back without touching a
    /// texture or a building's silhouette: the sun's shadows, and the LOD scalar — set here to
    /// the game's own Low preset value rather than to the floor that makes a city look like
    /// modelling clay. Buildings keep their meshes and every texture stays at full resolution.
    /// Measured 42.7 average and 20.3 on the 1% low, against 30.3 and 10.6 for sharp — and
    /// against 36.8 for potato, which is both slower than this and blurrier, and is kept only
    /// for machines with no upscaler at all.</para>
    ///
    /// <para><b>This tier is where the mod earns its place.</b> The settings above cannot say
    /// "cut the street furniture but not the buildings" — no setting in the game can. The mod
    /// can, because prefabs carry their own category. Adding it at Declutter takes the same
    /// configuration from 42.7 to 51.8 average and from 47.0 to 58.7 at normal play speed,
    /// which is a fifth of the total performance of this tier arriving from four hundred lines
    /// of mod. Install it with <c>cs2patch install-mod</c>.</para>
    /// </summary>
    public static readonly TuningProfile Skyline = new(
        "skyline", "Skyline",
        "Sharp buildings and full-resolution textures; the sun's shadows and the clutter give way "
        + "instead. Measured +88% on settings alone, +128% with the mod installed.",
        [
            .. SharpCity.Tweaks,

            // The single largest lever left once the screen-space effects are gone: it removes
            // the shadow map pass and every draw call that only existed to cast into it. The
            // city reads flatter, which is a real loss, but nothing becomes blurry or blocky.
            // Measured +15% average and +19% on the 1% low over sharp with the mod running.
            new(Shadows, "enabled", false, Cost.Visible,
                "No sun shadows. The city reads flatter; nothing gets blurrier or blockier."),

            // The game's own Low preset value, not the 0.10 floor that super-potato uses.
            // Buildings switch to their cheaper meshes sooner, but they are still their own
            // meshes rather than the blobs the floor produces.
            new(Lod, "levelOfDetail", 0.35, Cost.Visible,
                "The game's own Low preset value. Detail switches sooner; shapes stay theirs.",
                0.1, 1.0),

            // Only now worth anything. On sharp this was +3%, because the frame was waiting on
            // triangles; with the geometry cut it is +5% and rising as the rest gets cheaper.
            // It reduces the resolution the frame is rendered at and reconstructs from motion,
            // which softens the image without turning textures to mud the way a mip bias does.
            new(GraphicsRoot, "dlssQuality", "MaximumPerformance", Cost.Visible,
                "Renders at a lower internal resolution and reconstructs. Softer, never blockier."),

            // The game's own default is off; this only ever appears because someone turned it on
            // in the options. With it on, every frame waits for the next refresh, so work that
            // finishes at 17ms is presented at 20.8ms on a 240Hz panel. Measured +4%.
            new(GraphicsRoot, "vSync", false, Cost.Cheap,
                "Stops every frame waiting for the next refresh. May tear; the game ships it off."),
        ],
        new Measured(59.9, 33.0, 60));

    /// <summary>
    /// For hardware that has no business running this game. Trades looks for frames,
    /// deliberately and visibly.
    ///
    /// <para>On a machine with DLSS, <see cref="Skyline"/> now beats this on both counts —
    /// faster and sharper — so this tier is kept for hardware with no upscaler at all, where
    /// the mip bias and the terrain cuts are doing work that no amount of reconstruction can.</para>
    /// </summary>
    public static readonly TuningProfile Potato = new(
        "potato", "Old Laptop",
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
            new(Terrain, "finalTessellation", 2, Cost.Visible, "Lowest terrain tessellation the game declares. It refuses to go below 2.", 2, 5),

            // Bigger terrain patches mean fewer of them to submit. The game's own Low preset
            // stops at 24; the slider goes to 64, and measured +7% average there. Coarser
            // terrain LOD granularity is the cost, which is why it sits in this tier.
            new(Terrain, "targetPatchSize", 64, Cost.Visible,
                "Larger terrain patches, so fewer draw calls. Game presets stop at 24.", 4, 64),

            // See the note on the same tweak in Skyline. Worth +4% and the game's own default.
            new(GraphicsRoot, "vSync", false, Cost.Cheap,
                "Stops every frame waiting for the next refresh. May tear; the game ships it off."),
        ],
        new Measured(58.1, 35.7, 56));

    /// <summary>
    /// The bottom of the ladder, for a machine that has no business opening this game at all.
    /// Everything here was measured, and every entry is here because it earned its place:
    /// together they took an RTX 3050 from 22.7 fps to 51.2, and the 1% low from 10.5 to 16.4.
    ///
    /// It looks how it sounds. That is the trade being offered, not an accident.
    /// </summary>
    public static readonly TuningProfile SuperPotato = new(
        "super-potato", "Microwave",
        "Everything above, pushed to where the game stops looking like itself. Measured +126% average.",
        [
            .. Potato.Tweaks,

            // The single largest measured gain on the 1% low: +78% on its own. It is also the
            // most visible change in the whole tool, because it pulls every LOD transition in
            // towards the camera — buildings included.
            new(Lod, "levelOfDetail", 0.1, Cost.Visible,
                "The lowest LOD scalar the game's own slider allows. Everything switches to its "
                + "cheap mesh much closer to the camera.", 0.1, 1.0),

            // Worth +33% average and +25% on the 1% low, separately from each other.
            new(Extra, "cascadeShadowSplitCount", 0, Cost.Visible,
                "No directional shadow cascades at all. Measured +25% on the 1% low.", 0, 4),

            // At 1080p the game's own Auto picks MaximumQuality and nothing in the UI goes past
            // it, because the choice is made purely on pixel count. On a card that has the
            // hardware for it this is the single biggest average-fps lever available.
            new(GraphicsRoot, "dlssQuality", "UltraPerformance", Cost.Visible,
                "Forces DLSS past the level the game picks for itself. Ignored on cards without it."),

            // The fallback for everything without DLSS. The game disables dynamic resolution
            // whenever DLSS is active, so setting both is not a conflict: each machine gets
            // whichever upscaler it actually has.
            new(DynamicRes, "enabled", true, Cost.Visible, "Upscaler for cards without DLSS."),
            new(DynamicRes, "isAdaptive", false, Cost.Visible,
                "Constant rather than adaptive, so it does not climb back up and cost frames."),
            new(DynamicRes, "minScale", 0.5, Cost.Visible,
                "Half resolution per axis — a quarter of the pixels.", 0.5, 1.0),

            // Potato only ever lowered the shadow resolution; it never turned them off. Worth
            // +3.5% overall, +9% at fast-forward and +6% paused. It is worth nothing at normal
            // play speed, because by that point this tier has run out of GPU to save and is
            // waiting on the CPU instead — which is the whole story of this profile's ceiling.
            new(Shadows, "enabled", false, Cost.Visible,
                "No sun shadows at all. The city reads flat; this tier gave up looking right."),
        ],
        new Measured(77.8, 42.7, 95));

    /// <summary>
    /// Sixty frames without looking like it cost anything, which is a different goal from every
    /// other tier here and turned out to need a different route.
    ///
    /// <para>The obvious approach — start from the prettiest tier and cut until it reaches sixty —
    /// does not arrive: <c>sharp</c> with the mod running measures 48 at play speed and there is
    /// nothing left in it to cut that is not the city itself. So this starts from the fastest tier
    /// instead, which has fourteen frames of headroom over the target, and spends every one of
    /// them on the image.</para>
    ///
    /// <para>What it buys back, in the order the eye notices:</para>
    /// <list type="bullet">
    /// <item>The upscaler goes from a 640x360 internal render to 1114x627. This is the whole
    /// reason the bottom tiers look jagged, and it is not a subtle difference — a third of the
    /// resolution reconstructed is exactly what an edge stepping down a roofline looks like.</item>
    /// <item>Textures come back to full resolution. Mip bias 2 is not a soft look, it is a muddy
    /// one, and it never made the game faster here in a way worth measuring.</item>
    /// <item>Geometry goes from the LOD floor to the game's own Low preset, so buildings keep
    /// their silhouettes.</item>
    /// </list>
    ///
    /// <para>Measured 60.2 at normal play speed with 63% of frames at sixty or better and not one
    /// under thirty, against 77.8 for the tier it is built on. That is fourteen frames traded for
    /// three times the internal resolution and sharp textures, and it is the best exchange rate
    /// anywhere on this ladder.</para>
    /// </summary>
    public static readonly TuningProfile Handsome = new(
        "handsome", "Handsome",
        "Sixty frames that do not look like sixty frames cost anything. Full-resolution textures, "
        + "the game's own Low geometry, and three times the internal resolution of the tier below.",
        [
            .. SuperPotato.Tweaks,

            new(GraphicsRoot, "dlssQuality", "Balanced", Cost.Cheap,
                "1114x627 internal instead of 640x360. This is what stops edges stepping."),

            new(Texture, "mipbias", 0, Cost.Cheap,
                "Full-resolution textures. Mip bias is a muddy look, not a soft one.", 0, 3),

            new(Lod, "levelOfDetail", 0.35, Cost.Cheap,
                "The game's own Low preset rather than the floor, so buildings keep their outline.",
                0.1, 1.0),
        ],
        new Measured(61.4, 39.2, 67));

    /// <summary>
    /// Below the bottom. For hardware where the question is whether the game runs at all.
    ///
    /// Everything <see cref="SuperPotato"/> does, plus the last few things that were left alone
    /// because they cost looks out of proportion to the frames they return: the muddiest mip bias
    /// the game offers, no clouds of any kind, and no fog.
    /// </summary>
    public static readonly TuningProfile MegaPotato = new(
        "mega-potato", "If It Opened, It Runs",
        "Everything below super-potato that was left alone for being too ugly to be worth it.",
        [
            .. SuperPotato.Tweaks,

            new(Texture, "mipbias", 3, Cost.Visible,
                "The muddiest mip bias the game has. Textures resolve about half as far.", 0, 3),
            new(Texture, "terrainMipBias", 3, Cost.Visible, "The same, for the ground.", 0, 3),

            new(Clouds, "distanceCloudsEnabled", false, Cost.Visible, "No distant clouds at all."),
            new(Clouds, "volumetricCloudsShadows", false, Cost.Visible, "No cloud shadows."),
            new(Fog, "enabled", false, Cost.Visible,
                "No atmospheric fog. Distance stops reading as distance."),

            new(Water, "maxTessellationFactor", 0.0, Cost.Visible, "Flat water.", 0, 15),
        ],
        new Measured(75.8, 39.8, 92));

    public static readonly IReadOnlyList<TuningProfile> All =
        [FreeWins, TrafficSim, SharpCity, Handsome, Skyline, Potato, SuperPotato, MegaPotato];

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
