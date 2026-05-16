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
    /// random_research) plus pot placements and pot_flag_map are merged in,
    /// letting the mod replay AP seeds solo with the same behavior as online.
    /// </summary>
    internal static class SeedToSlotData
    {
        // ASCII "LM2A" — must match LM2AP_MAGIC in seed.py.
        private static readonly byte[] Lm2apMagic = new byte[] { (byte)'L', (byte)'M', (byte)'2', (byte)'A' };
        // v1 had no location_labels section; v2 appends it after pot_flag_map.
        // v3 appends greedy_charon after location_labels.
        // All versions remain readable: missing sections fall back to defaults.
        private const int Lm2apSupportedVersion = 3;

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
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "Failed reading seed.lm2ap: " + ex;
                return false;
            }
        }
    }
}
