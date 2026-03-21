using HarmonyLib;
using L2Base;
using L2Flag;
using LaMulana2Archipelago.Archipelago;
using LaMulana2Archipelago.Managers;
using LaMulana2RandomizerShared;
using LM2RandomiserMod;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Audio;

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

    // ══════════════════════════════════════════════════════════════════
    //  Manual SFX: play chest unlock/open sounds via seManager
    //  since non-blue prefabs have broken audio components.
    // ══════════════════════════════════════════════════════════════════

    [HarmonyPatch(typeof(TreasureBoxScript), nameof(TreasureBoxScript.groundFirst))]
    internal static class ChestSfxPatch
    {
        static void Prefix(TreasureBoxScript __instance, out int __state)
        {
            __state = Traverse.Create(__instance).Field("sta").GetValue<int>();
        }
        private static AudioMixerGroup _cachedSfxGroup;

        private static AudioMixerGroup GetSfxMixerGroup()
        {
            if (_cachedSfxGroup != null)
                return _cachedSfxGroup;

            var sources = UnityEngine.Object.FindObjectsOfType<AudioSource>();

            foreach (var src in sources)
            {
                if (src.outputAudioMixerGroup != null &&
                    src.outputAudioMixerGroup.name == "EnvironmentalVolume")
                {
                    _cachedSfxGroup = src.outputAudioMixerGroup;
                    Plugin.Log.LogDebug($"[CHEST] Cached SFX mixer group: {_cachedSfxGroup.name}");
                    return _cachedSfxGroup;
                }
            }

            Plugin.Log.LogWarning("[CHEST] Could not find EnvironmentalVolume mixer group!");
            return null;
        }
        static void Postfix(TreasureBoxScript __instance, int __state)
        {
            try
            {
                if (!__instance.gameObject.activeInHierarchy) return;

                if (__instance.itemObj == null) return;
                var item = __instance.itemObj.GetComponent<AbstractItemBase>();
                if (item == null || string.IsNullOrEmpty(item.itemLabel)) return;

                bool isAp = item.itemLabel.StartsWith("AP Item", StringComparison.Ordinal);
                bool isFiller = item.itemLabel.StartsWith("Weight") || item.itemLabel.StartsWith("Coin");

                if (!isAp && !isFiller) return;

                int newSta = Traverse.Create(__instance).Field("sta").GetValue<int>();
                bool unlocked = (__state == 0) && (newSta == 1 || newSta == 2);
                bool opened = (__state == 2 || __state == 101) && (newSta == 3 || newSta == 7);

                if (!unlocked && !opened) return;

                string seName = unlocked ? "SE116TresureUnlock" : "SE117TresureOpen";

                Transform audioNode = __instance.transform.Find(seName);
                if (audioNode == null)
                {
                    audioNode = __instance.transform.FindRecursive(seName);
                }

                if (audioNode != null)
                {
                    audioNode.gameObject.SetActive(true);
                    var audioSrc = audioNode.GetComponent<AudioSource>();

                    if (audioSrc != null)
                    {
                        audioSrc.enabled = true;
                        audioSrc.mute = false;
                        audioSrc.playOnAwake = false;

                        var mixer = GetSfxMixerGroup();
                        if (mixer != null)
                        {
                            audioSrc.outputAudioMixerGroup = mixer;
                        }
                        audioSrc.spatialBlend = 0f; // optional but recommended
                        audioSrc.PlayOneShot(audioSrc.clip);

                        Plugin.Log.LogDebug($"[CHEST] Force-played {seName} clip directly on {__instance.name}");
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[CHEST SFX ERROR] {ex}");
            }
        }
    }
    public static class TransformExtensions
    {
        public static Transform FindRecursive(this Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name) return child;
                Transform result = child.FindRecursive(name);
                if (result != null) return result;
            }
            return null;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  Shared helper: re-create AP-item chests with the correct color
    // ══════════════════════════════════════════════════════════════════

    internal static class ApChestRecolorHelper
    {
        private static MethodInfo _createChest;

        public static bool IsApItemChest(TreasureBoxScript chest)
        {
            if (chest == null || chest.itemObj == null) return false;

            var item = chest.itemObj.GetComponent<AbstractItemBase>();
            if (item == null) return false;

            return !string.IsNullOrEmpty(item.itemLabel)
                && item.itemLabel.StartsWith("AP Item", StringComparison.Ordinal);
        }

        public static TreasureBoxScript RecolorSingleChest(
            L2Rando rando,
            TreasureBoxScript old,
            int apColor)
        {
            if (_createChest == null)
            {
                _createChest = typeof(L2Rando).GetMethod(
                    "CreateChest",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    new Type[] { typeof(int), typeof(Vector3), typeof(Quaternion) },
                    null);
            }

            if (_createChest == null)
            {
                Plugin.Log.LogWarning("[ApChestColor] CreateChest method not found.");
                return null;
            }

            var newChest = _createChest.Invoke(
                rando,
                new object[] { apColor, old.transform.position, old.transform.rotation })
                as TreasureBoxScript;

            if (newChest == null)
            {
                Plugin.Log.LogWarning($"[ApChestColor] CreateChest returned null for color {apColor}");
                return null;
            }

            // ── Transfer gameplay properties ──
            newChest.curseMode = old.curseMode;
            newChest.curseAnime = old.curseAnime;
            newChest.curseParticle = old.curseParticle;
            newChest.closetMode = old.closetMode;
            newChest.costumeId = old.costumeId;
            newChest.forceOpenFlags = old.forceOpenFlags;
            newChest.itemFlags = old.itemFlags;
            newChest.openActionFlags = old.openActionFlags;
            newChest.openFlags = old.openFlags;
            newChest.unlockFlags = old.unlockFlags;
            newChest.itemObj = old.itemObj;

            // --- Animation State Strings ---
            newChest.closeState = old.closeState;
            newChest.unlockState = old.unlockState;
            newChest.openState = old.openState;
            newChest.unlockAnimeState = old.unlockAnimeState;
            newChest.lockAnimeState = old.lockAnimeState;
            newChest.openAnimeState = old.openAnimeState;
            newChest.closeAnimeState = old.closeAnimeState;

            // --- Physical Hitbox & Fit ---
            newChest.boxWidth = old.boxWidth;
            newChest.boxHeight = old.boxHeight;
            newChest.fit = old.fit;

            // Reparent curse effect if it was a child of the old chest
            if (old.curseMode && old.curseAnime != null
                && old.curseAnime.transform.IsChildOf(old.transform))
            {
                old.curseAnime.transform.SetParent(newChest.transform);
            }

            newChest.transform.SetParent(old.transform.parent);
            newChest.gameObject.SetActive(true);

            // Force the chest to re-evaluate initial state with copied flags
            Traverse.Create(newChest).Field("firstReset").SetValue(true);

            Plugin.Log.LogDebug($"[ApChestColor] Replaced chest at {old.transform.position}");

            // hide old chest
            Traverse.Create(old).Field("sta").SetValue(7);       // opened/done state — no interactions
            old.itemObj = null;                                    // prevent duplicate item drops
            old.gameObject.SetActive(false);                       // invisible, no rendering/physics

            return newChest;
        }

        public static void RecolorApChests(
            L2Rando rando,
            HashSet<GameObject> skipObjects = null,
            HashSet<GameObject> onlyObjects = null)
        {
            int apColor = ArchipelagoClient.ServerData.GetSlotInt("ap_color_chest", 1);

            var allChests = UnityEngine.Object.FindObjectsOfType<TreasureBoxScript>();
            int recolored = 0;

            foreach (var chest in allChests)
            {
                if (skipObjects != null && skipObjects.Contains(chest.gameObject))
                    continue;

                if (onlyObjects != null && !onlyObjects.Contains(chest.gameObject))
                    continue;

                if (!IsApItemChest(chest))
                    continue;

                if (RecolorSingleChest(rando, chest, apColor) != null)
                    recolored++;
            }

            if (recolored > 0)
                Plugin.Log.LogInfo($"[ApChestColor] Recolored {recolored} AP chest(s) to color {apColor}");
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  Recolor AP chests after L2Rando.ChangeTreasureChests
    // ══════════════════════════════════════════════════════════════════

    [HarmonyPatch(typeof(L2Rando), "ChangeTreasureChests")]
    internal static class ApChestColorPatch
    {
        static void Postfix(L2Rando __instance, List<GameObject> __result)
        {
            try
            {
                HashSet<GameObject> skipSet = null;
                if (__result != null && __result.Count > 0)
                {
                    skipSet = new HashSet<GameObject>();
                    foreach (var go in __result)
                        skipSet.Add(go);
                }

                ApChestRecolorHelper.RecolorApChests(__instance, skipObjects: skipSet);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[ApChestColor] Exception in ChangeTreasureChests postfix: " + ex);
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  Recolor AP chests after L2Rando.DissonanceChests
    // ══════════════════════════════════════════════════════════════════

    [HarmonyPatch(typeof(L2Rando), "DissonanceChests")]
    internal static class ApDissonanceChestColorPatch
    {
        private static HashSet<GameObject> _beforeChests;

        static void Prefix()
        {
            var all = UnityEngine.Object.FindObjectsOfType<TreasureBoxScript>();
            _beforeChests = new HashSet<GameObject>();
            foreach (var c in all)
                _beforeChests.Add(c.gameObject);
        }

        static void Postfix(L2Rando __instance)
        {
            try
            {
                if (_beforeChests == null) return;

                var afterChests = UnityEngine.Object.FindObjectsOfType<TreasureBoxScript>();
                var newlyCreated = new HashSet<GameObject>();

                foreach (var c in afterChests)
                {
                    if (!_beforeChests.Contains(c.gameObject))
                    {
                        var chest = c.GetComponent<TreasureBoxScript>();

                        // STRIP THE BEHERIT REQUIREMENT
                        if (chest != null && chest.unlockFlags != null && chest.unlockFlags.Length > 0)
                        {
                            var parent = chest.unlockFlags[0];
                            if (parent != null && parent.BOX != null)
                            {
                                var newBoxes = new System.Collections.Generic.List<L2FlagBox>();
                                foreach (var box in parent.BOX)
                                {
                                    // Skip the Beherit check (Sheet 2, Flag 3)
                                    if (box.seet_no1 == 2 && box.flag_no1 == 3) continue;
                                    newBoxes.Add(box);
                                }
                                parent.BOX = newBoxes.ToArray();
                            }
                        }
                        newlyCreated.Add(c.gameObject);
                    }
                }

                if (newlyCreated.Count == 0) return;

                Plugin.Log.LogDebug($"[ApDissonanceColor] DissonanceChests created " +
                    $"{newlyCreated.Count} new chest(s), checking for AP items.");

                ApChestRecolorHelper.RecolorApChests(
                    __instance,
                    onlyObjects: newlyCreated);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[ApDissonanceColor] Exception: " + ex);
            }
            finally
            {
                _beforeChests = null;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  GetItemSprite: fall back to Holy Grail for AP items
    // ══════════════════════════════════════════════════════════════════

    [HarmonyPatch(typeof(L2Rando), "GetItemSprite")]
    internal static class GetItemSpriteFallbackPatch
    {
        static bool Prefix(ref Sprite __result, string itemName, ItemID itemID)
        {
            try
            {
                bool isApName =
                    !string.IsNullOrEmpty(itemName) &&
                    itemName.StartsWith("AP Item", StringComparison.Ordinal);

                bool isApPlaceholder = ApItemIDs.IsApPlaceholder((int)itemID);

                if (!isApName && !isApPlaceholder)
                    return true;

                var grailData = L2SystemCore.getItemData("Holy Grail");
                if (grailData != null)
                    __result = L2SystemCore.getMapIconSprite(grailData);

                Plugin.Log.LogInfo(
                    "[GetItemSprite] AP fallback for '" + itemName + "' (ID=" + itemID + ").");

                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[GetItemSprite] Prefix error: " + ex);
                return true;
            }
        }
    }
}