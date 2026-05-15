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

        // Ownworld refill items that auto-check the shop location on shop entry.
        // See Archipelago/worlds/lamulana2/ids.py (ItemID 182-190).
        private static readonly HashSet<string> _autoCollectItemNames = new HashSet<string>
        {
            "Shuriken Ammo",
            "Rolling Shuriken Ammo",
            "Earth Spear Ammo",
            "Flare Ammo",
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
            _slotDisplayNames.Clear();
            _slotApLocationIds.Clear();

            // Re-run the logic now that the ScoutedLocationsCache is full
            Apply(_cachedInstance);

            Plugin.Log.LogInfo($"[ShopPatch] Re-applied overrides. New count: {_slotDisplayNames.Count}");
        }

        // ── Core apply ───────────────────────────────────────────────────────

        private static void Apply(L2ShopDataBase instance)
        {
            _slotDisplayNames.Clear();
            _slotApLocationIds.Clear();

            var client = ArchipelagoClientProvider.Client;
            if (client == null) return;

            bool offline = ArchipelagoClient.OfflineMode && !ArchipelagoClient.Authenticated;

            // Group entries by ShopId, sorted by PageId to determine UI slot order (0,1,2).
            var byShop = new Dictionary<int, List<ShopSlotEntry>>();

            foreach (var kvp in ShopCellMap.CellToLocation)
            {
                string apText;
                long apLocationIdValue;

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

            // Auto-collect ownworld refill items the moment the shop UI shows them.
            if (_autoCollectItemNames.Contains(apName))
            {
                long apLocationId;
                if (_slotApLocationIds.TryGetValue(cacheKey, out apLocationId))
                    CheckManager.NotifyApLocationId(apLocationId);
            }
        }

        // ── Helper ───────────────────────────────────────────────────────────

        private static string GetApItemText(ArchipelagoClient client, string locationName)
        {
            long? locationId = client.GetLocationIdByName(locationName);
            if (locationId == null) return null;

            var itemInfo = client.GetItemAtLocation(locationId.Value);
            if (itemInfo != null)
            {
                if (itemInfo.PlayerName != ArchipelagoClient.ServerData.SlotName)
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