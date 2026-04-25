using HarmonyLib;
using L2Base;
using L2Word;
using L2Hit;
using L2STATUS;

namespace LaMulana2Archipelago.Patches
{
    // Tracks when L2System.reInitSystem is executing so StatusResetPatch can suppress
    // the hardcoded MAINWEAPON.LWHIP that the vanilla method passes to resetPlayerStatus.
    [HarmonyPatch(typeof(L2System), "reInitSystem")]
    internal static class ReInitSystemPatch
    {
        internal static bool InProgress = false;

        static void Prefix() => InProgress = true;
        static void Postfix() => InProgress = false;
    }

    /// <summary>
    /// Replaces Status.resetPlayerStatus to use sys.setItem for weapon
    /// initialization, ensuring progressive weapon logic is applied.
    /// Port of the original randomizer's MonoMod patch.
    /// </summary>
    [HarmonyPatch(typeof(Status), nameof(Status.resetPlayerStatus))]
    internal static class StatusResetPatch
    {
        public static bool Enabled = false;

        static bool Prefix(Status __instance, int lv, int hp, int mcoin, int coin, int wait, int exp,
            MAINWEAPON now_wea, int now_wea_num, SUBWEAPON now_sub, int now_sub_num,
            USEITEM now_use, int now_use_num)
        {
            if (!Enabled) return true;

            var trav = Traverse.Create(__instance);
            var sys = trav.Field("sys").GetValue<L2System>();
            var statusbar = trav.Field("statusbar").GetValue<StatusBarIF>();
            int HPTANK = trav.Field("HPTANK").GetValue<int>();

            sys.setFlagData(2, 62, 0);
            __instance.clearItemsNum();

            // reInitSystem hardcodes MAINWEAPON.LWHIP; suppress setItem/equipItem so the
            // subsequent memLoad() can restore the correct state without a stale whip flag.
            if (!ReInitSystemPatch.InProgress)
            {
                string weaponName = string.Empty;
                if (now_wea != MAINWEAPON.NON)
                    weaponName = sys.exchengeMainWeaponEnumToName(now_wea);
                else if (now_sub != SUBWEAPON.NON)
                    weaponName = sys.exchengeSubWeaponEnumToName(now_sub);

                if (!string.IsNullOrEmpty(weaponName))
                {
                    sys.setItem(weaponName, 1, false, false, true);
                    sys.equipItem(weaponName, true);
                }
            }

            trav.Field("player_level").SetValue(lv < 1 ? 1 : lv);
            int playerLevel = lv < 1 ? 1 : lv;

            if (hp < 1)
                trav.Field("l2_hp").SetValue(HPTANK * playerLevel);
            else
                trav.Field("l2_hp").SetValue(hp);

            trav.Field("max_hp").SetValue(HPTANK * playerLevel);
            trav.Field("max_coin").SetValue(mcoin);
            trav.Field("l2_coin").SetValue(coin);

            __instance.setMainWeapon(now_wea);
            __instance.setSubWeapon(now_sub);
            __instance.setUseItem(now_use);

            int l2_hp = trav.Field("l2_hp").GetValue<int>();
            int max_hp = trav.Field("max_hp").GetValue<int>();
            statusbar.setInitHP(l2_hp / 100, (float)(max_hp / 100));

            trav.Field("l2Exp").SetValue(exp);
            statusbar.setExp(exp);

            if (mcoin < 1000)
            {
                statusbar.setCoin(coin, 3);
                statusbar.changeCoinMax(999);
            }
            else
            {
                statusbar.setCoin(coin, 4);
                statusbar.changeCoinMax(2000);
            }

            trav.Field("l2_wait").SetValue(wait);
            statusbar.setWait(wait);

            var l2_eq_main = trav.Field("l2_eq_main").GetValue<MAINWEAPON>();
            var l2_main = trav.Field("l2_main").GetValue<int[]>();
            var l2_eq_sub = trav.Field("l2_eq_sub").GetValue<SUBWEAPON>();
            var l2_sub = trav.Field("l2_sub").GetValue<int[]>();
            var l2_eq_use = trav.Field("l2_eq_use").GetValue<USEITEM>();
            var l2_use = trav.Field("l2_use").GetValue<int[]>();

            statusbar.setMain(l2_eq_main, l2_main[(int)now_wea]);
            statusbar.setSub(l2_eq_sub, l2_sub[(int)now_sub]);
            statusbar.setUse(l2_eq_use, l2_use[(int)now_use]);

            return false;
        }
    }

    /// <summary>
    /// Replaces Status.changeMainWeapon to properly cycle through weapons
    /// including progressive whip tiers.
    /// Port of the original randomizer's MonoMod patch.
    /// </summary>
    [HarmonyPatch(typeof(Status), nameof(Status.changeMainWeapon))]
    internal static class StatusChangeMainWeaponPatch
    {
        public static bool Enabled = false;

        static bool Prefix(Status __instance, int slide_vector, ref MAINWEAPON __result)
        {
            if (!Enabled) return true;

            MAINWEAPON mainweapon = __instance.getMainWeapon();

            for (int i = 0; i < 5; i++)
            {
                if (slide_vector == 1)
                {
                    mainweapon = mainweapon switch
                    {
                        MAINWEAPON.LWHIP or MAINWEAPON.MWIHP or MAINWEAPON.HWHIP => MAINWEAPON.KNIFE,
                        MAINWEAPON.KNIFE => MAINWEAPON.RAPIER,
                        MAINWEAPON.RAPIER => MAINWEAPON.AXE,
                        MAINWEAPON.AXE => MAINWEAPON.SWORD,
                        _ => GetHighestWhip(__instance),
                    };
                }
                else
                {
                    mainweapon = mainweapon switch
                    {
                        MAINWEAPON.LWHIP or MAINWEAPON.MWIHP or MAINWEAPON.HWHIP or MAINWEAPON.NON => MAINWEAPON.SWORD,
                        MAINWEAPON.KNIFE => GetHighestWhip(__instance),
                        MAINWEAPON.RAPIER => MAINWEAPON.KNIFE,
                        MAINWEAPON.AXE => MAINWEAPON.RAPIER,
                        MAINWEAPON.SWORD => MAINWEAPON.AXE,
                        _ => MAINWEAPON.SWORD,
                    };
                }

                if (__instance.isMainWeapon(mainweapon))
                {
                    __result = mainweapon;
                    return false;
                }
            }

            __result = MAINWEAPON.NON;
            return false;
        }

        private static MAINWEAPON GetHighestWhip(Status status)
        {
            if (status.isMainWeapon(MAINWEAPON.HWHIP)) return MAINWEAPON.HWHIP;
            if (status.isMainWeapon(MAINWEAPON.MWIHP)) return MAINWEAPON.MWIHP;
            return MAINWEAPON.LWHIP;
        }
    }
}
