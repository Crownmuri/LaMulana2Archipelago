using HarmonyLib;
using L2Base;
using L2Hit;
using L2Word;
using L2STATUS;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Replaces L2System.setItem() with the randomizer-aware version.
    /// This is a port of the original randomizer's MonoMod [MonoModReplace] on setItem.
    ///
    /// Handles progressive items (Whip1/2/3, Shield1/2/3, Research1-10, Beherit1-7),
    /// special items, and all weapon/equipment/consumable logic.
    /// </summary>
    [HarmonyPatch(typeof(L2System), nameof(L2System.setItem))]
    public static class SetItemPatch
    {
        /// <summary>
        /// Set to true once AP connection provides slot_data.
        /// When false, the patch is a no-op and the vanilla setItem runs.
        /// </summary>
        public static bool Enabled = false;

        static bool Prefix(L2System __instance, string item_name, int num, bool direct, bool loadcall, bool sub_add)
        {
            if (!Enabled)
                return true; // run vanilla

            // AP placeholder items are handled by SetItemApPatch — don't process here
            if (item_name == "AP Item" || item_name.StartsWith("AP Item "))
                return false;

            // Access private field 'playerst' via Traverse
            var playerst = Traverse.Create(__instance).Field("playerst").GetValue<Status>();
            if (playerst == null)
                return true; // safety fallback

            int value = num;
            int num2 = __instance.SeetNametoNo("02Items");
            int num3 = __instance.SeetNametoNo("00system");
            if (num2 < 0)
                return false;

            if (!direct)
            {
                if (item_name.Contains("Whip"))
                {
                    if (item_name == "Whip1") __instance.setFlagData(num2, 190, 1);
                    else if (item_name == "Whip2") __instance.setFlagData(num2, 191, 1);
                    else if (item_name == "Whip3") __instance.setFlagData(num2, 192, 1);

                    short data = 0;
                    __instance.getFlag(2, "Whip", ref data);
                    value = data + 1;
                }
                else if (item_name.Contains("Shield"))
                {
                    if (item_name == "Shield1") __instance.setFlagData(num2, 193, 1);
                    else if (item_name == "Shield2") __instance.setFlagData(num2, 194, 1);
                    else if (item_name == "Shield3") __instance.setFlagData(num2, 195, 1);

                    short data = 0;
                    __instance.getFlag(2, 196, ref data);
                    value = data + 1;
                    __instance.setFlagData(num2, 196, (short)(data + 1));
                }
                else if (item_name.Contains("Research"))
                {
                    switch (item_name)
                    {
                        case "Research1": __instance.setFlagData(num2, 180, 1); break;
                        case "Research2": __instance.setFlagData(num2, 181, 1); break;
                        case "Research3": __instance.setFlagData(num2, 182, 1); break;
                        case "Research4": __instance.setFlagData(num2, 183, 1); break;
                        case "Research5": __instance.setFlagData(num2, 184, 1); break;
                        case "Research6": __instance.setFlagData(num2, 185, 1); break;
                        case "Research7": __instance.setFlagData(num2, 186, 1); break;
                        case "Research8": __instance.setFlagData(num2, 187, 1); break;
                        case "Research9": __instance.setFlagData(num2, 188, 1); break;
                        case "Research10": __instance.setFlagData(num2, 189, 1); break;
                    }
                    item_name = "Research";
                }
                else if (item_name.Contains("Beherit") && !item_name.Equals("Beherit"))
                {
                    switch (item_name)
                    {
                        case "Beherit1": __instance.setFlagData(num2, 170, 1); break;
                        case "Beherit2": __instance.setFlagData(num2, 171, 1); break;
                        case "Beherit3": __instance.setFlagData(num2, 172, 1); break;
                        case "Beherit4": __instance.setFlagData(num2, 173, 1); break;
                        case "Beherit5": __instance.setFlagData(num2, 174, 1); break;
                        case "Beherit6": __instance.setFlagData(num2, 175, 1); break;
                        case "Beherit7": __instance.setFlagData(num2, 176, 1); break;
                    }

                    USEITEM item = __instance.exchengeUseItemNameToEnum("Beherit");
                    __instance.haveUsesItem(item, true);
                    __instance.addUseItemNum(item, 1);

                    short data = 0;
                    __instance.getFlag(2, 3, ref data);
                    __instance.setFlagData(num2, 3, (short)(data + 1));

                    return false; // skip original
                }
                else if (item_name.Contains("Mantra") && !item_name.Equals("Mantra"))
                {
                    __instance.setFlagData(num2, item_name, (short)value);
                    return false; // skip original
                }

                if (item_name.Equals("Lamp"))
                {
                    value = 2;
                }
            }

            if (item_name.Contains("Whip"))
            {
                if (value == 1)
                {
                    item_name = "Whip";
                    playerst.setMainWeaponNum(MAINWEAPON.MWIHP, 0);
                    __instance.haveMainWeapon(MAINWEAPON.MWIHP, false);
                    playerst.setMainWeaponNum(MAINWEAPON.HWHIP, 0);
                    __instance.haveMainWeapon(MAINWEAPON.HWHIP, false);
                }
                else if (value == 2)
                {
                    item_name = "Whip2";
                    value = 1;
                    playerst.setMainWeaponNum(MAINWEAPON.HWHIP, 0);
                    __instance.haveMainWeapon(MAINWEAPON.HWHIP, false);
                }
                else if (value >= 3)
                {
                    item_name = "Whip3";
                    value = 1;
                }
            }
            else if (item_name.Contains("Shield"))
            {
                if (value == 1) item_name = "Shield";
                else if (value == 2) item_name = "Shield2";
                else if (value >= 3) item_name = "Shield3";
                __instance.setFlagData(num2, "Shield", (short)value);
            }

            if (value == 0)
                return false;

            if (item_name == "Weight")
            {
                if (direct)
                    playerst.setWait(value);
                else
                    playerst.addWait(value);
                return false;
            }
            else if (item_name == "Gold Bangle")
            {
                playerst.setMaxCoin(2000);
                playerst.setCoin(playerst.getCoin());
            }
            else if (item_name == "Gold")
            {
                if (direct)
                    playerst.setCoin(value);
                else
                    playerst.addCoin(value);
                return false;
            }
            else if (item_name == "Soul")
            {
                if (direct)
                    playerst.setExp(value);
                else
                    playerst.addExp(value);
            }
            else if (item_name == "Sacred Orb")
            {
                if (direct)
                    playerst.setPLayerLevel(value);
                else
                    playerst.setPLayerLevel(playerst.getPlayerLevel() + 1);
            }
            else
            {
                if (__instance.isAnkJewel(item_name))
                {
                    __instance.setFlagData(num2, item_name, (short)value);
                    short num4 = 0;
                    if (!direct)
                    {
                        __instance.getFlag(num3, "A_Jewel", ref num4);
                        value += (int)num4;
                    }
                    __instance.setFlagData(num3, "A_Jewel", (short)value);
                    item_name = "A_Jewel";
                    goto IL_PostFlag;
                }

                if (item_name == "Ankh Jewel")
                {
                    short num5 = 0;
                    if (!direct)
                    {
                        __instance.getFlag(num3, "A_Jewel", ref num5);
                        value += (int)num5;
                    }
                    __instance.setFlagData(num3, "A_Jewel", (short)value);
                    goto IL_PostFlag;
                }

                if (item_name == "A_Jewel")
                {
                    short num6 = 0;
                    if (!direct)
                    {
                        __instance.getFlag(num3, "A_Jewel", ref num6);
                        value += (int)num6;
                    }
                    __instance.setFlagData(num3, "A_Jewel", (short)value);
                    goto IL_PostFlag;
                }

                if (item_name == "pistolBox")
                {
                    goto IL_PostFlag;
                }

                if (item_name == "Pepper")
                {
                    if (!direct)
                    {
                        __instance.setFlagData(num3, "Pepper-b", 69);
                    }
                }
                else if (L2SystemCore.getItemData(item_name) != null && L2SystemCore.getItemData(item_name).isEquipableItem())
                {
                    if (!L2SystemCore.getItemData(item_name).isSoftWare())
                    {
                        if (L2SystemCore.getItemData(item_name).isForceEquipItem())
                        {
                            if (!direct)
                            {
                                __instance.equipItem(item_name, true);
                            }
                            if (__instance.getPlayer() != null)
                            {
                                __instance.getPlayer().checkEquipItem();
                            }
                        }
                    }
                    else if (item_name == "Xelputter" && !loadcall)
                    {
                        for (int i = 0; i < 1; i++)
                        {
                            int[] softLiveData = __instance.getSoftLiveData(i);
                            for (int j = 0; j < softLiveData.Length; j++)
                            {
                                if (softLiveData[j] == -1)
                                {
                                    softLiveData[j] = 0;
                                    __instance.setSoftLive(i, ItemDatabaseSystem.ItemNames.Xelputter, true);
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            if (direct)
            {
                if (item_name == "Shield")
                {
                    if (value == 1) item_name = "Shield";
                    else if (value == 2) item_name = "Shield2";
                    else if (value >= 3) item_name = "Shield3";
                    __instance.setFlagData(num2, "Shield", (short)value);
                }
                else
                {
                    __instance.setFlagData(num2, item_name, (short)value);
                }
            }
            else
            {
                __instance.setFlagData(num2, item_name, (short)value);
            }

        IL_PostFlag:
            MAINWEAPON mainweapon = __instance.exchengeMainWeaponNameToEnum(item_name);
            if (mainweapon != MAINWEAPON.NON)
            {
                __instance.haveMainWeapon(mainweapon, true);
                if (direct)
                {
                    if (mainweapon == MAINWEAPON.LWHIP)
                    {
                        __instance.setFlagData(num2, "Whip", 1);
                        playerst.setMainWeaponNum(mainweapon, value);
                    }
                    else if (mainweapon == MAINWEAPON.MWIHP)
                    {
                        __instance.setFlagData(num2, "Whip", 2);
                        playerst.setMainWeaponNum(mainweapon, value);
                    }
                    else if (mainweapon == MAINWEAPON.HWHIP)
                    {
                        __instance.setFlagData(num2, "Whip", 3);
                        playerst.setMainWeaponNum(mainweapon, value);
                    }
                }
                else
                {
                    if (mainweapon == MAINWEAPON.LWHIP)
                    {
                        __instance.setFlagData(num2, "Whip", 1);
                    }
                    else if (mainweapon == MAINWEAPON.MWIHP)
                    {
                        __instance.setFlagData(num2, "Whip", 2);
                    }
                    else if (mainweapon == MAINWEAPON.HWHIP)
                    {
                        __instance.setFlagData(num2, "Whip", 3);
                    }
                    __instance.addMainWeaponNum(mainweapon, 1);
                }
                return false;
            }

            SUBWEAPON subweapon = __instance.exchengeSubWeaponNameToEnum(item_name);
            if (subweapon != SUBWEAPON.NON)
            {
                if (subweapon > SUBWEAPON.SUB_ANKJEWEL && subweapon != SUBWEAPON.SUB_REGUN)
                {
                    if (direct)
                    {
                        playerst.setSubWeaponNum(subweapon, value, false);
                    }
                    else
                    {
                        __instance.addSubWeaponNum(subweapon, value);
                    }
                }
                else if (loadcall)
                {
                    if (subweapon == SUBWEAPON.SUB_SHIELD1)
                    {
                        if (value == 2)
                        {
                            subweapon = SUBWEAPON.SUB_SHIELD2;
                        }
                        else if (value == 3)
                        {
                            subweapon = SUBWEAPON.SUB_SHIELD3;
                        }
                    }
                    __instance.haveSubWeapon(subweapon, true, false);
                }
                else
                {
                    __instance.haveSubWeapon(subweapon, true, sub_add);
                    if (subweapon == SUBWEAPON.SUB_REGUN)
                    {
                        playerst.addSubWeaponNum(subweapon, value);
                    }
                }
                return false;
            }

            USEITEM useitem = __instance.exchengeUseItemNameToEnum(item_name);
            if (useitem != USEITEM.NON)
            {
                __instance.haveUsesItem(useitem, true);
                if (direct)
                {
                    playerst.setUseItemNum(useitem, value);
                    if (useitem == USEITEM.USE_PEPPER_B)
                    {
                        __instance.setFlagData(num3, "Pepper-b", (short)value);
                    }
                    else if (useitem == USEITEM.USE_CRYSTAL_S_B)
                    {
                        __instance.setFlagData(num3, "Crystal S-b", (short)value);
                    }
                }
                else
                {
                    __instance.addUseItemNum(useitem, 1);
                    if (useitem == USEITEM.USE_PEPPER_B)
                    {
                        __instance.setFlagData(num3, "Pepper-b", (short)playerst.getUseItemNum(useitem));
                    }
                    else if (useitem == USEITEM.USE_CRYSTAL_S_B)
                    {
                        __instance.setFlagData(num3, "Crystal S-b", (short)playerst.getUseItemNum(useitem));
                    }
                }
                return false;
            }

            return false; // skip original — we handled everything
        }
    }

    /// <summary>
    /// Replaces L2System.isHaveItem() with the randomizer-aware version.
    /// Handles progressive/numbered item variants that the vanilla method doesn't know about.
    /// </summary>
    [HarmonyPatch(typeof(L2System), nameof(L2System.isHaveItem))]
    public static class IsHaveItemPatch
    {
        public static bool Enabled = false;

        static bool Prefix(L2System __instance, string name, ref int __result)
        {
            if (!Enabled)
                return true;

            short num = 0;
            int seet = __instance.SeetNametoNo("02Items");

            switch (name)
            {
                case "Beherit1": __instance.getFlag(seet, 170, ref num); __result = num; return false;
                case "Beherit2": __instance.getFlag(seet, 171, ref num); __result = num; return false;
                case "Beherit3": __instance.getFlag(seet, 172, ref num); __result = num; return false;
                case "Beherit4": __instance.getFlag(seet, 173, ref num); __result = num; return false;
                case "Beherit5": __instance.getFlag(seet, 174, ref num); __result = num; return false;
                case "Beherit6": __instance.getFlag(seet, 175, ref num); __result = num; return false;
                case "Beherit7": __instance.getFlag(seet, 176, ref num); __result = num; return false;
                case "Research1": __instance.getFlag(seet, 180, ref num); __result = num; return false;
                case "Research2": __instance.getFlag(seet, 181, ref num); __result = num; return false;
                case "Research3": __instance.getFlag(seet, 182, ref num); __result = num; return false;
                case "Research4": __instance.getFlag(seet, 183, ref num); __result = num; return false;
                case "Research5": __instance.getFlag(seet, 184, ref num); __result = num; return false;
                case "Research6": __instance.getFlag(seet, 185, ref num); __result = num; return false;
                case "Research7": __instance.getFlag(seet, 186, ref num); __result = num; return false;
                case "Research8": __instance.getFlag(seet, 187, ref num); __result = num; return false;
                case "Research9": __instance.getFlag(seet, 188, ref num); __result = num; return false;
                case "Research10": __instance.getFlag(seet, 189, ref num); __result = num; return false;
                case "Whip1": __instance.getFlag(seet, 190, ref num); __result = num; return false;
                case "Whip2": __instance.getFlag(seet, 191, ref num); __result = num; return false;
                case "Whip3": __instance.getFlag(seet, 192, ref num); __result = num; return false;
                case "Shield1": __instance.getFlag(seet, 193, ref num); __result = num; return false;
                case "Shield2": __instance.getFlag(seet, 194, ref num); __result = num; return false;
                case "Shield3": __instance.getFlag(seet, 195, ref num); __result = num; return false;
                default:
                {
                    if (name == "A_Jewel" || name == "Ankh Jewel")
                    {
                        if (name == "Ankh Jewel"
                            && GuardianSpecificAnkhPatch.GuardianSpecificAnkhsEnabled
                            && GuardianSpecificAnkhPatch.IsAnkhContextActive)
                            return true;

                        __instance.getFlag(__instance.SeetNametoNo("00system"), "A_Jewel", ref num);
                    }
                    else
                    {
                        __instance.getFlag(seet, name, ref num);
                    }
                    __result = num;
                    return false;
                }
            }
        }
    }

    /// <summary>
    /// Replaces L2System.getItemNum() with the randomizer-aware version.
    /// Handles progressive/numbered item variants and special item types.
    /// </summary>
    [HarmonyPatch(typeof(L2System), nameof(L2System.getItemNum))]
    public static class GetItemNumPatch
    {
        public static bool Enabled = false;

        [HarmonyPriority(Priority.Low)] // run after GuardianSpecificAnkhPatch
        static bool Prefix(L2System __instance, string item_name, ref int __result)
        {
            if (!Enabled)
                return true;

            var playerst = Traverse.Create(__instance).Field("playerst").GetValue<Status>();
            if (playerst == null)
                return true;

            short num = 0;
            int num2 = __instance.SeetNametoNo("02Items");
            if (num2 < 0)
            {
                __result = -1;
                return false;
            }

            switch (item_name)
            {
                case "Weight": __result = playerst.getWait(); return false;
                case "Money": __result = playerst.getCoin(); return false;
                case "Beherit1": __instance.getFlag(num2, 170, ref num); __result = num; return false;
                case "Beherit2": __instance.getFlag(num2, 171, ref num); __result = num; return false;
                case "Beherit3": __instance.getFlag(num2, 172, ref num); __result = num; return false;
                case "Beherit4": __instance.getFlag(num2, 173, ref num); __result = num; return false;
                case "Beherit5": __instance.getFlag(num2, 174, ref num); __result = num; return false;
                case "Beherit6": __instance.getFlag(num2, 175, ref num); __result = num; return false;
                case "Beherit7": __instance.getFlag(num2, 176, ref num); __result = num; return false;
                case "Research1": __instance.getFlag(num2, 180, ref num); __result = num; return false;
                case "Research2": __instance.getFlag(num2, 181, ref num); __result = num; return false;
                case "Research3": __instance.getFlag(num2, 182, ref num); __result = num; return false;
                case "Research4": __instance.getFlag(num2, 183, ref num); __result = num; return false;
                case "Research5": __instance.getFlag(num2, 184, ref num); __result = num; return false;
                case "Research6": __instance.getFlag(num2, 185, ref num); __result = num; return false;
                case "Research7": __instance.getFlag(num2, 186, ref num); __result = num; return false;
                case "Research8": __instance.getFlag(num2, 187, ref num); __result = num; return false;
                case "Research9": __instance.getFlag(num2, 188, ref num); __result = num; return false;
                case "Research10": __instance.getFlag(num2, 189, ref num); __result = num; return false;
                case "Whip1": __instance.getFlag(num2, 190, ref num); __result = num; return false;
                case "Whip2": __instance.getFlag(num2, 191, ref num); __result = num; return false;
                case "Whip3": __instance.getFlag(num2, 192, ref num); __result = num; return false;
                case "Shield1": __instance.getFlag(num2, 193, ref num); __result = num; return false;
                case "Shield2": __instance.getFlag(num2, 194, ref num); __result = num; return false;
                case "Shield3": __instance.getFlag(num2, 195, ref num); __result = num; return false;
                case "MSX": __instance.getFlag(num2, "MSX", ref num); __result = num == 2 ? 1 : 0; return false;
                default:
                {
                    if (item_name == "Weight")
                    {
                        __result = playerst.getWait();
                        return false;
                    }
                    if (item_name == "Gold")
                    {
                        __result = playerst.getCoin();
                        return false;
                    }
                    if (item_name == "A_Jewel" || item_name == "Ankh Jewel")
                    {
                        // When guardian-specific ankhs are active and we're inside an AnchScript,
                        // let the GuardianSpecificAnkhPatch handle this call instead of returning
                        // the global A_Jewel counter (which would let any jewel work at any ankh).
                        if (item_name == "Ankh Jewel"
                            && GuardianSpecificAnkhPatch.GuardianSpecificAnkhsEnabled
                            && GuardianSpecificAnkhPatch.IsAnkhContextActive)
                            return true; // fall through to GuardianSpecificAnkhPatch or original

                        __instance.getFlag(__instance.SeetNametoNo("00system"), "A_Jewel", ref num);
                        __result = num;
                        return false;
                    }
                    if (!__instance.getFlag(num2, item_name, ref num))
                    {
                        num = -1;
                    }
                    MAINWEAPON mainweapon = __instance.exchengeMainWeaponNameToEnum(item_name);
                    if (mainweapon != MAINWEAPON.NON)
                    {
                        __instance.getFlag(num2, item_name, ref num);
                        __result = num > 0 ? 1 : 0;
                        return false;
                    }
                    SUBWEAPON subweapon = __instance.exchengeSubWeaponNameToEnum(item_name);
                    if (subweapon != SUBWEAPON.NON && subweapon > SUBWEAPON.SUB_ANKJEWEL)
                    {
                        num = (short)__instance.getSubWeaponNum(subweapon);
                    }
                    USEITEM useitem = __instance.exchengeUseItemNameToEnum(item_name);
                    if (useitem != USEITEM.NON)
                    {
                        num = (short)__instance.getUseItemNum(useitem);
                    }
                    __result = num;
                    return false;
                }
            }
        }
    }
}
