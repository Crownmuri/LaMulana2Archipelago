using System.Collections.Generic;
using HarmonyLib;
using LaMulana2Archipelago.Archipelago;
using LaMulana2Archipelago.Managers;
using LaMulana2RandomizerShared;
using LM2RandomiserMod;
using UnityEngine;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Glossanity floor sprite. Registered glossary chips (MonsterChipScript) keep
    /// their tiny chip graphic on the ground even though they now hold an AP-placed
    /// item. This swaps the in-world sprite to the placed item's icon (own item) or
    /// the AP placeholder icon (AP / foreign / filler), while preserving the chip's
    /// small footprint by scaling the new sprite to the chip's original rendered
    /// height (chips render ~1/4 the size of a regular item).
    ///
    /// Done once per object (chips don't re-randomize their sprite), via a Postfix
    /// on groundFirst which runs each frame while the chip sits on the ground.
    /// </summary>
    [HarmonyPatch(typeof(MonsterChipScript), "groundFirst")]
    internal static class MonsterChipSpritePatch
    {
        // The chip world sprite and the item map-icon share the same ~20u bounds,
        // but the chip artwork only fills ~1/4 of it, so a 1:1 swap renders ~4× too
        // big. Render the icon at this fraction to match the chip's on-screen size.
        // Tune if needed.
        private const float IconScale = 0.5f;

        private struct Style { public Sprite sprite; public Vector3 scale; }

        // Precomputed style per object; reapplied every frame so it survives any
        // scale/sprite reset the chip does. _skip = known non-glossary instances.
        private static readonly Dictionary<int, Style> _cache = new Dictionary<int, Style>();
        private static readonly HashSet<int> _skip = new HashSet<int>();
        // instanceId → LocationID for identified glossary chips, so we can hide a chip
        // once its location is checked (decoupled model never sets the chip's book flag,
        // so vanilla would otherwise keep re-showing it on every room re-entry).
        private static readonly Dictionary<int, LocationID> _glossaryLoc = new Dictionary<int, LocationID>();

        public static void Clear() { _cache.Clear(); _skip.Clear(); _glossaryLoc.Clear(); }

        private static bool IsChecked(LocationID loc) => GlossaryManager.IsLocationCollected(loc);

        static void Postfix(MonsterChipScript __instance)
        {
            if (!GlossaryManager.Enabled) return;

            int instanceId = __instance.GetInstanceID();

            var sr = __instance.GetComponent<SpriteRenderer>();
            if (sr == null) return;

            // Known glossary chip whose location is already checked → keep it hidden
            // (we never set its book flag, so the engine still thinks it's uncollected).
            if (_glossaryLoc.TryGetValue(instanceId, out LocationID knownLoc))
            {
                if (IsChecked(knownLoc)) { sr.enabled = false; return; }
                if (!sr.enabled) sr.enabled = true;
            }

            // Already computed → force sprite + scale every frame (overrides resets).
            if (_cache.TryGetValue(instanceId, out Style cached))
            {
                if (cached.sprite != null) sr.sprite = cached.sprite;
                __instance.transform.localScale = cached.scale;
                return;
            }
            if (_skip.Contains(instanceId)) return;

            // Covers BOTH freestanding glossary chips AND dynamic enemy drops (dropItem):
            // both carry a chipId/itemValue book flag and are now fully AP-handled
            // (MonsterChipGlossaryPatch + MonsterChipDropGatePatch), so a drop whose
            // location holds a non-glossary item should show that item's icon at the
            // chip's small footprint too — not the vanilla cartridge graphic. Chips that
            // aren't registered glossary entries fall through to _skip below.
            int chipId = Traverse.Create(__instance).Field("chipId").GetValue<int>();
            int bookFlag = chipId > -1
                ? chipId
                : Traverse.Create(__instance).Field("itemValue").GetValue<int>();

            if (!GlossaryManager.TryGetLocation(bookFlag, out LocationID locId))
            {
                _skip.Add(instanceId);
                return;
            }

            // Remember this is a glossary chip so we can hide it once checked.
            _glossaryLoc[instanceId] = locId;
            if (IsChecked(locId)) { sr.enabled = false; return; }

            if (sr.sprite == null) return; // chip sprite not ready yet — wait

            // Cache the real chip sprite so shops/dialog/non-chip freestanding can reuse it.
            if (GlossaryChipSprite.LiveChip == null) GlossaryChipSprite.LiveChip = sr.sprite;

            Sprite target = ResolveFloorSprite(locId);
            if (target == null) { _skip.Add(instanceId); return; }

            Vector3 origScale = __instance.transform.localScale;
            Vector3 newScale = new Vector3(origScale.x * IconScale, origScale.y * IconScale, origScale.z);

            sr.sprite = target;
            __instance.transform.localScale = newScale;
            _cache[instanceId] = new Style { sprite = target, scale = newScale };
        }

        private static Sprite ResolveFloorSprite(LocationID locId)
        {
            var scouted = ArchipelagoClientProvider.Client?.GetItemAtLocation(430000L + (int)locId);

            // Another glossary ROM (own OR another player's) → keep the chip's native
            // cartridge sprite (it IS a glossary ROM). null tells the caller not to swap.
            if (scouted != null && GlossaryManager.IsGlossaryRomId(scouted.ItemId))
            {
                return null;
            }

            // Filler (own coins/weights/ammo) → Shell Horn icon, matching the
            // freestanding filler placeholder.
            if (scouted != null && scouted.IsOwnItem && scouted.ItemName != null
                && ItemPotPatch.TryParseReward(scouted.ItemName, out _, out _))
            {
                return ShellHornSprite();
            }

            // Own real LM item (below the filler/weight range): show its icon.
            var sr = SceneRandomizer.Instance;
            ItemID placed = sr != null ? sr.GetItemIDForLocation(locId) : ItemID.None;
            // Own glossary ROM with no scout (offline) → also keep the chip sprite.
            if ((int)placed >= 2000 && (int)placed <= 2251)
                return null;
            if (placed != ItemID.None && (int)placed < (int)ItemID.ChestWeight01)
            {
                ItemInfo info = ItemDB.GetItemInfo(placed);
                if (info != null && !string.IsNullOrEmpty(info.BoxName))
                {
                    // GetItemSprite resolves progressive Whip/Shield to the player's
                    // current (resulting) tier via the count flag, instead of the fixed
                    // placed instance — otherwise the floor icon sticks on whatever
                    // Shield1/2/3 the seed placed here rather than the tier you'll get.
                    var sprite = sr.GetItemSprite(info.BoxName, placed);
                    if (sprite != null)
                        return sprite;
                }
            }

            // AP / foreign → AP placeholder icon (progression/trap-aware, matching
            // every other floor call-site — otherwise a foreign progression item
            // dropped on a chip location shows the plain icon while its dialog is right).
            if (ApSpriteLoader.IsLoaded)
                return ApSpriteLoader.GetMapSprite(CheckManager.GetApIconClassAt(locId));

            return ShellHornSprite();
        }

        private static Sprite ShellHornSprite()
        {
            var data = L2SystemCore.getItemData("Shell Horn") ?? L2SystemCore.getItemData("ShellHorn");
            return data != null ? L2SystemCore.getMapIconSprite(data) : null;
        }
    }
}
