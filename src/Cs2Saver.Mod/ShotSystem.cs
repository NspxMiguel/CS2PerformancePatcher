using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Game;
using Game.UI.Menu;
using UnityEngine;

namespace Cs2Saver
{
    /// <summary>
    /// Takes screenshots at fixed points on the benchmark's own clock, so two runs can be
    /// compared frame for frame.
    ///
    /// <para>The obvious way to do this is from outside: start the game, wait N seconds, grab the
    /// window. That is what was tried first, and it does not work. The wall clock outside the
    /// game measures the launcher, the level load and the benchmark's three-second settle as
    /// well as the run, and none of those take the same time twice — so "twelve seconds in"
    /// landed on a different part of the city in every run, and half the pairs framed different
    /// streets.</para>
    ///
    /// <para>The benchmark camera itself is honest about where it is. <c>BenchmarkUISystem</c>
    /// accumulates <c>unscaledDeltaTime</c> into a total and feeds that total straight to the
    /// camera path as its parameter, which means camera position is a pure function of elapsed
    /// benchmark time and nothing else. A slow run and a fast run reach the same corner at the
    /// same reading. Read that field and the matching problem disappears.</para>
    ///
    /// <para>The field is private, so this reflects. That is a real dependency on a private
    /// detail of someone else's code and it will break on a game update; it is confined to this
    /// file, it is checked once at startup, and everything it touches is a diagnostic. If the
    /// field is ever renamed this system logs and stands down, and the mod carries on.</para>
    ///
    /// <para>A side benefit worth naming: the benchmark hides the HUD while it runs, so these
    /// come out as clean city photographs rather than screenshots with a toolbar across the
    /// bottom.</para>
    /// </summary>
    public sealed partial class ShotSystem : GameSystemBase
    {
        /// <summary>
        /// Where the runner leaves its instructions. A file rather than a mod setting on
        /// purpose: this is scaffolding for producing comparison images, it has no business
        /// appearing on the options page, and it must never end up saved into a player's
        /// settings the way a real setting would.
        /// </summary>
        private const string CueFileName = "shots.txt";

        private readonly List<float> m_Marks = new List<float>();
        private string m_OutputDir;
        private string m_Prefix = "shot";
        private int m_SuperSize = 1;

        private BenchmarkUISystem m_Benchmark;
        private FieldInfo m_ElapsedField;

        private int m_Next;
        private float m_LastElapsed = -1f;
        private bool m_Armed;
        private bool m_Broken;

        protected override void OnCreate()
        {
            base.OnCreate();
            ReadCue();
        }

        /// <summary>
        /// Loads the cue file if there is one. Absent, malformed or empty all mean the same
        /// thing — take no pictures — because this only ever runs deliberately.
        /// </summary>
        private void ReadCue()
        {
            try
            {
                var dir = Path.Combine(Application.persistentDataPath, "Cs2Saver");
                var cue = Path.Combine(dir, CueFileName);
                if (!File.Exists(cue)) return;

                // Line 1: output directory. Line 2: filename prefix. Line 3: supersampling
                // factor. Line 4 onwards: one benchmark-elapsed second per line.
                var lines = File.ReadAllLines(cue);
                if (lines.Length < 4) return;

                m_OutputDir = lines[0].Trim();
                m_Prefix = lines[1].Trim();
                int.TryParse(lines[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out m_SuperSize);
                if (m_SuperSize < 1 || m_SuperSize > 4) m_SuperSize = 1;

                for (var i = 3; i < lines.Length; i++)
                {
                    if (float.TryParse(lines[i].Trim(), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out var at))
                    {
                        m_Marks.Add(at);
                    }
                }

                if (m_Marks.Count == 0 || string.IsNullOrEmpty(m_OutputDir)) return;

                m_Marks.Sort();
                Directory.CreateDirectory(m_OutputDir);
                m_Armed = true;

                Mod.Log.Info(
                    $"Shot cue loaded: {m_Marks.Count} marks, prefix '{m_Prefix}', " +
                    $"supersize {m_SuperSize}, into {m_OutputDir}");
            }
            catch (Exception ex)
            {
                Mod.Log.Warn($"Shot cue ignored: {ex.Message}");
            }
        }

        /// <summary>
        /// Finds the benchmark system and the one field on it worth reading. Returns false once
        /// and stays false if either is missing, so a game update costs one log line rather than
        /// a reflection attempt every frame for the rest of the session.
        /// </summary>
        private bool Attach()
        {
            if (m_Broken) return false;
            if (m_ElapsedField != null && m_Benchmark != null) return true;

            try
            {
                m_Benchmark = World.GetExistingSystemManaged<BenchmarkUISystem>();
                if (m_Benchmark == null) return false; // not in a benchmark session yet

                m_ElapsedField = typeof(BenchmarkUISystem).GetField(
                    "m_TotalElapsed", BindingFlags.Instance | BindingFlags.NonPublic);

                if (m_ElapsedField == null || m_ElapsedField.FieldType != typeof(float))
                {
                    m_Broken = true;
                    Mod.Log.Warn(
                        "Benchmark elapsed time is no longer where this build expects it; " +
                        "timed screenshots are off for this session.");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                m_Broken = true;
                Mod.Log.Warn($"Could not attach to the benchmark clock: {ex.Message}");
                return false;
            }
        }

        protected override void OnUpdate()
        {
            if (!m_Armed || m_Broken) return;
            if (!Attach()) return;

            float elapsed;
            try
            {
                elapsed = (float)m_ElapsedField.GetValue(m_Benchmark);
            }
            catch (Exception ex)
            {
                m_Broken = true;
                Mod.Log.Warn($"Benchmark clock unreadable: {ex.Message}");
                return;
            }

            // The benchmark zeroes its total when a run starts and when one ends. Treating any
            // backwards step as a fresh run means a second run in the same session shoots the
            // same list again instead of silently doing nothing.
            if (elapsed < m_LastElapsed) m_Next = 0;
            m_LastElapsed = elapsed;

            if (elapsed <= 0f || m_Next >= m_Marks.Count) return;

            // A single mark per frame. Two captures in one frame would write two files of the
            // same image, and CaptureScreenshot queues to end-of-frame regardless.
            if (elapsed < m_Marks[m_Next]) return;

            var index = m_Next + 1;
            var path = Path.Combine(m_OutputDir, $"{m_Prefix}-{index:00}.png");
            m_Next++;

            try
            {
                // Queued for the end of this frame and written asynchronously. It costs a hitch,
                // which is why this is never on during a run whose numbers matter -- and why the
                // marks are read off the benchmark clock rather than counted in frames, so the
                // hitch it causes cannot move the next one.
                ScreenCapture.CaptureScreenshot(path, m_SuperSize);
                Mod.Log.Info($"Shot {index} queued at t={elapsed:F2}s -> {path}");
            }
            catch (Exception ex)
            {
                Mod.Log.Warn($"Shot {index} failed: {ex.Message}");
            }
        }
    }
}
