using System;
using System.Collections.Generic;
using L2Base;
using LaMulana2RandomizerShared;
using LM2RandomiserMod;

namespace LaMulana2Archipelago.Managers
{
    /// <summary>
    /// Costumesanity: when enabled, costumes (the 5 closet costumes —
    /// Kimono Cowgirl, Valkyrie, Little Demon, Eastern Europe, Fish Suit) start
    /// hidden and only become wearable once their AP unlock item is received.
    ///
    /// Implementation: the game maps the profile-global clothbox into per-seed
    /// item-sheet flags (sheet 2) via <c>L2System.setSystemDataToClothFlag</c>.
    /// We post-process that sync (<see cref="Patches.CostumeHidePatch"/>) and
    /// force every not-yet-received costume's flag back to 0 — so the profile's
    /// real costume unlocks are never touched (no "nuke"), but the seed only sees
    /// the AP-received subset.
    ///
    /// The flag writes are wrapped in <see cref="ItemGrantRecursiveGuard"/> so
    /// toggling a costume flag never trips CheckManager into reporting a location.
    /// </summary>
    public static class CostumeManager
    {
        /// <summary>Costume unlock items, in clothbox order 0-4.</summary>
        public static readonly ItemID[] Costumes =
        {
            ItemID.KimonoCowgirl,   // clothbox 0
            ItemID.Valkyrie,        // clothbox 1
            ItemID.LittleDemon,     // clothbox 2
            ItemID.EasternEurope,   // clothbox 3
            ItemID.FishSuit,        // clothbox 4 (DLC)
        };

        /// <summary>True when costumesanity is active for this seed.</summary>
        public static bool Enabled = false;

        private static readonly HashSet<ItemID> Received = new HashSet<ItemID>();

        public static bool IsCostume(ItemID id) => Array.IndexOf(Costumes, id) >= 0;

        public static void Reset()
        {
            Enabled = false;
            Received.Clear();
        }

        /// <summary>Record an AP-received costume and immediately make it wearable.</summary>
        public static void MarkReceived(L2System sys, ItemID id)
        {
            if (!IsCostume(id)) return;
            Received.Add(id);
            ApplyToFlags(sys);
        }

        /// <summary>
        /// Force the sheet-2 costume flags to match the AP-received set, overriding
        /// the vanilla profile-global sync. No-op unless costumesanity is on.
        /// </summary>
        public static void ApplyToFlags(L2System sys)
        {
            if (!Enabled || sys == null) return;

            // Guard so the costume flag writes don't masquerade as location checks.
            using (ItemGrantRecursiveGuard.Begin())
            {
                foreach (ItemID c in Costumes)
                {
                    ItemInfo info = ItemDB.GetItemInfo(c);
                    if (info == null) continue;
                    short want = (short)(Received.Contains(c) ? 1 : 0);
                    sys.setFlagData(info.ItemSheet, info.ItemFlag, want);
                }
            }
        }
    }
}
