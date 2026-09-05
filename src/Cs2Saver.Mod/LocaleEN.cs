using System.Collections.Generic;
using Colossal;

namespace Cs2Saver
{
    /// <summary>
    /// English strings for the options page. Without a dictionary source the game renders raw
    /// setting keys, so this is not decoration.
    /// </summary>
    public sealed class LocaleEN : IDictionarySource
    {
        private readonly Settings m_Settings;

        public LocaleEN(Settings settings) => m_Settings = settings;

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(
            IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Settings.GetSettingsLocaleID(), "CS2 Saver" },
                { m_Settings.GetOptionTabLocaleID(Settings.MainSection), "Main" },

                { m_Settings.GetOptionGroupLocaleID(Settings.RenderingGroup), "Rendering" },
                { m_Settings.GetOptionGroupLocaleID(Settings.MeasurementGroup), "Measurement" },

                {
                    m_Settings.GetOptionLabelLocaleID(nameof(Settings.Preset)),
                    "Stop drawing the clutter sooner"
                },
                {
                    m_Settings.GetOptionDescLocaleID(nameof(Settings.Preset)),
                    "Trees, street furniture, traffic and crowds are removed from the frame at a " +
                    "shorter distance than the game would use. Each step roughly halves that " +
                    "distance. Buildings are never affected by any of these settings, which is " +
                    "what separates this from turning the game's own detail slider down: the " +
                    "skyline keeps every triangle it started with."
                },

                {
                    m_Settings.GetEnumValueLocaleID(Settings.BudgetPreset.Off),
                    "Off"
                },
                {
                    m_Settings.GetEnumValueLocaleID(Settings.BudgetPreset.Balanced),
                    "Balanced — people fade earlier, traffic untouched"
                },
                {
                    m_Settings.GetEnumValueLocaleID(Settings.BudgetPreset.TrafficFocus),
                    "Traffic focus — people close-range only, traffic pulled in a little"
                },
                {
                    m_Settings.GetEnumValueLocaleID(Settings.BudgetPreset.Aggressive),
                    "Aggressive — anything that moves is drawn only near the camera"
                },
                {
                    m_Settings.GetEnumValueLocaleID(Settings.BudgetPreset.Declutter),
                    "Declutter — everything except the buildings is pulled in hard"
                },
                {
                    m_Settings.GetEnumValueLocaleID(Settings.BudgetPreset.TreesOnly),
                    "Trees only — foliage fades earlier, nothing else changes"
                },
                {
                    m_Settings.GetEnumValueLocaleID(Settings.BudgetPreset.PropsOnly),
                    "Street clutter only — signs, lamps and fences fade earlier"
                },

                {
                    m_Settings.GetOptionLabelLocaleID(nameof(Settings.RecordFrameTimings)),
                    "Record frame timings to a file"
                },
                {
                    m_Settings.GetOptionDescLocaleID(nameof(Settings.RecordFrameTimings)),
                    "Writes a CSV every ten seconds with average FPS and, more usefully, the 1% " +
                    "and 0.1% lows — the slow frames you actually feel. Use it to check whether a " +
                    "setting helped instead of guessing. Saved under Cs2Saver in the game's user " +
                    "data folder."
                },

                {
                    m_Settings.GetOptionLabelLocaleID(nameof(Settings.RunLabel)),
                    "Label for this run"
                },
                {
                    m_Settings.GetOptionDescLocaleID(nameof(Settings.RunLabel)),
                    "Written into every row so runs can be compared. Change it whenever you " +
                    "change a setting, e.g. \"off\" then \"traffic focus\"."
                },
            };
        }

        public void Unload() { }
    }
}
