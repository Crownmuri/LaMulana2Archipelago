using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using LaMulana2RandomizerShared;
using LM2RandomiserMod;
using Newtonsoft.Json.Linq;

namespace LaMulana2Archipelago.Managers
{
    internal static class SeedFlagMapBuilder
    {
        private static string SeedPath
        {
            get
            {
                // Unity/older .NET: avoid Path.Combine overloads > 3 args
                string path = Path.Combine(Paths.GameRootPath, "LaMulana2Randomizer");
                path = Path.Combine(path, "Seed");
                path = Path.Combine(path, "seed.lm2r");
                return path;
            }
        }

        /// <summary>
        /// FakeItem script sets sheet=31 flag=0..39
        /// Reserve mapping for filler purposes.
        /// </summary>
        public static readonly Dictionary<int, LocationID> ChestWeightFlagToLocation = new Dictionary<int, LocationID>();

        /// <summary>
        /// FakeItem script sets sheet=31 flag=40..79
        /// Reserve mapping for filler purposes.
        /// </summary>
        public static readonly Dictionary<int, LocationID> FakeItemFlagToLocation = new Dictionary<int, LocationID>();

        /// <summary>
        /// NPCMoney script sets sheet=31 flag=80..89 (NPCMoney01..10).
        /// Reserve mapping for filler purposes.
        /// </summary>
        public static readonly Dictionary<int, LocationID> NpcMoneyFlagToLocation = new Dictionary<int, LocationID>();

        /// <summary>
        /// FakeScan script sets sheet=31 flag=90..94 (FakeScan01..15).
        /// Reserve mapping for filler purposes.
        /// </summary>
        public static readonly Dictionary<int, LocationID> FakeScanFlagToLocation = new Dictionary<int, LocationID>();

        /// <summary>
        /// Reverse mapping for tracking items instead of locations.
        /// Used for redirecting setItem("Ankh Jewel") to unique numbered jewels.
        /// </summary>
        public static readonly Dictionary<LocationID, ItemID> LocationToItem = new Dictionary<LocationID, ItemID>();

        /// <summary>
        /// Reverse mapping for reporting NPC items before the location check flag happens.
        /// Used for getting AP label names on NPC item grants.
        /// </summary>
        public static readonly Dictionary<string, LocationID> BoxNameToLocation = new Dictionary<string, LocationID>();


        /// <summary>
        /// Reads slot_data from AP connection and registers:
        ///   - main numeric map: (item sheet, item flag) -> LocationID (unique flags only)
        ///   - seed-derived maps for FakeItem/NPCMoney container flags
        /// Falls back to seed.lm2r if slot_data does not contain item_placements.
        /// </summary>
        public static void BuildIntoMap(
            Action<int, int, LocationID> addNumeric,
            Action<string> logInfo,
            Action<string> logWarn,
            Dictionary<string, object> slotData = null)
        {
            if (addNumeric == null) throw new ArgumentNullException(nameof(addNumeric));
            if (logInfo == null) throw new ArgumentNullException(nameof(logInfo));
            if (logWarn == null) throw new ArgumentNullException(nameof(logWarn));

            ChestWeightFlagToLocation.Clear();
            FakeItemFlagToLocation.Clear();
            NpcMoneyFlagToLocation.Clear();
            FakeScanFlagToLocation.Clear();
            LocationToItem.Clear();
            BoxNameToLocation.Clear();

            // Try slot_data first (standalone mode), fall back to seed.lm2r (legacy mode)
            if (slotData != null && slotData.ContainsKey("item_placements"))
            {
                logInfo("[AP] Building flag map from slot_data (standalone mode)");
                BuildFromSlotData(addNumeric, logInfo, logWarn, slotData);
            }
            else
            {
                logInfo("[AP] Building flag map from seed.lm2r (legacy mode)");
                BuildFromSeedFile(addNumeric, logInfo, logWarn);
            }
        }

        /// <summary>
        /// Build maps from AP slot_data dictionaries.
        /// </summary>
        private static void BuildFromSlotData(
            Action<int, int, LocationID> addNumeric,
            Action<string> logInfo,
            Action<string> logWarn,
            Dictionary<string, object> slotData)
        {
            int added = 0;
            int collisions = 0;
            int noItemInfo = 0;
            int chestWeightMapped = 0;
            int fakeItemMapped = 0;
            int npcMoneyMapped = 0;
            int fakeScanMapped = 0;

            HashSet<int> seenKeys = new HashSet<int>();

            try
            {
                // Parse item_placements: [{location: int, item: int}, ...]
                if (slotData.TryGetValue("item_placements", out object rawPlacements))
                {
                    var placements = rawPlacements as JArray;
                    if (placements != null)
                    {
                        foreach (var entry in placements)
                        {
                            LocationID loc = (LocationID)(int)entry["location"];
                            ItemID item = (ItemID)(int)entry["item"];

                            TryAdd(item, loc, addNumeric, seenKeys,
                                ref added, ref collisions, ref noItemInfo,
                                ref chestWeightMapped, ref fakeItemMapped,
                                ref npcMoneyMapped, ref fakeScanMapped,
                                logWarn);
                        }
                    }
                }

                // Parse shop_placements: [{location: int, item: int, price: int}, ...]
                if (slotData.TryGetValue("shop_placements", out object rawShops))
                {
                    var shops = rawShops as JArray;
                    if (shops != null)
                    {
                        foreach (var entry in shops)
                        {
                            LocationID loc = (LocationID)(int)entry["location"];
                            ItemID item = (ItemID)(int)entry["item"];
                            // price not needed for flag mapping

                            TryAdd(item, loc, addNumeric, seenKeys,
                                ref added, ref collisions, ref noItemInfo,
                                ref chestWeightMapped, ref fakeItemMapped,
                                ref npcMoneyMapped, ref fakeScanMapped,
                                logWarn);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logWarn("[AP] Failed reading slot_data placements: " + ex);
                return;
            }

            logInfo("[AP] Slot_data flag map built: added=" + added
                    + " collisions=" + collisions
                    + " noItemInfo=" + noItemInfo
                    + " chestWeightMapped=" + chestWeightMapped
                    + " fakeItemMapped=" + fakeItemMapped
                    + " npcMoneyMapped=" + npcMoneyMapped
                    + " fakeScanMapped=" + fakeScanMapped);
        }

        /// <summary>
        /// Legacy: build maps from seed.lm2r binary file.
        /// </summary>
        private static void BuildFromSeedFile(
            Action<int, int, LocationID> addNumeric,
            Action<string> logInfo,
            Action<string> logWarn)
        {
            if (!File.Exists(SeedPath))
            {
                logWarn("[AP] seed.lm2r not found at: " + SeedPath);
                return;
            }

            int added = 0;
            int collisions = 0;
            int noItemInfo = 0;
            int chestWeightMapped = 0;
            int fakeItemMapped = 0;
            int npcMoneyMapped = 0;
            int fakeScanMapped = 0;

            // Track (sheet,flag) collisions globally
            HashSet<int> seenKeys = new HashSet<int>();

            try
            {
                using (BinaryReader br = new BinaryReader(File.Open(SeedPath, FileMode.Open, FileAccess.Read, FileShare.Read)))
                {
                    // ---- HEADER: must match L2Rando.LoadSeedFile() ----
                    br.ReadInt32();   // StartingWeapon
                    br.ReadInt32();   // StartingArea
                    br.ReadBoolean(); // randomDissonance
                    br.ReadInt32();   // requiredGuardians
                    br.ReadInt32();   // RequiredSkulls
                    br.ReadBoolean(); // RemoveITStatue
                    br.ReadInt32();   // echidna
                    br.ReadBoolean(); // AutoScanTablets
                    br.ReadBoolean(); // autoPlaceSkull
                    br.ReadInt32();   // StartingMoney
                    br.ReadInt32();   // StartingWeights
                    br.ReadInt32();   // itemChestColour
                    br.ReadInt32();   // weightChestColour

                    int startingItems = br.ReadInt32();
                    for (int i = 0; i < startingItems; i++)
                        br.ReadInt32(); // discard starting item ids

                    // ---- NORMAL PLACEMENTS ----
                    int itemCount = br.ReadInt32();
                    for (int i = 0; i < itemCount; i++)
                    {
                        LocationID loc = (LocationID)br.ReadInt32();
                        ItemID item = (ItemID)br.ReadInt32();

                        TryAdd(item, loc, addNumeric, seenKeys,
                            ref added, ref collisions, ref noItemInfo,
                            ref chestWeightMapped, ref fakeItemMapped,
                            ref npcMoneyMapped, ref fakeScanMapped,
                            logWarn);
                    }

                    // ---- SHOP PLACEMENTS ----
                    // seed stores: LocationID slot, ItemID item, int priceFactor/multiplier
                    int shopCount = br.ReadInt32();
                    for (int i = 0; i < shopCount; i++)
                    {
                        LocationID shopSlot = (LocationID)br.ReadInt32();
                        ItemID shopItem = (ItemID)br.ReadInt32();
                        br.ReadInt32(); // priceFactor/multiplier

                        TryAdd(shopItem, shopSlot, addNumeric, seenKeys,
                            ref added, ref collisions, ref noItemInfo,
                            ref chestWeightMapped, ref fakeItemMapped,
                            ref npcMoneyMapped, ref fakeScanMapped,
                            logWarn);
                    }

                    // Remaining seed sections not needed for flag->location checks.
                }
            }
            catch (Exception ex)
            {
                logWarn("[AP] Failed reading seed.lm2r: " + ex);
                return;
            }

            logInfo("[AP] Seed flag map built: added=" + added
                    + " collisions=" + collisions
                    + " noItemInfo=" + noItemInfo
                    + " chestWeightMapped=" + chestWeightMapped
                    + " fakeItemMapped=" + fakeItemMapped
                    + " npcMoneyMapped=" + npcMoneyMapped
                    + " fakeScanMapped=" + fakeScanMapped);
        }

        private static void TryAdd(
            ItemID item,
            LocationID location,
            Action<int, int, LocationID> addNumeric,
            HashSet<int> seenKeys,
            ref int added,
            ref int collisions,
            ref int noItemInfo,
            ref int chestWeightMapped,
            ref int fakeItemMapped,
            ref int npcMoneyMapped,
            ref int fakeScanMapped,
            Action<string> logWarn)
        {
            ItemInfo info = ItemDB.GetItemInfo(item);
            if (info == null)
            {
                noItemInfo++;
                logWarn("[AP] No ItemInfo for ItemID=" + item + " (location=" + location + ")");
                return;
            }

            // 1) Seed-derived maps (container flags)
            if (IsSeedChestWeight(item, info))
            {
                if (info.ItemSheet == 31 && info.ItemFlag >= 0 && info.ItemFlag <= 39)
                {
                    ChestWeightFlagToLocation[info.ItemFlag] = location;
                    chestWeightMapped++;
                }
            }

            if (IsSeedFakeItem(item, info))
            {
                if (info.ItemSheet == 31 && info.ItemFlag >= 40 && info.ItemFlag <= 79)
                {
                    FakeItemFlagToLocation[info.ItemFlag] = location;
                    fakeItemMapped++;
                }
            }

            if (IsSeedNpcMoney(item, info))
            {
                if (info.ItemSheet == 31 && info.ItemFlag >= 80 && info.ItemFlag <= 89)
                {
                    NpcMoneyFlagToLocation[info.ItemFlag] = location;
                    npcMoneyMapped++;
                }
            }

            if (IsSeedFakeScan(item, info))
            {
                if (info.ItemSheet == 31 && info.ItemFlag >= 80 && info.ItemFlag <= 89)
                {
                    FakeScanFlagToLocation[info.ItemFlag] = location;
                    fakeScanMapped++;
                }
            }

            // 2) Reverse mapping for AP item labeling
            LocationToItem[location] = item;


            if (!IsNonUniqueFiller(info))
            {
                if (!string.IsNullOrEmpty(info.BoxName) && !BoxNameToLocation.ContainsKey(info.BoxName))
                    BoxNameToLocation[info.BoxName] = location;

                // Also key by enum name (e.g. "Map4", "Ankh Jewel6") so kataribe's
                // raw itemIdBuff values resolve correctly without normalization.
                string enumName = item.ToString();
                if (!string.IsNullOrEmpty(enumName) && !BoxNameToLocation.ContainsKey(enumName))
                    BoxNameToLocation[enumName] = location;
            }

            // 3) Main numeric map only for unique flags - Currently using unique FakeItem flags
            // if (IsNonUniqueFiller(info))
            //    return;

            int sheet = info.ItemSheet;
            int flag = info.ItemFlag;

            if (sheet < 0 || flag < 0)
                return;

            int key = (sheet << 16) | (flag & 0xFFFF);

            if (!seenKeys.Add(key))
            {
                collisions++;
                logWarn("[AP] Flag collision: seet=" + sheet + " flag=" + flag + " already used; also at location=" + location);
                return;
            }

            addNumeric(sheet, flag, location);
            added++;
        }

        private static bool IsSeedChestWeight(ItemID item, ItemInfo info)
        {
            // Prefer enum name; seed stores actual ItemID even if BoxName is remapped to Coin/Weight
            string name = item.ToString();
            if (!string.IsNullOrEmpty(name) &&
                name.StartsWith("ChestWeight", StringComparison.OrdinalIgnoreCase))
                return true;

            // Fallback: sheet31 flags 0..39 are ChestWeight container flags
            return (info != null && info.ItemSheet == 31 && info.ItemFlag >= 0 && info.ItemFlag <= 39);
        }

        private static bool IsSeedFakeItem(ItemID item, ItemInfo info)
        {
            // Prefer enum name; seed stores actual ItemID even if BoxName is remapped to Coin/Weight
            string name = item.ToString();
            if (!string.IsNullOrEmpty(name) &&
                name.StartsWith("FakeItem", StringComparison.OrdinalIgnoreCase))
                return true;

            // Fallback: sheet31 flags 40..79 are FakeItem container flags
            return (info != null && info.ItemSheet == 31 && info.ItemFlag >= 40 && info.ItemFlag <= 79);
        }

        private static bool IsSeedNpcMoney(ItemID item, ItemInfo info)
        {
            string name = item.ToString();
            if (!string.IsNullOrEmpty(name) &&
                name.StartsWith("NPCMoney", StringComparison.OrdinalIgnoreCase))
                return true;

            // Fallback: sheet31 flags 80..89 are NPCMoney container flags
            return (info != null && info.ItemSheet == 31 && info.ItemFlag >= 80 && info.ItemFlag <= 89);
        }
        private static bool IsSeedFakeScan(ItemID item, ItemInfo info)
        {
            string name = item.ToString();
            if (!string.IsNullOrEmpty(name) &&
                name.StartsWith("FakeScan", StringComparison.OrdinalIgnoreCase))
                return true;

            // Fallback: sheet31 flags 90..94 are FakeScan container flags
            return (info != null && info.ItemSheet == 31 && info.ItemFlag >= 90 && info.ItemFlag <= 94);
        }

        private static bool IsNonUniqueFiller(ItemInfo info)
        {
            // Prefer BoxName since your remap changes FakeItemXX -> Weight5 etc.
            string box = info.BoxName;
            if (string.IsNullOrEmpty(box))
                return false;

            box = box.Trim();

            // Anything used as repeatable filler should be excluded from the main (sheet,flag)->location map.
            if (box.StartsWith("FakeItem", StringComparison.OrdinalIgnoreCase))
                return true;

            if (box.IndexOf("Coin", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (box.IndexOf("Weight", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }
    }
}
