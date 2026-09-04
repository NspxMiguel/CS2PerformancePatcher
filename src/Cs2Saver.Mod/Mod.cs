using System;
using Colossal.Logging;
using Game;
using Game.Modding;
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
        internal static FrameLogSystem FrameLog;

        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.Info($"{nameof(Cs2Saver)} loading.");

            // Registered independently: a failure in one must not take out the other, and the
            // frame log in particular has to survive so a broken budget can still be measured.
            TryRegister("render budget", () =>
            {
                // PreCulling runs immediately before PreCullingSystem decides visibility, so a
                // floor written here applies on the same frame rather than one frame late.
                updateSystem.UpdateAt<RenderBudgetSystem>(SystemUpdatePhase.PreCulling);
                BudgetSystem = updateSystem.World.GetOrCreateSystemManaged<RenderBudgetSystem>();

                // Starts inert. Nothing changes until something sets a budget, so a broken
                // build degrades to "does nothing" rather than "ruins the view".
                BudgetSystem.Budget = RenderBudget.Off;
            });

            TryRegister("frame log", () =>
            {
                // LateUpdate: once per rendered frame, after the frame's work is done.
                updateSystem.UpdateAt<FrameLogSystem>(SystemUpdatePhase.LateUpdate);
                FrameLog = updateSystem.World.GetOrCreateSystemManaged<FrameLogSystem>();
                FrameLog.Recording = false;
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
