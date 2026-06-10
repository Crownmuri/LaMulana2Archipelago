using System.Collections.Generic;
using HarmonyLib;
using L2Base;
using LaMulana2Archipelago.Archipelago;
using LaMulana2Archipelago.Managers;
using LaMulana2RandomizerShared;
using UnityEngine;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Floor sprite for glossary ROMs at non-chip freestanding EventItem locations.
    ///
    /// FreeStandingSpritePatch only covers FakeItem filler (Shell Horn), and TreasureBoxSpritePatch
    /// only fires for chests (setTreasureBoxOut). A glossary ROM placed at a regular freestanding
    /// EventItem therefore shows the AP icon. AbstractItemBase.groundFirst runs every frame while
    /// the item sits on the ground (the same hook MonsterChipSpritePatch uses for chips), so we
    /// force the Shell Horn floor sprite there for own glossary ROMs (matching filler). Chips are
    /// handled by MonsterChipSpritePatch.
    /// </summary>
    [HarmonyPatch(typeof(AbstractItemBase), "groundFirst")]
    internal static class FreeStandingGlossarySpritePatch
    {
        private static readonly HashSet<int> _skip = new HashSet<int>();
        private static readonly Dictionary<int, bool> _isGlossary = new Dictionary<int, bool>();

        static void Postfix(AbstractItemBase __instance)
        {
            try
            {
                if (!GlossaryManager.Enabled || __instance == null) return;
                if (__instance is MonsterChipScript) return; // handled by MonsterChipSpritePatch

                int id = __instance.GetInstanceID();
                if (_skip.Contains(id)) return;

                string label = __instance.itemLabel;
                if (string.IsNullOrEmpty(label) || !label.StartsWith("AP Item", System.StringComparison.Ordinal))
                    return; // not an AP placeholder (label can change, so don't permanently skip)

                bool gloss;
                if (!_isGlossary.TryGetValue(id, out gloss))
                {
                    gloss = false;
                    if (TreasureBoxSpritePatch.TryGetApLocation(__instance, out LocationID loc))
                    {
                        var sc = ArchipelagoClientProvider.Client?.GetItemAtLocation(430000L + (int)loc);
                        gloss = GlossaryManager.IsOwnGlossaryRom(sc);
                    }
                    _isGlossary[id] = gloss;
                    if (!gloss) { _skip.Add(id); return; } // resolved as non-glossary → stop checking
                }
                if (!gloss) return;

                var chip = GlossaryChipSprite.FloorIcon();
                if (chip == null) return;

                var sr = __instance.GetComponent<SpriteRenderer>()
                         ?? __instance.GetComponentInChildren<SpriteRenderer>(true);
                if (sr != null) sr.sprite = chip;
            }
            catch { }
        }
    }
}
