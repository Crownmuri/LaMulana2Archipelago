using BepInEx;
using L2Base;
using L2Hit;
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

        // True when the most recent successful grant used the popup UI (coins,
        // weights, ammo, pot filler) instead of the item dialog. Plugin.Update
        // reads this to clear ItemDialogPatch.Pending* — otherwise the prime
        // it set before TryGrantItem would leak into the next location check
        // and overwrite that check's dialog label.
        public static bool LastGrantUsedPopupOnly { get; private set; }

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

            // Reset before each attempt; only the popup-only branches set this true.
            LastGrantUsedPopupOnly = false;

            try
            {
                int gameId = (int)(apItemId - BaseApItemId);

                // Recognize explicit coin/weight AP IDs 300-309 (coins: 300-305, weights: 306-309)
                if (gameId >= 300 && gameId <= 309)
                {
                    LastGrantUsedPopupOnly = true;
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

                // Ammo bundle AP items (310-316). IDs match items.py AP_FILLER:
                //   310 ShurikenBundle         -> 10 shuriken         (SUB_SYURIKEN)
                //   311 RollingShurikenBundle  -> 10 rolling shuriken (SUB_KURUMA)
                //   312 EarthSpearBundle       -> 10 earth spears     (SUB_DAICHI)
                //   313 FlareBundle            -> 10 flares           (SUB_HATUDAN)
                //   314 CaltropsBundle         -> 10 caltrops         (SUB_MAKIBI)
                //   315 ChakramBundle          ->  1 chakram          (SUB_CHAKURA)
                //   316 BombBundle             ->  3 bombs            (SUB_BOM)
                if (gameId >= 310 && gameId <= 316)
                {
                    LastGrantUsedPopupOnly = true;

                    switch (gameId)
                    {
                        case 310:
                            Plugin.Log.LogInfo($"[ITEM] AP ammo: granting 10 shuriken (AP {apItemId})");
                            GrantAmmo(sys, L2Hit.SUBWEAPON.SUB_SYURIKEN, 10);
                            break;
                        case 311:
                            Plugin.Log.LogInfo($"[ITEM] AP ammo: granting 10 rolling shuriken (AP {apItemId})");
                            GrantAmmo(sys, L2Hit.SUBWEAPON.SUB_KURUMA, 10);
                            break;
                        case 312:
                            Plugin.Log.LogInfo($"[ITEM] AP ammo: granting 10 earth spears (AP {apItemId})");
                            GrantAmmo(sys, L2Hit.SUBWEAPON.SUB_DAICHI, 10);
                            break;
                        case 313:
                            Plugin.Log.LogInfo($"[ITEM] AP ammo: granting 10 flares (AP {apItemId})");
                            GrantAmmo(sys, L2Hit.SUBWEAPON.SUB_HATUDAN, 10);
                            break;
                        case 314:
                            Plugin.Log.LogInfo($"[ITEM] AP ammo: granting 10 caltrops (AP {apItemId})");
                            GrantAmmo(sys, L2Hit.SUBWEAPON.SUB_MAKIBI, 10);
                            break;
                        case 315:
                            Plugin.Log.LogInfo($"[ITEM] AP ammo: granting 1 chakram (AP {apItemId})");
                            GrantAmmo(sys, L2Hit.SUBWEAPON.SUB_CHAKURA, 1);
                            break;
                        case 316:
                            Plugin.Log.LogInfo($"[ITEM] AP ammo: granting 3 bombs (AP {apItemId})");
                            GrantAmmo(sys, L2Hit.SUBWEAPON.SUB_BOM, 3);
                            break;
                    }

                    FinishGrant(apItemId, now);
                    return true;
                }

                // Pot filler items. ItemID.PotFiller01 = 1001 → game ids run
                // 1001..1049 for the 49 pots produced by POT_FILLER_DISTRIBUTION
                // in the AP world. Range allows headroom up to PotFiller250 (1250)
                // so we don't crash if the world later expands the table.
                if (gameId >= 1001 && gameId <= 1250)
                {
                    LastGrantUsedPopupOnly = true;
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
                    LastGrantUsedPopupOnly = true;

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

                // Progressive Beherit: AP grants must NOT set a unique Beherit flag
                // (sheet=2, flag=170..176). All 7 share the same AP code 420175
                //
                // Increment the count flag and Beherit useitem counter only. The unique
                // flags are set exclusively by physical chest pickups via setItem("BeheritN").
                if (itemId >= ItemID.ProgressiveBeherit1 && itemId <= ItemID.ProgressiveBeherit7)
                {
                    if (RestoreWithAnimations)
                        TryPlayGetItemPresentation(sys, pl, "Beherit");

                    short newCount;
                    using (ItemGrantRecursiveGuard.Begin())
                    {
                        USEITEM useitem = sys.exchengeUseItemNameToEnum("Beherit");
                        sys.haveUsesItem(useitem, true);
                        sys.addUseItemNum(useitem, 1);

                        short count = 0;
                        sys.getFlag(2, 3, ref count);
                        newCount = (short)(count + 1);
                        sys.setFlagData(2, 3, newCount);
                    }

                    Plugin.Log.LogInfo($"[ITEM] AP Progressive Beherit granted (count={newCount}; unique itemid flag not set)");
                    FinishGrant(apItemId, now);
                    return true;
                }

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

                    // Progressive Whip/Shield: AP grants always carry "Whip1"/"Shield1"
                    // (every Progressive Whip shares AP code 420061; every Progressive
                    // Shield shares 420076). SetItemPatch maps those literal labels to
                    // setFlagData(num2, 190/193, 1) — the unique chest-marker flags
                    // (hdb keys "d190"/"d193") for the level-1 chest. If a local Whip1
                    // or Shield1 chest exists, that write happens inside
                    // ItemGrantRecursiveGuard.IsGranting and pre-opens the chest while
                    // suppressing its location check (same root cause as Progressive
                    // Beherit).
                    //
                    // Pass the bare "Whip"/"Shield" label instead. SetItemPatch then
                    // skips the explicit Whip1/Shield1 == branch, sets only the named
                    // count flag ("Whip"/"d196"), and runs the mainweapon/subweapon
                    // setup. The unique chest markers stay untouched and only set when
                    // the player physically opens the local chest.
                    if (itemId >= ItemID.Whip1 && itemId <= ItemID.Whip3)
                        itemLabel = "Whip";
                    else if (itemId >= ItemID.Shield1 && itemId <= ItemID.Shield3)
                        itemLabel = "Shield";

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
                    case L2Hit.SUBWEAPON.SUB_KURUMA:   ammoSw = L2Hit.SUBWEAPON.SUB_KURUMA_B;   break;
                    case L2Hit.SUBWEAPON.SUB_DAICHI:   ammoSw = L2Hit.SUBWEAPON.SUB_DAICHI_B;   break;
                    case L2Hit.SUBWEAPON.SUB_HATUDAN:  ammoSw = L2Hit.SUBWEAPON.SUB_HATUDAN_B;  break;
                    case L2Hit.SUBWEAPON.SUB_MAKIBI:   ammoSw = L2Hit.SUBWEAPON.SUB_MAKIBI_B;   break;
                    case L2Hit.SUBWEAPON.SUB_CHAKURA:  ammoSw = L2Hit.SUBWEAPON.SUB_CHAKURA_B;  break;
                    case L2Hit.SUBWEAPON.SUB_BOM:      ammoSw = L2Hit.SUBWEAPON.SUB_BOM_B;      break;
                    default:                           ammoSw = sw;                            break;
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
                        case L2Hit.SUBWEAPON.SUB_SYURIKEN: popType = ItemPopUpController.PopUpType.Shuriken;  break;
                        case L2Hit.SUBWEAPON.SUB_KURUMA:   popType = ItemPopUpController.PopUpType.RShuriken; break;
                        case L2Hit.SUBWEAPON.SUB_DAICHI:   popType = ItemPopUpController.PopUpType.ESpear;    break;
                        case L2Hit.SUBWEAPON.SUB_HATUDAN:  popType = ItemPopUpController.PopUpType.FlareGun;  break;
                        case L2Hit.SUBWEAPON.SUB_MAKIBI:   popType = ItemPopUpController.PopUpType.Caltrops;  break;
                        case L2Hit.SUBWEAPON.SUB_CHAKURA:  popType = ItemPopUpController.PopUpType.Chakram;   break;
                        case L2Hit.SUBWEAPON.SUB_BOM:      popType = ItemPopUpController.PopUpType.Bomb;      break;
                        default:                           popType = ItemPopUpController.PopUpType.Shuriken;  break;
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

        // Mirrors POT_FILLER_DISTRIBUTION in worlds/lamulana2/items.py. Order
        // and counts must stay in lockstep with Python — the AP world hands out
        // PotFillerNN ids by walking this same list, and we map them back here.
        // Sum = 49 pots → uses PotFiller01..PotFiller49 (gameId 1001..1049).
        private static readonly string[] PotFillerRewards =
        {
            "Weight1", "Coin10", "Coin30", "Coin50", "Coin80", "Coin100",
            "Shuriken", "RShuriken", "ESpear", "FlareGun", "Caltrops",
            "Chakram", "Bomb",
        };
        private static readonly int[] PotFillerCounts =
        {
            14, 17, 4, 0, 1, 1,
            6, 4, 0, 0, 0,
            1, 1,
        };

        /// <summary>
        /// Grants pot filler rewards. PotFiller01 = gameId 1001, so idx = gameId - 1001.
        /// Distribution must match POT_FILLER_DISTRIBUTION in items.py exactly.
        /// </summary>
        private static void GrantPotFiller(L2System sys, int gameId, long apItemId)
        {
            int idx = gameId - 1001;

            int cumulative = 0;
            for (int i = 0; i < PotFillerRewards.Length; i++)
            {
                int count = PotFillerCounts[i];
                if (count <= 0) continue;

                if (idx < cumulative + count)
                {
                    DispensePotReward(sys, PotFillerRewards[i], apItemId);
                    return;
                }
                cumulative += count;
            }

            Plugin.Log.LogWarning($"[ITEM] Pot filler index out of range: idx={idx} (AP {apItemId})");
        }

        private static void DispensePotReward(L2System sys, string reward, long apItemId)
        {
            switch (reward)
            {
                case "Coin1":
                    Plugin.Log.LogInfo($"[ITEM] Pot filler: granting 1 coin (AP {apItemId})");
                    GrantCoins(sys, 1, "Coin1");
                    break;
                case "Coin10":
                    Plugin.Log.LogInfo($"[ITEM] Pot filler: granting 10 coins (AP {apItemId})");
                    GrantCoins(sys, 10, "Coin10");
                    break;
                case "Coin30":
                    Plugin.Log.LogInfo($"[ITEM] Pot filler: granting 30 coins (AP {apItemId})");
                    GrantCoins(sys, 30, "Coin30");
                    break;
                case "Coin50":
                    Plugin.Log.LogInfo($"[ITEM] Pot filler: granting 50 coins (AP {apItemId})");
                    GrantCoins(sys, 50, "Coin50");
                    break;
                case "Coin80":
                    Plugin.Log.LogInfo($"[ITEM] Pot filler: granting 80 coins (AP {apItemId})");
                    GrantCoins(sys, 80, "Coin80");
                    break;
                case "Coin100":
                    Plugin.Log.LogInfo($"[ITEM] Pot filler: granting 100 coins (AP {apItemId})");
                    GrantCoins(sys, 100, "Coin100");
                    break;
                case "Weight1":
                    Plugin.Log.LogInfo($"[ITEM] Pot filler: granting 1 weight (AP {apItemId})");
                    GrantWeights(sys, 1, "Weight1");
                    break;
                case "Weight5":
                    Plugin.Log.LogInfo($"[ITEM] Pot filler: granting 5 weights (AP {apItemId})");
                    GrantWeights(sys, 5, "Weight5");
                    break;
                case "Weight10":
                    Plugin.Log.LogInfo($"[ITEM] Pot filler: granting 10 weights (AP {apItemId})");
                    GrantWeights(sys, 10, "Weight10");
                    break;
                case "Weight20":
                    Plugin.Log.LogInfo($"[ITEM] Pot filler: granting 20 weights (AP {apItemId})");
                    GrantWeights(sys, 20, "Weight20");
                    break;
                case "Shuriken":
                    Plugin.Log.LogInfo($"[ITEM] Pot filler: granting 10 shuriken (AP {apItemId})");
                    GrantAmmo(sys, L2Hit.SUBWEAPON.SUB_SYURIKEN, 10);
                    break;
                case "RShuriken":
                    Plugin.Log.LogInfo($"[ITEM] Pot filler: granting 10 rolling shuriken (AP {apItemId})");
                    GrantAmmo(sys, L2Hit.SUBWEAPON.SUB_KURUMA, 10);
                    break;
                case "ESpear":
                    Plugin.Log.LogInfo($"[ITEM] Pot filler: granting 10 earth spears (AP {apItemId})");
                    GrantAmmo(sys, L2Hit.SUBWEAPON.SUB_DAICHI, 10);
                    break;
                case "FlareGun":
                    Plugin.Log.LogInfo($"[ITEM] Pot filler: granting 10 flares (AP {apItemId})");
                    GrantAmmo(sys, L2Hit.SUBWEAPON.SUB_HATUDAN, 10);
                    break;
                case "Caltrops":
                    Plugin.Log.LogInfo($"[ITEM] Pot filler: granting 10 caltrops (AP {apItemId})");
                    GrantAmmo(sys, L2Hit.SUBWEAPON.SUB_MAKIBI, 10);
                    break;
                case "Chakram":
                    Plugin.Log.LogInfo($"[ITEM] Pot filler: granting 1 chakram (AP {apItemId})");
                    GrantAmmo(sys, L2Hit.SUBWEAPON.SUB_CHAKURA, 1);
                    break;
                case "Bomb":
                    Plugin.Log.LogInfo($"[ITEM] Pot filler: granting 3 bombs (AP {apItemId})");
                    GrantAmmo(sys, L2Hit.SUBWEAPON.SUB_BOM, 3);
                    break;
                default:
                    Plugin.Log.LogWarning($"[ITEM] Unknown pot filler reward '{reward}' (AP {apItemId})");
                    break;
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