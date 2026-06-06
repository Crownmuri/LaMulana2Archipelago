using HarmonyLib;
using L2Base;
using L2Flag;
using LaMulana2Archipelago.Archipelago;
using LaMulana2Archipelago.Managers;
using LaMulana2RandomizerShared;
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

                if (ApSpriteLoader.IsLoaded)
                {
                    // This postfix also fires for pot pickups (TrySpawnItemPickup
                    // calls setTreasureBoxOut), so resolve the item's world flag to
                    // its AP location instead of assuming a chest's sheet 31. Both
                    // chest (sheet 31) and pot (pot sheet) flags are registered in
                    // LocationFlagMap, so progression items show the "up arrow" icon
                    // for either source.
                    bool isProgression = TryGetApLocation(__instance, out LocationID location)
                        && CheckManager.IsApItemProgressionAt(location);

                    sr.sprite = ApSpriteLoader.GetMapSprite(isProgression);
                }
                else
                {
                    var grailData = L2SystemCore.getItemData("Holy Grail");
                    if (grailData == null) return;
                    sr.sprite = L2SystemCore.getMapIconSprite(grailData);
                }
            }
            catch { }
        }

        // The item's itemActiveFlag carries the (sheet, flag) pair that identifies
        // its AP location — sheet 31 for chests, the pot sheet for pot pickups.
        // Return the first box that LocationFlagMap can resolve to a location.
        static bool TryGetApLocation(AbstractItemBase item, out LocationID location)
        {
            location = LocationID.None;

            var active = item.itemActiveFlag;
            if (active == null) return false;

            foreach (var parent in active)
            {
                if (parent == null || parent.BOX == null) continue;

                foreach (var box in parent.BOX)
                {
                    if (box == null) continue;

                    if (LocationFlagMap.TryGetNumeric(box.seet_no1, box.flag_no1, out location))
                        return true;
                }
            }

            return false;
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
}
