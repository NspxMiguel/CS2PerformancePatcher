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
                    "Balanced â people fade earlier, traffic untouched"
                },
                {
                    m_Settings.GetEnumValueLocaleID(Settings.BudgetPreset.TrafficFocus),
                    "Traffic focus â people close-range only, traffic pulled in a little"
                },
                {
                    m_Settings.GetEnumValueLocaleID(Settings.BudgetPreset.Aggressive),
                    "Aggressive â anything that moves is drawn only near the camera"
                },
                {
                    m_Settings.GetEnumValueLocaleID(Settings.BudgetPreset.Declutter),
                    "Declutter â everything except the buildings is pulled in hard"
                },
                {
                    m_Settings.GetEnumValueLocaleID(Settings.BudgetPreset.DeclutterMax),
                    "Declutter, maximum â street level will notice; the view from above will not"
                },
                {
                    m_Settings.GetEnumValueLocaleID(Settings.BudgetPreset.TreesOnly),
                    "Trees only â foliage fades earlier, nothing else changes"
                },
                {
                    m_Settings.GetEnumValueLocaleID(Settings.BudgetPreset.PropsOnly),
                    "Street clutter only â signs, lamps and fences fade earlier"
                },

                {
                    m_Settings.GetOptionLabelLocaleID(nameof(Settings.CityLook)),
                    "Give the city a look"
                },
                {
                    m_Settings.GetOptionDescLocaleID(nameof(Settings.CityLook)),
                    "Colour, contrast and tone. Costs nothing to render, and exists because a " +
                    "city with its shadows and effects turned off looks broken rather than " +
                    "stylised until something puts an intention back on it."
                },
                { m_Settings.GetEnumValueLocaleID(Look.Off), "Off â the game's own colours" },
                { m_Settings.GetEnumValueLocaleID(Look.Vivid), "Vivid â colour put back, nothing else" },
                { m_Settings.GetEnumValueLocaleID(Look.Toybox), "Toybox â cartoon: strong colour, warm sun, cool shade" },
                { m_Settings.GetEnumValueLocaleID(Look.Miniature), "Miniature â model railway under a lamp" },
                { m_Settings.GetEnumValueLocaleID(Look.Showroom), "Showroom â architectural render: warm light, cool sky, soft shadows" },
                { m_Settings.GetEnumValueLocaleID(Look.Cel), "Cel â flat bands of light, the drawn look" },

                {
                    m_Settings.GetOptionLabelLocaleID(nameof(Settings.Greenery)),
                    "How much greenery survives"
                },
                {
                    m_Settings.GetOptionDescLocaleID(nameof(Settings.Greenery)),
                    "Kept apart from the preset above because it is taste rather than degree. On " +
                    "the machine this was measured on, five frames per second separate a city with " +
                    "trees in it from one without: 56 fps at Full, 58 at Balanced, 61 at Thin."
                },
                { m_Settings.GetEnumValueLocaleID(RenderBudget.Foliage.Untouched), "Untouched — every tree the game would draw" },
                { m_Settings.GetEnumValueLocaleID(RenderBudget.Foliage.Full), "Full — half distance, still a green city" },
                { m_Settings.GetEnumValueLocaleID(RenderBudget.Foliage.Balanced), "Balanced — thinner, still recognisably planted" },
                { m_Settings.GetEnumValueLocaleID(RenderBudget.Foliage.Thin), "Thin — fields where a forest was, and five frames for it" },

                {
                    m_Settings.GetOptionLabelLocaleID(nameof(Settings.SunShadows)),
                    "How far the sun's shadow reaches"
                },
                {
                    m_Settings.GetOptionDescLocaleID(nameof(Settings.SunShadows)),
                    "Bounds the sun's shadow; it does not create one. If the graphics profile in " +
                    "use has shadows switched off, nothing here brings them back — that is the " +
                    "game's own setting and only a profile reaches it. Where it does apply it is " +
                    "a saving, not a cost: the game offers a resolution and a cascade count, and " +
                    "distance is the better trade than either. On the reference machine the " +
                    "shadow pass cost 5.3 fps at the game's own distance and 2.5 at a hundred " +
                    "metres — half the price, with nothing lost anywhere close enough to notice."
                },
                { m_Settings.GetEnumValueLocaleID(ShadowReach.Untouched), "As the game asked — untouched" },
                { m_Settings.GetEnumValueLocaleID(ShadowReach.Block), "Block — 100 m, the cheapest that still gives shape" },
                { m_Settings.GetEnumValueLocaleID(ShadowReach.Street), "Street — 175 m, this street and the ones crossing it" },
                { m_Settings.GetEnumValueLocaleID(ShadowReach.Neighbourhood), "Neighbourhood — 300 m, everything a close camera sees" },
                { m_Settings.GetEnumValueLocaleID(ShadowReach.District), "District — 500 m, survives zooming out a step" },

                {
                    m_Settings.GetOptionLabelLocaleID(nameof(Settings.CitySurface)),
                    "How surfaces catch the light"
                },
                {
                    m_Settings.GetOptionDescLocaleID(nameof(Settings.CitySurface)),
                    "Rewrites the game's own materials so asphalt stops looking wet and roof tiles " +
                    "stop glinting. This changes the art rather than the picture, and glass is never " +
                    "touched 2014 leaving the windows glossy against flat walls is what makes a city read " +
                    "as an architectural render instead of a clay model."
                },
                { m_Settings.GetEnumValueLocaleID(Surface.Off), "Off â the game's own materials" },
                { m_Settings.GetEnumValueLocaleID(Surface.Matte), "Matte â the wet sheen comes off" },
                { m_Settings.GetEnumValueLocaleID(Surface.Painted), "Painted â flat as poster paint" },

                {
                    m_Settings.GetOptionLabelLocaleID(nameof(Settings.WatchForHitches)),
                    "Write down every stutter"
                },
                {
                    m_Settings.GetOptionDescLocaleID(nameof(Settings.WatchForHitches)),
                    "Notes each stalled frame in the log with the camera height and how far it " +
                    "moved, so a stutter while zooming in can be told apart from one that happens " +
                    "anyway. Costs nothing while it is off and almost nothing while it is on."
                },

                {
                    m_Settings.GetOptionLabelLocaleID(nameof(Settings.RecordFrameTimings)),
                    "Record frame timings to a file"
                },
                {
                    m_Settings.GetOptionDescLocaleID(nameof(Settings.RecordFrameTimings)),
                    "Writes a CSV every ten seconds with average FPS and, more usefully, the 1% " +
                    "and 0.1% lows â the slow frames you actually feel. Use it to check whether a " +
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
