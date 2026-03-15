using HarmonyLib;
using L2Base;
using LaMulana2Archipelago.Managers;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Fired exactly once when a fresh save initializes costume flags.
    /// This is the canonical "gameplay has begun" signal.
    /// </summary>
    [HarmonyPatch(typeof(L2System), "setSystemDataToClothFlag")]
    internal static class SetSystemDataToClothFlagPatch
    {
        static void Prefix()
        {
            if (CheckManager.IsGameplayReady)
                return;

            Plugin.Log.LogInfo(
                "[AP INIT] setSystemDataToClothFlag detected — gameplay ready"
            );

            CheckManager.MarkGameplayReady();
        }
    }
}