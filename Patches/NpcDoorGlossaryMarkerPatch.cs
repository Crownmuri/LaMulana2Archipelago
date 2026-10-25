using System;
using System.Collections.Generic;
using HarmonyLib;
using LaMulana2Archipelago.Managers;
using LaMulana2RandomizerShared;
using UnityEngine;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Glossanity: shows a small AP logo inside NPC/shop doors (ShopGateScript)
    /// whose glossary chip location hasn't been checked yet.
    ///
    /// NPC glossary entries are ordinary MonsterChipScript chips ("chipN-Talk xx-yy")
    /// that appear after you talk to the NPC / enter the shop. They're placed on
    /// the door itself: scanning every level file, 76 of the 83 talk chips sit
    /// within 4 units of a ShopGateScript and 3 more within 7 (BTK: chip
    /// (-1352,623), ShopGate (-1352,624)). So doors and chips are paired purely by
    /// position, PairRadius below. The 4 talk chips not near any door (Sakit's two
    /// 14-15 copies, 14-12, 03-13) get no marker.
    ///
    /// The logo is a child of the door, so it follows the door's own activation
    /// (the door's shadow switches it off while the NPC isn't there). Overlapping
    /// doors (Hiner's talk/shop/next-shop, BTK's two shop sheets) each get a
    /// marker, but only the active one shows.
    /// </summary>
    [HarmonyPatch(typeof(ShopGateScript), "Start")]
    internal static class NpcDoorGlossaryMarkerPatch
    {
        /// <summary>Set from the BepInEx config (Gameplay/MarkGlossaryNpcDoors).</summary>
        public static bool Enabled = true;

        [HarmonyPostfix]
        static void Postfix(ShopGateScript __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                var existing = __instance.GetComponent<NpcDoorGlossaryMarker>();
                if (existing != null)
                {
                    if (existing.Gate == __instance)
                        return;
                    // Instantiated copy of a marked door (e.g. the Start Shop is cloned
                    // from a field's ShopGate): drop the inherited marker and logo.
                    UnityEngine.Object.DestroyImmediate(existing);
                    Transform oldIcon = __instance.transform.Find(NpcDoorGlossaryMarker.IconName);
                    if (oldIcon != null)
                        UnityEngine.Object.Destroy(oldIcon.gameObject);
                }
                __instance.gameObject.AddComponent<NpcDoorGlossaryMarker>().Setup(__instance);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[GLOSS] Door marker attach failed: {ex.Message}");
            }
        }
    }

    internal class NpcDoorGlossaryMarker : MonoBehaviour
    {
        private const float PairRadius = 8f;     // chip ↔ door distance (world units)
        private const float IconSize = 8f;       // world units tall, same as the enemy marker
        private const float IconZ = 1f;          // synthetic world-sprite convention
        private const int RecheckInterval = 10;  // frames

        private ShopGateScript _gate;
        private List<LocationID> _locations;     // null until paired; empty = no chips here
        private GameObject _icon;
        private SpriteRenderer _iconRenderer;
        private int _nextCheckFrame;

        internal const string IconName = "AP_GlossaryDoorMarker";

        internal ShopGateScript Gate => _gate;

        public void Setup(ShopGateScript gate)
        {
            _gate = gate;
        }

        private void LateUpdate()
        {
            if (_gate == null || Time.frameCount < _nextCheckFrame)
                return;
            _nextCheckFrame = Time.frameCount + RecheckInterval;

            if (!GlossaryManager.Enabled)
            {
                SetShown(false);
                return;
            }

            if (_locations == null)
                Pair();
            if (_locations.Count == 0)
            {
                enabled = false; // no glossary chip at this door
                return;
            }

            bool show = false;
            if (NpcDoorGlossaryMarkerPatch.Enabled)
            {
                for (int i = 0; i < _locations.Count && !show; i++)
                    show = !GlossaryManager.IsLocationCollected(_locations[i]);
            }
            SetShown(show && EnsureIcon());
        }

        private void SetShown(bool show)
        {
            if (_iconRenderer != null && _iconRenderer.enabled != show)
                _iconRenderer.enabled = show;
        }

        /// <summary>
        /// Collects the registered glossary chips (including inactive ones, which
        /// is how un-triggered talk chips sit) within PairRadius of this door.
        /// </summary>
        private void Pair()
        {
            _locations = new List<LocationID>();
            Vector2 door = _gate.transform.position;
            var scene = _gate.gameObject.scene;

            foreach (MonsterChipScript chip in ChipsInScene(scene))
            {
                if (chip == null)
                    continue;
                if (Vector2.Distance(door, chip.transform.position) > PairRadius)
                    continue;
                if (!GlossaryChipActiveGate.TryResolve(chip, out LocationID loc) || _locations.Contains(loc))
                    continue;
                _locations.Add(loc);
                Plugin.Log.LogDebug($"[GLOSS] Door '{_gate.name}' ({_gate.sheetName}) at {door} ↔ chip '{chip.name}' → {loc}");
            }
        }

        // Resources.FindObjectsOfTypeAll is slow and every door in the scene asks
        // in the same frames, so share one result per scene per frame.
        private static int _chipCacheFrame = -1;
        private static readonly Dictionary<int, List<MonsterChipScript>> _chipCache = new Dictionary<int, List<MonsterChipScript>>();

        private static List<MonsterChipScript> ChipsInScene(UnityEngine.SceneManagement.Scene scene)
        {
            if (_chipCacheFrame != Time.frameCount)
            {
                _chipCacheFrame = Time.frameCount;
                _chipCache.Clear();
                foreach (MonsterChipScript chip in Resources.FindObjectsOfTypeAll<MonsterChipScript>())
                {
                    if (chip == null || !chip.gameObject.scene.IsValid())
                        continue; // prefabs / assets
                    int handle = chip.gameObject.scene.handle;
                    if (!_chipCache.TryGetValue(handle, out var list))
                        _chipCache[handle] = list = new List<MonsterChipScript>();
                    list.Add(chip);
                }
            }
            return _chipCache.TryGetValue(scene.handle, out var found) ? found : new List<MonsterChipScript>();
        }

        private bool EnsureIcon()
        {
            if (_icon != null)
                return true;
            Sprite sprite = ApSpriteLoader.MarkerSprite ?? ApSpriteLoader.MapSprite;
            if (sprite == null)
                return false;

            // Child of the door so it disappears whenever the door is switched off.
            _icon = new GameObject(IconName);
            _icon.transform.SetParent(_gate.transform, false);

            // Centre of the door frame: the gate rect is GateWidth x GateHeight
            // with the transform at its bottom centre.
            Vector3 door = _gate.transform.position;
            _icon.transform.position = new Vector3(door.x, door.y + _gate.GateHeight * 0.5f, IconZ);

            // Undo the door's own scale so the logo is IconSize world units tall.
            Vector3 parentScale = _gate.transform.lossyScale;
            float size = IconSize / sprite.bounds.size.y;
            _icon.transform.localScale = new Vector3(
                size / Mathf.Max(Mathf.Abs(parentScale.x), 0.0001f),
                size / Mathf.Max(Mathf.Abs(parentScale.y), 0.0001f),
                1f);

            _iconRenderer = _icon.AddComponent<SpriteRenderer>();
            _iconRenderer.sprite = sprite;
            _iconRenderer.enabled = false;
            return true;
        }
    }
}
