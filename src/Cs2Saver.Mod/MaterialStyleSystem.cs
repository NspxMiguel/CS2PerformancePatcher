using System.Collections.Generic;
using System.Text;
using Game;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cs2Saver
{
    /// <summary>
    /// Restyles the game's surfaces by rewriting the materials it has loaded, rather than by
    /// grading the picture afterwards.
    ///
    /// <para><b>Why this and not more colour grading.</b> Grading changes what colour a pixel
    /// ends up; it cannot change what a surface <i>is</i>. The thing that makes this game read as
    /// photoreal is per-surface specular response — asphalt with wet sheen, roof tiles with
    /// microfacet glint, render with bumped normals. Flattening those is what turns a photograph
    /// into an illustration, and it is a property of the material, not of the frame.</para>
    ///
    /// <para><b>Nothing is redistributed.</b> This edits material instances the player's own copy
    /// of the game loaded into memory, on the player's machine, and puts the original values back
    /// when switched off. No asset from the game is copied, modified on disk, or shipped.</para>
    ///
    /// <para>The shader list it writes to was found by survey rather than assumed, and the survey
    /// still runs once per session into the log. Writing to a guessed property name fails
    /// silently and looks exactly like a setting that does nothing, which is the failure the
    /// prefab LOD system already made once and which took a benchmark to catch. This city turned
    /// out to be 521 materials across 169 shaders, of which six carry nearly all of it.</para>
    /// </summary>
    public partial class MaterialStyleSystem : GameSystemBase
    {
        private static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
        private static readonly int Metallic = Shader.PropertyToID("_Metallic");

        /// <summary>
        /// The shaders the city itself is built from, found by survey rather than assumed.
        /// Windows are deliberately absent: glass is the one surface that should stay glossy, and
        /// leaving it alone is what makes a flattened city read as an architectural render rather
        /// than as a clay model.
        /// </summary>
        private static readonly string[] CitySurfaces =
        {
            "BH/SG_DefaultShader",
            "BH/SG_CurvedShader",
            "BH/NetCompositionMeshLitShader",
        };

        // Deliberately NOT restyled, after a report of the bottom toolbar going missing:
        //
        //     Shader Graphs/AreaDecalShader
        //     Shader Graphs/AreaShader
        //     BH/Decals/CurvedDecalShader
        //
        // These are overlays -- zoning, districts, road markings -- and they are Shader Graphs,
        // where a property called _Metallic is only called that. The graph is free to wire it to
        // opacity, to a blend factor, to anything. On a lit surface shader the name is a contract
        // because HDRP's lit stack defines it; on an overlay graph it is a label. Three lit
        // shaders carry the city and are worth having; the overlays were worth almost nothing
        // visually and carried a risk that could not be checked from outside.

        private readonly Dictionary<Material, Vector2> m_Original = new Dictionary<Material, Vector2>();

        private Surface m_Surface = Surface.Off;
        private bool m_Dirty;
        private int m_LastMaterialCount = -1;

        /// <summary>
        /// How often to look for newly loaded materials, in frames — often while the answer is
        /// still changing, rarely once it has settled.
        ///
        /// Twice a second was the first attempt and it cost more than the whole feature was worth:
        /// the paused-phase 1% low fell from 61.8 fps to 25.4, which is not a steady cost but a
        /// periodic stall, because <c>FindObjectsOfTypeAll</c> walks every loaded object and
        /// allocates an array of them. Materials arrive while a city loads and essentially never
        /// afterwards, so the poll backs off to once every ten seconds as soon as the count stops
        /// moving, and a setting change resets it.
        /// </summary>
        private const int PollWhileLoading = 30;
        // Ten seconds still cost something: with the settled poll at 600 frames the paused-phase
        // 1% low sat at 48.6 against 61.8 with the feature off, and a paused phase is 1665 frames
        // of which the slowest sixteen decide that number -- roughly the number of polls in it.
        // Thirty seconds is still fast enough to notice a different city being loaded.
        private const int PollWhenSettled = 1800;
        private const int PollsToSettle = 5;

        private int m_FramesSincePoll;
        private int m_StablePolls;
        private bool m_Surveyed;
        private bool m_WantSurvey;

        /// <summary>Live configuration. Assign from the mod's settings; takes effect next frame.</summary>
        public Surface Surface
        {
            get => m_Surface;
            set
            {
                if (value == m_Surface) return;
                m_Surface = value;
                m_Dirty = true;
            }
        }

        /// <summary>Ask for one survey pass. Cleared once it has run.</summary>
        public void RequestSurvey() => m_WantSurvey = true;

        protected override void OnUpdate()
        {
            // Two reasons to run: the player changed the setting, or materials appeared that this
            // system has never seen. The second one is not optional — the mod is configured while
            // the main menu is up, when a few dozen materials exist, and the entire city arrives
            // afterwards. A first version ran once on the setting alone and restyled 32 materials
            // out of 521, which looked exactly like a setting that does not work.
            //
            // But the check for it cannot be per-frame. FindObjectsOfTypeAll walks every loaded
            // object and allocates, and this project has already measured what a per-frame sweep
            // costs: five milliseconds of CPU to save one of GPU. Twice a second is far more often
            // than a city gains materials.
            var interval = m_StablePolls >= PollsToSettle ? PollWhenSettled : PollWhileLoading;
            var poll = m_Surface != Cs2Saver.Surface.Off && ++m_FramesSincePoll >= interval;
            if (poll) m_FramesSincePoll = 0;

            if (m_Dirty || poll)
            {
                var live = Resources.FindObjectsOfTypeAll<Material>().Length;

                if (m_Dirty || live != m_LastMaterialCount)
                {
                    m_Dirty = false;
                    m_StablePolls = 0;
                    m_LastMaterialCount = live;
                    try { Restyle(); }
                    catch (System.Exception ex) { Mod.Log.Error(ex, "Could not restyle materials."); }
                }
                else m_StablePolls++;
            }

            if (!m_WantSurvey || m_Surveyed) return;

            try
            {
                // Not yet, if the city has not loaded its materials. The menu has a few dozen;
                // a city has thousands, and surveying the menu would answer a question nobody
                // asked while marking the real one as done.
                if (Resources.FindObjectsOfTypeAll<Material>().Length < 500) return;

                m_Surveyed = true;
                m_WantSurvey = false;
                Survey();
            }
            catch (System.Exception ex)
            {
                m_Surveyed = true;
                Mod.Log.Error(ex, "Material survey failed.");
            }
        }

        /// <summary>
        /// Rewrites specular response on the city's surfaces, and puts it back on Off.
        ///
        /// Both properties are stored the first time a material is touched, so every pass is
        /// computed from the game's own value rather than from the last result. That makes this
        /// idempotent, and it makes switching the setting off genuinely restore the city instead
        /// of leaving it flattened until the game restarts.
        /// </summary>
        private void Restyle()
        {
            // Smoothness first, metallic second. Smoothness is the wet-asphalt, glinting-roof-tile
            // response and is most of what reads as photoreal; metallic decides whether a surface
            // reflects the world or its own colour, and at zero everything behaves like paint.
            var target = m_Surface switch
            {
                Cs2Saver.Surface.Matte => new Vector2(0.18f, 0f),
                Cs2Saver.Surface.Painted => new Vector2(0.05f, 0f),
                _ => Vector2.zero,
            };

            var touched = 0;

            foreach (var material in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (material == null || material.shader == null) continue;
                if (System.Array.IndexOf(CitySurfaces, material.shader.name) < 0) continue;
                if (!material.HasProperty(Smoothness)) continue;

                if (!m_Original.TryGetValue(material, out var original))
                {
                    original = new Vector2(
                        material.GetFloat(Smoothness),
                        material.HasProperty(Metallic) ? material.GetFloat(Metallic) : 0f);
                    m_Original[material] = original;
                }

                var value = m_Surface == Cs2Saver.Surface.Off ? original : target;

                material.SetFloat(Smoothness, value.x);
                if (material.HasProperty(Metallic)) material.SetFloat(Metallic, value.y);
                touched++;
            }

            Mod.Log.Info($"Surface style '{m_Surface}' applied to {touched} materials.");
        }

        private static void Survey()
        {
            // FindObjectsOfTypeAll reaches assets that are loaded but not attached to anything in
            // a scene, which is where almost every material in this game lives.
            var materials = Resources.FindObjectsOfTypeAll<Material>();
            var byShader = new Dictionary<string, int>();
            var example = new Dictionary<string, Material>();

            foreach (var material in materials)
            {
                if (material == null || material.shader == null) continue;
                var name = material.shader.name;

                byShader.TryGetValue(name, out var count);
                byShader[name] = count + 1;
                if (!example.ContainsKey(name)) example[name] = material;
            }

            Mod.Log.Info($"Material survey: {materials.Length} materials across {byShader.Count} shaders.");

            var ranked = new List<KeyValuePair<string, int>>(byShader);
            ranked.Sort((a, b) => b.Value.CompareTo(a.Value));

            for (var i = 0; i < ranked.Count && i < 12; i++)
            {
                Mod.Log.Info($"  {ranked[i].Value,5} x {ranked[i].Key}");
            }

            // The properties of the three commonest shaders, which between them will be most of
            // the city. Names and types both matter: writing a float to a colour does nothing.
            for (var i = 0; i < ranked.Count && i < 3; i++)
            {
                var shader = example[ranked[i].Key].shader;
                var line = new StringBuilder();
                line.Append($"  properties of {shader.name}: ");

                var count = shader.GetPropertyCount();
                for (var p = 0; p < count; p++)
                {
                    var propertyName = shader.GetPropertyName(p);
                    var type = shader.GetPropertyType(p);

                    // Only the ones worth restyling. A shader here has upwards of a hundred
                    // properties and the log is not the place for all of them.
                    if (!IsInteresting(propertyName)) continue;
                    line.Append($"{propertyName}:{type} ");
                }

                Mod.Log.Info(line.ToString());
            }
        }

        private static bool IsInteresting(string name)
        {
            return name.IndexOf("Smooth", System.StringComparison.OrdinalIgnoreCase) >= 0
                   || name.IndexOf("Metallic", System.StringComparison.OrdinalIgnoreCase) >= 0
                   || name.IndexOf("Normal", System.StringComparison.OrdinalIgnoreCase) >= 0
                   || name.IndexOf("BaseColor", System.StringComparison.OrdinalIgnoreCase) >= 0
                   || name.IndexOf("Emissive", System.StringComparison.OrdinalIgnoreCase) >= 0
                   || name.IndexOf("AORemap", System.StringComparison.OrdinalIgnoreCase) >= 0
                   || name.IndexOf("MaskMap", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    /// <summary>How the city's surfaces respond to light. Windows are never affected.</summary>
    public enum Surface
    {
        /// <summary>The game's own materials, untouched.</summary>
        Off,

        /// <summary>The wet sheen comes off. Still lit, just not photographed.</summary>
        Matte,

        /// <summary>Flat as poster paint. Only the glass still catches the light.</summary>
        Painted,
    }
}
