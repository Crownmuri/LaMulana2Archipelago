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
        // instanceId → glossary ROM game_id, so the floor sprite can pick the N/R/SR/UR tier.
        private static readonly Dictionary<int, int> _romGameId = new Dictionary<int, int>();
        // instanceId → own Guardian Soul game_id (shuffle_guardian_souls), for its portrait.
        private static readonly Dictionary<int, int> _soulGameId = new Dictionary<int, int>();

        static void Postfix(AbstractItemBase __instance)
        {
            try
            {
                if ((!GlossaryManager.Enabled && !GuardianSoulManager.Enabled) || __instance == null) return;
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
                        gloss = GlossaryManager.Enabled && GlossaryManager.IsOwnGlossaryRom(sc);
                        if (gloss) _romGameId[id] = GlossaryManager.RomGameId(sc.ItemId);
                        else if (GuardianSoulManager.TryGetOwnSoulAt(loc, out int soulId))
                        {
                            _soulGameId[id] = soulId;
                            gloss = true; // "has a custom floor sprite"
                        }
                    }
                    _isGlossary[id] = gloss;
                    if (!gloss) { _skip.Add(id); return; } // resolved as neither → stop checking
                }
                if (!gloss) return;

                Sprite chip;
                if (_soulGameId.TryGetValue(id, out int soul))
                    chip = GuardianSoulSprite.FloorIcon(soul);
                else
                    chip = GlossaryChipSprite.FloorIcon(_romGameId.TryGetValue(id, out int gid) ? gid : -1);
                if (chip == null) return;

                var sr = __instance.GetComponent<SpriteRenderer>()
                         ?? __instance.GetComponentInChildren<SpriteRenderer>(true);
                if (sr != null) sr.sprite = chip;
            }
            catch { }
        }
    }
}
