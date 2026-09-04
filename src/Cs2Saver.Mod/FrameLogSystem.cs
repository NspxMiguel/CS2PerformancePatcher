using System;
using System.Globalization;
using System.IO;
using System.Text;
using Game;
using Unity.Mathematics;
using UnityEngine;

namespace Cs2Saver
{
    /// <summary>
    /// Writes frame timing to a CSV so a profile can be judged by numbers instead of by feel.
    ///
    /// This exists because the person tuning the profiles cannot always be the person running
    /// the game. A player enables this, plays for a few minutes on each profile, and the CSV
    /// is enough to tell whether a change actually helped — including whether it helped the
    /// part that matters, which is the slow frames rather than the average.
    ///
    /// Averages hide stutter. A build that goes from 45 to 50 average FPS while its 1% low
    /// drops from 30 to 18 feels worse, not better, so the 1% and 0.1% lows are recorded as
    /// first-class columns rather than derived later.
    /// </summary>
    public sealed partial class FrameLogSystem : GameSystemBase
    {
        /// <summary>Seconds of gameplay summarised per CSV row.</summary>
        private const float WindowSeconds = 10f;

        /// <summary>Frames per window at 240 Hz, so the buffer never reallocates mid-session.</summary>
        private const int MaxSamples = 4096;

        private readonly float[] m_Samples = new float[MaxSamples];
        private int m_Count;
        private float m_Elapsed;
        private string m_Path;
        private bool m_Failed;

        /// <summary>
        /// Off by default. Nothing is written until a player asks for it.
        ///
        /// Deliberately not called "Enabled": ComponentSystemBase already has that, and
        /// shadowing it would mean a caller could switch off the system itself while believing
        /// they had only stopped the logging.
        /// </summary>
        public bool Recording { get; set; }

        /// <summary>Free-form label written into every row, so profiles can be told apart.</summary>
        public string Label { get; set; } = "unlabelled";

        protected override void OnCreate()
        {
            base.OnCreate();

            try
            {
                var dir = Path.Combine(Application.persistentDataPath, "Cs2Saver");
                Directory.CreateDirectory(dir);
                m_Path = Path.Combine(dir, $"frames-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            }
            catch (Exception ex)
            {
                m_Failed = true;
                Mod.Log.Warn($"Frame log disabled, could not prepare an output file: {ex.Message}");
            }
        }

        protected override void OnUpdate()
        {
            if (!Recording || m_Failed) return;

            // Fully qualified: unqualified 'Time' binds to ComponentSystemBase.Time, which is
            // the ECS TimeData struct rather than the engine clock.
            var dt = UnityEngine.Time.unscaledDeltaTime;

            // A hitch from loading or alt-tabbing is not a rendering measurement.
            if (dt <= 0f || dt > 1f) return;

            if (m_Count < MaxSamples) m_Samples[m_Count++] = dt;
            m_Elapsed += dt;

            if (m_Elapsed >= WindowSeconds) Flush();
        }

        private void Flush()
        {
            if (m_Count == 0)
            {
                m_Elapsed = 0f;
                return;
            }

            try
            {
                var window = new float[m_Count];
                Array.Copy(m_Samples, window, m_Count);
                Array.Sort(window); // ascending frame time: fast frames first, worst last

                var total = 0f;
                for (var i = 0; i < window.Length; i++) total += window[i];

                var mean = total / window.Length;

                if (!File.Exists(m_Path))
                {
                    File.AppendAllText(m_Path,
                        "timestamp,label,frames,seconds,avg_fps,p50_ms,p95_ms,p99_ms,low_1pct_fps,low_01pct_fps\n",
                        Encoding.UTF8);
                }

                // Percentiles are taken on frame TIME, so a high percentile is a slow frame.
                var row = string.Join(",",
                    DateTime.Now.ToString("s", CultureInfo.InvariantCulture),
                    Sanitise(Label),
                    window.Length.ToString(CultureInfo.InvariantCulture),
                    total.ToString("F2", CultureInfo.InvariantCulture),
                    (1f / mean).ToString("F1", CultureInfo.InvariantCulture),
                    Ms(Percentile(window, 0.50f)),
                    Ms(Percentile(window, 0.95f)),
                    Ms(Percentile(window, 0.99f)),
                    Fps(Percentile(window, 0.99f)),
                    Fps(Percentile(window, 0.999f)));

                File.AppendAllText(m_Path, row + "\n", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                // Losing a measurement must never cost a frame or crash a session.
                m_Failed = true;
                Mod.Log.Warn($"Frame log stopped after a write error: {ex.Message}");
            }
            finally
            {
                m_Count = 0;
                m_Elapsed = 0f;
            }
        }

        private static float Percentile(float[] ascending, float q)
        {
            var index = math.clamp((int)math.round(q * (ascending.Length - 1)), 0, ascending.Length - 1);
            return ascending[index];
        }

        private static string Ms(float seconds) =>
            (seconds * 1000f).ToString("F2", CultureInfo.InvariantCulture);

        private static string Fps(float seconds) =>
            (seconds > 0f ? 1f / seconds : 0f).ToString("F1", CultureInfo.InvariantCulture);

        /// <summary>Keeps a label from breaking the CSV if someone puts a comma in it.</summary>
        private static string Sanitise(string label) =>
            string.IsNullOrEmpty(label) ? "unlabelled" : label.Replace(',', ';').Replace('\n', ' ');

        /// <summary>Where rows are being written, for a UI to show. Null if setup failed.</summary>
        public string OutputPath => m_Failed ? null : m_Path;
    }
}
