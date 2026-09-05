using Game;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Cs2Saver
{
    /// <summary>
    /// Gives the city a deliberate look, so that a cut-down city reads as a style rather than as
    /// damage.
    ///
    /// <para><b>Why this belongs in a performance mod.</b> Every tier in this project buys frames
    /// by removing something: the sun's shadows, the screen-space effects, the distance at which
    /// clutter is drawn. What is left is correct but flat — a photoreal renderer with most of its
    /// photorealism switched off, which is the worst of both. Grading is free — the components
    /// here are already in HDRP's post chain whether this mod sets them or not — so it is the one
    /// thing that can be spent on looks without spending frames.
    ///
    /// Measured rather than assumed: <c>skyline</c> with <see cref="Look.Cel"/> ran 53.7 average
    /// against 53.2 for three runs of the same profile with no look at all, and the GPU frame did
    /// not move off 17.9 ms. The difference is inside the run-to-run spread.</para>
    ///
    /// <para><b>Why no shaders.</b> A cartoon look normally means an edge-detection pass and
    /// posterised lighting, which means shipping compiled shader assets, which means the official
    /// Unity toolchain. This mod is built with plain <c>dotnet build</c>, so instead it drives the
    /// grading stack HDRP already has loaded — the same components the game's own volumes use.
    /// That turns out to reach further than expected: saturation, contrast, tone response and
    /// split toning are the obvious half, and <see cref="ColorCurves"/> with a stepped luminance
    /// ramp gives real posterisation, which is the part that actually reads as drawn. The one
    /// thing genuinely out of reach is the ink outline, which needs to sample depth and normals.
    /// </para>
    ///
    /// <para>The volume is created once, marked global, and given a priority far above anything
    /// the game sets, so it wins wherever the camera is. Setting <see cref="Look.Off"/> disables
    /// the volume rather than resetting its values, so the game's own grading returns untouched.
    /// </para>
    /// </summary>
    public partial class CityLookSystem : GameSystemBase
    {
        /// <summary>Priority high enough to sit above the game's own volumes without a fight.</summary>
        private const float OverrideEverything = 10_000f;

        /// <summary>How the game tags its directional light. Found by tag there and here.</summary>
        private const string SunLightTag = "SunLight";

        /// <summary>
        /// Frames between checks that the sun still has the shadow this mod asked for. Two
        /// seconds at sixty, which is fast enough that a player closing the options page never
        /// sees the shadow missing and slow enough to cost nothing.
        /// </summary>
        private const int SunRecheckFrames = 120;

        /// <summary>
        /// Texels on a side for the forced sun shadow. The game's own Low preset uses this, and
        /// there is no reason to go above it: the reach setting keeps the map over a small patch
        /// of city, so 1024 texels here are denser than 4096 spread across the whole view.
        /// </summary>
        private const int ForcedShadowResolution = 1024;

        private GameObject m_Holder;
        private Volume m_Volume;
        private ColorAdjustments m_Color;
        private Tonemapping m_Tonemapping;
        private SplitToning m_SplitToning;
        private WhiteBalance m_WhiteBalance;
        private Vignette m_Vignette;
        private ColorCurves m_Curves;
        private Bloom m_Bloom;
        private IndirectLightingController m_Indirect;
        private HDShadowSettings m_Shadow;
        private TextureCurve m_Linear;
        private TextureCurve m_Stepped;

        private HDAdditionalLightData m_Sun;
        private Light m_SunLight;
        private LightShadows m_SunShadowsBefore;
        private bool m_SunWasForced;
        private bool m_SunReported;
        private int m_SunSearches;
        private int m_SunCountdown;

        private Look m_Look = Look.Off;
        private ShadowReach m_Reach = ShadowReach.Untouched;
        private bool m_Dirty = true;
        private bool m_Failed;

        /// <summary>Live configuration. Assign from the mod's settings; takes effect next frame.</summary>
        public Look Look
        {
            get => m_Look;
            set
            {
                if (value == m_Look) return;
                m_Look = value;
                m_Dirty = true;
            }
        }

        /// <summary>
        /// How far from the camera the sun's shadows are drawn, in metres.
        ///
        /// <para>Unlike everything else on this system, this one costs frames — it is here rather
        /// than in its own system only because the volume it needs already exists here.</para>
        /// </summary>
        public ShadowReach Reach
        {
            get => m_Reach;
            set
            {
                if (value == m_Reach) return;
                m_Reach = value;
                m_Dirty = true;
            }
        }

        protected override void OnUpdate()
        {
            if (m_Failed) return;

            // The sun is the one thing here the game can take back on its own, so it is checked
            // on a timer instead of only when something on this system changes.
            if (!m_Dirty && m_Reach != ShadowReach.Untouched)
            {
                if (--m_SunCountdown > 0) return;
                m_SunCountdown = SunRecheckFrames;

                try { ForceSun(); }
                catch (System.Exception ex)
                {
                    m_Failed = true;
                    Mod.Log.Error(ex, "Could not hold the sun's shadow on; leaving it to the game.");
                }

                return;
            }

            if (!m_Dirty) return;
            m_Dirty = false;
            m_SunCountdown = SunRecheckFrames;

            try
            {
                if (m_Volume == null) Build();
                Apply(m_Look);
            }
            catch (System.Exception ex)
            {
                // A grading volume is a luxury. If HDRP is not shaped the way this expects, the
                // mod stands down here and the rest of it goes on working.
                m_Failed = true;
                Mod.Log.Error(ex, "Could not set up the city look; leaving the game's own grading alone.");
            }
        }

        private void Build()
        {
            m_Holder = new GameObject("Cs2Saver City Look") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(m_Holder);

            m_Volume = m_Holder.AddComponent<Volume>();
            m_Volume.isGlobal = true;
            m_Volume.priority = OverrideEverything;
            m_Volume.weight = 1f;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.hideFlags = HideFlags.HideAndDontSave;
            m_Volume.sharedProfile = profile;

            m_Color = profile.Add<ColorAdjustments>();
            m_Tonemapping = profile.Add<Tonemapping>();
            m_SplitToning = profile.Add<SplitToning>();
            m_WhiteBalance = profile.Add<WhiteBalance>();
            m_Vignette = profile.Add<Vignette>();
            m_Curves = profile.Add<ColorCurves>();
            m_Bloom = profile.Add<Bloom>();
            m_Indirect = profile.Add<IndirectLightingController>();

            // The one component here that is not colour. HDRP keeps the sun's shadow distance in
            // a volume rather than in a light, and the game exposes a resolution and a cascade
            // count but never the distance -- so this is a knob no settings file can turn.
            m_Shadow = profile.Add<HDShadowSettings>();

            m_Linear = Ramp(bands: 0);
            // Fourteen, not nine. Nine was tried on a real city and read as damage rather than as
            // style: the steps are far enough apart that neighbouring surfaces jump colour, which
            // looks like a broken renderer instead of a deliberate one. Fourteen keeps the flat
            // banding that makes light read as drawn without the jumps being the first thing seen.
            m_Stepped = Ramp(bands: 14);

            Mod.Log.Info("City look volume created.");
        }

        private void Apply(Look look)
        {
            // The volume carries two unrelated things now, so it stays alive while either wants
            // it. Disabling it whenever the look is Off would have quietly taken the shadow reach
            // with it, which is the kind of coupling that only shows up as "the setting does
            // nothing" three profiles later.
            // Before the early return below, not after: with both switched off this is the call
            // that hands the sun back, and skipping it would leave a forced shadow burning after
            // the player had turned the feature off.
            ApplyReach();

            m_Volume.enabled = look != Cs2Saver.Look.Off || m_Reach != ShadowReach.Untouched;
            if (!m_Volume.enabled) return;

            if (look == Cs2Saver.Look.Off)
            {
                // Reach only. Every grading component goes inactive so the game's own colour
                // comes back untouched, which is what Off has always promised.
                foreach (var component in new VolumeComponent[]
                         {
                             m_Color, m_Tonemapping, m_SplitToning, m_WhiteBalance,
                             m_Vignette, m_Curves, m_Bloom, m_Indirect,
                         })
                {
                    if (component != null) component.active = false;
                }

                return;
            }

            foreach (var component in new VolumeComponent[]
                     {
                         m_Color, m_Tonemapping, m_SplitToning, m_WhiteBalance, m_Bloom, m_Indirect,
                     })
            {
                if (component != null) component.active = true;
            }

            // Neutral starting point for the two components that are not set by every look. A
            // volume override sticks until something overwrites it, so without this a preset
            // would silently inherit whatever the previously selected one left behind.
            Glow(intensity: 0f, warmth: 0f);
            Ambient(1f);

            switch (look)
            {
                // Colour put back where the game's tone mapping flattened it, and nothing else.
                // For someone who wants the game to look like itself on a good day.
                case Cs2Saver.Look.Vivid:
                    Grade(saturation: 22f, contrast: 12f, exposure: 0f);
                    Tone(TonemappingMode.ACES);
                    Split(shadows: new Color(0.46f, 0.49f, 0.55f), highlights: new Color(0.55f, 0.52f, 0.47f), balance: 0f);
                    Balance(temperature: 0f, tint: 0f);
                    Vignette(0f);
                    break;

                // The cartoon one. Strong colour, hard contrast, cool shade against a barely warm
                // sun, and the neutral tone curve rather than the filmic one — ACES rolls
                // highlights off into grey, which is the look being escaped from here.
                //
                // The warmth is kept deliberately small. A first attempt used temperature 12 with
                // strongly warm highlights and turned every field of grass olive: saturation
                // multiplies whatever cast is already there, so a tint that looks mild on a grey
                // chart is not mild on a city that is mostly one colour.
                case Cs2Saver.Look.Toybox:
                    Grade(saturation: 45f, contrast: 32f, exposure: 0.08f);
                    Tone(TonemappingMode.Neutral);
                    Split(shadows: new Color(0.34f, 0.44f, 0.66f), highlights: new Color(0.58f, 0.54f, 0.46f), balance: 0f);
                    Balance(temperature: 3f, tint: -3f);
                    Vignette(0.15f);
                    break;

                // Model-railway light: warm, lifted, vignetted. Reads as a scale model under a
                // lamp, which is a flattering thing for a city with no shadows to be. This is the
                // one preset where the warmth is the point rather than an accident.
                case Cs2Saver.Look.Miniature:
                    Grade(saturation: 32f, contrast: 20f, exposure: 0.18f);
                    Tone(TonemappingMode.Neutral);
                    Split(shadows: new Color(0.40f, 0.44f, 0.56f), highlights: new Color(0.66f, 0.58f, 0.42f), balance: 10f);
                    Balance(temperature: 12f, tint: 3f);
                    Vignette(0.30f);
                    break;

                // The drawn one. Everything above plus a stepped luminance response, so light
                // falls in flat bands instead of a smooth gradient — which is the thing that
                // actually reads as illustration rather than as a photograph with the colour
                // turned up. It is the closest this gets to cel shading without a shader: real
                // cel shading also draws an ink outline, which needs a depth-and-normal pass
                // this mod has no way to ship.
                // The one aimed at looking good rather than at looking stylised: an architectural
                // render, which is what a city builder is closest to anyway. Warm light against a
                // cool sky, shadows lifted so shape survives in them, and enough glow on bright
                // surfaces that a lit window reads as lit.
                //
                // Nothing here is physically honest. Bounced light above 1.0 does not happen, and
                // that is the point — a photograph lets its shadows go black and a render of a
                // building never does, because the building is the subject.
                case Cs2Saver.Look.Showroom:
                    // Ambient at 1.35 was tried against flattened materials and washed the city
                    // out: matte surfaces have no specular highlight to hold the top of the range,
                    // so lifting the shadows as well leaves nothing anywhere. Lifting less and
                    // taking the contrast back in grading holds shape at both ends.
                    Grade(saturation: 34f, contrast: 22f, exposure: 0.06f);
                    Tone(TonemappingMode.Neutral);

                    // The blue in the shadows used to be 0.40/0.46/0.62, and a photograph of the
                    // benchmark's dusk is what brought it down. Split toning assumes a lit scene
                    // with distinct highlights and shadows; at dusk almost the whole frame is
                    // shadow, so the shadow tint stops being a tint and becomes the colour of the
                    // image. The game's own dusk is a strong orange and this turned it olive.
                    //
                    // Halving the separation keeps the cool-shade-against-warm-sun reading in
                    // daylight, where there is something for it to be a contrast against, and
                    // stops it taking over a frame that has no highlights left.
                    Split(shadows: new Color(0.45f, 0.48f, 0.56f), highlights: new Color(0.60f, 0.55f, 0.47f), balance: 0f);
                    Balance(temperature: 5f, tint: 0f);
                    Glow(intensity: 0.40f, warmth: 0.06f);
                    Ambient(1.12f);
                    Vignette(0.14f);
                    break;

                case Cs2Saver.Look.Cel:
                    // Brighter and far closer to neutral than the other looks, because the stepped
                    // curve is itself a large change: it lowers everything in the lower bands, and
                    // any colour cast applied on top of that lands on a darker image than it was
                    // chosen against. An earlier pass paired it with the same blue shadows Toybox
                    // uses and turned every road purple.
                    Grade(saturation: 30f, contrast: 10f, exposure: 0.35f);
                    Tone(TonemappingMode.Neutral);
                    Split(shadows: new Color(0.47f, 0.49f, 0.53f), highlights: new Color(0.54f, 0.52f, 0.48f), balance: -10f);
                    Balance(temperature: 3f, tint: 0f);
                    Vignette(0.08f);
                    break;
            }

            // The curve is the only thing that separates Cel from the rest, so it is set here
            // rather than inside the switch: every other look must actively put it back to linear
            // or it would inherit whatever the last one left behind.
            m_Curves.active = true;
            m_Curves.master.Override(look == Cs2Saver.Look.Cel ? m_Stepped : m_Linear);
        }

        /// <summary>
        /// Pulls the sun's shadow distance in to <see cref="Reach"/> metres, or leaves the game's
        /// own alone.
        ///
        /// <para>The game's shadow settings are a resolution and a cascade count, and both of
        /// those trade sharpness for cost across the whole shadowed area. Distance is a different
        /// trade and a better one for this project: every building outside the radius stops being
        /// rasterised into the shadow map at all, and near the camera — which is where a player
        /// is looking, and the only place a shadow reads as anything but a smudge — nothing is
        /// lost. It also spends the resolution better, because one cascade covering 150 metres
        /// has the texel density of a much larger map covering the whole view.</para>
        ///
        /// <para>Only the distance is overridden. Cascade count and split ratios are left
        /// unspecified so they blend through from whatever the game set, which means a player who
        /// has chosen four cascades still gets four.</para>
        /// </summary>
        private void ApplyReach()
        {
            if (m_Shadow == null) return;

            if (m_Reach == ShadowReach.Untouched)
            {
                m_Shadow.active = false;
                RestoreSun();
                return;
            }

            m_Shadow.active = true;
            m_Shadow.maxShadowDistance.Override((float)(int)m_Reach);

            // The cascade count has to come with the distance, and finding that out cost two
            // benchmark runs. Every fast tier sets ExtraQualitySettings.cascadeShadowSplitCount
            // to zero, and zero cascades means HDRP renders no directional shadow at all — so
            // switching the sun's shadow back on and bounding its distance both succeeded, logged
            // success, and changed not one pixel.
            //
            // One cascade rather than more. Cascades exist to spend shadow-map resolution where
            // the eye is, and this setting has already solved that problem by refusing to draw
            // shadows further away than the reach; splitting the map again on top of that buys
            // nothing and costs a second rasterisation of the scene.
            m_Shadow.cascadeShadowSplitCount.Override(1);

            ForceSun();
        }

        /// <summary>
        /// Makes sure the sun is in a state where it can cast the shadow this setting bounds.
        ///
        /// <para><b>This does not turn shadows on by itself, and four benchmark runs went into
        /// establishing that.</b> Enabling them needs the game's own
        /// <c>ShadowsQualitySettings.enabled</c> — which is to say, a tuning profile that asks
        /// for shadows. With that switched off, setting <c>legacyLight.shadows</c> to Soft
        /// succeeds, reports success, and renders nothing; so does adding a cascade; so does
        /// forcing the resolution. There is a fourth gate somewhere in how the game hands the sun
        /// to HDRP, and finding it was not worth more runs when the profile already reaches it
        /// from the front.</para>
        ///
        /// <para>What is kept here is the part that is load-bearing when a profile does ask for
        /// shadows: the resolution. A profile that enables shadows without naming a resolution
        /// gets whatever is in the settings file, and a file written while shadows were off holds
        /// a zero there — which produces a light with shadows enabled, a cascade to render them
        /// into, and no texels to render them with.</para>
        ///
        /// <para>The game finds this light by tag and so does this. Reasserted on a slow poll
        /// rather than every frame: the game re-applies its own quality settings whenever the
        /// options page is touched or a save is loaded, and that would otherwise silently undo
        /// this until something else marked the look dirty.</para>
        /// </summary>
        private void ForceSun()
        {
            if (!FindSun()) return;

            if (!m_SunWasForced)
            {
                m_SunShadowsBefore = m_SunLight.shadows;
                m_SunWasForced = true;
            }

            if (m_SunLight.shadows == LightShadows.None)
            {
                // Soft rather than Hard: the shadow map here is small and close, and a hard edge
                // on 1024 texels stretched over a street reads as a staircase.
                m_Sun.EnableShadows(true);
                m_SunLight.shadows = LightShadows.Soft;
            }

            // The third thing that has to be true, and the last one to be found. A profile with
            // shadows switched off leaves every field of ShadowsQualitySettings at its C# default,
            // which for the resolution is zero -- and the game hands that zero to the light
            // whether shadows are on or not. A light with shadows enabled, a cascade to render
            // them into and a zero-texel map produces exactly nothing, silently.
            m_Sun.SetShadowResolutionOverride(true);
            m_Sun.SetShadowResolution(ForcedShadowResolution);

            // Once per session, and only the first time. A silent no-op is the failure mode this
            // whole feature is most likely to have -- the first attempt at it logged nothing,
            // changed nothing and cost nothing, which took a benchmark run to notice.
            if (!m_SunReported)
            {
                m_SunReported = true;
                Mod.Log.Info(
                    $"Sun shadow forced on at {(int)m_Reach}m " +
                    $"(was {m_SunShadowsBefore}, now {m_SunLight.shadows}, " +
                    $"HDRP dimmer {m_Sun.shadowDimmer:F2}).");
            }
        }

        /// <summary>Hands the sun back exactly as it was found, for Untouched and for unload.</summary>
        private void RestoreSun()
        {
            if (!m_SunWasForced || !FindSun()) return;

            m_SunLight.shadows = m_SunShadowsBefore;
            m_Sun.EnableShadows(m_SunShadowsBefore != LightShadows.None);
            m_SunWasForced = false;
        }

        private bool FindSun()
        {
            if (m_Sun != null && m_SunLight != null) return true;

            var holder = GameObject.FindGameObjectWithTag(SunLightTag);
            if (holder == null)
            {
                // Not an error while the main menu is up -- there is no sun until a city loads.
                // Worth one line after enough tries that "no city yet" has stopped being the
                // explanation, because a missing tag looks identical from here.
                if (++m_SunSearches == 20)
                {
                    Mod.Log.Warn(
                        $"No object tagged '{SunLightTag}' after {m_SunSearches} tries; " +
                        "the sun shadow setting will do nothing.");
                }

                return false;
            }

            m_Sun = holder.GetComponent<HDAdditionalLightData>();
            m_SunLight = holder.GetComponent<Light>();

            if (m_Sun == null || m_SunLight == null)
            {
                Mod.Log.Warn(
                    $"Found '{SunLightTag}' but it has no " +
                    $"{(m_SunLight == null ? "Light" : "HDAdditionalLightData")}; standing down.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// A luminance response curve. <paramref name="bands"/> of 0 is the identity ramp; any
        /// other value is a staircase with that many treads.
        ///
        /// The staircase is built with infinite tangents, which is how a runtime AnimationCurve
        /// expresses constant interpolation — there is no scripting access to Unity's tangent
        /// modes outside the editor, and a curve with zero tangents would ease between steps
        /// instead of jumping, which just produces a soft gradient with lumps in it.
        /// </summary>
        private static TextureCurve Ramp(int bands)
        {
            var bounds = new Vector2(0f, 1f);

            if (bands <= 0)
            {
                var identity = new[] { new Keyframe(0f, 0f, 1f, 1f), new Keyframe(1f, 1f, 1f, 1f) };
                return new TextureCurve(identity, 0f, false, in bounds);
            }

            var keys = new Keyframe[bands + 1];
            for (var i = 0; i <= bands; i++)
            {
                var x = i / (float)bands;

                // Half a band up, and this matters more than it looks. Constant interpolation
                // holds the value of the key on its LEFT, so a tread of i/bands maps everything
                // below the first step to zero: a first attempt did exactly that and turned an
                // afternoon into midnight, because most of a city sits in the lower bands.
                // Centring each tread inside its own range keeps the darkest band off black and
                // the brightest off white, which is also what a paint set does.
                var y = Mathf.Clamp01((i + 0.5f) / bands);

                keys[i] = new Keyframe(x, y)
                {
                    inTangent = float.PositiveInfinity,
                    outTangent = float.PositiveInfinity,
                };
            }

            return new TextureCurve(keys, 0f, false, in bounds);
        }

        // Every parameter needs overrideState set, or the volume system treats it as "not set by
        // this volume" and the value is ignored no matter what it holds.
        private void Grade(float saturation, float contrast, float exposure)
        {
            m_Color.active = true;
            m_Color.saturation.Override(saturation);
            m_Color.contrast.Override(contrast);
            m_Color.postExposure.Override(exposure);
        }

        private void Tone(TonemappingMode mode)
        {
            m_Tonemapping.active = true;
            m_Tonemapping.mode.Override(mode);
        }

        private void Split(Color shadows, Color highlights, float balance)
        {
            m_SplitToning.active = true;
            m_SplitToning.shadows.Override(shadows);
            m_SplitToning.highlights.Override(highlights);
            m_SplitToning.balance.Override(balance);
        }

        private void Balance(float temperature, float tint)
        {
            m_WhiteBalance.active = true;
            m_WhiteBalance.temperature.Override(temperature);
            m_WhiteBalance.tint.Override(tint);
        }

        /// <summary>
        /// Light bleeding out of bright surfaces. This is what makes a lit window at dusk read as
        /// a lit window rather than as a pale rectangle, and it is most of the difference between
        /// an architectural render and a screenshot.
        /// </summary>
        private void Glow(float intensity, float warmth)
        {
            m_Bloom.active = intensity > 0f;
            m_Bloom.intensity.Override(intensity);

            // Scatter is how far the light spreads. High values look soft and expensive; the
            // spread itself is free, because it is a weighting across mip levels the pass builds
            // regardless.
            m_Bloom.scatter.Override(0.72f);

            // The pass is not free, though, and it was the whole cost of the look: with it on,
            // `handsome` fell from 61.4 to 59.1 at play speed, which is the wrong side of sixty.
            // The low quality level builds fewer mips. On a glow this soft the difference is not
            // visible and the frames are.
            m_Bloom.quality.Override((int)ScalableSettingLevelParameter.Level.Low);

            // Only the genuinely bright things, so the whole image does not go milky.
            m_Bloom.threshold.Override(0.9f);
            m_Bloom.tint.Override(new Color(0.5f + warmth, 0.5f, 0.5f - warmth * 0.6f));
        }

        /// <summary>
        /// How much bounced light fills the shadows. Above one is not physically honest, and that
        /// is the point: an architectural render lifts its shadows so that shape stays readable
        /// where a photograph would go black.
        /// </summary>
        private void Ambient(float multiplier)
        {
            m_Indirect.active = true;
            m_Indirect.indirectDiffuseLightingMultiplier.Override(multiplier);
            m_Indirect.reflectionLightingMultiplier.Override(multiplier);
        }

        private void Vignette(float intensity)
        {
            m_Vignette.active = intensity > 0f;
            m_Vignette.intensity.Override(intensity);
            m_Vignette.smoothness.Override(0.5f);
        }

        protected override void OnDestroy()
        {
            m_Linear?.Release();
            m_Stepped?.Release();
            if (m_Holder != null) Object.Destroy(m_Holder);
            m_Holder = null;
            m_Volume = null;
            base.OnDestroy();
        }
    }

    /// <summary>The looks the mod can put on the city. All cost the same to render.</summary>
    public enum Look
    {
        /// <summary>The game's own grading, untouched.</summary>
        Off,

        /// <summary>Colour restored. Subtle enough to leave on without thinking about it.</summary>
        Vivid,

        /// <summary>The cartoon one: strong colour, hard contrast, warm sun against cool shade.</summary>
        Toybox,

        /// <summary>Model-railway light. Warm, lifted, slightly vignetted.</summary>
        Miniature,

        /// <summary>Architectural render: warm light, cool sky, lifted shadows, glow.</summary>
        Showroom,

        /// <summary>The drawn one: flat bands of light instead of a smooth gradient.</summary>
        Cel,
    }

    /// <summary>
    /// How far the sun's shadows are drawn, in metres. The value is the distance, so the enum
    /// documents itself and the code never needs a lookup table.
    ///
    /// <para>The names describe what still has a shadow at that distance rather than the number,
    /// because the number means nothing to anyone who has not stood in the city and measured a
    /// block.</para>
    /// </summary>
    public enum ShadowReach
    {
        /// <summary>Whatever the game asked for. The only value that changes nothing.</summary>
        Untouched = 0,

        /// <summary>The building you are looking at and its neighbours.</summary>
        Block = 100,

        /// <summary>The street and the ones crossing it.</summary>
        Street = 175,

        /// <summary>Everything a zoomed-in camera can see.</summary>
        Neighbourhood = 300,

        /// <summary>Far enough to survive zooming out one step.</summary>
        District = 500,
    }
}
