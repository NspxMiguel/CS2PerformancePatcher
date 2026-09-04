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
                    "Stop drawing people and traffic sooner"
                },
                {
                    m_Settings.GetOptionDescLocaleID(nameof(Settings.Preset)),
                    "Pedestrians and vehicles are removed from the frame at a shorter distance " +
                    "than the game would use. Buildings, roads and terrain are untouched, so the " +
                    "city itself stays exactly as sharp as it was. Each step roughly halves the " +
                    "distance at which people disappear."
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
