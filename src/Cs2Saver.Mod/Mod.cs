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
                PrefabFloor.Floor = 0;
            });

            TryRegister("frame log", () =>
            {
                // LateUpdate: once per rendered frame, after the frame's work is done.
                updateSystem.UpdateAt<FrameLogSystem>(SystemUpdatePhase.LateUpdate);
                FrameLog = updateSystem.World.GetOrCreateSystemManaged<FrameLogSystem>();
                FrameLog.Recording = false;
            });

            // Registered last, so the systems it drives already exist when it applies itself.
            TryRegister("settings", () =>
            {
                Setting = new Settings(this);
                Setting.RegisterInOptionsUI();

                GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(Setting));
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
                if (FrameLog != null) FrameLog.Recording = false;
            }
            catch (Exception ex)
            {
                Log.Warn($"Cleanup failed: {ex.Message}");
            }
            finally
            {
                BudgetSystem = null;
                FrameLog = null;
                Log.Info($"{nameof(Cs2Saver)} unloaded.");
            }
        }
    }
}
