using HarmonyLib;
using L2Base;
using L2Hit;
using L2MobTask;
using L2Word;
using L2STATUS;
using L2Flag;
using L2Menu;
using LaMulana2Archipelago.Managers;
using LaMulana2RandomizerShared;
using LM2RandomiserMod;
using System.Collections.Generic;
using UnityEngine;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Replaces L2System.gameFlagResets() with the randomizer-aware version.
    /// Sets starting weapon, area, items, money, weights, and various game flags
    /// based on AP slot_data.
    ///
    /// Port of the original randomizer's MonoMod [MonoModReplace] on gameFlagResets.
    /// </summary>
    [HarmonyPatch(typeof(L2System), nameof(L2System.gameFlagResets))]
    public static class GameFlagResetsPatch
    {
        public static bool Enabled = false;

        // These are populated from slot_data after AP connection
        public static int StartingWeapon = 0;
        public static int StartingArea = 0;
        public static List<int> StartingItems = new List<int>();
        public static int StartingMoney = 200;
        public static int StartingWeights = 10;
        public static int RequiredSkulls = 6;
        public static bool RemoveITStatue = true;
        public static bool AutoPlaceSkull = true;

        /// <summary>
        /// Call after AP connection to populate settings from slot_data.
        /// </summary>
        public static void LoadFromSlotData(Archipelago.ArchipelagoData serverData)
        {
            StartingWeapon = serverData.GetSlotInt("starting_weapon", 0);
            StartingArea = serverData.GetSlotInt("starting_area", 0);
            StartingMoney = serverData.GetSlotInt("starting_money", 200);
            StartingWeights = serverData.GetSlotInt("starting_weights", 10);
            RequiredSkulls = serverData.GetSlotInt("required_skulls", 6);
            RemoveITStatue = serverData.GetSlotBool("remove_it_statue", true);
            AutoPlaceSkull = serverData.GetSlotBool("auto_place_skull", true);

            StartingItems.Clear();
            // starting_items comes as a JArray of ints from slot_data
            if (serverData.GetSlotRaw("starting_items") is Newtonsoft.Json.Linq.JArray arr)
            {
                foreach (var token in arr)
                    StartingItems.Add((int)token);
            }

            Plugin.Log.LogInfo($"[AP] GameFlagResets loaded: weapon={StartingWeapon} area={StartingArea} " +
                $"money={StartingMoney} weights={StartingWeights} skulls={RequiredSkulls} " +
                $"startingItems={StartingItems.Count}");
        }

        static bool Prefix(L2System __instance)
        {
            if (!Enabled)
                return true; // run vanilla

            var trav = Traverse.Create(__instance);
            var playerst = trav.Field("playerst").GetValue<Status>();
            var fsys = trav.Field("fsys").GetValue<L2FlagSystem>();
            var menusys = trav.Field("menusys").GetValue<MenuSystem>();

            if (playerst == null || fsys == null || menusys == null)
                return true; // safety fallback

            int Init_Coin_num = StartingMoney;
            int Init_Weight_num = StartingWeights;
            int Init_PLayer_lv = trav.Field("Init_PLayer_lv").GetValue<int>();

            fsys.allReset();
            playerst.clearItemsNum();
            menusys.menuSysReStart();
            __instance.setSystemDataToClothFlag();

            MAINWEAPON mainWeapon = MAINWEAPON.NON;
            SUBWEAPON subWeapon = SUBWEAPON.NON;

            playerst.addCoin(Init_Coin_num);
            playerst.addWait(Init_Weight_num);

            // Starting weapon
            ItemID weaponId = (ItemID)StartingWeapon;
            ItemInfo itemInfo = ItemDB.GetItemInfo(weaponId);
            if (itemInfo != null)
            {
                if (weaponId == ItemID.Whip1 || weaponId == ItemID.Whip2 || weaponId == ItemID.Whip3)
                {
                    __instance.setFlagData(itemInfo.ItemSheet, itemInfo.ItemFlag, 1);
                    mainWeapon = __instance.exchengeMainWeaponNameToEnum("Whip");
                }
                else if (weaponId == ItemID.ClaydollSuit)
                {
                    __instance.setItem(itemInfo.BoxName, 1, false, false, true);
                }
                else
                {
                    mainWeapon = __instance.exchengeMainWeaponNameToEnum(itemInfo.BoxName);
                    subWeapon = __instance.exchengeSubWeaponNameToEnum(itemInfo.BoxName);
                }
            }

            playerst.resetPlayerStatus(Init_PLayer_lv, 0, 999, Init_Coin_num, Init_Weight_num, 0,
                mainWeapon, 0, subWeapon, 0, USEITEM.NON, 0);
            playerst.resetExp();

            // Starting items
            foreach (int itemIdInt in StartingItems)
            {
                ItemID startItemId = (ItemID)itemIdInt;
                ItemInfo startInfo = ItemDB.GetItemInfo(startItemId);
                if (startInfo != null)
                {
                    __instance.setItem(startInfo.BoxName, 1, false, false, true);
                    __instance.setEffectFlag(SceneRandomizer.Instance.CreateGetFlags(startItemId, startInfo));
                }
            }

            // Remove Icefire Treetop statue
            if (RemoveITStatue)
                __instance.setFlagData(8, 10, 1);

            // Standard randomizer flags
            __instance.setFlagData(0, 42, 1);   // randomizer active flag
            __instance.setFlagData(4, 60, 4);   // required guardians (could be slot_data driven)
            __instance.setFlagData(4, 62, 2);   // some gate flag
            __instance.setFlagData(0, 12, 0);   // starting area clear
            __instance.setFlagData(5, 47, (short)(12 - RequiredSkulls));

            // Starting area flags
            AreaID area = (AreaID)StartingArea;
            switch (area)
            {
                case AreaID.VoD:
                    __instance.setFlagData(0, 12, 1);
                    break;
                case AreaID.RoY:
                    __instance.setFlagData(0, 13, 1);
                    break;
                case AreaID.AnnwfnMain:
                    __instance.setFlagData(0, 14, 1);
                    break;
                case AreaID.IBMain:
                    __instance.setFlagData(0, 15, 1);
                    break;
                case AreaID.ITLeft:
                    __instance.setFlagData(0, 16, 1);
                    break;
                case AreaID.DFMain:
                    __instance.setFlagData(0, 17, 1);
                    break;
                case AreaID.SotFGGrail:
                    __instance.setFlagData(0, 18, 1);
                    __instance.setFlagData(10, 27, 1);
                    __instance.setFlagData(10, 87, 1);
                    break;
                case AreaID.TSLeft:
                    __instance.setFlagData(0, 20, 1);
                    __instance.setFlagData(12, 38, 1);
                    __instance.setFlagData(12, 45, 1);
                    __instance.setFlagData(12, 50, 1);
                    break;
                case AreaID.ValhallaMain:
                    __instance.setFlagData(0, 26, 1);
                    break;
                case AreaID.DSLMMain:
                    __instance.setFlagData(0, 28, 1);
                    break;
                case AreaID.ACTablet:
                    __instance.setFlagData(0, 29, 1);
                    break;
                case AreaID.HoMTop:
                    __instance.setFlagData(0, 30, 1);
                    __instance.setFlagData(17, 2, 1);
                    __instance.setFlagData(17, 62, 1);
                    break;
            }

            // --- Intercept the first transition to starting region ---
            if (Managers.SceneRandomizer.Instance != null && Managers.SceneRandomizer.Instance.IsRandomising)
            {
                Managers.SceneRandomizer.Instance.StartingGame = true;
            }
            return false; // skip original
        }
    }
}
