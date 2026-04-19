using System;
using System.Collections.Generic;
using System.IO;
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
    /// </summary>
    internal static class SeedToSlotData
    {
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

                // Features that exist only in AP slot_data — keep them explicit
                // so GetSlotBool/GetSlotDict return deterministic offline defaults.
                dict["potsanity"] = 0;
                dict["death_link"] = 0;
                dict["guardian_specific_ankhs"] = 0;

                slotData = dict;
                return true;
            }
            catch (Exception ex)
            {
                error = "Failed reading seed.lm2r: " + ex;
                return false;
            }
        }
    }
}
