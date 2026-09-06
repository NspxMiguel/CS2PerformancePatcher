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
/// Declutter, its greenery on Full, the Showroom look and Matte surfaces, because that is the
/// configuration the tool recommends and quoting anything else would be quoting a number nobody
/// runs. Every figure in the table comes from a single session of back-to-back runs.
///
/// <para><b>Two different averages of the same run exist and they are far apart</b>, which cost an
/// afternoon once. <c>cs2patch bench</c> reports the whole 90 seconds; these report the speed-1
/// phase. The benchmark spends 35 of its 90 seconds at triple simulation speed, where the
/// simulation is the ceiling and the frame rate is around 38 whatever the graphics settings are,
/// so the whole-run average reads about fifteen percent lower. Comparing one against the other
/// will always look like a regression.</para>
///
/// <para><b>Two mod settings move these by more than a frame each</b> and both were chosen from
/// photographs. Greenery on Full rather than Balanced costs about a frame and is what keeps a
/// suburb wooded; Declutter rather than Declutter Max costs 1.2 and is what keeps the markings on
/// a sports field. Both labels in the mod say what they cost, so a player who wants those frames
/// back can take them.</para>
/// </summary>
/// <param name="Fps">Average frames per second.</param>
/// <param name="OnePercentLow">Mean of the slowest 1% of frames, as a rate.</param>
/// <param name="ShareAtSixty">Percentage of frames that met 60 fps.</param>
public sealed record Measured(double Fps, double OnePercentLow, int ShareAtSixty)
{
    /// <summary>
    /// Untouched settings with the mod off, at play speed: the denominator under every percentage
    /// this tool prints.
    ///
    /// <para>Measured three times back to back rather than once, because it is the number every
    /// claim divides by. The three runs came back 26.2, 26.0 and 26.0 — a spread of 0.2, which is
    /// tighter than anything else in this project and a useful thing to know: when two runs of the
    /// same thing differ by more than that, the configuration differed, not the machine. Twice
    /// during this project a "stock" run measured well above this, and both times it turned out
    /// the mod had been left switched on.</para>
    /// </summary>
    public const double StockFps = 26.1;

    public int GainPercent => (int)Math.Round((Fps - StockFps) / StockFps * 100);
}

/// <param name="ScreenScale">
/// Render at this fraction of the desktop resolution, or null to leave it alone.
///
/// This is the only lever left once every graphics setting is at its floor, and it is deliberately
/// expressed as a fraction rather than as a resolution: the tier has to mean the same thing on a
/// 4K monitor, a 1080p one and a Steam Deck's 1280x800. The actual mode is computed at apply time
/// from whatever the desktop is currently in, refresh rate included, because a mode the driver
/// rejects is a black screen on somebody else's computer.
/// </param>
/// <param name="ModShadowReach">
/// What to set Cs2Saver's sun-shadow reach to when this profile is applied, or null to leave the
/// mod's settings alone.
///
/// Only a profile that switches the sun's shadow on has any business setting this, and such a
/// profile has to: turning shadows on is a game setting, bounding their distance is an HDRP volume
/// parameter the game never exposes, and the difference between the two is six frames per second
/// against one. See <see cref="ModTuning"/>.
/// </param>
public sealed record TuningProfile(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<Tweak> Tweaks,
    Measured? Measured = null,
    double? ScreenScale = null,
    string? ModShadowReach = null);

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
    private const string AntiAliasing = "Game.Settings.AntiAliasingQualitySettings";

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
        new Measured(41.5, 26.3, 0));

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
        new Measured(42.4, 26.7, 0));

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
        new Measured(50.2, 28.4, 7));

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
        new Measured(56.7, 29.7, 46));

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
        new Measured(56.7, 31.2, 48));

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
        new Measured(70.4, 33.8, 89));

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
    ///
    /// <para>What it buys back, in the order the eye notices:</para>
    /// <list type="bullet">
    /// <item><b>The sun's shadow and ambient occlusion.</b> This is the one that mattered and it
    /// took photographs to see. Every tier below switches both off, and matched captures of the
    /// same benchmark frame against stock said plainly what four hundred benchmark rows had not:
    /// a city with neither is not a stylised city, it is a flat one. Grass with no texture,
    /// buildings with no volume, trees pasted onto the ground.</item>
    /// <item>Textures come back to full resolution. Mip bias 2 is not a soft look, it is a muddy
    /// one, and it never made the game faster here in a way worth measuring.</item>
    /// <item>Geometry goes to twice the LOD floor, so buildings keep their silhouettes.</item>
    /// </list>
    ///
    /// <para>Measured 60.5 at normal play speed with a 32.7 1% low, 64% of frames at sixty or
    /// better and not one under thirty, against 68.5 for the tier it is built on. Eight frames
    /// for the difference between a city and a diagram of one.</para>
    ///
    /// <para><b>The shadow is only affordable with the mod installed.</b> Turning it on is a game
    /// setting; bounding how far it reaches is an HDRP volume parameter the game never exposes.
    /// Bounded to a hundred metres it costs 0.9 fps and unbounded it costs 6.0, so applying this
    /// profile also writes the mod's reach setting, and says so when the mod is not there.</para>
    ///
    /// <para>Two earlier versions of this tier are worth knowing about, because both were tried
    /// and both lost on a photograph rather than on a number. A 1114x627 internal render instead
    /// of 960x540 cost 2.0 fps and could not be told apart in a 1:1 crop — by this tier the frame
    /// is draw calls, not pixels. And LOD 0.35 instead of 0.2 cost 4.5 fps to keep the window
    /// frames on one shopfront at street level, which is the entire margin between making sixty
    /// and missing it.</para>
    /// </summary>
    public static readonly TuningProfile Handsome = new(
        "handsome", "Handsome",
        "Sixty frames that do not look like sixty frames cost anything. Full-resolution textures, "
        + "twice the geometry of the tier below, and -- the thing that separates this tier from "
        + "every other one here -- the sun's shadow and ambient occlusion back on, because a city "
        + "with neither is not stylised, it is flat.",
        [
            .. SuperPotato.Tweaks,

            // Not Balanced, and that is a measurement rather than a preference. Balanced renders
            // 1114x627 against MaximumPerformance's 960x540, and buying those pixels back cost
            // 2.0 fps here while buying the same 2.0 back from the geometry cost far less that
            // anyone could see. By this tier the frame is draw calls, not pixels.
            new(GraphicsRoot, "dlssQuality", "MaximumPerformance", Cost.Cheap,
                "960x540 internal. Past this tier, pixels are not where the frames are."),

            new(Texture, "mipbias", 0, Cost.Cheap,
                "Full-resolution textures. Mip bias is a muddy look, not a soft one.", 0, 3),

            // Twice the game's own floor, and two thirds of its Low preset. 0.35 was tried and
            // photographed against this: it holds window frames on a shopfront at street level
            // and costs 4.5 fps for them, which is the whole margin between this tier making its
            // sixty and missing it. The shopfront lost the argument.
            new(Lod, "levelOfDetail", 0.2, Cost.Cheap,
                "Twice the game's floor. Buildings keep their outline; the nearest few lose some "
                + "window detail.", 0.1, 1.0),

            // Bought back from SuperPotato, which switches both off. Photographs of the same
            // benchmark frame with and without settled it: a city with no sun shadow and no
            // ambient occlusion is not a stylised city, it is a flat one — grass with no texture,
            // buildings with no volume, trees pasted on. This tier's whole claim is that it does
            // not look like sixty frames cost anything, and it could not make that claim.
            //
            // Priced against a same-session control at 48.8: shadows 2.5 and occlusion 0.9.
            // The shadow number depends on Cs2Saver bounding the distance -- at the game's own
            // distance the same shadows cost 5.3.
            new(Shadows, "enabled", true, Cost.Cheap,
                "The sun's shadow, back on. Without it the city reads flat rather than stylised."),

            // Not optional alongside the line above. A profile that enables shadows without
            // naming a resolution inherits whatever is in the settings file, and a file written
            // while shadows were off holds a zero -- which renders nothing at all.
            new(Shadows, "directionalShadowResolution", 1024, Cost.Cheap,
                "The game's own Low value. Zero is what this field holds when shadows were off."),

            new(Extra, "cascadeShadowSplitCount", 1, Cost.Cheap,
                "One cascade. Zero means HDRP draws no directional shadow whatever the light says.",
                0, 4),

            new(Shadows, "terrainCastShadows", false, Cost.Cheap,
                "Terrain still casts nothing. Hills shadowing hills is not what was missing."),

            new(Shadows, "shadowCullingThresholdHeight", 5.0, Cost.Cheap,
                "Only things tall enough for their shadow to read as one get to cast."),
            new(Shadows, "shadowCullingThresholdVolume", 8.0, Cost.Cheap,
                "Same, by volume. Street furniture stops paying into the shadow map."),

            // Turned ON, not off, and this is the only tweak in the whole tool that puts back
            // something the game was not doing in the first place.
            //
            // AntiAliasingQualitySettings serialises to an empty block, which means every field
            // is at its C# default -- and the default for the method is None. So the game had no
            // anti-aliasing at all, no profile here had ever touched that setting, and nobody
            // looked until a 1:1 crop was taken to answer a complaint about stepped edges. The
            // crop showed something else: at this tier the image is soft rather than jagged,
            // because DLSS is reconstructing it. SMAA on top still cleans up window mullions,
            // roof edges and columns visibly, for 0.3 fps.
            new(AntiAliasing, "antiAliasingMethod", "SMAA", Cost.Cheap,
                "Anti-aliasing, which the game had switched off entirely. Measured at 0.3 fps."),
            new(AntiAliasing, "smaaQuality", "Low", Cost.Cheap,
                "The cheapest SMAA the game offers. The expensive levels were not needed here."),

            // Free, and measured as free: 60.7 against 60.8, which is inside the run-to-run
            // spread. It buys aerial depth at distance, which is the one thing a city seen from
            // above has instead of a horizon.
            new(Volumetrics, "enabled", true, Cost.Cheap,
                "Volumetric fog, back on. Measured at 0.1 fps, which is nothing."),
            new(Volumetrics, "budget", 0.15, Cost.Cheap,
                "The cheapest froxel budget the game offers.", 0.0, 1.0),
            new(Volumetrics, "resolutionDepthRatio", 0.5, Cost.Cheap,
                "Half depth resolution. Fog has no detail to lose.", 0.0, 1.0),

            new(Ssao, "enabled", true, Cost.Cheap,
                "Ambient occlusion, back on. It is what puts a building back on its ground."),
            new(Ssao, "stepCount", 4, Cost.Cheap, "Fewest samples the effect still reads at.", 2, 32),
            new(Ssao, "maxPixelRadius", 24, Cost.Cheap, "Contact shading, not a global darkening.",
                16, 256),
        ],
        new Measured(60.7, 31.0, 66),

        // Bounded to a hundred metres by the mod, without which the shadows above cost six frames
        // per second instead of one. Street level is the only place a shadow is looked at.
        ModShadowReach: "Block");

    /// <summary>
    /// Below the bottom. For hardware where the question is whether the game runs at all.
    ///
    /// Everything <see cref="SuperPotato"/> does, plus the last few things that were left alone
    /// because they cost looks out of proportion to the frames they return: the muddiest mip bias
    /// the game offers, no clouds of any kind, and no fog.
    /// </summary>
    /// <summary>
    /// For hardware where the GPU is the wall. On the machine everything here was measured on it
    /// is not, and this tier is honest about buying nothing there.
    ///
    /// <para>Three separate attempts to find something below <see cref="SuperPotato"/> all came
    /// back empty on the reference machine: the muddiest textures with no fog or clouds measured
    /// 75.8 against 77.8; two thirds of the resolution measured 77.0; half of it measured 80.3.
    /// Cutting 63% of the pixels moved the GPU frame by 3%, which is the whole story — by this
    /// point the frame is geometry and draw calls, and neither is priced in pixels.</para>
    ///
    /// <para>That is a fact about one computer, not about the tier. A Steam Deck, an integrated
    /// GPU or anything without an upscaler is GPU-bound exactly where this machine stops being
    /// so, and there the resolution cut is the difference between running and not. The tier is
    /// kept for those, and says plainly that it does nothing here.</para>
    /// </summary>
    public static readonly TuningProfile MegaPotato = new(
        "mega-potato", "If It Opened, It Runs",
        "Two thirds of the resolution, no clouds, no fog, muddiest textures. Buys nothing on a "
        + "machine already limited by its CPU; buys everything on one limited by its GPU.",
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
        new Measured(71.2, 29.9, 89),
        ScreenScale: 0.67);

    /// <summary>
    /// The floor. Half resolution, and nothing else left to give.
    ///
    /// There is no tier below this one because there is nothing below it to set: every graphics
    /// setting the game exposes is already at the end of its range, and the only remaining lever
    /// is the number of pixels, which this halves per axis. A quarter of the pixels of the
    /// desktop, upscaled by the display.
    /// </summary>
    public static readonly TuningProfile BoneDry = new(
        "bone-dry", "Bone Dry",
        "Half the resolution per axis, on top of everything else being at its floor. Nothing "
        + "below this exists, because there is nothing left to turn down.",
        [.. MegaPotato.Tweaks],
        new Measured(72.1, 36.7, 89),
        ScreenScale: 0.5);

    public static readonly IReadOnlyList<TuningProfile> All =
        [FreeWins, TrafficSim, SharpCity, Handsome, Skyline, Potato, SuperPotato, MegaPotato, BoneDry];

    public static TuningProfile? ById(string id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Mesh streaming budget is the one knob that should go UP, not down: a budget that
    /// undershoots available VRAM makes the game evict and re-upload meshes it is about
    /// to need again. Sized off reported VRAM, clamped to the game's own 128–4096 MB range.
    /// </summary>
    /// <summary>
    /// The output resolution tweak for a profile that asks for one, sized against the desktop.
    ///
    /// Rounded to even numbers because odd render targets upset upscalers, and floored at 640x360
    /// because below that the interface stops being readable and the game stops being playable,
    /// which is not a trade any tier here is offering.
    /// </summary>
    public static Tweak? ResolutionFor(double scale, DisplayMode? display)
    {
        if (display is null) return null;

        var wanted = (long)(display.Width * scale) * (long)(display.Height * scale);

        // Pick from what the driver actually offers, at the same aspect ratio, closest to the
        // pixel count asked for. Computing a resolution arithmetically produces sizes like
        // 1286x724 that no display mode matches, and exclusive fullscreen refuses those.
        var aspect = (double)display.Width / display.Height;
        var candidate = DisplayProbe.SupportedModes(display)
            .Where(m => m.Width >= 640 && m.Height >= 360)
            .Where(m => Math.Abs((double)m.Width / m.Height - aspect) < 0.02)
            .Where(m => (long)m.Width * m.Height < (long)display.Width * display.Height)
            .OrderBy(m => Math.Abs((long)m.Width * m.Height - wanted))
            .FirstOrDefault();

        if (candidate is null) return null;

        var width = candidate.Width;
        var height = candidate.Height;

        if (width >= display.Width && height >= display.Height) return null;

        // The shape the game itself writes: ScreenResolution.Write emits width, height and the
        // refresh rate as a numerator/denominator pair, under its own type marker.
        var value = new JsonObject
        {
            ["@type"] = "Game.Settings.ScreenResolution",
            ["width"] = width,
            ["height"] = height,
            ["numerator"] = display.RefreshNumerator,
            ["denominator"] = display.RefreshDenominator,
        };

        return new Tweak(GraphicsRoot, "resolution", value, Cost.Visible,
            $"Renders at {width}x{height} instead of {display.Width}x{display.Height} and lets the "
            + "display scale it up. The last lever there is once every setting is at its floor.");
    }

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
