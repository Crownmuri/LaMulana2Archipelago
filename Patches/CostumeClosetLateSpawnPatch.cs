using HarmonyLib;
using LaMulana2Archipelago.Managers;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Costumesanity: some costume closets aren't present during the one-shot
    /// <see cref="SceneRandomizer.OnSceneLoaded"/> scan. The Fish Suit closet in
    /// Tower of Oannes, in particular, is only spawned/activated after its
    /// miniboss is defeated — so <c>ChangeTreasureChests</c>' active-only
    /// <c>FindObjectsOfType</c> never sees it, leaving the vanilla locked closet.
    ///
    /// This polls every closet's per-frame state machine and converts it the
    /// moment it goes live. SceneRandomizer dedups via <c>processedClosets</c>,
    /// so this is a cheap no-op once a closet has been handled. Gated on
    /// <see cref="CostumeManager.Enabled"/> + <c>closetMode</c> so non-costume
    /// chests and non-costumesanity seeds pay only two early-out checks.
    /// </summary>
    [HarmonyPatch(typeof(TreasureBoxScript), nameof(TreasureBoxScript.groundFirst))]
    internal static class CostumeClosetLateSpawnPatch
    {
        static void Postfix(TreasureBoxScript __instance)
        {
            if (!CostumeManager.Enabled) return;
            if (__instance == null || !__instance.closetMode) return;
            SceneRandomizer.Instance?.TryConvertCostumeClosetLate(__instance);
        }
    }
}
