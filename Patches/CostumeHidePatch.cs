using HarmonyLib;
using L2Base;
using LaMulana2Archipelago.Managers;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// The game maps profile-global costume unlocks into
    /// the per-seed sheet-2 costume flags via <c>L2System.setSystemDataToClothFlag</c>
    /// (called on save-load and reset). We post-process that sync to hide every
    /// costume the player has not yet received from AP, so a costume the player
    /// unlocked in a previous (non-AP) playthrough can't leak into the seed.
    ///
    /// No-op unless costumesanity is enabled (see <see cref="CostumeManager"/>),
    /// so non-costumesanity seeds keep vanilla costume behavior.
    /// </summary>
    [HarmonyPatch(typeof(L2System), "setSystemDataToClothFlag")]
    internal static class CostumeHidePatch
    {
        static void Postfix(L2System __instance)
        {
            CostumeManager.ApplyToFlags(__instance);
        }
    }
}
