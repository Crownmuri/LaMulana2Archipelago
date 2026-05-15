using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using L2Base;
using L2Flag;
using L2Hit;
using LaMulana2Archipelago.Archipelago;
using LaMulana2Archipelago.Managers;
using LaMulana2RandomizerShared;
using LM2RandomiserMod;
using UnityEngine;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Harmony prefix on ItemPotScript.taskDead() — intercepts pot destruction
    /// when Potsanity is enabled.
    ///
    /// Behavior mirrors chests:
    ///   - Filler (coins/weights/ammo for own world): drop physically at pot
    ///     position, set pot flag, send AP check immediately.
    ///   - Non-filler (real items, AP items): spawn a pickup item at the pot
    ///     position with the correct sprite and "AP Item" label. The AP check
    ///     fires when the player picks up the spawned item (via the flag chain:
    ///     addFlag → AddFlagPatch → CheckManager.NotifyNumericFlag → LocationFlagMap).
    ///
    /// pot_flag_map (from slot_data) maps LocationID.value → potFlagNo.
    /// We invert it to potFlagNo → LocationID for fast lookup at runtime,
    /// and register each (sheet=21, potFlagNo) → LocationID in LocationFlagMap
    /// so the pickup flag chain can map back to the AP location.
    /// </summary>
    [HarmonyPatch(typeof(ItemPotScript), "taskDead")]
    internal static class ItemPotPatch
    {
        // Pot flags live on sheet 21 ("21itempot")
        private const int PotSheet = 21;

        // Cached pot_flag_map: potFlagNo → LocationID
        private static Dictionary<int, LocationID> PotFlagToLocation;
        private static bool _potsanityEnabled;
        private static bool _initialized;

        /// <summary>
        /// Set before NotifyLocation when the pot reward is own filler,
        /// so CheckManager skips the item dialog prime.
        /// </summary>
        public static bool PotFillerDialog;

        /// <summary>
        /// Called from ArchipelagoClient.HandleConnectResult to parse
        /// pot_flag_map from slot_data and cache it.
        /// </summary>
        public static void Initialize()
        {
            _initialized = true;
            PotFlagToLocation = new Dictionary<int, LocationID>();

            var serverData = ArchipelagoClient.ServerData;
            if (serverData == null)
            {
                _potsanityEnabled = false;
                Plugin.Log.LogWarning("[POT] ServerData is null during Initialize");
                return;
            }

            _potsanityEnabled = serverData.GetSlotBool("potsanity");
            if (!_potsanityEnabled)
            {
                Plugin.Log.LogInfo("[POT] Potsanity disabled");
                return;
            }

            var potMap = serverData.GetSlotDict("pot_flag_map");
            if (potMap == null || potMap.Count == 0)
            {
                Plugin.Log.LogWarning("[POT] Potsanity enabled but pot_flag_map is empty or missing");
                _potsanityEnabled = false;
                return;
            }

            // pot_flag_map format from Python: { "locationIdValue": potFlagNo }
            // We invert it to: potFlagNo → LocationID
            foreach (var kvp in potMap)
            {
                try
                {
                    int locationIdValue = Convert.ToInt32(kvp.Key);
                    int potFlagNo = Convert.ToInt32(kvp.Value);
                    LocationID locId = (LocationID)locationIdValue;
                    PotFlagToLocation[potFlagNo] = locId;

                    // Register in LocationFlagMap so the pickup flag chain
                    // (addFlag → AddFlagPatch → CheckManager) can detect pot item pickups.
                    LocationFlagMap.RegisterNumeric(PotSheet, potFlagNo, locId);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[POT] Failed to parse pot_flag_map entry {kvp.Key}={kvp.Value}: {ex.Message}");
                }
            }

            Plugin.Log.LogInfo($"[POT] Potsanity initialized: {PotFlagToLocation.Count} pot locations mapped");
        }

        public static void Reset()
        {
            _initialized = false;
            _potsanityEnabled = false;
            PotFlagToLocation = null;
        }

        [HarmonyPrefix]
        static bool Prefix(ItemPotScript __instance)
        {
            if (!_initialized || !_potsanityEnabled)
                return true; // vanilla

            int potFlagNo = __instance.potFlagNo;
            if (potFlagNo < 0)
                return true; // no-flag pot, let vanilla handle

            // Look up this pot in the AP map
            LocationID locId;
            if (!PotFlagToLocation.TryGetValue(potFlagNo, out locId))
                return true; // not an AP pot, vanilla behavior

            // --- From here we fully replace taskDead() ---

            // Replicate always-run side effects from vanilla taskDead
            Traverse inst = Traverse.Create(__instance);

            // eventActiveFlg |= 8
            byte eventFlags = inst.Field("eventActiveFlg").GetValue<byte>();
            inst.Field("eventActiveFlg").SetValue((byte)(eventFlags | 8));

            // recoveryObj.startRecovery()
            try
            {
                var recoveryObj = inst.Field("recoveryObj").GetValue<object>();
                if (recoveryObj != null)
                    Traverse.Create(recoveryObj).Method("startRecovery").GetValue();
            }
            catch { }

            // myCollider.enabled = false
            var myCollider = inst.Field("myCollider").GetValue<BoxCollider>();
            if (myCollider != null)
                myCollider.enabled = false;

            // Get system references
            int seetNo = inst.Field("seetNo").GetValue<int>();
            L2System sys = inst.Field("sys").GetValue<L2System>();

            // Check if already collected
            short flagVal = 0;
            sys.getFlag(seetNo, potFlagNo, ref flagVal);

            if (flagVal != 0)
            {
                // Already collected — just do the normal random drop fallback
                RunDropItem(inst);
                RunShadowPause(inst, __instance);
                return false;
            }

            // Determine what the AP location contains
            PotFillerDialog = false;
            long apLocationId = 430000L + (int)locId;
            var client = ArchipelagoClientProvider.Client;
            var scouted = client?.GetItemAtLocation(apLocationId);

            bool isFiller = false;
            if (scouted != null && scouted.IsOwnItem && scouted.ItemName != null)
                isFiller = TryParseReward(scouted.ItemName, out _, out _);

            if (isFiller)
            {
                // === FILLER PATH: drop physically, flag + AP check immediately ===
                // Set PotFillerDialog BEFORE the flag write, because setFlagData
                // synchronously triggers the flag chain (SetFlagDataFlagSystemPatch
                // → CheckManager.NotifyNumericFlag → ReportLocation) which checks
                // this flag to decide whether to prime a dialog.
                PotFillerDialog = true;
                sys.setFlagData(seetNo, potFlagNo, 1);
                DropFiller(sys, __instance, scouted.ItemName);
            }
            else
            {
                // === ITEM PATH: spawn pickup with correct sprite, defer AP check ===
                bool spawned = TrySpawnItemPickup(__instance, inst, sys, seetNo, potFlagNo, locId, scouted);

                if (spawned)
                {
                    // Don't set pot flag or send AP check — the spawned item's
                    // itemGetFlags will set the pot flag when picked up, which
                    // triggers AddFlagPatch → CheckManager → AP check.
                    Plugin.Log.LogInfo($"[POT] Spawned pickup for {locId} (potFlag={potFlagNo})");
                }
                else
                {
                    // Fallback: no prefab available → immediate grant flow
                    sys.setFlagData(seetNo, potFlagNo, 1);
                    CheckManager.NotifyLocation(locId);
                    Plugin.Log.LogInfo($"[POT] No prefab for {locId}, using immediate AP check");
                }
            }

            // Shadow pause
            RunShadowPause(inst, __instance);

            return false; // skip vanilla taskDead
        }

        // =================================================================
        // Item spawning (chest-like behavior)
        // =================================================================

        /// <summary>
        /// Instantiates the pot's exItemPrefab at the pot position with the
        /// correct item sprite and "AP Item" label. Picking it up triggers
        /// the AP location check via the flag detection chain.
        /// Returns false if the pot has no exItemPrefab.
        /// </summary>
        private static bool TrySpawnItemPickup(ItemPotScript pot, Traverse inst, L2System sys, int seetNo, int potFlagNo, LocationID locId,
                    ArchipelagoClient.ScoutedItem scouted)
        {
            // FORCE the use of the generic chest item prefab.
            // Vanilla coin pots have physical coin prefabs, which breaks the visual.
            GameObject basePrefab = pot.exItemPrefab;
            if (PrefabHarvester.CachedPrefabs.TryGetValue("blueChest", out GameObject blueChest))
            {
                var box = blueChest.GetComponent<TreasureBoxScript>();
                if (box != null && box.itemObj != null)
                    basePrefab = box.itemObj;
            }

            if (basePrefab == null) return false;

            Vector3 pos = inst.Field("actionPosition").GetValue<Vector3>();
            pos.y += 9f;

            GameObject spawned = UnityEngine.Object.Instantiate(basePrefab, pos, Quaternion.identity);
            var spawnedItem = spawned.GetComponent<AbstractItemBase>();
            if (spawnedItem == null)
            {
                UnityEngine.Object.Destroy(spawned);
                return false;
            }

            spawnedItem.positionType = AbstractItemBase.PositionType.FIX;
            spawnedItem.dropItem = false;
            spawnedItem.scanItem = false;

            string internalLabel = "AP Item";
            ItemID? ownItemId = null;
            ItemInfo ownItemInfo = null;

            // Try the local placement map first — this works in offline mode
            // (no AP session, no scout cache) and also covers online cases
            // where the scout result hasn't arrived yet.
            // PotFiller IDs (1001-1049) and AP placeholder IDs (410001+) must
            // fall through to the AP path / "AP Item" label, since they don't
            // correspond to a vanilla LM2 item.
            if (Managers.SceneRandomizer.Instance != null)
            {
                ItemID mappedId = Managers.SceneRandomizer.Instance.GetItemIDForLocation(locId);
                if (mappedId != ItemID.None && (int)mappedId < 1000)
                    ownItemId = mappedId;
            }

            // AP-server fallback: if the local map didn't resolve and we have
            // a scouted own item, derive the ItemID from the AP code so we
            // can still show the right sprite/label.
            if (ownItemId == null && scouted != null && scouted.IsOwnItem)
                ownItemId = (ItemID)(int)(scouted.ItemId - 420000);

            if (ownItemId.HasValue)
            {
                ownItemInfo = ItemDB.GetItemInfo(ownItemId.Value);
                // Use BoxName (valid vanilla name) for the label — ShopName
                // variants like "Map7" or "Sacred Orb0" crash the vanilla
                // dialog system which runs in AP mode.
                if (ownItemInfo != null && !string.IsNullOrEmpty(ownItemInfo.BoxName))
                    internalLabel = ownItemInfo.BoxName;
            }

            // NOTE: We deliberately do NOT override internalLabel with the AP
            // location_labels string. The LM2 engine uses AbstractItemBase.itemLabel
            // as a flag-table key during pickup (pl.setGetItem → setFlagData),
            // and AP labels frequently diverge from any registered LM2 item name
            // — even for own items (e.g. "Secret Treasure of Life" vs the
            // engine's "Secret Treasure"). A divergent label lands on an
            // unregistered flag and crashes with an out-of-range index.
            // Display names (AP-style, guardian-specific Ankhs, foreign items)
            // are surfaced through ItemDialogPatch.PendingDisplayLabel, which
            // CheckManager.ReportLocation primes from the scout cache or the
            // seed.lm2ap label fallback.

            spawnedItem.itemLabel = internalLabel;
            spawnedItem.itemValue = 1;

            spawnedItem.itemActiveFlag = new L2FlagBoxParent[]
            {
                new L2FlagBoxParent
                {
                    BOX = new L2FlagBox[]
                    {
                        new L2FlagBox { seet_no1 = seetNo, flag_no1 = potFlagNo, seet_no2 = -1, flag_no2 = 0, comp = COMPARISON.Equal, logic = LOGIC.NON }
                    }
                }
            };

            // Build itemGetFlags: item-specific flags first (Sacred Orb count,
            // Crystal Skull count, Ankh score, per-item collected flag, etc.),
            // then the pot flag last so the AP location check fires via
            // AddFlagPatch → CheckManager once everything is granted.
            // Mirrors what chests do via ChangeChestItemFlags → CreateGetFlags.
            var getFlags = new List<L2FlagBoxEnd>();
            if (ownItemId.HasValue && ownItemInfo != null && Managers.SceneRandomizer.Instance != null)
            {
                var itemFlags = Managers.SceneRandomizer.Instance.CreateGetFlags(ownItemId.Value, ownItemInfo);
                if (itemFlags != null)
                    getFlags.AddRange(itemFlags);
            }
            getFlags.Add(new L2FlagBoxEnd { seet_no1 = seetNo, flag_no1 = potFlagNo, calcu = CALCU.EQR, data = 1 });
            spawnedItem.itemGetFlags = getFlags.ToArray();

            // Pass internalLabel to properly resolve the sprite.
            // `isOwnItem` is true when either the scout says so OR the local
            // placement map produced a vanilla ItemID — the latter is what
            // lets offline mode (no scout cache) still show the real sprite
            // instead of falling back to the AP map icon for every pot.
            bool isOwnItem = (scouted != null && scouted.IsOwnItem) || ownItemId.HasValue;
            SetItemSprite(spawned, sys, isOwnItem, internalLabel);

            spawnedItem.initTask();
            spawnedItem.setTreasureBoxOut();

            return true;
        }

        private static void SetItemSprite(GameObject itemObj, L2System sys, bool isOwnItem, string internalLabel)
        {
            var renderer = itemObj.GetComponent<SpriteRenderer>();
            if (renderer == null) return;

            try
            {
                if (!isOwnItem)
                {
                    if (ApSpriteLoader.IsLoaded) renderer.sprite = ApSpriteLoader.MapSprite;
                    else
                    {
                        var fallback = L2SystemCore.getItemData("Holy Grail");
                        if (fallback != null) renderer.sprite = L2SystemCore.getMapIconSprite(fallback);
                    }
                    return;
                }

                // Strip numbered suffixes so getItemData finds the correct sprite.
                // Progressive Whip/Shield always arrive as "Whip1"/"Shield1" (every
                // Progressive Whip shares AP code 420061; every Progressive Shield
                // shares 420076). Resolve to the next-tier sprite by reading the
                // current progression flag — same as SceneRandomizer.GetItemSprite.
                string lookupName = internalLabel;
                if (lookupName.StartsWith("Whip"))
                {
                    short data = 0;
                    if (sys != null) sys.getFlag(2, "Whip", ref data);
                    if (data == 0) lookupName = "Whip";
                    else if (data == 1) lookupName = "Whip2";
                    else lookupName = "Whip3";
                }
                else if (lookupName.StartsWith("Shield"))
                {
                    short data = 0;
                    if (sys != null) sys.getFlag(2, 196, ref data);
                    if (data == 0) lookupName = "Shield";
                    else if (data == 1) lookupName = "Shield2";
                    else lookupName = "Shield3";
                }
                else if (lookupName.StartsWith("Ankh Jewel")) lookupName = "Ankh Jewel";
                else if (lookupName.StartsWith("Sacred Orb")) lookupName = "Sacred Orb";
                else if (lookupName.StartsWith("Crystal S")) lookupName = "Crystal S";
                else if (lookupName.StartsWith("Mantra")) lookupName = "Mantra";
                else if (lookupName.StartsWith("Research")) lookupName = "Research";
                else if (lookupName.StartsWith("Beherit")) lookupName = "Beherit";


                var itemData = L2SystemCore.getItemData(lookupName);
                if (itemData != null)
                {
                    renderer.sprite = L2SystemCore.getMapIconSprite(itemData);
                }
                else if (ApSpriteLoader.IsLoaded)
                {
                    renderer.sprite = ApSpriteLoader.MapSprite;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[POT] SetItemSprite failed: {ex.Message}");
            }
        }

        // =================================================================
        // Filler drop (coins / weights / ammo)
        // =================================================================

        private static void DropFiller(L2System sys, ItemPotScript pot, string itemName)
        {
            try
            {
                string rewardType;
                int amount;
                if (!TryParseReward(itemName, out rewardType, out amount))
                    return;

                Vector3 pos = Traverse.Create(pot).Field("actionPosition").GetValue<Vector3>();
                pos.y += 1.5f;

                switch (rewardType)
                {
                    case "Coin":
                        InvokeDropCoins(sys, pos, amount);
                        Plugin.Log.LogDebug($"[POT] Dropped {amount} coin(s)");
                        break;

                    case "Weight":
                        InvokeDropWeight(sys, pos, amount);
                        Plugin.Log.LogDebug($"[POT] Dropped {amount} weight(s)");
                        break;

                    case "Shuriken":
                        DropOrGrantAmmo(sys, pos, SUBWEAPON.SUB_SYURIKEN,
                            DropItemGeneratorScript.SubBltType.SHURIKEN, amount);
                        Plugin.Log.LogDebug($"[POT] Shuriken ×{amount}");
                        break;

                    case "RShuriken":
                        DropOrGrantAmmo(sys, pos, SUBWEAPON.SUB_KURUMA,
                            DropItemGeneratorScript.SubBltType.WHEEL_SHURIKEN, amount);
                        Plugin.Log.LogDebug($"[POT] Rolling Shuriken ×{amount}");
                        break;

                    case "ESpear":
                        DropOrGrantAmmo(sys, pos, SUBWEAPON.SUB_DAICHI,
                            DropItemGeneratorScript.SubBltType.YARI, amount);
                        Plugin.Log.LogDebug($"[POT] Earth Spear ×{amount}");
                        break;

                    case "FlareGun":
                        DropOrGrantAmmo(sys, pos, SUBWEAPON.SUB_HATUDAN,
                            DropItemGeneratorScript.SubBltType.FLARE, amount);
                        Plugin.Log.LogDebug($"[POT] Flare ×{amount}");
                        break;

                    case "Caltrops":
                        DropOrGrantAmmo(sys, pos, SUBWEAPON.SUB_MAKIBI,
                            DropItemGeneratorScript.SubBltType.MAKIBISHI, amount);
                        Plugin.Log.LogDebug($"[POT] Caltrops ×{amount}");
                        break;

                    case "Chakram":
                        DropOrGrantAmmo(sys, pos, SUBWEAPON.SUB_CHAKURA,
                            DropItemGeneratorScript.SubBltType.CHAKRAM, amount);
                        Plugin.Log.LogDebug($"[POT] Chakram ×{amount}");
                        break;

                    case "Bomb":
                        DropOrGrantAmmo(sys, pos, SUBWEAPON.SUB_BOM,
                            DropItemGeneratorScript.SubBltType.GRENADE, amount);
                        Plugin.Log.LogDebug($"[POT] Bomb ×{amount}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[POT] DropFiller failed: {ex}");
            }
        }

        /// <summary>
        /// Tries to physically drop subweapon ammo via DropItemGenerator.
        /// Falls back to silent addSubWeaponNum if the player doesn't own the weapon
        /// or the drop generator is unavailable.
        /// </summary>
        private static void DropOrGrantAmmo(L2System sys, Vector3 pos, SUBWEAPON sw,
            DropItemGeneratorScript.SubBltType subType, int amount)
        {
            try
            {
                object core = Traverse.Create(sys).Method("getL2SystemCore").GetValue();
                if (core != null)
                {
                    var dropGen = Traverse.Create(core).Property("dropItemGenerator").GetValue<DropItemGeneratorScript>()
                               ?? Traverse.Create(core).Field("dropItemGen").GetValue<DropItemGeneratorScript>();

                    if (dropGen != null && dropGen.dropSubWeapon(subType, ref pos, amount))
                        return; // physical drop succeeded
                }
            }
            catch { }

            // Fallback: grant silently. Use the "_B" ammo slot, not the
            // weapon-have slot — addSubWeaponNum on SUB_SYURIKEN/SUB_BOM/
            // SUB_CHAKURA only bumps a flag the game doesn't read for ammo.
            try
            {
                SUBWEAPON ammoSw;
                switch (sw)
                {
                    case SUBWEAPON.SUB_SYURIKEN: ammoSw = SUBWEAPON.SUB_SYURIKEN_B; break;
                    case SUBWEAPON.SUB_KURUMA: ammoSw = SUBWEAPON.SUB_KURUMA_B; break;
                    case SUBWEAPON.SUB_DAICHI: ammoSw = SUBWEAPON.SUB_DAICHI_B; break;
                    case SUBWEAPON.SUB_HATUDAN: ammoSw = SUBWEAPON.SUB_HATUDAN_B; break;
                    case SUBWEAPON.SUB_MAKIBI: ammoSw = SUBWEAPON.SUB_MAKIBI_B; break;
                    case SUBWEAPON.SUB_BOM: ammoSw = SUBWEAPON.SUB_BOM_B; break;
                    case SUBWEAPON.SUB_CHAKURA: ammoSw = SUBWEAPON.SUB_CHAKURA_B; break;
                    default: ammoSw = sw; break;
                }
                sys.addSubWeaponNum(ammoSw, amount);
            }
            catch { }
        }

        /// <summary>
        /// Parses AP item names like "10 Coins", "1 Weight", "10 Shuriken", "3 Bombs", "1 Chakram"
        /// into a reward type and amount.
        /// </summary>
        private static bool TryParseReward(string itemName, out string rewardType, out int amount)
        {
            rewardType = null;
            amount = 0;

            int spaceIdx = itemName.IndexOf(' ');
            if (spaceIdx <= 0) return false;

            string amountStr = itemName.Substring(0, spaceIdx);
            string typeStr = itemName.Substring(spaceIdx + 1);

            if (!int.TryParse(amountStr, out amount))
                return false;

            if (typeStr.StartsWith("Coin")) { rewardType = "Coin"; return true; }
            if (typeStr.StartsWith("Weight")) { rewardType = "Weight"; return true; }
            if (typeStr.StartsWith("Shuriken")) { rewardType = "Shuriken"; return true; }
            if (typeStr.StartsWith("Rolling Shuriken")) { rewardType = "RShuriken"; return true; }
            if (typeStr.StartsWith("Earth Spear")) { rewardType = "ESpear"; return true; }
            if (typeStr.StartsWith("Flare")) { rewardType = "FlareGun"; return true; }
            if (typeStr.StartsWith("Caltrops")) { rewardType = "Caltrops"; return true; }
            if (typeStr.StartsWith("Chakram")) { rewardType = "Chakram"; return true; }
            if (typeStr.StartsWith("Bomb")) { rewardType = "Bomb"; return true; }

            return false;
        }

        // =================================================================
        // Drop helpers
        // =================================================================

        private static void InvokeDropCoins(L2System sys, Vector3 pos, int amount)
        {
            try
            {
                object core = Traverse.Create(sys).Method("getL2SystemCore").GetValue();
                if (core == null) return;

                object dropGen = Traverse.Create(core).Property("dropItemGenerator").GetValue()
                              ?? Traverse.Create(core).Field("dropItemGen").GetValue();
                if (dropGen == null) return;

                InvokeDrop(dropGen, "dropCoins", pos, amount);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[POT] InvokeDropCoins failed: {ex}");
            }
        }

        private static void InvokeDropWeight(L2System sys, Vector3 pos, int amount)
        {
            try
            {
                object core = Traverse.Create(sys).Method("getL2SystemCore").GetValue();
                if (core == null) return;

                object dropGen = Traverse.Create(core).Property("dropItemGenerator").GetValue()
                              ?? Traverse.Create(core).Field("dropItemGen").GetValue();
                if (dropGen == null) return;

                InvokeDrop(dropGen, "dropWeight", pos, amount);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[POT] InvokeDropWeight failed: {ex}");
            }
        }

        private static bool InvokeDrop(object dropGen, string methodName, Vector3 pos, int amount)
        {
            try
            {
                Type t = dropGen.GetType();
                Type refVec3 = typeof(Vector3).MakeByRefType();

                var mi = AccessTools.Method(t, methodName, new[] { refVec3, typeof(int) });
                if (mi != null)
                {
                    mi.Invoke(dropGen, new object[] { pos, amount });
                    return true;
                }

                mi = AccessTools.Method(t, methodName, new[] { typeof(Vector3), typeof(int) });
                if (mi != null)
                {
                    mi.Invoke(dropGen, new object[] { pos, amount });
                    return true;
                }

                Plugin.Log.LogWarning($"[POT] Could not find {t.Name}.{methodName} overload");
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[POT] InvokeDrop failed for {methodName}: {ex}");
                return false;
            }
        }

        // =================================================================
        // Vanilla side-effect helpers
        // =================================================================

        private static void RunDropItem(Traverse inst)
        {
            try
            {
                var dropitem = inst.Field("dropitem").GetValue<object>();
                if (dropitem != null)
                    Traverse.Create(dropitem).Method("popDropItem").GetValue();
            }
            catch { }
        }

        private static void RunShadowPause(Traverse inst, ItemPotScript pot)
        {
            try
            {
                var shadowtask = inst.Field("shdowtask").GetValue<object>();
                if (shadowtask != null)
                {
                    var shadowObj = Traverse.Create(pot).Method("getShadowObject").GetValue();
                    if (shadowObj != null)
                        Traverse.Create(shadowObj).Method("pause", 5).GetValue();
                }
            }
            catch { }
        }
    }
}
