using HarmonyLib;
using L2Base;
using LaMulana2Archipelago.Archipelago;
using LM2RandomiserMod;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace LaMulana2Archipelago.Patches
{
    // ── Chest item icon: swap the drop sprite to Holy Grail icon for AP items ──

    [HarmonyPatch(typeof(AbstractItemBase), "setTreasureBoxOut")]
    internal static class TreasureBoxSpritePatch
    {
        static void Postfix(AbstractItemBase __instance)
        {
            try
            {
                if (__instance == null) return;

                if (string.IsNullOrEmpty(__instance.itemLabel) ||
                    !__instance.itemLabel.StartsWith("AP Item", StringComparison.Ordinal))
                    return;

                var sr = __instance.GetComponent<SpriteRenderer>();
                if (sr == null) sr = __instance.GetComponentInChildren<SpriteRenderer>(true);
                if (sr == null) return;

                var grailData = L2SystemCore.getItemData("Holy Grail");
                if (grailData == null) return;

                sr.sprite = L2SystemCore.getMapIconSprite(grailData);
            }
            catch { }
        }
    }

    // ── Chest box color: re-create AP chests with the ap_chest_color slot value ──

    [HarmonyPatch(typeof(L2Rando), "ChangeTreasureChests")]
    internal static class ApChestColorPatch
    {
        static void Postfix(L2Rando __instance)
        {
            try
            {
                int apColor = ArchipelagoClient.ServerData.GetSlotInt("ap_chest_color", 2);

                MethodInfo createChest = typeof(L2Rando).GetMethod(
                    "CreateChest",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    new Type[] { typeof(int), typeof(Vector3), typeof(Quaternion) },
                    null);

                if (createChest == null)
                {
                    Plugin.Log.LogWarning("[ApChestColor] CreateChest not found — skipped.");
                    return;
                }

                var allChests = UnityEngine.Object.FindObjectsOfType<TreasureBoxScript>();
                var toRecolor = new List<TreasureBoxScript>();

                foreach (var chest in allChests)
                {
                    if (IsApItemChest(chest))
                        toRecolor.Add(chest);
                }

                Plugin.Log.LogInfo("[ApChestColor] Recoloring " + toRecolor.Count + " AP chest(s) to color " + apColor);

                foreach (var old in toRecolor)
                {
                    var newChest = createChest.Invoke(
                        __instance,
                        new object[] { apColor, old.transform.position, old.transform.rotation })
                        as TreasureBoxScript;

                    if (newChest == null) continue;

                    newChest.curseMode = old.curseMode;
                    newChest.curseAnime = old.curseAnime;
                    newChest.curseParticle = old.curseParticle;
                    newChest.closetMode = old.closetMode;
                    newChest.forceOpenFlags = old.forceOpenFlags;
                    newChest.itemFlags = old.itemFlags;
                    newChest.openActionFlags = old.openActionFlags;
                    newChest.openFlags = old.openFlags;
                    newChest.unlockFlags = old.unlockFlags;
                    newChest.itemObj = old.itemObj;
                    newChest.transform.SetParent(old.transform.parent);
                    newChest.gameObject.SetActive(true);

                    UnityEngine.Object.Destroy(old.gameObject);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[ApChestColor] Exception: " + ex);
            }
        }

        private static bool IsApItemChest(TreasureBoxScript chest)
        {
            if (chest.itemObj == null) return false;
            var item = chest.itemObj.GetComponent<AbstractItemBase>();
            if (item == null) return false;
            return !string.IsNullOrEmpty(item.itemLabel)
                && item.itemLabel.StartsWith("AP Item", StringComparison.Ordinal);
        }
    }
}