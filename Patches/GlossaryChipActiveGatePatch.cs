using System.Collections.Generic;
using HarmonyLib;
using L2Flag;
using LaMulana2Archipelago.Managers;
using LaMulana2RandomizerShared;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Decoupled glossanity spawn gate for freestanding chips (AbstractItemBase.itemActiveFlag).
    ///
    /// A scene glossary chip is gated on its own sheet-20 book flag ("20book.player2 == 0"):
    /// vanilla hides the chip once you own the entry. In the decoupled model that flag is the
    /// encyclopedia-unlock marker, NOT the location-collected marker — receiving this entry's
    /// ROM from AP (ItemGrantManager.DeliverGlossaryRom) sets the book flag while its location
    /// is still unchecked, so the chip's gate goes false and the location becomes unreachable.
    /// AbstractItemBase.groundInit then computes finished=true without calling hideItemSymbol,
    /// so the chip first appears present-but-unobtainable and vanishes on the next room reset —
    /// the two halves of the same bug.
    ///
    /// So we re-gate the chip on whether its AP location has been checked, mirroring
    /// MonsterChipDropGatePatch (which does the same for enemy drops):
    ///   - collected → force the whole gate false (hidden, exactly like vanilla-collected).
    ///   - otherwise → neutralise ONLY the sheet-20 book box to always-true, leaving any other
    ///                 condition (e.g. "the pot in front of me is broken") intact.
    /// Both places that derive `finished` from the gate — groundInit and resetActionCharacter —
    /// are prefixed, so the rewrite lands before the value is read; groundFirst's per-frame
    /// re-check then sees the same rewritten array and revives or hides the chip on its own.
    /// </summary>
    internal static class GlossaryChipActiveGate
    {
        private const int BookSheet = 20;

        private const int ViewSheet = 1;   // 01View — per-screen scratch, wiped by resetViewFlag()

        private struct Entry
        {
            public LocationID Loc;
            public L2FlagBoxParent[] Original;

            /// <summary>
            /// Whether this chip's L2TaskShadow.startflag has to be kept in lockstep with the
            /// gate. See MirrorToShadow.
            /// </summary>
            public bool MirrorShadow;
        }

        // instanceId → resolved glossary chip. Rewriting is done from Original every time, so
        // re-gating stays correct after we've already replaced the live array once.
        private static readonly Dictionary<int, Entry> _known = new Dictionary<int, Entry>();
        private static readonly HashSet<int> _skip = new HashSet<int>();

        public static void Clear() { _known.Clear(); _skip.Clear(); }

        // seet_no < 0 makes L2FlagBox read flag_no as a literal, so these compare two
        // constants and need no flag lookup at all.
        private static L2FlagBox Constant(bool value, LOGIC logic)
        {
            return new L2FlagBox
            {
                seet_no1 = -1,
                flag_no1 = 0,
                seet_no2 = -1,
                flag_no2 = value ? 0 : 1,
                comp = COMPARISON.Equal,
                logic = logic
            };
        }

        internal static void Apply(AbstractItemBase item)
        {
            try
            {
                if (!GlossaryManager.Enabled || item == null) return;

                int id = item.GetInstanceID();
                if (_skip.Contains(id)) return;

                Entry entry;
                if (!_known.TryGetValue(id, out entry))
                {
                    var chip = item as MonsterChipScript;
                    if (chip == null) { _skip.Add(id); return; }

                    // Same derivation as MonsterChipGlossaryPatch/MonsterChipSpritePatch.
                    int chipId = Traverse.Create(chip).Field("chipId").GetValue<int>();
                    int bookFlag = chipId > -1 ? chipId : chip.itemValue;

                    LocationID loc;
                    if (!GlossaryManager.TryGetLocation(bookFlag, out loc)) { _skip.Add(id); return; }

                    entry = new Entry
                    {
                        Loc = loc,
                        Original = item.itemActiveFlag,
                        MirrorShadow = item.shdowtask != null && ReadsViewFlag(item.itemActiveFlag)
                    };
                    _known[id] = entry;
                }

                if (GlossaryManager.IsLocationCollected(entry.Loc))
                {
                    item.itemActiveFlag = new[]
                    {
                        new L2FlagBoxParent { BOX = new[] { Constant(false, LOGIC.NON) }, logoc = LOGIC.NON }
                    };
                    MirrorToShadow(item, entry);
                    return;
                }

                item.itemActiveFlag = WithoutBookGate(entry.Original, entry.Loc);
                MirrorToShadow(item, entry);
            }
            catch { }
        }

        /// <summary>
        /// True when the gate depends on a 01View flag.
        ///
        /// Those are per-screen scratch, written by a greeting script (f01shop/firstTalk and the
        /// two Hiner shops' s-welcome for Hiner; the Village of Departure shop brothers'
        /// chipN-Talk 17-11/17-12 work the same way) and wiped wholesale by resetViewFlag() on
        /// every screen scroll. Only three of the game's 83 chipN-Talk chips are built this way,
        /// and only they can hit the trap MirrorToShadow exists to close.
        /// </summary>
        private static bool ReadsViewFlag(L2FlagBoxParent[] gate)
        {
            if (gate == null) return false;

            for (int i = 0; i < gate.Length; i++)
            {
                var parent = gate[i];
                if (parent == null || parent.BOX == null) continue;

                for (int j = 0; j < parent.BOX.Length; j++)
                    if (parent.BOX[j] != null && parent.BOX[j].seet_no1 == ViewSheet)
                        return true;
            }
            return false;
        }

        /// <summary>
        /// Keep a view-flag-gated chip's L2TaskShadow.startflag equal to the gate we just wrote.
        ///
        /// AbstractItemBase.hideItemSymbol() is gameObject.SetActive(false), so a chip whose gate
        /// reads false during a room reset switches its own GameObject off — and an inactive
        /// object never runs groundFirst, so nothing on the chip can ever switch it back on. The
        /// only route back is the shadow's SetActive(true), which fires on the rising edge of
        /// L2TaskShadow.startflag.
        ///
        /// Vanilla gets away with pointing that startflag at the NPC's 22talker flag because the
        /// greeting script writes the talker flag and the view flag in the same frame, so the
        /// edge and the gate land together. Decoupled glossanity breaks that pairing: receiving
        /// this entry's ROM from AP sets its sheet-20 book flag, fieldL00's FlagWatcher (2)
        /// ((22,180)==0 AND (20,229)==1 → set 1) mirrors that into the talker flag at room
        /// entry, and the shadow then spends its activation edge while the view flag is still 0.
        /// The chip hides itself, the shadow settles into its "true" branch with check_f false,
        /// and the greeting's view-flag write arrives with nothing left to re-activate the
        /// object — the location becomes unreachable purely because its ROM was found elsewhere.
        ///
        /// Mirroring the gate onto the startflag puts activation and visibility back in lockstep,
        /// so the edge always lands on the frame the trigger fires, whatever the talker flag did
        /// beforehand. Chips with no view flag in their gate keep their vanilla shadow condition:
        /// an empty (or book-gate-only) gate never self-hides, so they have nothing to recover
        /// from, and rewriting their startflag would spawn them before the player has met the NPC.
        ///
        /// The shadow gets its own deep copy — the chip's array is rewritten on every Apply.
        /// </summary>
        private static void MirrorToShadow(AbstractItemBase item, Entry entry)
        {
            if (!entry.MirrorShadow || item.shdowtask == null) return;

            item.shdowtask.startflag = Clone(item.itemActiveFlag);
        }

        private static L2FlagBoxParent[] Clone(L2FlagBoxParent[] source)
        {
            if (source == null) return null;

            var parents = new L2FlagBoxParent[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                var src = source[i];
                if (src == null || src.BOX == null) { parents[i] = src; continue; }

                var boxes = new L2FlagBox[src.BOX.Length];
                for (int j = 0; j < src.BOX.Length; j++)
                {
                    var box = src.BOX[j];
                    if (box == null) { boxes[j] = null; continue; }

                    boxes[j] = new L2FlagBox
                    {
                        seet_no1 = box.seet_no1, flag_no1 = box.flag_no1,
                        seet_no2 = box.seet_no2, flag_no2 = box.flag_no2,
                        comp = box.comp, logic = box.logic
                    };
                }
                parents[i] = new L2FlagBoxParent { BOX = boxes, logoc = src.logoc };
            }
            return parents;
        }

        // Copy the original gate, swapping every box that reads a registered sheet-20 book flag
        // for an always-true constant. Keeping the array shape (and each box's LOGIC) means any
        // sibling condition still decides when the chip may appear.
        private static L2FlagBoxParent[] WithoutBookGate(L2FlagBoxParent[] original, LocationID loc)
        {
            if (original == null || original.Length == 0) return original;

            var parents = new L2FlagBoxParent[original.Length];
            for (int i = 0; i < original.Length; i++)
            {
                var src = original[i];
                if (src == null || src.BOX == null) { parents[i] = src; continue; }

                var boxes = new L2FlagBox[src.BOX.Length];
                for (int j = 0; j < src.BOX.Length; j++)
                {
                    var box = src.BOX[j];
                    LocationID boxLoc;
                    bool isBookGate = box != null
                        && box.seet_no1 == BookSheet
                        && GlossaryManager.TryGetLocation(box.flag_no1, out boxLoc)
                        && boxLoc == loc;

                    boxes[j] = isBookGate ? Constant(true, box.logic) : box;
                }
                parents[i] = new L2FlagBoxParent { BOX = boxes, logoc = src.logoc };
            }
            return parents;
        }
    }

    [HarmonyPatch(typeof(AbstractItemBase), "groundInit")]
    internal static class GlossaryChipGroundInitGatePatch
    {
        static void Prefix(AbstractItemBase __instance) => GlossaryChipActiveGate.Apply(__instance);
    }

    [HarmonyPatch(typeof(AbstractItemBase), "resetActionCharacter")]
    internal static class GlossaryChipResetGatePatch
    {
        static void Prefix(AbstractItemBase __instance) => GlossaryChipActiveGate.Apply(__instance);
    }
}
