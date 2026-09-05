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
        /// How the city's surfaces respond to light. This is the one setting that changes the art
        /// rather than the picture: it rewrites the game's own materials in memory, so asphalt
        /// stops looking wet and roof tiles stop glinting. Glass is never touched.
        /// </summary>
        [SettingsUISection(MainSection, RenderingGroup)]
        public Surface CitySurface { get; set; } = Surface.Off;

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
            CitySurface = Surface.Off;
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

            if (Mod.BudgetSystem != null) Mod.BudgetSystem.Budget = budget;

            // The prefab pass is the durable half: it survives the game re-seeding an entity's
            // culling data from its prefab, which the entity pass alone would not. It is also
            // the only one of the two that can reach trees and props, which have no live
            // per-frame equivalent worth sweeping.
            if (Mod.PrefabFloor != null) Mod.PrefabFloor.Budget = budget;

            if (Mod.CityLook != null) Mod.CityLook.Look = CityLook;

            // One survey per session, once a city is loaded. It writes what the city is actually
            // built from into the log, which is the only reliable way to find out: a material
            // property written by a guessed name fails silently and looks exactly like a setting
            // that does nothing.
            if (Mod.MaterialStyle != null)
            {
                Mod.MaterialStyle.Surface = CitySurface;
                Mod.MaterialStyle.RequestSurvey();
            }

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
