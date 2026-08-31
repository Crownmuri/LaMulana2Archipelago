using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using Newtonsoft.Json.Linq;

namespace LaMulana2Archipelago.Managers
{
    /// <summary>
    /// Reads a legacy LaMulana2Randomizer seed.lm2r file and produces a slot_data
    /// shaped Dictionary that the standalone-mode patches can consume exactly as
    /// they would an AP slot_data payload.
    ///
    /// Binary layout mirrors Archipelago\worlds\lamulana2\seed.py write_seed_file.
    ///
    /// If a companion seed.lm2ap file is present next to seed.lm2r, its AP-only
    /// settings (guardian_specific_ankhs, potsanity, ap_chest_color, death_link,
    /// logic_difficulty, costume_clip, dlc_item_logic, life_sigil_to_awaken_hom,
    /// random_research, game_difficulty, glossanity, costumesanity,
    /// persistent_inventory, goal, glossary_hunt_count, and the per-pool
    /// potsanity/glossanity partitions plus oannesanity/gate_entrances)
    /// plus pot placements,
    /// </summary>
    internal static class SeedToSlotData
    {
        // Pot pools and glossary categories, in the exact order seed.py
        // writes them (ids.py POT_POOLS / GLOSS_POOLS). Order is part of
        // the binary format: adding a pool means bumping the version.
        private static readonly string[] PotPools =
        {
            "low_value", "high_value", "shuriken", "rolling_shuriken", "earth_spear",
            "flare", "caltrops", "chakram", "bomb",
        };
        private static readonly string[] GlossPools =
        {
            "freestanding", "scannable", "npc", "enemy",
        };

        // Entrance shuffle categories, in seed.py's write order. The tracker
        // pre-fills a pair as vanilla whenever its category is off, so all of
        // these must reach slot_data.options or the whole entrance graph locks
        // to vanilla and the seed's real pairings are ignored.
        private static readonly string[] EntranceCategories =
        {
            "horizontal_entrances", "vertical_entrances", "gate_entrances",
            "unique_transitions", "include_dlc_entrances", "soul_gate_entrances",
        };

        // ASCII "LM2A" — must match LM2AP_MAGIC in seed.py.
        private static readonly byte[] Lm2apMagic = new byte[] { (byte)'L', (byte)'M', (byte)'2', (byte)'A' };
        // v1 had no location_labels section; v2 appends it after pot_flag_map.
        // v3 appends greedy_charon after location_labels.
        // v4 appends game_difficulty int32 after greedy_charon.
        // v5 appends the AP-only placements (glossary/DLC/costume, which the
        // writer stopped putting in seed.lm2r), the glossary flag map, the
        // goal fields, the costumesanity/persistent_inventory toggles, and
        // the per-pool potsanity/glossanity partitions plus oannesanity
        // and gate_entrances, plus own_placeholder_items (the real own-world
        // ItemID behind each per-location AP placeholder).
        // All versions remain readable: missing sections fall back to defaults.
        private const int Lm2apSupportedVersion = 5;

        public static string SeedPath
        {
            get
            {
                string path = Path.Combine(Paths.GameRootPath, "LaMulana2Randomizer");
                path = Path.Combine(path, "Seed");
                path = Path.Combine(path, "seed.lm2r");
                return path;
            }
        }

        public static string ApSeedPath
        {
            get
            {
                string path = Path.Combine(Paths.GameRootPath, "LaMulana2Randomizer");
                path = Path.Combine(path, "Seed");
                path = Path.Combine(path, "seed.lm2ap");
                return path;
            }
        }

        public static bool TryLoad(out Dictionary<string, object> slotData, out string error)
        {
            slotData = null;
            error = null;

            if (!File.Exists(SeedPath))
            {
                error = "seed.lm2r not found at: " + SeedPath;
                return false;
            }

            try
            {
                var dict = new Dictionary<string, object>();

                using (var br = new BinaryReader(File.Open(SeedPath, FileMode.Open, FileAccess.Read, FileShare.Read)))
                {
                    // Header / Settings
                    dict["starting_weapon"]    = br.ReadInt32();
                    dict["starting_area"]      = br.ReadInt32();
                    dict["random_dissonance"]  = br.ReadBoolean() ? 1 : 0;
                    dict["required_guardians"] = br.ReadInt32();
                    dict["required_skulls"]    = br.ReadInt32();
                    dict["remove_it_statue"]   = br.ReadBoolean() ? 1 : 0;
                    dict["echidna"]            = br.ReadInt32();
                    dict["auto_scan_tablets"]  = br.ReadBoolean() ? 1 : 0;
                    dict["auto_place_skull"]   = br.ReadBoolean() ? 1 : 0;
                    dict["starting_money"]     = br.ReadInt32();
                    dict["starting_weights"]   = br.ReadInt32();
                    dict["item_chest_color"]   = br.ReadInt32();
                    dict["filler_chest_color"] = br.ReadInt32();

                    // Offline has no AP items; reuse the item chest color so the
                    // runtime never tints a chest "AP blue" when there is no AP peer.
                    // Overridden by .lm2ap if present.
                    dict["ap_chest_color"] = dict["item_chest_color"];

                    // Starting items
                    int startingItemCount = br.ReadInt32();
                    var startingItems = new JArray();
                    for (int i = 0; i < startingItemCount; i++)
                        startingItems.Add(br.ReadInt32());
                    dict["starting_items"] = startingItems;

                    // Normal item placements
                    int itemCount = br.ReadInt32();
                    var itemPlacements = new JArray();
                    for (int i = 0; i < itemCount; i++)
                    {
                        int loc = br.ReadInt32();
                        int item = br.ReadInt32();
                        itemPlacements.Add(new JObject
                        {
                            ["location"] = loc,
                            ["item"] = item,
                        });
                    }
                    dict["item_placements"] = itemPlacements;

                    // Shop placements — seed stores price as a multiplier, not a
                    // literal price. The standalone path treats this field as
                    // "price" directly, which matches the original behavior.
                    int shopCount = br.ReadInt32();
                    var shopPlacements = new JArray();
                    for (int i = 0; i < shopCount; i++)
                    {
                        int loc = br.ReadInt32();
                        int item = br.ReadInt32();
                        int price = br.ReadInt32();
                        shopPlacements.Add(new JObject
                        {
                            ["location"] = loc,
                            ["item"] = item,
                            ["price"] = price,
                        });
                    }
                    dict["shop_placements"] = shopPlacements;

                    // Cursed locations
                    int cursedCount = br.ReadInt32();
                    var cursed = new JArray();
                    for (int i = 0; i < cursedCount; i++)
                        cursed.Add(br.ReadInt32());
                    dict["cursed_locations"] = cursed;

                    // Entrance pairs
                    int entranceCount = br.ReadInt32();
                    var entrances = new JArray();
                    for (int i = 0; i < entranceCount; i++)
                    {
                        int a = br.ReadInt32();
                        int b = br.ReadInt32();
                        entrances.Add(new JArray { a, b });
                    }
                    dict["entrance_pairs"] = entrances;

                    // Soul gate pairs
                    int soulGateCount = br.ReadInt32();
                    var soulGates = new JArray();
                    for (int i = 0; i < soulGateCount; i++)
                    {
                        int a = br.ReadInt32();
                        int b = br.ReadInt32();
                        int req = br.ReadInt32();
                        soulGates.Add(new JArray { a, b, req });
                    }
                    dict["soul_gate_pairs"] = soulGates;
                }

                // Features that exist only in AP slot_data — start with
                // deterministic offline defaults so GetSlotBool/GetSlotDict
                // behave predictably when the .lm2ap companion is missing.
                dict["potsanity"] = 0;
                dict["death_link"] = 0;
                dict["guardian_specific_ankhs"] = 0;
                dict["greedy_charon"] = 0;
                dict["game_difficulty"] = 0;
                dict["glossanity"] = 0;
                dict["costumesanity"] = 0;
                dict["persistent_inventory"] = 0;
                dict["goal"] = 0;
                dict["glossary_hunt_count"] = 0;
                dict["oannesanity"] = 0;
                dict["gate_entrances"] = 0;
                foreach (string category in EntranceCategories) dict[category] = 0;
                foreach (string pool in PotPools) dict["potsanity_" + pool] = 0;
                foreach (string pool in GlossPools) dict["glossanity_" + pool] = 0;
                // Online slot_data nests these under "options"; mirror that
                // shape so a consumer written against the online payload
                // reads the same path offline. Only the keys seed.lm2ap
                // actually encodes appear here -- everything else stays a
                // flat top-level key, which offline populates in full.
                dict["options"] = new Dictionary<string, object>();

                // Merge the AP-extended companion file if it exists. Failure to
                // load is non-fatal: a stock LM2 randomizer seed has no .lm2ap.
                if (File.Exists(ApSeedPath))
                {
                    string apError;
                    if (!TryMergeApSeed(dict, out apError))
                    {
                        // Surface the parse error to logs but keep the legacy
                        // seed usable rather than failing the whole load.
                        Plugin.Log.LogWarning("[SeedToSlotData] " + apError);
                    }
                }

                slotData = dict;
                return true;
            }
            catch (Exception ex)
            {
                error = "Failed reading seed.lm2r: " + ex;
                return false;
            }
        }

        /// <summary>
        /// Reads seed.lm2ap (AP-extended settings + pot data) and merges its
        /// values into the slot_data dict. Layout mirrors write_ap_seed_file
        /// in seed.py.
        /// </summary>
        private static bool TryMergeApSeed(Dictionary<string, object> dict, out string error)
        {
            error = null;
            try
            {
                using (var br = new BinaryReader(File.Open(ApSeedPath, FileMode.Open, FileAccess.Read, FileShare.Read)))
                {
                    byte[] magic = br.ReadBytes(4);
                    if (magic.Length != 4
                        || magic[0] != Lm2apMagic[0]
                        || magic[1] != Lm2apMagic[1]
                        || magic[2] != Lm2apMagic[2]
                        || magic[3] != Lm2apMagic[3])
                    {
                        error = "seed.lm2ap has bad magic header; expected LM2A";
                        return false;
                    }

                    int version = br.ReadInt32();
                    if (version < 1 || version > Lm2apSupportedVersion)
                    {
                        error = $"seed.lm2ap version {version} is not supported (expected 1..{Lm2apSupportedVersion})";
                        return false;
                    }

                    // --- AP-only settings ---
                    dict["guardian_specific_ankhs"]  = br.ReadBoolean() ? 1 : 0;
                    dict["potsanity"]                = br.ReadBoolean() ? 1 : 0;
                    dict["ap_chest_color"]           = br.ReadInt32();
                    dict["logic_difficulty"]         = br.ReadInt32();
                    dict["costume_clip"]             = br.ReadBoolean() ? 1 : 0;
                    dict["dlc_item_logic"]           = br.ReadBoolean() ? 1 : 0;
                    dict["life_sigil_to_awaken_hom"] = br.ReadBoolean() ? 1 : 0;
                    dict["random_research"]          = br.ReadBoolean() ? 1 : 0;
                    dict["death_link"]               = br.ReadBoolean() ? 1 : 0;

                    // Online slot_data carries pot and AP-only placements inside
                    // item_placements; the seed splits them across files, so both
                    // are folded back in here. SeedFlagMapBuilder and
                    // LocationFlagMap only ever look at item_placements, so
                    // without this the offline flag map is missing every pot,
                    // glossary, DLC and costume location.
                    JArray itemPlacements = dict.TryGetValue("item_placements", out object rawItems)
                        ? rawItems as JArray
                        : null;

                    // --- Pot placements: same shape as item_placements so the
                    //     runtime can treat them uniformly. ItemPotPatch keys
                    //     pickups off pot_flag_map; the placements list is for
                    //     anything that needs (location, item) per pot.
                    int potCount = br.ReadInt32();
                    var potPlacements = new JArray();
                    for (int i = 0; i < potCount; i++)
                    {
                        int loc = br.ReadInt32();
                        int item = br.ReadInt32();
                        potPlacements.Add(new JObject
                        {
                            ["location"] = loc,
                            ["item"] = item,
                        });
                        AppendPlacement(itemPlacements, loc, item);
                    }
                    dict["pot_placements"] = potPlacements;

                    // --- Pot flag map: keyed by stringified LocationID to match
                    //     the AP slot_data shape that ItemPotPatch.Initialize
                    //     consumes via GetSlotDict.
                    int flagCount = br.ReadInt32();
                    var potFlagMap = new Dictionary<string, object>();
                    for (int i = 0; i < flagCount; i++)
                    {
                        int locationIdValue = br.ReadInt32();
                        int potFlagNo = br.ReadInt32();
                        potFlagMap[locationIdValue.ToString()] = potFlagNo;
                    }
                    dict["pot_flag_map"] = potFlagMap;

                    // --- Location labels (v2+): LocationID → display name as
                    //     it should appear in-game. Lets the offline path show
                    //     foreign-player items by name and lets own items keep
                    //     guardian-specific Ankh names instead of falling back
                    //     to the vanilla BoxName.
                    var locationLabels = new Dictionary<int, string>();
                    if (version >= 2)
                    {
                        int labelCount = br.ReadInt32();
                        for (int i = 0; i < labelCount; i++)
                        {
                            int locationIdValue = br.ReadInt32();
                            int byteCount = br.ReadInt32();
                            byte[] nameBytes = br.ReadBytes(byteCount);
                            locationLabels[locationIdValue] = Encoding.UTF8.GetString(nameBytes);
                        }
                    }
                    dict["location_labels"] = locationLabels;

                    // --- v3+ QoL toggles: greedy_charon overrides the default
                    //     set in TryLoad. v1/v2 readers keep the default (0).
                    if (version >= 3)
                    {
                        dict["greedy_charon"] = br.ReadBoolean() ? 1 : 0;
                    }

                    // --- v4+ Difficulty: game_difficulty (0=normal, 1=hard, 2=hardest).
                    //     Pre-v4 seeds default to 0 set in TryLoad.
                    if (version >= 4)
                    {
                        dict["game_difficulty"] = br.ReadInt32();
                    }

                    // --- v5+ AP-only placements: glossary, DLC and costume
                    //     locations. The writer stopped emitting these into
                    //     seed.lm2r (the legacy mod cannot parse them), so
                    //     item_placements is the only place they can land.
                    if (version >= 5)
                    {
                        int apCount = br.ReadInt32();
                        for (int i = 0; i < apCount; i++)
                        {
                            int loc = br.ReadInt32();
                            int item = br.ReadInt32();
                            AppendPlacement(itemPlacements, loc, item);
                        }

                        // --- Glossanity: same shape as pot_flag_map, keyed by
                        //     stringified LocationID for GlossaryManager's
                        //     GetSlotDict consumption.
                        dict["glossanity"] = br.ReadBoolean() ? 1 : 0;
                        int glossaryFlagCount = br.ReadInt32();
                        var glossaryFlagMap = new Dictionary<string, object>();
                        for (int i = 0; i < glossaryFlagCount; i++)
                        {
                            int locationIdValue = br.ReadInt32();
                            int bookFlagNo = br.ReadInt32();
                            glossaryFlagMap[locationIdValue.ToString()] = bookFlagNo;
                        }
                        dict["glossary_flag_map"] = glossaryFlagMap;

                        // --- Goal: without these both goal trackers read 0 and
                        //     no non-default victory condition can fire offline.
                        dict["goal"] = br.ReadInt32();
                        dict["glossary_hunt_count"] = br.ReadInt32();

                        // --- Remaining slot_data toggles. costumesanity was
                        //     previously inferred by SceneRandomizer from the
                        //     costume closets showing up in the placements.
                        dict["costumesanity"] = br.ReadBoolean() ? 1 : 0;
                        dict["persistent_inventory"] = br.ReadBoolean() ? 1 : 0;

                        // --- Pool partitions. The collapsed potsanity /
                        //     glossanity bools only say "any pool on";
                        //     these say which. Read in PotPools /
                        //     GlossPools order to match the writer.
                        var options = dict["options"] as Dictionary<string, object>;
                        foreach (string pool in PotPools)
                        {
                            int on = br.ReadBoolean() ? 1 : 0;
                            dict["potsanity_" + pool] = on;
                            if (options != null) options["potsanity_" + pool] = on;
                        }
                        foreach (string pool in GlossPools)
                        {
                            int on = br.ReadBoolean() ? 1 : 0;
                            dict["glossanity_" + pool] = on;
                            if (options != null) options["glossanity_" + pool] = on;
                        }

                        // oannesanity was inferred from the Fish Suit closet
                        // appearing in the placements; gate_entrances had no
                        // offline source at all. Both are explicit now.
                        int oannesanity = br.ReadBoolean() ? 1 : 0;
                        dict["oannesanity"] = oannesanity;
                        if (options != null)
                        {
                            options["oannesanity"] = oannesanity;
                            options["costumesanity"] = dict["costumesanity"];
                        }

                        foreach (string category in EntranceCategories)
                        {
                            int on = br.ReadBoolean() ? 1 : 0;
                            dict[category] = on;
                            if (options != null) options[category] = on;
                        }

                        // --- Own items hidden behind AP placeholders. The
                        //     generator routes our OWN glossary ROMs and pot
                        //     filler through the per-location sheet-31
                        //     placeholder (410000+n) so the location's AP
                        //     machinery fires the check. Online the scout reply
                        //     still names the real item and its owner; offline
                        //     the seed is the only source, and a placeholder
                        //     there is indistinguishable from another player's
                        //     item. This map resolves them back.
                        //
                        //     Guarded on remaining length rather than a version
                        //     bump: this landed inside v5 before v5 shipped, so
                        //     a seed generated from an earlier v5 build has the
                        //     same version stamp and simply ends here. Reading
                        //     past it would throw and take the whole offline
                        //     activation down with it.
                        if (br.BaseStream.Position < br.BaseStream.Length)
                        {
                            int ownCount = br.ReadInt32();
                            var ownItems = new Dictionary<string, object>();
                            for (int i = 0; i < ownCount; i++)
                            {
                                int loc = br.ReadInt32();
                                int item = br.ReadInt32();
                                ownItems[loc.ToString()] = item;
                            }
                            dict["own_placeholder_items"] = ownItems;
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "Failed reading seed.lm2ap: " + ex;
                return false;
            }
        }

        /// <summary>
        /// Reads the own_placeholder_items section back out of a parsed
        /// slot_data dict as game LocationID -> real own game ItemID.
        ///
        /// Empty for a pre-v5 seed (and online, where the section never
        /// exists), which is the correct answer there: every consumer falls
        /// back to reading the placement at face value.
        /// </summary>
        public static Dictionary<int, int> GetOwnPlaceholderItems(Dictionary<string, object> slotData)
        {
            var result = new Dictionary<int, int>();
            if (slotData == null) return result;

            object raw;
            if (!slotData.TryGetValue("own_placeholder_items", out raw)) return result;

            var map = raw as Dictionary<string, object>;
            if (map == null) return result;

            foreach (var kvp in map)
            {
                int loc;
                if (!int.TryParse(kvp.Key, out loc) || kvp.Value == null) continue;
                try { result[loc] = Convert.ToInt32(kvp.Value); }
                catch { /* malformed entry -- fall back to the raw placement */ }
            }
            return result;
        }

        /// <summary>
        /// Appends one (location, item) pair to the item_placements array in
        /// the online slot_data shape. Null-tolerant: a seed.lm2r that somehow
        /// parsed without an item_placements array just skips the merge rather
        /// than taking down the whole .lm2ap load.
        /// </summary>
        private static void AppendPlacement(JArray itemPlacements, int loc, int item)
        {
            if (itemPlacements == null) return;
            itemPlacements.Add(new JObject
            {
                ["location"] = loc,
                ["item"] = item,
            });
        }
    }
}
