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

        public static void Clear() { _cache.Clear(); _skip.Clear(); }

        static void Postfix(MonsterChipScript __instance)
        {
            if (!GlossaryManager.Enabled) return;

            int instanceId = __instance.GetInstanceID();

            var sr = __instance.GetComponent<SpriteRenderer>();
            if (sr == null) return;

            // Already computed → force sprite + scale every frame (overrides resets).
            if (_cache.TryGetValue(instanceId, out Style cached))
            {
                if (cached.sprite != null) sr.sprite = cached.sprite;
                __instance.transform.localScale = cached.scale;
                return;
            }
            if (_skip.Contains(instanceId)) return;

            // Only freestanding glossary chips; leave dynamic enemy drops alone.
            bool dropItem = Traverse.Create(__instance).Field("dropItem").GetValue<bool>();
            if (dropItem) { _skip.Add(instanceId); return; }

            int chipId = Traverse.Create(__instance).Field("chipId").GetValue<int>();
            int bookFlag = chipId > -1
                ? chipId
                : Traverse.Create(__instance).Field("itemValue").GetValue<int>();

            if (!GlossaryManager.TryGetLocation(bookFlag, out LocationID locId))
            {
                _skip.Add(instanceId);
                return;
            }

            if (sr.sprite == null) return; // chip sprite not ready yet — wait

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
            // Filler (own coins/weights/ammo) → Shell Horn icon, matching the
            // freestanding filler placeholder.
            var scouted = ArchipelagoClientProvider.Client?.GetItemAtLocation(430000L + (int)locId);
            if (scouted != null && scouted.IsOwnItem && scouted.ItemName != null
                && ItemPotPatch.TryParseReward(scouted.ItemName, out _, out _))
            {
                return ShellHornSprite();
            }

            // Own real LM item (below the filler/weight range): show its icon.
            var sr = SceneRandomizer.Instance;
            ItemID placed = sr != null ? sr.GetItemIDForLocation(locId) : ItemID.None;
            if (placed != ItemID.None && (int)placed < (int)ItemID.ChestWeight01)
            {
                ItemInfo info = ItemDB.GetItemInfo(placed);
                if (info != null && !string.IsNullOrEmpty(info.BoxName))
                {
                    var data = L2SystemCore.getItemData(info.BoxName);
                    if (data != null)
                        return L2SystemCore.getMapIconSprite(data);
                }
            }

            // AP / foreign → AP placeholder icon.
            if (ApSpriteLoader.IsLoaded)
                return ApSpriteLoader.GetMapSprite(false);

            return ShellHornSprite();
        }

        private static Sprite ShellHornSprite()
        {
            var data = L2SystemCore.getItemData("Shell Horn") ?? L2SystemCore.getItemData("ShellHorn");
            return data != null ? L2SystemCore.getMapIconSprite(data) : null;
        }
    }
}
