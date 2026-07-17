using HarmonyLib;
using L2Base;
using LaMulana2Archipelago.Managers;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Answers <c>getClothBox</c> from the AP-received set instead of the
    /// profile-global clothbox when costumesanity is on. This is what makes the
    /// game agree with AP about which costumes the player owns, without ever
    /// writing the profile (a costume unlocked in a previous non-AP playthrough
    /// stays out of the seed, and an AP costume doesn't leak out of it).
    ///
    /// The four vanilla callers all want that answer:
    ///   - setSystemDataToClothFlag — derives the sheet-2 costume flags (fashion
    ///     availability) from it on every load.
    ///   - L2SaveAndLoad.memLoad + NewPlayer.changeCostume — unequip guards that
    ///     would otherwise strip a worn AP costume back to the default on load.
    ///   - TreasureBoxScript.resetActionCharacter — closet "already opened"
    ///     state, only reached with closetMode=true; SceneRandomizer clears that
    ///     on the chests it converts, so it never sees this.
    /// </summary>
    [HarmonyPatch(typeof(L2System), nameof(L2System.getClothBox))]
    internal static class ClothBoxPatch
    {
        static void Postfix(L2System __instance, int no, ref bool __result)
        {
            if (!CostumeManager.Enabled) return;
            __result = CostumeManager.IsReceived(__instance, no);
        }
    }
}
