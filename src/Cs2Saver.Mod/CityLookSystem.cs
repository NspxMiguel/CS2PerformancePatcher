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
        private TextureCurve m_Linear;
        private TextureCurve m_Stepped;

        private Look m_Look = Look.Off;
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

        protected override void OnUpdate()
        {
            if (m_Failed || !m_Dirty) return;
            m_Dirty = false;

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
            if (look == Cs2Saver.Look.Off)
            {
                m_Volume.enabled = false;
                return;
            }

            m_Volume.enabled = true;

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
                    Grade(saturation: 28f, contrast: 12f, exposure: 0.12f);
                    Tone(TonemappingMode.Neutral);
                    Split(shadows: new Color(0.40f, 0.46f, 0.62f), highlights: new Color(0.62f, 0.56f, 0.46f), balance: 0f);
                    Balance(temperature: 5f, tint: 0f);
                    Glow(intensity: 0.35f, warmth: 0.06f);
                    Ambient(1.35f);
                    Vignette(0.12f);
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

            // Scatter is how far the light spreads. High values are soft and expensive-looking;
            // they are not expensive, the pass costs the same either way.
            m_Bloom.scatter.Override(0.72f);

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
}
