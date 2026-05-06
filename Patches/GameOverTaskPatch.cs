using HarmonyLib;
using L2Base;
using LaMulana2Archipelago.Managers;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Resets the guardian-kill state machine when the game-over screen
    /// activates. gameOverStart() runs before any post-death scene transition,
    /// so this prevents the load-from-save scene load from being misread as
    /// a successful boss-field exit.
    /// </summary>
    [HarmonyPatch(typeof(GameOverTask), nameof(GameOverTask.gameOverStart))]
    internal static class GameOverTaskPatch
    {
        static void Postfix()
        {
            BossKillTracker.NotifyGameOver();
        }
    }
}
