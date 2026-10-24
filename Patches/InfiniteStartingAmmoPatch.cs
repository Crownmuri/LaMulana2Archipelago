using HarmonyLib;
using L2Base;
using L2Base.Charcter;
using L2Hit;
using LaMulana2RandomizerShared;
using LM2RandomiserMod;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Quality-of-life: when the seed's starting weapon is a sub-weapon, firing it
    /// never uses up ammo.
    ///
    /// Vanilla spends one round in <c>PlayerAnimetion.callAdherePart()</c>, which
    /// calls <c>L2System.addSubWeaponNum(ammoSlot, -1)</c> as the shot leaves.
    /// We only block that call, and only while it is running, so shops, pots, AP
    /// grants and every other ammo write behave as normal. Other sub-weapons
    /// still use ammo.
    /// </summary>
    [HarmonyPatch]
    internal static class InfiniteStartingAmmoPatch
    {
        /// <summary>Master switch — <c>Plugin</c> binds this to a BepInEx config entry.</summary>
        internal static bool Enabled = false;

        // True only while vanilla's fire routine runs.
        private static bool _inFire;

        [HarmonyPatch(typeof(PlayerAnimetion), nameof(PlayerAnimetion.callAdherePart))]
        [HarmonyPrefix]
        private static void FirePrefix() => _inFire = true;

        [HarmonyPatch(typeof(PlayerAnimetion), nameof(PlayerAnimetion.callAdherePart))]
        [HarmonyFinalizer]
        private static void FireFinalizer() => _inFire = false;

        [HarmonyPatch(typeof(L2System), nameof(L2System.addSubWeaponNum))]
        [HarmonyPrefix]
        private static bool AddSubWeaponNumPrefix(L2System __instance, SUBWEAPON sw, int add)
        {
            if (!Enabled || !_inFire || add >= 0)
                return true;

            return !IsStartingAmmo(__instance, sw);
        }

        private static bool IsStartingAmmo(L2System sys, SUBWEAPON sw)
        {
            ItemInfo info = ItemDB.GetItemInfo((ItemID)GameFlagResetsPatch.StartingWeapon);
            if (info == null)
                return false;

            SUBWEAPON start = sys.exchengeSubWeaponNameToEnum(info.BoxName);
            switch (start)
            {
                case SUBWEAPON.SUB_SYURIKEN: return sw == SUBWEAPON.SUB_SYURIKEN_B;
                case SUBWEAPON.SUB_KURUMA:   return sw == SUBWEAPON.SUB_KURUMA_B;
                case SUBWEAPON.SUB_DAICHI:   return sw == SUBWEAPON.SUB_DAICHI_B;
                case SUBWEAPON.SUB_HATUDAN:  return sw == SUBWEAPON.SUB_HATUDAN_B;
                case SUBWEAPON.SUB_MAKIBI:   return sw == SUBWEAPON.SUB_MAKIBI_B;
                case SUBWEAPON.SUB_CHAKURA:  return sw == SUBWEAPON.SUB_CHAKURA_B;
                case SUBWEAPON.SUB_BOM:      return sw == SUBWEAPON.SUB_BOM_B;
                case SUBWEAPON.SUB_GUN:      return sw == SUBWEAPON.SUB_GUN_B || sw == SUBWEAPON.SUB_REGUN;
                default:                     return false;
            }
        }
    }
}
