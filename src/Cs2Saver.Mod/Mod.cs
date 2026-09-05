using System;
using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.Prefabs;
using Game.Rendering;
using Game.SceneFlow;

namespace Cs2Saver
{
    /// <summary>
    /// Entry point. Registers the render-budget system and nothing else.
    ///
    /// Deliberately defensive: if any part of registration throws, the mod logs and stands down
    /// rather than taking the game with it. A performance mod that prevents the game from
    /// starting is worse than no performance mod.
    /// </summary>
    public sealed class Mod : IMod
    {
        public static readonly ILog Log =
            LogManager.GetLogger(nameof(Cs2Saver)).SetShowsErrorsInUI(false);

        internal static RenderBudgetSystem BudgetSystem;
        internal static PrefabLodFloorSystem PrefabFloor;
        internal static FrameLogSystem FrameLog;
        internal static CityLookSystem CityLook;
        internal static MaterialStyleSystem MaterialStyle;
        internal static HitchLogSystem HitchLog;
        internal static ShotSystem Shots;
        internal static Settings Setting;

        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.Info($"{nameof(Cs2Saver)} loading.");

            // Registered independently: a failure in one must not take out the other, and the
            // frame log in particular has to survive so a broken budget can still be measured.
            TryRegister("render budget", () =>
            {
                // Explicitly BEFORE PreCullingSystem, not merely in the same phase. Mods register
                // after the game's own SystemOrder, so a plain UpdateAt would land after
                // PreCullingSystem and the floor would only take effect a frame later -- and for
                // entities the game re-seeds every frame, never at all.
                updateSystem.UpdateBefore<RenderBudgetSystem, PreCullingSystem>(
                    SystemUpdatePhase.PreCulling);
                BudgetSystem = updateSystem.World.GetOrCreateSystemManaged<RenderBudgetSystem>();

                // Starts inert. Nothing changes until something sets a budget, so a broken
                // build degrades to "does nothing" rather than "ruins the view".
                BudgetSystem.Budget = RenderBudget.Off;
            });

            TryRegister("prefab LOD floor", () =>
            {
                // Explicitly AFTER ObjectInitializeSystem, which is what computes
                // ObjectGeometryData.m_MinLod in the first place. Running before it would have
                // the floor immediately overwritten by the value we mean to raise.
                updateSystem.UpdateAfter<PrefabLodFloorSystem, ObjectInitializeSystem>(
                    SystemUpdatePhase.PrefabUpdate);
                PrefabFloor = updateSystem.World.GetOrCreateSystemManaged<PrefabLodFloorSystem>();
                PrefabFloor.Budget = RenderBudget.Off;
            });

            TryRegister("city look", () =>
            {
                // PostSimulation rather than a rendering phase on purpose: this touches a
                // GameObject and a Volume, not ECS data, so it only needs to run somewhere
                // ordinary once per frame and do nothing on almost all of them.
                updateSystem.UpdateAt<CityLookSystem>(SystemUpdatePhase.PostSimulation);
                CityLook = updateSystem.World.GetOrCreateSystemManaged<CityLookSystem>();
                CityLook.Look = Look.Off;
            });

            TryRegister("material style", () =>
            {
                updateSystem.UpdateAt<MaterialStyleSystem>(SystemUpdatePhase.PostSimulation);
                MaterialStyle = updateSystem.World.GetOrCreateSystemManaged<MaterialStyleSystem>();
            });

            TryRegister("hitch watch", () =>
            {
                // LateUpdate, so the frame time it reads is the whole frame rather than the part
                // that had happened by the time simulation ran.
                updateSystem.UpdateAt<HitchLogSystem>(SystemUpdatePhase.LateUpdate);
                HitchLog = updateSystem.World.GetOrCreateSystemManaged<HitchLogSystem>();
                HitchLog.Watching = false;
            });

            TryRegister("frame log", () =>
            {
                // LateUpdate: once per rendered frame, after the frame's work is done.
                updateSystem.UpdateAt<FrameLogSystem>(SystemUpdatePhase.LateUpdate);
                FrameLog = updateSystem.World.GetOrCreateSystemManaged<FrameLogSystem>();
                FrameLog.Recording = false;
            });

            TryRegister("timed shots", () =>
            {
                // LateUpdate, so the frame being photographed is the finished one. Inert unless
                // a cue file is sitting next to the logs, which only the benchmark runner writes.
                updateSystem.UpdateAt<ShotSystem>(SystemUpdatePhase.LateUpdate);
                Shots = updateSystem.World.GetOrCreateSystemManaged<ShotSystem>();
            });

            // Registered last, so the systems it drives already exist when it applies itself.
            TryRegister("settings", () =>
            {
                Setting = new Settings(this);
                Setting.RegisterInOptionsUI();

                // Both registered; the game serves whichever matches its own language setting, so
                // there is nothing here to detect and nothing for a player to choose twice.
                GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(Setting));
                GameManager.instance.localizationManager.AddSource("pt-BR", new LocalePT(Setting));
                AssetDatabase.global.LoadSettings(nameof(Cs2Saver), Setting, new Settings(this));

                // Push whatever was loaded from disk into the live systems, so a returning
                // player gets the preset they chose last time rather than an inert mod.
                Setting.ApplyToSystems();
            });
        }

        private static void TryRegister(string what, Action register)
        {
            try
            {
                register();
                Log.Info($"Registered {what} (inactive until configured).");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to register {what}; continuing without it.");
            }
        }

        public void OnDispose()
        {
            try
            {
                if (BudgetSystem != null) BudgetSystem.Budget = RenderBudget.Off;

                // Now that the original value of every prefab is remembered, setting the budget
                // to Off actually puts the city back rather than leaving it cut until restart.
                if (PrefabFloor != null) PrefabFloor.Budget = RenderBudget.Off;

                // Same reason for both: the city keeps this mod's colour grading and its flattened
                // materials after the mod itself is gone unless they are handed back here.
                // Reach before Look: the reach is what holds the sun's shadow on, and clearing it
                // is what hands the light back to the game.
                if (CityLook != null)
                {
                    CityLook.Reach = ShadowReach.Untouched;
                    CityLook.Look = Look.Off;
                }
                if (MaterialStyle != null) MaterialStyle.Surface = Surface.Off;

                if (FrameLog != null) FrameLog.Recording = false;
                if (HitchLog != null) HitchLog.Watching = false;
            }
            catch (Exception ex)
            {
                Log.Warn($"Cleanup failed: {ex.Message}");
            }
            finally
            {
                BudgetSystem = null;
                PrefabFloor = null;
                CityLook = null;
                MaterialStyle = null;
                FrameLog = null;
                HitchLog = null;
                Shots = null;
                Log.Info($"{nameof(Cs2Saver)} unloaded.");
            }
        }
    }
}
