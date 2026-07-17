using System;
using System.Collections.Generic;
using LaMulana2Archipelago.Archipelago;
using LaMulana2RandomizerShared;

namespace LaMulana2Archipelago.Managers
{
    /// <summary>
    /// Glossanity: registers glossary ("book") flag → LocationID mappings so that
    /// unlocking a glossary entry sends its AP location check.
    ///
    /// Glossary entries are flags on sheet 20 ("20book"). When the player obtains
    /// a freestanding glossary record/chip, the game sets its sheet-20 flag via
    /// setFlagData(20, flagNo, 1) — confirmed for enemyRG=147 in the DevUI
    /// flagwatcher ("[20,147]20book.enemyRG = 1"). That write already flows
    /// through SetFlagDataFlagSystemPatch → CheckManager.NotifyNumericFlag, so all
    /// we do here is register (20, flagNo) → LocationID. No new Harmony patch.
    ///
    /// DETECTION ONLY: this does not replace the freestanding object with an AP
    /// item, so the player still receives the vanilla record/chip in addition to
    /// the AP item the server echoes for the location. Physical replacement
    /// (like ItemPotPatch does for pots) is a later step.
    ///
    /// glossary_flag_map (from slot_data) maps LocationID.value → sheet-20 flagNo,
    /// parallel to pot_flag_map.
    /// </summary>
    public static class GlossaryManager
    {
        // Glossary flags live on sheet 20 ("20book")
        public const int BookSheet = 20;

        private static bool _glossanityEnabled;

        // bookFlagNo → LocationID, used by MonsterChipGlossaryPatch to identify a
        // freestanding glossary chip at pickup and run the AP replacement flow.
        private static readonly Dictionary<int, LocationID> BookFlagToLocation = new Dictionary<int, LocationID>();

        // glossary ROM item game_id → sheet-20 book flagNo, used by ItemGrantManager
        // to deliver a *received* ROM (unlock the encyclopedia entry + floating popup).
        // The apworld numbers each glossary entry so its item game_id == its LocationID
        // value, so this is just the glossary_flag_map keyed by id instead of by flag.
        private static readonly Dictionary<int, int> ItemGameIdToBookFlag = new Dictionary<int, int>();

        /// <summary>True when glossanity is on and at least one entry is mapped.</summary>
        public static bool Enabled => _glossanityEnabled && BookFlagToLocation.Count > 0;

        /// <summary>Map a sheet-20 book flag number to its glossary LocationID.</summary>
        public static bool TryGetLocation(int bookFlagNo, out LocationID location)
        {
            return BookFlagToLocation.TryGetValue(bookFlagNo, out location);
        }

        /// <summary>
        /// Sheet-20 flag numbers for every AP-shuffled glossary entry. Used by
        /// GlossaryGoalTracker to count how many shuffled entries the player has
        /// unlocked (an entry's flag is set only when its ROM is received).
        /// </summary>
        public static ICollection<int> RegisteredBookFlags => BookFlagToLocation.Keys;

        /// <summary>
        /// Map a received glossary ROM item's game id to its sheet-20 book flagNo.
        /// True only when glossanity is on and the id is a registered glossary entry.
        /// </summary>
        public static bool TryGetBookFlagForItem(int gameId, out int bookFlagNo)
        {
            bookFlagNo = 0;
            return _glossanityEnabled && ItemGameIdToBookFlag.TryGetValue(gameId, out bookFlagNo);
        }

        // AP item id windows: foreign placeholders are [410000, 420000); our own items are
        // BASE_ITEM_ID(420000) + game_id. A glossary ROM is therefore an own item whose
        // game_id is a registered glossary entry — identified by ID, never by name.
        private const long ApBaseItemId = 420000;

        // Glossary item game_id range (apworld assigns 2000..2251).
        private const int GlossaryGameIdMin = 2000;
        private const int GlossaryGameIdMax = 2251;

        /// <summary>game_id of a scouted/received AP item (apItemId - BASE_ITEM_ID).</summary>
        public static int RomGameId(long apItemId) => (int)(apItemId - ApBaseItemId);

        /// <summary>True if the AP item id is a glossary ROM (any owner — own or another LM2
        /// player's), by id window. Used where the owner doesn't matter (e.g. keep the chip's
        /// native floor sprite for any glossary ROM).</summary>
        public static bool IsGlossaryRomId(long apItemId)
        {
            int g = RomGameId(apItemId);
            return g >= GlossaryGameIdMin && g <= GlossaryGameIdMax;
        }

        /// <summary>
        /// True once a glossary location's AP check has fired (this session or on the server).
        /// In the decoupled model this — never the sheet-20 book flag — is the "location
        /// collected" marker, so every gate that decides whether a glossary chip may still be
        /// obtained must ask this.
        /// </summary>
        public static bool IsLocationCollected(LocationID loc)
        {
            long apLoc = 430000L + (int)loc;
            return CheckManager.IsLocationReported(apLoc)
                || (ArchipelagoClient.ServerData != null
                    && ArchipelagoClient.ServerData.CheckedLocations != null
                    && ArchipelagoClient.ServerData.CheckedLocations.Contains(apLoc));
        }

        /// <summary>
        /// True when a scouted item is one of THIS player's registered glossary ROMs. Robust to
        /// renaming the glossary items (uses the id window + flag map, not the display name).
        /// </summary>
        public static bool IsOwnGlossaryRom(ArchipelagoClient.ScoutedItem scout)
        {
            return scout != null && scout.IsOwnItem
                && TryGetBookFlagForItem(RomGameId(scout.ItemId), out _);
        }

        /// <summary>
        /// Called from ArchipelagoClient after slot_data is available
        /// (right after ItemPotPatch.Initialize()).
        /// </summary>
        public static void Initialize()
        {
            BookFlagToLocation.Clear();

            var serverData = ArchipelagoClient.ServerData;
            if (serverData == null)
            {
                _glossanityEnabled = false;
                Plugin.Log.LogWarning("[GLOSSARY] ServerData is null during Initialize");
                return;
            }

            _glossanityEnabled = serverData.GetSlotBool("glossanity");
            if (!_glossanityEnabled)
            {
                Plugin.Log.LogInfo("[GLOSSARY] Glossanity disabled");
                return;
            }

            var map = serverData.GetSlotDict("glossary_flag_map");
            if (map == null || map.Count == 0)
            {
                Plugin.Log.LogWarning("[GLOSSARY] Glossanity enabled but glossary_flag_map is empty or missing");
                _glossanityEnabled = false;
                return;
            }

            // glossary_flag_map format from Python: { "locationIdValue": bookFlagNo }
            int count = 0;
            foreach (var kvp in map)
            {
                try
                {
                    int locationIdValue = Convert.ToInt32(kvp.Key);
                    int bookFlagNo = Convert.ToInt32(kvp.Value);
                    LocationID locId = (LocationID)locationIdValue;

                    // Register (sheet=20, bookFlagNo) → LocationID so the existing
                    // setFlagData → SetFlagDataFlagSystemPatch → CheckManager chain
                    // resolves the glossary unlock to its AP location.
                    LocationFlagMap.RegisterNumeric(BookSheet, bookFlagNo, locId);

                    // Also keep the inverse lookup for the pickup-replacement patch.
                    BookFlagToLocation[bookFlagNo] = locId;
                    // ROM-delivery lookup: item game_id == LocationID value for glossary.
                    ItemGameIdToBookFlag[locationIdValue] = bookFlagNo;
                    count++;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[GLOSSARY] Failed to parse glossary_flag_map entry {kvp.Key}={kvp.Value}: {ex.Message}");
                }
            }

            Plugin.Log.LogInfo($"[GLOSSARY] Glossanity initialized: {count} glossary locations mapped");
        }

        public static void Reset()
        {
            _glossanityEnabled = false;
            BookFlagToLocation.Clear();
            ItemGameIdToBookFlag.Clear();
            Patches.MonsterChipSpritePatch.Clear();
            Patches.GlossaryChipActiveGate.Clear();
        }
    }
}
