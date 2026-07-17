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

        private struct Entry
        {
            public LocationID Loc;
            public L2FlagBoxParent[] Original;
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

                    entry = new Entry { Loc = loc, Original = item.itemActiveFlag };
                    _known[id] = entry;
                }

                if (GlossaryManager.IsLocationCollected(entry.Loc))
                {
                    item.itemActiveFlag = new[]
                    {
                        new L2FlagBoxParent { BOX = new[] { Constant(false, LOGIC.NON) }, logoc = LOGIC.NON }
                    };
                    return;
                }

                item.itemActiveFlag = WithoutBookGate(entry.Original, entry.Loc);
            }
            catch { }
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
