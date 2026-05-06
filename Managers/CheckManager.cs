using LaMulana2Archipelago.Archipelago;
using LaMulana2Archipelago.Patches;
using LaMulana2RandomizerShared;
using System.Collections.Generic;

namespace LaMulana2Archipelago.Managers
{
    public static class CheckManager
    {
        // Must match your AP world base location id (LocationTable used 430000)
        private const long BaseApLocationId = 430000;

        private static bool gameplayReady = false;

        // Dedup by AP location id (works even if multiple LM2 flags map to same location)
        private static readonly HashSet<long> reportedLocations = new HashSet<long>();

        // Lazily built from ShopCellMap on first use, cleared on Reset().
        private static HashSet<long> _shopApLocationIds;

        // Track which numbered ankh jewel is at the current pickup location.
        // Read by ItemDialogPatch to resolve "Ankh Jewel (BossName)" for in-game pickups.
        public static string PendingAnkhJewelName { get; internal set; }

        // =====================================================================
        // Gameplay gate
        // =====================================================================

        public static void MarkGameplayReady()
        {
            if (gameplayReady) return;
            gameplayReady = true;
            Plugin.Log.LogInfo("[CHECK] Gameplay is now active");
        }

        // =====================================================================
        // Flag notification entry points
        // =====================================================================

        public static void NotifyStringFlag(int seet, string name, int value)
        {
            if (ItemGrantRecursiveGuard.IsGranting) return;

            if (!gameplayReady)
            {
                Plugin.Log.LogDebug("[CHECK] Ignored string flag outside gameplay: " + name + " = " + value);
                return;
            }

            LocationID location;
            if (LocationFlagMap.TryGetString(seet, name, out location))
                ReportLocation(location);
        }

        public static void NotifyNumericFlag(int seet, int flag, short value)
        {
            if (ItemGrantRecursiveGuard.IsGranting) return;

            if (!gameplayReady)
            {
                Plugin.Log.LogDebug("[CHECK] Ignored numeric flag outside gameplay");
                return;
            }

            LocationID location;
            if (LocationFlagMap.TryGetNumeric(seet, flag, value, out location))
                ReportLocation(location);
        }

        public static void NotifyLocation(LocationID location)
        {
            if (!gameplayReady)
            {
                Plugin.Log.LogDebug("[CHECK] Ignored direct location outside gameplay: " + location);
                return;
            }

            ReportLocation(location);
        }

        /// <summary>
        /// Silent shop auto-collect: send the AP location check without dialog
        /// priming or local item grants. The AP server's echo grants the item.
        /// </summary>
        public static void NotifyApLocationId(long apLocationId)
        {
            if (!gameplayReady)
            {
                Plugin.Log.LogDebug("[CHECK] Ignored shop auto-collect outside gameplay: AP " + apLocationId);
                return;
            }

            if (reportedLocations.Contains(apLocationId))
                return;

            if (!ArchipelagoClient.Authenticated && !ArchipelagoClient.OfflineMode)
                return;

            var client = ArchipelagoClientProvider.Client;
            if (client == null) return;

            // Already-checked on the server: dedupe locally and bail.
            if (ArchipelagoClient.ServerData.CheckedLocations.Contains(apLocationId))
            {
                reportedLocations.Add(apLocationId);
                return;
            }

            reportedLocations.Add(apLocationId);
            Plugin.Log.LogInfo("[CHECK] Reporting location: AP " + apLocationId + " (shop auto-collect)");
            client.SendLocationCheck(apLocationId);
        }

        // =====================================================================
        // Core report
        // =====================================================================

        private static void ReportLocation(LocationID location)
        {
            long apLocation = ToApLocationId(location);

            if (reportedLocations.Contains(apLocation))
            {
                Plugin.Log.LogDebug("[CHECK] Already reported: " + location);
                return;
            }

            if (!ArchipelagoClient.Authenticated && !ArchipelagoClient.OfflineMode)
            {
                Plugin.Log.LogDebug("[CHECK] Not connected, skipping report");
                return;
            }

            var client = ArchipelagoClientProvider.Client;
            if (client == null)
            {
                Plugin.Log.LogDebug("[CHECK] Client is null, skipping report");
                return;
            }

            reportedLocations.Add(apLocation);
            Plugin.Log.LogInfo("[CHECK] Reporting location: " + location + " (AP " + apLocation + ")");

            bool isFillerItem = false;
            ItemID itemAtLocation;
            if (SeedFlagMapBuilder.LocationToItem.TryGetValue(location, out itemAtLocation))
            {
                int raw = (int)itemAtLocation;
                // Filler ranges: ChestWeight (191-230), FakeItem (231-270), NPCMoney (271-280), FakeScan (281-295)
                // FakeItem freestanding filler shows only a pop-up + SFX (no dialog), so skip dialog priming.
                isFillerItem = (raw >= 191 && raw <= 270);
            }

            if (!IsShopLocation(apLocation, client))
            {
                bool kataribeHandled = (apLocation == KataribeDialogPatch.LastPrimedApLocationId);
                KataribeDialogPatch.LastPrimedApLocationId = -1L; // consume it

                bool chestFillerHandled = Managers.SetItemApPatch.ChestFillerDialog;
                Managers.SetItemApPatch.ChestFillerDialog = false;

                bool potFillerHandled = ItemPotPatch.PotFillerDialog;
                ItemPotPatch.PotFillerDialog = false;

                if (isFillerItem)
                {
                    Plugin.Log.LogDebug("[CHECK] Skipping dialog prime — filler item");
                }

                // Don't overwrite a dialog label that was already primed this frame
                // (e.g. two pickups on the same frame — show the first, skip the second).
                else if (ItemDialogPatch.PendingDisplayLabel != null)
                {
                    Plugin.Log.LogInfo("[CHECK] Skipping dialog prime — another label already pending");
                }

                else if (!kataribeHandled && !chestFillerHandled && !potFillerHandled)
                {
                    var scouted = client.GetItemAtLocation(apLocation);

                    if (scouted != null)
                    {
                        bool isForOtherPlayer = !scouted.IsOwnItem;

                        ItemDialogPatch.PendingDisplayLabel = scouted.ItemName;

                        if (isForOtherPlayer)
                            ItemDialogPatch.PendingRecipientName = scouted.PlayerName;

                        string label = isForOtherPlayer
                            ? scouted.ItemName + " (" + scouted.PlayerName + ")"
                            : scouted.ItemName;
                        Plugin.Log.LogInfo("[CHECK] Dialog label primed: \"" + label + "\"");
                    }
                    // We intentionally DO NOT have an 'else' block here.
                    // If the item isn't in the AP cache, we let the base game use the real LM2 item name!

                    PendingAnkhJewelName = null;
                    if (itemAtLocation >= ItemID.AnkhJewel1 && itemAtLocation <= ItemID.AnkhJewel9)
                    {
                        int jewIdx = (int)(itemAtLocation - ItemID.AnkhJewel1) + 1;
                        PendingAnkhJewelName = "Ankh Jewel" + jewIdx;
                        Plugin.Log.LogInfo("[CHECK] Pending ankh jewel for dialog: " + PendingAnkhJewelName);
                    }
                }
                else
                {
                    Plugin.Log.LogDebug("[CHECK] Skipping dialog prime — "
                        + (chestFillerHandled ? "chest filler drop" : "kataribe handled it"));
                }
            }

            client.SendLocationCheck(apLocation);
        }

        // =====================================================================
        // Shop location detection
        // =====================================================================

        private static bool IsShopLocation(long apLocationId, ArchipelagoClient client)
        {
            if (_shopApLocationIds == null)
                BuildShopLocationIds(client);

            return _shopApLocationIds != null && _shopApLocationIds.Contains(apLocationId);
        }

        private static void BuildShopLocationIds(ArchipelagoClient client)
        {
            _shopApLocationIds = new HashSet<long>();

            // CellToLocation values may contain duplicates (e.g. Hiner/BTK have two ShopIds
            // pointing at the same location name), so track seen names to avoid double-resolving.
            var seen = new HashSet<string>();
            foreach (var locName in ShopCellMap.CellToLocation.Values)
            {
                if (!seen.Add(locName)) continue;

                long? id = client.GetLocationIdByName(locName);
                if (id.HasValue)
                    _shopApLocationIds.Add(id.Value);
            }

            Plugin.Log.LogInfo("[CHECK] Built shop location id set: " + _shopApLocationIds.Count + " entries");
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        /// <summary>
        /// Returns true if this AP location ID was already reported in the current session
        /// (local dedup cache). Used by SnapMenuPatch to prevent duplicate mural grants.
        /// </summary>
        public static bool IsLocationReported(long apLocationId)
        {
            return reportedLocations.Contains(apLocationId);
        }

        private static long ToApLocationId(LocationID location)
        {
            // AP IDs are contiguous: AP = 430000 + (int)LocationID
            return BaseApLocationId + (int)location;
        }

        // =====================================================================
        // Reset
        // =====================================================================

        public static void Reset()
        {
            gameplayReady = false;
            reportedLocations.Clear();
            _shopApLocationIds = null;
            PendingAnkhJewelName = null;
            ItemGrantStateGuard.IsPostExitTalkActive = false;
            BossKillTracker.Reset();
            Plugin.Log.LogInfo("[CHECK] CheckManager reset");
        }
    }
}