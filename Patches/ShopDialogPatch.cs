using HarmonyLib;
using L2Base;
using L2Word;
using LaMulana2Archipelago.Archipelago;
using LaMulana2Archipelago.Managers;
using LaMulana2RandomizerShared;
using System.Collections.Generic;
using TMPro;

namespace LaMulana2Archipelago.Patches
{
    [HarmonyPatch]
    public static class ShopDialogPatch
    {
        private static L2ShopDataBase _cachedInstance;

        // "shopId:slotIndex" → AP display name
        private static readonly Dictionary<string, string> _slotDisplayNames = new Dictionary<string, string>();

        // "shopId:slotIndex" → AP location id (used for shop-entry auto-collect)
        private static readonly Dictionary<string, long> _slotApLocationIds = new Dictionary<string, long>();

        // "shopId:slotIndex" → icon class (progression / trap / plain) of the AP item in
        // that slot. Drives the progressive/trap shop icon variant. Plain/absent when
        // unknown (offline, not yet scouted, or non-AP slot).
        private static readonly Dictionary<string, ApIconClass> _slotIconClass = new Dictionary<string, ApIconClass>();

        // Ownworld refill items that auto-check the shop location on shop entry.
        // See Archipelago/worlds/lamulana2/ids.py (ItemID 182-190).
        private static readonly HashSet<string> _autoCollectItemNames = new HashSet<string>
        {
            "Shuriken Ammo",
            "Rolling Shuriken Ammo",
            "Earth Spear Ammo",
            "Flare Gun Ammo",
            "Bomb Ammo",
            "Chakram Ammo",
            "Caltrops Ammo",
            "Pistol Ammo",
            "Weights",
        };

        private class ShopSlotEntry
        {
            public int PageId;
            public ShopCell Cell;
            public string DisplayName;
            public long ApLocationId;
            public ApIconClass IconClass;
        }

        // ── Constructor: cache instance, apply if already connected ──────────

        [HarmonyPostfix]
        [HarmonyPatch(typeof(L2ShopDataBase), MethodType.Constructor)]
        static void ShopDB_Ctor_Postfix(L2ShopDataBase __instance)
        {
            _cachedInstance = __instance;

            if (ArchipelagoClient.Authenticated || ArchipelagoClient.OfflineMode)
                Apply(__instance);
        }

        // ── Called by Plugin.Update once the scout cache is populated ────────

        public static void Reapply()
        {
            if (_cachedInstance == null)
            {
                Plugin.Log.LogWarning("[ShopPatch] Reapply called but no cached L2ShopDataBase instance.");
                return;
            }
            // Clear the old (empty) data
            Reset();

            // Re-run the logic now that the ScoutedLocationsCache is full
            Apply(_cachedInstance);

            Plugin.Log.LogInfo($"[ShopPatch] Re-applied overrides. New count: {_slotDisplayNames.Count}");
        }

        // ── Teardown ─────────────────────────────────────────────────────────

        /// <summary>
        /// Drop the previous seed's slot overrides. Without this the shop item
        /// listing keeps rendering the old seed's AP names after a disconnect
        /// (ItemCallBack_Postfix only stands down when the cache is empty).
        /// The cellData names this patch wrote are reverted by WorldDataRestore.
        /// </summary>
        public static void Reset()
        {
            _slotDisplayNames.Clear();
            _slotApLocationIds.Clear();
            _slotIconClass.Clear();
        }

        // ── Core apply ───────────────────────────────────────────────────────

        private static void Apply(L2ShopDataBase instance)
        {
            Reset();

            // First write of the process into shop cellData may come from here
            // rather than SceneRandomizer, so arm the restore baseline too.
            Managers.WorldDataRestore.EnsureShopBaseline(instance);

            var client = ArchipelagoClientProvider.Client;
            if (client == null) return;

            bool offline = ArchipelagoClient.OfflineMode && !ArchipelagoClient.Authenticated;

            // Group entries by ShopId, sorted by PageId to determine UI slot order (0,1,2).
            var byShop = new Dictionary<int, List<ShopSlotEntry>>();

            foreach (var kvp in ShopCellMap.CellToLocation)
            {
                string apText;
                long apLocationIdValue;
                ApIconClass iconClass = ApIconClass.Plain;

                if (offline)
                {
                    // Offline: AP session can't resolve names. Parse the AP
                    // location name to LocationID, then pull the label out of
                    // the seed.lm2ap location_labels via SceneRandomizer.
                    LocationID locId = ShopCellMap.ResolveLocationId(kvp.Value);
                    if (locId == LocationID.None) continue;

                    string apLabel = SceneRandomizer.Instance?.GetLabelForLocation(locId);
                    if (string.IsNullOrEmpty(apLabel)) continue;

                    apText = apLabel;
                    apLocationIdValue = 430000L + (int)locId;
                }
                else
                {
                    apText = GetApItemText(client, kvp.Value);
                    if (apText == null) continue;

                    long? apLocationId = client.GetLocationIdByName(kvp.Value);
                    if (apLocationId == null) continue;

                    apLocationIdValue = apLocationId.Value;

                    // Icon class comes from the scouted item flags. Offline seeds have
                    // no flag data, so the plain icon is used there.
                    var scouted = client.GetItemAtLocation(apLocationIdValue);
                    iconClass = scouted != null ? scouted.IconClass : ApIconClass.Plain;
                }

                int shopId = kvp.Key.ShopId;
                if (!byShop.ContainsKey(shopId))
                    byShop[shopId] = new List<ShopSlotEntry>();

                byShop[shopId].Add(new ShopSlotEntry
                {
                    PageId = kvp.Key.PageId,
                    Cell = kvp.Key,
                    DisplayName = apText,
                    ApLocationId = apLocationIdValue,
                    IconClass = iconClass,
                });
            }

            foreach (var kvp in byShop)
            {
                // Sort by PageId so slot 0/1/2 match the order itemCallBack registers them.
                kvp.Value.Sort((a, b) => a.PageId.CompareTo(b.PageId));

                for (int slot = 0; slot < kvp.Value.Count; slot++)
                {
                    var entry = kvp.Value[slot];

                    // Patch cellData for the NPC confirmation dialog.
                    try
                    {
                        instance.cellData[entry.Cell.ShopId][entry.Cell.PageId][entry.Cell.CellId][entry.Cell.TokenIndex] = entry.DisplayName;
                    }
                    catch
                    {
                        Plugin.Log.LogWarning("[ShopPatch] Out-of-range for ("
                            + entry.Cell.ShopId + ","
                            + entry.Cell.PageId + ","
                            + entry.Cell.CellId + ","
                            + entry.Cell.TokenIndex + ")");
                    }

                    // Cache for item listing UI patch.
                    string cacheKey = kvp.Key + ":" + slot;
                    _slotDisplayNames[cacheKey] = entry.DisplayName;
                    _slotApLocationIds[cacheKey] = entry.ApLocationId;
                    _slotIconClass[cacheKey] = entry.IconClass;
                }
            }

            Plugin.Log.LogInfo("[ShopPatch] Applied " + _slotDisplayNames.Count + " shop slot overrides.");
        }

        // ── itemCallBack postfix: override item name in shop listing UI ───────

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ShopScript), "itemCallBack")]
        static void ItemCallBack_Postfix(ShopScript __instance)
        {
            if (_slotDisplayNames.Count == 0) return;

            var t = Traverse.Create(__instance);

            int slot = t.Field("item_copunter").GetValue<int>() - 1;
            if (slot < 0 || slot > 2) return;

            var sys = t.Field("sys").GetValue<L2System>();
            string sheetName = t.Field("sheet_name").GetValue<string>();
            if (sys == null || string.IsNullOrEmpty(sheetName)) return;

            // Resolve shopId via the same lookup the shop system uses internally.
            var shopDb = sys.getMojiScript(mojiScriptType.shop);
            if (shopDb == null) return;

            int shopId = sys.mojiSheetNameToNo(sheetName, shopDb);
            string cacheKey = shopId + ":" + slot;

            string apName;
            if (!_slotDisplayNames.TryGetValue(cacheKey, out apName)) return;

            var itemNames = t.Field("item_name").GetValue<TextMeshProUGUI[]>();
            if (itemNames == null || slot >= itemNames.Length || itemNames[slot] == null) return;

            itemNames[slot].text = apName;
            Plugin.Log.LogInfo("[ShopPatch] Slot " + slot + " (shopId=" + shopId + ") name -> \"" + apName + "\"");

            // (Glossary slot icon — R Book — is set authoritatively in
            // ItemSendManager.ShopItemCallBackApPatch, which also writes icons[idx].)

            // Auto-collect ownworld refill items the moment the shop UI shows them.
            if (_autoCollectItemNames.Contains(apName))
            {
                long apLocationId;
                if (_slotApLocationIds.TryGetValue(cacheKey, out apLocationId))
                    CheckManager.NotifyApLocationId(apLocationId);
            }
            // LEAKED-FILLER WORKAROUND (067) — leaked filler in a shop is now a free
            // purchasable slot (SceneRandomizer.CreateSetItemString), not auto-collected.
        }

        // ── Public helper: icon-class lookup for the shop icon patch ─────────

        /// <summary>
        /// Resolves the icon class (progression / trap / plain) of the AP item shown in
        /// the given shop slot, using the same shopId/slot key the name override uses.
        /// Returns <see cref="ApIconClass.Plain"/> when unknown (offline, not yet scouted,
        /// or non-AP slot).
        /// </summary>
        public static ApIconClass GetSlotIconClass(ShopScript instance, int slot)
        {
            if (_slotIconClass.Count == 0 || instance == null) return ApIconClass.Plain;

            var t = Traverse.Create(instance);
            var sys = t.Field("sys").GetValue<L2System>();
            string sheetName = t.Field("sheet_name").GetValue<string>();
            if (sys == null || string.IsNullOrEmpty(sheetName)) return ApIconClass.Plain;

            var shopDb = sys.getMojiScript(mojiScriptType.shop);
            if (shopDb == null) return ApIconClass.Plain;

            int shopId = sys.mojiSheetNameToNo(sheetName, shopDb);
            return _slotIconClass.TryGetValue(shopId + ":" + slot, out ApIconClass iconClass)
                ? iconClass : ApIconClass.Plain;
        }

        /// <summary>True if the given shop slot holds one of this player's glossary ROMs, by
        /// scouting the slot's AP location and checking the id window (not the display name).
        /// Used to pick the R Book shop icon instead of the AP icon.</summary>
        public static bool IsGlossarySlot(ShopScript instance, int slot, out int gameId)
        {
            gameId = -1;
            if (_slotApLocationIds.Count == 0 || instance == null) return false;

            var t = Traverse.Create(instance);
            var sys = t.Field("sys").GetValue<L2System>();
            string sheetName = t.Field("sheet_name").GetValue<string>();
            if (sys == null || string.IsNullOrEmpty(sheetName)) return false;

            var shopDb = sys.getMojiScript(mojiScriptType.shop);
            if (shopDb == null) return false;

            int shopId = sys.mojiSheetNameToNo(sheetName, shopDb);
            if (!_slotApLocationIds.TryGetValue(shopId + ":" + slot, out long apLoc)) return false;

            var scouted = ArchipelagoClientProvider.Client?.GetItemAtLocation(apLoc);
            if (!GlossaryManager.IsOwnGlossaryRom(scouted)) return false;

            gameId = GlossaryManager.RomGameId(scouted.ItemId);
            return true;
        }

        // ── Helper ───────────────────────────────────────────────────────────

        private static string GetApItemText(ArchipelagoClient client, string locationName)
        {
            long? locationId = client.GetLocationIdByName(locationName);
            if (locationId == null) return null;

            var itemInfo = client.GetItemAtLocation(locationId.Value);
            if (itemInfo != null)
            {
                // Scout-reported ownership rather than a name compare. Only
                // reached online (the offline branch in Apply labels shop slots
                // straight from location_labels), where the two agree — but a
                // name compare is the wrong question to ask of a scout, and it
                // is exactly what broke the equivalent test in
                // KataribeDialogPatch once offline started using this cache.
                if (!itemInfo.IsOwnItem)
                    return itemInfo.ItemName + " (" + itemInfo.PlayerName + ")";

                return itemInfo.ItemName;
            }

            // Fallback for already-collected locations not in the scout cache.
            if (ArchipelagoClient.ServerData.CheckedLocations.Contains(locationId.Value))
                return "AP Item";

            return null;
        }
    }
}