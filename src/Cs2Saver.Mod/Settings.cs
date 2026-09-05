using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;

namespace Cs2Saver
{
    /// <summary>
    /// The in-game options page.
    ///
    /// Wording matters more than usual here. Every option below trades something away, and a
    /// player who cannot tell what they are trading cannot make a sensible choice — so the
    /// labels name the cost, not just the setting.
    /// </summary>
    [FileLocation(nameof(Cs2Saver))]
    [SettingsUIGroupOrder(RenderingGroup, MeasurementGroup)]
    [SettingsUIShowGroupName(RenderingGroup, MeasurementGroup)]
    public sealed class Settings : ModSetting
    {
        public const string MainSection = "Main";
        public const string RenderingGroup = "Rendering";
        public const string MeasurementGroup = "Measurement";

        public Settings(IMod mod) : base(mod) { }

        public enum BudgetPreset
        {
            Off,
            Balanced,
            TrafficFocus,
            Aggressive,

            /// <summary>Everything that is not a building, cut hard. The one this mod is for.</summary>
            Declutter,

            /// <summary>The same, pushed to where street level starts to notice.</summary>
            DeclutterMax,

            /// <summary>Foliage only. Here because it is also how foliage gets measured.</summary>
            TreesOnly,

            /// <summary>Street clutter only. Here because it is also how clutter gets measured.</summary>
            PropsOnly,
        }

        /// <summary>
        /// How early each category of thing stops being drawn. Each step up roughly halves the
        /// distance at which it disappears. Buildings are never affected by any of these.
        /// </summary>
        [SettingsUISection(MainSection, RenderingGroup)]
        public BudgetPreset Preset { get; set; } = BudgetPreset.Off;

        /// <summary>
        /// The look put on top of the city. Costs nothing to render — it drives the grading stack
        /// HDRP already runs — and exists because a city with its shadows and effects cut reads as
        /// broken rather than as stylised unless something puts an intention back on it.
        /// </summary>
        [SettingsUISection(MainSection, RenderingGroup)]
        public Look CityLook { get; set; } = Look.Off;

        /// <summary>
        /// How far from the camera the sun's shadows are drawn.
        ///
        /// <para>This bounds shadows; it does not create them. If the graphics profile in use has
        /// the sun's shadows switched off, nothing here brings them back — that is the game's own
        /// setting and only a profile reaches it.</para>
        ///
        /// <para>Where it does apply, it is a saving rather than a cost, and a large one: the
        /// game offers a resolution and a cascade count, both of which trade sharpness across the
        /// whole shadowed area, and neither of which is the trade worth making. Distance is. On
        /// the reference machine the shadow pass measured 5.3 frames per second at the game's own
        /// distance and 2.5 at a hundred metres — half the cost, and nothing lost anywhere a
        /// player was looking closely enough to notice a shadow.</para>
        /// </summary>
        [SettingsUISection(MainSection, RenderingGroup)]
        public ShadowReach SunShadows { get; set; } = ShadowReach.Untouched;

        /// <summary>
        /// How much of the city's greenery survives. Kept apart from the preset above because
        /// it is the one cut here that is taste rather than degree: five frames per second
        /// separate a city with trees from one without, and that is not a call to make for
        /// somebody else.
        /// </summary>
        [SettingsUISection(MainSection, RenderingGroup)]
        public RenderBudget.Foliage Greenery { get; set; } = RenderBudget.Foliage.Balanced;

        /// <summary>
        /// How the city's surfaces respond to light. This is the one setting that changes the art
        /// rather than the picture: it rewrites the game's own materials in memory, so asphalt
        /// stops looking wet and roof tiles stop glinting. Glass is never touched.
        /// </summary>
        [SettingsUISection(MainSection, RenderingGroup)]
        public Surface CitySurface { get; set; } = Surface.Off;

        /// <summary>
        /// Watches for stalls while you play and writes each one down with the camera height,
        /// because the benchmark this project measures with never descends to street level and
        /// therefore cannot see the hitch people actually complain about.
        /// </summary>
        [SettingsUISection(MainSection, MeasurementGroup)]
        public bool WatchForHitches { get; set; }

        /// <summary>Records frame timing to a CSV so a preset can be judged by numbers.</summary>
        [SettingsUISection(MainSection, MeasurementGroup)]
        public bool RecordFrameTimings { get; set; }

        /// <summary>Written into each CSV row so separate runs can be told apart.</summary>
        [SettingsUISection(MainSection, MeasurementGroup)]
        [SettingsUIDisableByCondition(typeof(Settings), nameof(IsNotRecording))]
        public string RunLabel { get; set; } = "baseline";

        public bool IsNotRecording() => !RecordFrameTimings;

        public override void SetDefaults()
        {
            Preset = BudgetPreset.Off;
            CityLook = Look.Off;
            SunShadows = ShadowReach.Untouched;
            CitySurface = Surface.Off;
            Greenery = RenderBudget.Foliage.Balanced;
            WatchForHitches = false;
            RecordFrameTimings = false;
            RunLabel = "baseline";
        }

        /// <summary>Pushes the current values into the live systems. Safe to call repeatedly.</summary>
        public void ApplyToSystems()
        {
            var budget = Preset switch
            {
                BudgetPreset.Balanced => RenderBudget.Balanced,
                BudgetPreset.TrafficFocus => RenderBudget.TrafficFocus,
                BudgetPreset.Aggressive => RenderBudget.Aggressive,
                BudgetPreset.Declutter => RenderBudget.Declutter,
                BudgetPreset.DeclutterMax => RenderBudget.DeclutterMax,
                BudgetPreset.TreesOnly => RenderBudget.TreesOnly,
                BudgetPreset.PropsOnly => RenderBudget.PropsOnly,
                _ => RenderBudget.Off,
            };

            // Foliage is the player's call, so it overrides whatever the preset had in mind.
            budget.TreeHalvings = (byte)Greenery;

            if (Mod.BudgetSystem != null) Mod.BudgetSystem.Budget = budget;

            // The prefab pass is the durable half: it survives the game re-seeding an entity's
            // culling data from its prefab, which the entity pass alone would not. It is also
            // the only one of the two that can reach trees and props, which have no live
            // per-frame equivalent worth sweeping.
            if (Mod.PrefabFloor != null) Mod.PrefabFloor.Budget = budget;

            if (Mod.CityLook != null)
            {
                Mod.CityLook.Look = CityLook;
                Mod.CityLook.Reach = SunShadows;
            }

            // One survey per session, once a city is loaded. It writes what the city is actually
            // built from into the log, which is the only reliable way to find out: a material
            // property written by a guessed name fails silently and looks exactly like a setting
            // that does nothing.
            if (Mod.MaterialStyle != null)
            {
                Mod.MaterialStyle.Surface = CitySurface;
                Mod.MaterialStyle.RequestSurvey();
            }

            if (Mod.HitchLog != null) Mod.HitchLog.Watching = WatchForHitches;

            if (Mod.FrameLog != null)
            {
                Mod.FrameLog.Label = string.IsNullOrWhiteSpace(RunLabel) ? "unlabelled" : RunLabel;
                Mod.FrameLog.Recording = RecordFrameTimings;
            }
        }

        public override void Apply()
        {
            base.Apply();
            ApplyToSystems();
        }
    }
}
