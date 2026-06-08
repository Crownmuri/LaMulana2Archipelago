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
        private const int BookSheet = 20;

        private static bool _glossanityEnabled;
        private static bool _initialized;

        // bookFlagNo → LocationID, used by MonsterChipGlossaryPatch to identify a
        // freestanding glossary chip at pickup and run the AP replacement flow.
        private static readonly Dictionary<int, LocationID> BookFlagToLocation = new Dictionary<int, LocationID>();

        /// <summary>True when glossanity is on and at least one entry is mapped.</summary>
        public static bool Enabled => _glossanityEnabled && BookFlagToLocation.Count > 0;

        /// <summary>Map a sheet-20 book flag number to its glossary LocationID.</summary>
        public static bool TryGetLocation(int bookFlagNo, out LocationID location)
        {
            return BookFlagToLocation.TryGetValue(bookFlagNo, out location);
        }

        /// <summary>
        /// Called from ArchipelagoClient after slot_data is available
        /// (right after ItemPotPatch.Initialize()).
        /// </summary>
        public static void Initialize()
        {
            _initialized = true;
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
            _initialized = false;
            _glossanityEnabled = false;
            BookFlagToLocation.Clear();
            Patches.MonsterChipSpritePatch.Clear();
        }
    }
}
