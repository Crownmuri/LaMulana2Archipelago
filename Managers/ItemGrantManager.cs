using BepInEx;
using L2Base;
using LaMulana2RandomizerShared;
using LM2RandomiserMod;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace LaMulana2Archipelago.Managers
{
    public static class ItemGrantManager
    {
        private const long BaseApItemId = 420000;

        // Backoff so a failing item doesn't hard-freeze the game loop
        private static readonly Dictionary<long, float> NextAttemptTime = new();
        private static float GlobalCooldownUntil = 0f;

        // ─── ItemFlag range for the 12 individually-tracked Crystal Skulls ────────
        private const int CrystalSkullNumberedFlagMin = 140;
        private const int CrystalSkullNumberedFlagMax = 151;

        // If true: restored items will show GETITEM animation + SFX + dialogs just like normal grants.
        // If false: restore is silent (recommended default).
        private const bool RestoreWithAnimations = true;

        public static bool TryGrantItem(L2System sys, NewPlayer pl, long apItemId)
        {
            float now = Time.realtimeSinceStartup;

            //if (!ShadowSaveManager.IsApplying)
            //{
            if (now < GlobalCooldownUntil)
                return false;
            
            if (NextAttemptTime.TryGetValue(apItemId, out float next) && now < next)
                return false;
            //}

            try
            {
                int gameId = (int)(apItemId - BaseApItemId);

                // Recognize explicit coin/weight AP IDs 300-309 (coins: 300-305, weights: 306-309)
                if (gameId >= 300 && gameId <= 309)
                {
                    switch (gameId)
                    {
                        case 300:
                            Plugin.Log.LogInfo($"[ITEM] AP coins: granting 1 coin (AP {apItemId})");
                            GrantCoins(sys, 1, "Coin1");
                            break;
                        case 301:
                            Plugin.Log.LogInfo($"[ITEM] AP coins: granting 10 coins (AP {apItemId})");
                            GrantCoins(sys, 10, "Coin10");
                            break;
                        case 302:
                            Plugin.Log.LogInfo($"[ITEM] AP coins: granting 30 coins (AP {apItemId})");
                            GrantCoins(sys, 30, "Coin30");
                            break;
                        case 303:
                            Plugin.Log.LogInfo($"[ITEM] AP coins: granting 50 coins (AP {apItemId})");
                            GrantCoins(sys, 50, "Coin50");
                            break;
                        case 304:
                            Plugin.Log.LogInfo($"[ITEM] AP coins: granting 80 coins (AP {apItemId})");
                            GrantCoins(sys, 80, "Coin80");
                            break;
                        case 305:
                            Plugin.Log.LogInfo($"[ITEM] AP coins: granting 100 coins (AP {apItemId})");
                            GrantCoins(sys, 100, "Coin100");
                            break;
                        case 306:
                            Plugin.Log.LogInfo($"[ITEM] AP weights: granting 1 weight (AP {apItemId})");
                            GrantWeights(sys, 1, "Weight1");
                            break;
                        case 307:
                            Plugin.Log.LogInfo($"[ITEM] AP weights: granting 5 weights (AP {apItemId})");
                            GrantWeights(sys, 5, "Weight5");
                            break;
                        case 308:
                            Plugin.Log.LogInfo($"[ITEM] AP weights: granting 10 weights (AP {apItemId})");
                            GrantWeights(sys, 10, "Weight10");
                            break;
                        case 309:
                            Plugin.Log.LogInfo($"[ITEM] AP weights: granting 20 weights (AP {apItemId})");
                            GrantWeights(sys, 20, "Weight20");
                            break;
                        default:
                            Plugin.Log.LogWarning($"[ITEM] Unhandled coin/weight AP id: {gameId} (AP {apItemId})");
                            break;
                    }

                    FinishGrant(apItemId, now);
                    return true;
                }

                // Ammo bundle AP items (310-312)
                if (gameId >= 310 && gameId <= 312)
                {
                    switch (gameId)
                    {
                        case 310:
                            Plugin.Log.LogInfo($"[ITEM] AP ammo: granting 10 shuriken (AP {apItemId})");
                            GrantAmmo(sys, L2Hit.SUBWEAPON.SUB_SYURIKEN, 10);
                            break;
                        case 311:
                            Plugin.Log.LogInfo($"[ITEM] AP ammo: granting 3 bombs (AP {apItemId})");
                            GrantAmmo(sys, L2Hit.SUBWEAPON.SUB_BOM, 3);
                            break;
                        case 312:
                            Plugin.Log.LogInfo($"[ITEM] AP ammo: granting 1 chakram (AP {apItemId})");
                            GrantAmmo(sys, L2Hit.SUBWEAPON.SUB_CHAKURA, 1);
                            break;
                    }

                    FinishGrant(apItemId, now);
                    return true;
                }

                // Pot filler items (400-429)
                if (gameId >= 400 && gameId <= 429)
                {
                    GrantPotFiller(sys, gameId, apItemId);
                    FinishGrant(apItemId, now);
                    return true;
                }

                if (gameId <= 0)
                {
                    Plugin.Log.LogWarning("[ITEM] AP item id out of range (base=" + BaseApItemId + "): " + apItemId);
                    return true; // discard
                }

                // ── Dynamic Filler variant check ────────────────────────────────────
                if ((gameId >= 191 && gameId <= 270) || (gameId >= 271 && gameId <= 295))
                {
                    FillerRewardMap.GetReward(gameId, out int coinAmount, out int weightAmount, out string boxName);
                    if (coinAmount > 0)
                    {
                        Plugin.Log.LogInfo($"[ITEM] Filler: granting {coinAmount} coins (AP {apItemId})");
                        GrantCoins(sys, coinAmount, boxName);
                    }
                    else
                    {
                        Plugin.Log.LogInfo($"[ITEM] Filler: granting {weightAmount} weights (AP {apItemId})");
                        GrantWeights(sys, weightAmount, boxName);
                    }

                    FinishGrant(apItemId, now);
                    return true;
                }

                ItemID itemId = (ItemID)gameId;
                if (!Enum.IsDefined(typeof(ItemID), itemId))
                {
                    Plugin.Log.LogWarning("[ITEM] Unknown ItemID enum value: " + gameId + " (AP " + apItemId + ")");
                    return true; // discard
                }

                ItemInfo info = ItemDB.GetItemInfo(itemId);
                if (info == null)
                {
                    Plugin.Log.LogWarning("[ITEM] No ItemInfo found for ItemID=" + itemId + " (AP " + apItemId + ")");
                    return true; // discard
                }

                string itemLabel = info.BoxName;

                if (string.IsNullOrEmpty(itemLabel) || itemLabel.Trim().Length == 0)
                {
                    Plugin.Log.LogWarning("[ITEM] ItemInfo.BoxName empty for ItemID=" + itemId + " (AP " + apItemId + ")");
                    return true; // discard
                }

                Plugin.Log.LogInfo($"[ITEM] Granting AP item {apItemId} -> {itemLabel} (ItemID={itemId})");

                bool isNumberedCrystalSkull =
                    itemLabel == "Crystal S" &&
                    info.ItemSheet == 2 &&
                    info.ItemFlag >= CrystalSkullNumberedFlagMin &&
                    info.ItemFlag <= CrystalSkullNumberedFlagMax;

                if (isNumberedCrystalSkull)
                {
                    Plugin.Log.LogInfo($"[ITEM] Crystal Skull: ItemID={itemId} sheet={info.ItemSheet} flag={info.ItemFlag}");
                }

                bool isSacredOrb =
                    itemId >= ItemID.SacredOrb0 && itemId <= ItemID.SacredOrb9;

                bool isMSX3p = itemId == ItemID.MobileSuperx3P;

                if (isSacredOrb)
                {
                    Plugin.Log.LogInfo($"[ITEM] Sacred Orb: ItemID={itemId} sheet={info.ItemSheet} flag={info.ItemFlag}");
                }

                //if (!ShadowSaveManager.IsApplying || RestoreWithAnimations)
                if (RestoreWithAnimations)
                    TryPlayGetItemPresentation(sys, pl, itemLabel);

                using (ItemGrantRecursiveGuard.Begin())
                {
             
                    if (itemId >= ItemID.AnkhJewel1 && itemId <= ItemID.AnkhJewel9)
                    {
                        int jewIdx = (int)(itemId - ItemID.AnkhJewel1) + 1;
                        itemLabel = "Ankh Jewel" + jewIdx;
                    }

                    // Standard grant: increments the consumable UseItem counter.
                    sys.setItem(itemLabel, 1, direct: false, loadcall: false, sub_add: true);

                    if (isNumberedCrystalSkull)
                    {
                        // Stamp individual skull flag
                        sys.setFlagData(info.ItemSheet, info.ItemFlag, 1);

                        // Match vanilla randomiser persistent counters:
                        // sheet 0, flag 32 += 1
                        // sheet 3, flag 30 += 4
                        short skullCount = 0;
                        sys.getFlag(0, 32, ref skullCount);
                        sys.setFlagData(0, 32, (short)(skullCount + 1));

                        short skullTally = 0;
                        sys.getFlag(3, 30, ref skullTally);
                        sys.setFlagData(3, 30, (short)(skullTally + 4));

#if LEGACY
                        var l2Rando = UnityEngine.Object.FindObjectOfType<L2Rando>();
                        bool autoPlace = false;
                        if (l2Rando != null)
                        {
                            var f = l2Rando.GetType().GetField("autoPlaceSkull",
                                System.Reflection.BindingFlags.Instance |
                                System.Reflection.BindingFlags.NonPublic);
                            if (f != null) autoPlace = (bool)f.GetValue(l2Rando);
                        }
#else
                        bool autoPlace = LaMulana2Archipelago.Patches.GameFlagResetsPatch.AutoPlaceSkull;
#endif
                        if (autoPlace)
                        {
                            int nibiruFlag = (int)itemId - (int)ItemID.SacredOrb4;
                            short cur = 0;
                            sys.getFlag(5, nibiruFlag, ref cur);
                            sys.setFlagData(5, nibiruFlag, (short)(cur + 1));

                            short nibiruTotal = 0;
                            sys.getFlag(5, 47, ref nibiruTotal);
                            sys.setFlagData(5, 47, (short)(nibiruTotal + 1));

                            Plugin.Log.LogInfo("[ITEM] Auto-skull placed: sheet=5 flag=" + nibiruFlag + " (ItemID=" + itemId + ")");
                        }
                    }

                    if (isSacredOrb)
                    {
                        // CALCU.ADD: persistent total orb count (sheet 0, flag 2) += 1
                        short orbCount = 0;
                        sys.getFlag(0, 2, ref orbCount);
                        sys.setFlagData(0, 2, (short)(orbCount + 1));

                        // CALCU.EQR: stamp this specific orb's flag
                        sys.setFlagData(info.ItemSheet, info.ItemFlag, 1);
                    }

                    if (isMSX3p)
                    {
                        // Match vanilla randomiser CreateGetFlags: CALCU.EQR sheet=2 flag=15 -> 2.
                        // setItem("MSX3p",...) alone doesn't stamp the "MSX" flag the game checks
                        // (isHaveItem("MSX") == 2 for the upgraded variant).
                        sys.setFlagData(info.ItemSheet, info.ItemFlag, 2);
                    }
                }

                Plugin.Log.LogInfo($"[ITEM] Granted via sys.setItem: {itemLabel} (AP {apItemId})");

                // Record AP id only after success
                //ShadowSaveManager.RecordApItemId(apItemId);

                FinishGrant(apItemId, now);
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[ITEM] TryGrantItem exception: " + ex);

                float delay = 1.0f;
                if (NextAttemptTime.TryGetValue(apItemId, out float prevNext))
                    delay = Math.Min(8.0f, (prevNext - now) + 1.0f);
                NextAttemptTime[apItemId] = now + delay;
                GlobalCooldownUntil = now + 0.25f;

                return false;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Filler grant helpers
        // ─────────────────────────────────────────────────────────────────────────

        private static void GrantCoins(L2System sys, int amount, string dialogKey)
        {
            try
            {
                using (ItemGrantRecursiveGuard.Begin())
                {
                    if (RestoreWithAnimations)
                        ShowFillerPopUp(sys, ItemPopUpController.PopUpType.Coin, amount, 109);

                    sys.setItem("Gold", amount, direct: false, loadcall: false, sub_add: true);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[ITEM] GrantCoins failed: " + ex);
            }
        }

        private static void GrantWeights(L2System sys, int amount, string dialogKey)
        {
            try
            {
                using (ItemGrantRecursiveGuard.Begin())
                {
                    if (RestoreWithAnimations)
                        ShowFillerPopUp(sys, ItemPopUpController.PopUpType.Weight, amount, 23);

                    sys.setItem("Weight", amount, direct: false, loadcall: false, sub_add: true);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[ITEM] GrantWeights failed: " + ex);
            }
        }

        /// <summary>
        /// Non-blocking filler notification: shows the small popup above the
        /// player (same widget CoinScript/DropItemScript use when picking up
        /// world drops) and plays the given SFX. Does NOT open the item dialog
        /// and does NOT pause the game.
        /// </summary>
        public static void ShowFillerPopUp(L2System sys, ItemPopUpController.PopUpType type, int amount, int seNo)
        {
            try
            {
                var pl = sys?.getPlayer();
                if (pl != null)
                    pl.setPopUp(type, amount);

                if (seNo > 0)
                    PlayPickupSFX(seNo);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[ITEM] ShowFillerPopUp failed: {ex.Message}");
            }
        }

        private static void PlayPickupSFX(int seNo)
        {
            try
            {
                var core = UnityEngine.Object.FindObjectOfType<L2SystemCore>();
                if (core?.seManager != null)
                {
                    int se = core.seManager.playSE(null, seNo);
                    core.seManager.releaseGameObjectFromPlayer(se);
                }
            }
            catch { }
        }

        private static void FinishGrant(long apItemId, float now)
        {
            GlobalCooldownUntil = now + 0.20f;
            NextAttemptTime.Remove(apItemId);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Ammo grant helper
        // ─────────────────────────────────────────────────────────────────────────

        private static void GrantAmmo(L2System sys, L2Hit.SUBWEAPON sw, int amount)
        {
            try
            {
                // The non-"_B" SUBWEAPON values (SUB_SYURIKEN etc.) are the
                // "have weapon" flags — vanilla ammo pickups live in the "_B"
                // slots (SUB_SYURIKEN_B etc.), which is what addSubWeaponNum
                // and the status bar actually read from for ammo counts.
                L2Hit.SUBWEAPON ammoSw;
                switch (sw)
                {
                    case L2Hit.SUBWEAPON.SUB_SYURIKEN: ammoSw = L2Hit.SUBWEAPON.SUB_SYURIKEN_B; break;
                    case L2Hit.SUBWEAPON.SUB_BOM:     ammoSw = L2Hit.SUBWEAPON.SUB_BOM_B;     break;
                    case L2Hit.SUBWEAPON.SUB_CHAKURA: ammoSw = L2Hit.SUBWEAPON.SUB_CHAKURA_B; break;
                    default:                          ammoSw = sw;                           break;
                }

                using (ItemGrantRecursiveGuard.Begin())
                {
                    sys.addSubWeaponNum(ammoSw, amount);
                }

                if (RestoreWithAnimations)
                {
                    ItemPopUpController.PopUpType popType;
                    switch (sw)
                    {
                        case L2Hit.SUBWEAPON.SUB_SYURIKEN: popType = ItemPopUpController.PopUpType.Shuriken; break;
                        case L2Hit.SUBWEAPON.SUB_BOM:     popType = ItemPopUpController.PopUpType.Bomb;     break;
                        case L2Hit.SUBWEAPON.SUB_CHAKURA: popType = ItemPopUpController.PopUpType.Chakram;  break;
                        default:                          popType = ItemPopUpController.PopUpType.Shuriken; break;
                    }
                    ShowFillerPopUp(sys, popType, amount, 23);
                }

                Plugin.Log.LogInfo($"[ITEM] Granted {amount} {ammoSw} ammo");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[ITEM] GrantAmmo failed for {sw}: {ex}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Pot filler grant helper
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Grants pot filler rewards (PotFiller01-30, gameId 400-429).
        /// Distribution matches Python POT_FILLER_DISTRIBUTION:
        ///   400-413 (14): 10 coins
        ///   414-415 (2):  30 coins
        ///   416-423 (8):  1 weight
        ///   424-427 (4):  10 shuriken
        ///   428 (1):      3 bombs
        ///   429 (1):      1 chakram
        /// </summary>
        private static void GrantPotFiller(L2System sys, int gameId, long apItemId)
        {
            int idx = gameId - 400;

            if (idx < 14) // 0-13: 10 coins
            {
                Plugin.Log.LogInfo($"[ITEM] Pot filler: granting 10 coins (AP {apItemId})");
                GrantCoins(sys, 10, "Coin10");
            }
            else if (idx < 16) // 14-15: 30 coins
            {
                Plugin.Log.LogInfo($"[ITEM] Pot filler: granting 30 coins (AP {apItemId})");
                GrantCoins(sys, 30, "Coin30");
            }
            else if (idx < 24) // 16-23: 1 weight
            {
                Plugin.Log.LogInfo($"[ITEM] Pot filler: granting 1 weight (AP {apItemId})");
                GrantWeights(sys, 1, "Weight1");
            }
            else if (idx < 28) // 24-27: 10 shuriken
            {
                Plugin.Log.LogInfo($"[ITEM] Pot filler: granting 10 shuriken (AP {apItemId})");
                GrantAmmo(sys, L2Hit.SUBWEAPON.SUB_SYURIKEN, 10);
            }
            else if (idx == 28) // 28: 3 bombs
            {
                Plugin.Log.LogInfo($"[ITEM] Pot filler: granting 3 bombs (AP {apItemId})");
                GrantAmmo(sys, L2Hit.SUBWEAPON.SUB_BOM, 3);
            }
            else // 29: 1 chakram
            {
                Plugin.Log.LogInfo($"[ITEM] Pot filler: granting 1 chakram (AP {apItemId})");
                GrantAmmo(sys, L2Hit.SUBWEAPON.SUB_CHAKURA, 1);
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // GETITEM animation
        // ─────────────────────────────────────────────────────────────────────────

        private static void TryPlayGetItemPresentation(L2System sys, NewPlayer pl, string itemLabel)
        {
            try
            {
                // Extra safety: never present during restore
                //if (ShadowSaveManager.IsApplying && !RestoreWithAnimations)
                //    return;

                PlayPickupSFX(39);

                pl.setActionOder(PLAYERACTIONODER.getitem);

                if (itemLabel.Contains("Whip"))
                {
                    short data = 0;
                    string show = string.Empty;
                    sys.getFlag(2, "Whip", ref data);

                    if (data == 0) show = "Whip";
                    else if (data == 1) show = "Whip2";
                    else show = "Whip3";

                    pl.setGetItem(ref show);
                    var d = L2SystemCore.getItemData(show);
                    if (d != null) pl.setGetItemIcon(d);
                    return;
                }

                if (itemLabel.Contains("Shield"))
                {
                    short data = 0;
                    string show = string.Empty;
                    sys.getFlag(2, 196, ref data);

                    if (data == 0) show = "Shield";
                    else if (data == 1) show = "Shield2";
                    else show = "Shield3";

                    pl.setGetItem(ref show);
                    var d = L2SystemCore.getItemData(show);
                    if (d != null) pl.setGetItemIcon(d);
                    return;
                }

                string shown = itemLabel;
                pl.setGetItem(ref shown);

                if (itemLabel.Contains("Mantra"))
                {
                    var d = L2SystemCore.getItemData("Mantra");
                    if (d != null) pl.setGetItemIcon(d);
                    return;
                }
                if (itemLabel.Contains("Research"))
                {
                    var d = L2SystemCore.getItemData("Research");
                    if (d != null) pl.setGetItemIcon(d);
                    return;
                }
                if (itemLabel.Contains("Beherit"))
                {
                    var d = L2SystemCore.getItemData("Beherit");
                    if (d != null) pl.setGetItemIcon(d);
                    return;
                }

                var dataDefault = L2SystemCore.getItemData(itemLabel);
                if (dataDefault != null)
                    pl.setGetItemIcon(dataDefault);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[ITEM] TryPlayGetItemPresentation failed: " + ex);
            }
        }
    }
}