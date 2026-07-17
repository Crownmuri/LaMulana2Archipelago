using HarmonyLib;
using LaMulana2Archipelago.Managers;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Tower of Oannes' Dark Fish Crystal (fEx1_Rout2) and Fish-Slime Zero (fEx1_Lout)
    /// escapes are instantiated by an NPC conversation, so they don't exist during the
    /// one-shot <see cref="SceneRandomizer.OnSceneLoaded"/> scan and ChangeEntrances'
    /// active-only <c>FindObjectsOfType</c> never sees them. Left alone they keep their
    /// vanilla destination, giving an unshuffled route into Bailey Bottom that the seed's
    /// logic doesn't know about.
    ///
    /// Start() is where an AnchorGateZ registers itself, so it fires for late-spawned
    /// gates too. SceneRandomizer dedups via <c>processedGates</c>, which matters here:
    /// scene gates run Start() after ChangeEntrances has already rewritten them, and
    /// re-mapping an AnchorName that is now a destination would send them somewhere else.
    /// </summary>
    [HarmonyPatch(typeof(AnchorGateZ), "Start")]
    internal static class AnchorGateZLateSpawnPatch
    {
        static void Postfix(AnchorGateZ __instance)
        {
            SceneRandomizer.Instance?.TryRewriteGateLate(__instance);
        }
    }
}
