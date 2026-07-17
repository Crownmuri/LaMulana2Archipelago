using System;
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
    /// Vanilla stores costume ownership in the profile-global clothbox (system
    /// file 2, <c>setClothBox</c>/<c>getClothBox</c>), and regenerates the
    /// per-seed sheet-2 costume flags from it on every load via
    /// <c>L2System.setSystemDataToClothFlag</c>. We must not write the clothbox —
    /// it is shared across profiles and seeds — so the AP-received set is stored
    /// in <see cref="StateFlag"/> instead and fed back to the game by
    /// <see cref="Patches.ClothBoxPatch"/>.
    ///
    /// The state flag lives in the game's own save, which is what makes a received
    /// costume survive a relaunch: the shadow log only re-grants items above the
    /// save's checkpoint, so anything below it must persist the way a real item
    /// does. It also gives death/hardload the right semantics for free — the flag
    /// reverts to the checkpoint along with every other flag, and costumes
    /// received after that point are re-granted by the shadow log.
    ///
    /// Flag writes are wrapped in <see cref="ItemGrantRecursiveGuard"/> so
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

        // Bitmask of AP-received costumes, bit i = clothbox index i. Sheet-2 slot
        // 179 is a blank scratch flag ("d179"), free in the gap between SacredOrb19
        // (178) and Research1 (180). Flags save as a full short, so all 5 bits
        // round-trip.
        private const int StateSheet = 2;
        private const int StateFlag = 179;

        /// <summary>True when costumesanity is active for this seed.</summary>
        public static bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                // Costumes just came under AP control. The game only regenerates the
                // costume flags on load, so if this seed was connected with a save
                // already loaded they need a resync now.
                _applyPending = value;
            }
        }

        private static bool _enabled;
        private static bool _applyPending;

        public static bool IsCostume(ItemID id) => Array.IndexOf(Costumes, id) >= 0;

        public static void Reset()
        {
            Enabled = false;
        }

        /// <summary>
        /// Per-frame hook for the resync queued when costumesanity turns on
        /// mid-session. No-op on every other frame.
        /// </summary>
        public static void TryApplyPending(L2System sys)
        {
            if (!_applyPending || !_enabled || sys == null) return;
            _applyPending = false;
            ApplyToFlags(sys);
        }

        /// <summary>
        /// True if the AP item for the costume in the given clothbox slot (0-4)
        /// has been received. False for every costume when costumesanity is off,
        /// so callers fall back to the vanilla profile clothbox.
        /// </summary>
        public static bool IsReceived(L2System sys, int clothBoxIndex)
        {
            if (!Enabled || sys == null) return false;
            if (clothBoxIndex < 0 || clothBoxIndex >= Costumes.Length) return false;
            return (ReadState(sys) & (1 << clothBoxIndex)) != 0;
        }

        /// <summary>Record an AP-received costume and immediately make it wearable.</summary>
        public static void MarkReceived(L2System sys, ItemID id)
        {
            int idx = Array.IndexOf(Costumes, id);
            if (idx < 0 || sys == null) return;

            using (ItemGrantRecursiveGuard.Begin())
                sys.setFlagData(StateSheet, StateFlag, (short)(ReadState(sys) | (1 << idx)));

            // No cloth-flag sync happens on item receipt, so apply now rather than
            // waiting for the next load.
            ApplyToFlags(sys);
        }

        /// <summary>
        /// Force the sheet-2 costume flags to match the AP-received set. No-op
        /// unless costumesanity is on.
        /// </summary>
        public static void ApplyToFlags(L2System sys)
        {
            if (!Enabled || sys == null) return;

            short state = ReadState(sys);

            // Guard so the costume flag writes don't masquerade as location checks.
            using (ItemGrantRecursiveGuard.Begin())
            {
                for (int i = 0; i < Costumes.Length; i++)
                {
                    ItemInfo info = ItemDB.GetItemInfo(Costumes[i]);
                    if (info == null) continue;
                    short want = (short)(((state >> i) & 1) != 0 ? 1 : 0);
                    sys.setFlagData(info.ItemSheet, info.ItemFlag, want);
                }
            }
        }

        private static short ReadState(L2System sys)
        {
            short state = 0;
            sys.getFlag(StateSheet, StateFlag, ref state);
            return state;
        }
    }
}
