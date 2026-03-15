using HarmonyLib;
using LaMulana2Archipelago.Managers;
using LaMulana2Archipelago.Trackers;
using L2Base;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// This patch fires ONLY when KataribeScript has fully finished.
    /// At this point:
    /// - All dialog text has resolved
    /// - Item dialogs (itemd) have completed
    /// - The state machine has exited
    /// - It is safe to flush AP checks or mutate inventory
    /// </summary>
    [HarmonyPatch(typeof(KataribeScript), nameof(KataribeScript.Final))]
    internal static class KataribeScriptFinalPatch
    {
        static void Postfix()
        {
            Plugin.Log.LogInfo("[KATARIBE DEBUG] Final() reached");

            if (!DialogStateTracker.IsInDialog)
                return;

            // Hard guarantee: dialog is no longer running
            DialogStateTracker.ForceDialogEnd();

            FlagQueueDeferred.Flush();

            // Now it is safe to report locations / grant AP items
            CheckManagerDeferred.Flush();
        }
    }
}