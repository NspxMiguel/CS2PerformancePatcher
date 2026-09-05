using Game;
using UnityEngine;

namespace Cs2Saver
{
    /// <summary>
    /// Catches the stalls that only happen while somebody is playing, and writes down enough
    /// about each one to tell them apart.
    ///
    /// <para><b>Why this exists.</b> The benchmark this project measures everything with flies a
    /// fixed path high over a city and never descends. That makes it an excellent A/B rig and a
    /// useless witness to the complaint that matters most in practice — the game hitching as the
    /// camera comes down to street level and the world builds itself in. Two candidate fixes were
    /// measured against the benchmark and both landed inside the noise, which says nothing about
    /// whether they work: the benchmark cannot see the thing they were meant to fix.</para>
    ///
    /// <para>So rather than guess again, this records the hitch where it happens. Each entry
    /// carries the camera's height and how far it moved, because the difference between "stalls
    /// when zooming in" and "stalls every so often regardless" is the difference between an asset
    /// streaming problem and something else entirely, and one line of log settles it.</para>
    ///
    /// <para>Cost is a handful of floats per frame and no allocation. It writes only when a frame
    /// is genuinely bad, and not more than twice a second even then.</para>
    /// </summary>
    public partial class HitchLogSystem : GameSystemBase
    {
        /// <summary>A frame has to be this much worse than recent ones to count as a stall.</summary>
        private const float SpikeRatio = 2.5f;

        /// <summary>...and this slow in absolute terms, so a fast machine's noise is not a hitch.</summary>
        private const float SpikeFloorMs = 40f;

        /// <summary>Rolling average weight. Slow enough that one bad frame does not hide the next.</summary>
        private const float Smoothing = 0.02f;

        private float m_AverageMs = 16.7f;
        private float m_LastLogTime;
        private float m_LastCameraHeight;
        private Vector3 m_LastCameraPosition;

        private int m_Hitches;
        private float m_WorstMs;

        /// <summary>Whether to watch at all. Off by default; costs nothing when off.</summary>
        public bool Watching { get; set; }

        /// <summary>Hitches seen since the mod loaded, for anything that wants to report them.</summary>
        public int HitchCount => m_Hitches;

        protected override void OnUpdate()
        {
            if (!Watching) return;

            var frameMs = UnityEngine.Time.unscaledDeltaTime * 1000f;
            var now = UnityEngine.Time.unscaledTime;

            var camera = Camera.main;
            var position = camera != null ? camera.transform.position : Vector3.zero;
            var height = position.y;
            var moved = m_LastCameraPosition == Vector3.zero ? 0f : Vector3.Distance(position, m_LastCameraPosition);
            var descended = m_LastCameraHeight - height;

            // Compare against the rolling average BEFORE folding this frame into it, or a stall
            // partly hides itself.
            var isSpike = frameMs > m_AverageMs * SpikeRatio && frameMs > SpikeFloorMs;

            m_AverageMs += (frameMs - m_AverageMs) * Smoothing;
            m_LastCameraHeight = height;
            m_LastCameraPosition = position;

            if (!isSpike) return;

            m_Hitches++;
            if (frameMs > m_WorstMs) m_WorstMs = frameMs;

            // Two a second at most. A hitch storm is one event, and logging it a hundred times
            // makes the log slower than the thing it is measuring.
            if (now - m_LastLogTime < 0.5f) return;
            m_LastLogTime = now;

            Mod.Log.Info(
                $"Hitch #{m_Hitches}: {frameMs:F0}ms against a {m_AverageMs:F1}ms average. "
                + $"Camera {height:F0}m up, moved {moved:F0}m"
                + (descended > 1f ? $", descending {descended:F0}m" : string.Empty)
                + $". Worst so far {m_WorstMs:F0}ms.");
        }

        /// <summary>Start again, so a run can be compared against a change.</summary>
        public void Reset()
        {
            m_Hitches = 0;
            m_WorstMs = 0;
            m_AverageMs = 16.7f;
            Mod.Log.Info("Hitch watch reset.");
        }
    }
}
