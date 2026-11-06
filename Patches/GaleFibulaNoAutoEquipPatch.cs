using HarmonyLib;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Stops the Gale Fibula from being put on automatically when it is obtained.
    ///
    /// <c>L2System.setItem()</c> (and its copy in <see cref="SetItemPatch"/>) equips
    /// any non-software equipable item whose <c>ItemData.isForceEquipItem()</c> is
    /// true. The Fibula is one of them, so picking it up starts permanent dashing
    /// straight away. Reporting it as not force-equip leaves it in the inventory
    /// for the player to equip from the menu or a shortcut
    /// (<see cref="QuickToggleGaleFibulaPatch"/>).
    ///
    /// The force-equip flag is only read by those pickup paths, so nothing else
    /// changes: the menu still equips and removes it as normal.
    /// </summary>
    [HarmonyPatch(typeof(ItemData), nameof(ItemData.isForceEquipItem))]
    internal static class GaleFibulaNoAutoEquipPatch
    {
        private const string GBandId = "G Band";

        static void Postfix(ItemData __instance, ref bool __result)
        {
            if (__result && __instance.getItemId() == GBandId)
                __result = false;
        }
    }
}
