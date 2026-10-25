using System;
using HarmonyLib;
using LaMulana2Archipelago.Managers;
using LaMulana2RandomizerShared;
using L2MobTask;
using UnityEngine;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Glossanity: floats a small AP logo above enemies whose glossary-chip drop
    /// location hasn't been checked yet.
    ///
    /// Enemies are SpriteStudio characters merged into one shared mesh per
    /// frame, so they can't be tinted/outlined individually; a separate marker
    /// sprite is the practical option. Hooked on DropItemSettingScript.setItemValues,
    /// which every enemy with drops runs from resetParameter and which copies its
    /// chipId (the sheet-20 book flag that MonsterChipDropGatePatch maps to the
    /// AP location).
    /// </summary>
    [HarmonyPatch(typeof(DropItemSettingScript), "setItemValues")]
    internal static class GlossaryEnemyMarkerPatch
    {
        /// <summary>Set from the BepInEx config (Gameplay/MarkGlossaryEnemies).</summary>
        public static bool Enabled = true;

        [HarmonyPostfix]
        static void Postfix(GroundCharacter myChara)
        {
            try
            {
                if (myChara == null || myChara.chipId < 0
                    || myChara.GetComponent<GlossaryEnemyMarker>() != null)
                    return;
                myChara.gameObject.AddComponent<GlossaryEnemyMarker>().Setup(myChara);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[GLOSS] Enemy marker attach failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Lives on an enemy with a glossary chip. Shows the AP logo while the enemy
    /// is alive and drawn and its chip location is unchecked, placed just above
    /// the top of its hitboxes (which follow the animation).
    /// </summary>
    internal class GlossaryEnemyMarker : MonoBehaviour
    {
        private const int RecheckInterval = 10;   // frames between location checks
        private const float IconSize = 8f;       // world units (≈ pixels) tall
        private const float Margin = 3f;          // gap above the top hitbox
        private const float DefaultOffsetY = 24f; // above the enemy root until hitboxes are known
        private const float Smoothing = 0.3f;     // per-frame approach to the target (animation jitter)
        private const byte DeadOrOut = 8 | 2;     // eventActiveFlg: taskDead / taskOut
        private const float FrontPlaneGap = 1f;   // how far behind FrontZ the icon sits

        private static readonly AccessTools.FieldRef<GroundCharacter, int> NowGameLevelRef =
            AccessTools.FieldRefAccess<GroundCharacter, int>("nowGameLevel");
        private static readonly AccessTools.FieldRef<MoveCharacterBase, bool> RenderActiveRef =
            AccessTools.FieldRefAccess<MoveCharacterBase, bool>("renderActive");
        private static readonly AccessTools.FieldRef<MoveCharacterBase, L2SystemCore> SysMainRef =
            AccessTools.FieldRefAccess<MoveCharacterBase, L2SystemCore>("sysMain");

        private static readonly AccessTools.FieldRef<MoveCharacterBase, SpriteStudioContainer[]> ContainersRef =
            AccessTools.FieldRefAccess<MoveCharacterBase, SpriteStudioContainer[]>("SSContainers");
        private static readonly AccessTools.FieldRef<MoveCharacterBase, int> NumAnimesRef =
            AccessTools.FieldRefAccess<MoveCharacterBase, int>("numAnimes");
        private static readonly AccessTools.FieldRef<MoveCharacterBase, bool> NoDropRef =
            AccessTools.FieldRefAccess<MoveCharacterBase, bool>("nodropitems");

        private GroundCharacter _enemy;
        private GameObject _icon;
        private SpriteRenderer _iconRenderer;
        private bool _unchecked;
        private int _nextCheckFrame;
        private Vector2 _offset = new Vector2(0f, DefaultOffsetY); // icon centre relative to enemy root
        private bool _offsetKnown;
        private bool _logged;

        public void Setup(GroundCharacter enemy)
        {
            _enemy = enemy;
        }

        private void LateUpdate()
        {
            if (_enemy == null)
                return;

            if (Time.frameCount >= _nextCheckFrame)
            {
                _nextCheckFrame = Time.frameCount + RecheckInterval;
                _unchecked = IsUnchecked();
            }

            bool show = _unchecked && HintIndicators.GlossaryHints && GlossaryEnemyMarkerPatch.Enabled
                && IsDrawn() && EnsureIcon();
            if (show)
            {
                UpdatePosition();
                if (!_logged)
                {
                    _logged = true;
                    Plugin.Log.LogDebug($"[GLOSS] Marker on '{_enemy.name}' chip {_enemy.chipId} " +
                        $"level {NowGameLevelRef(_enemy)} (range {_enemy.LowestLevel}-{_enemy.HardestLevel}) " +
                        $"at {_enemy.transform.position}");
                }
            }
            else
                _offsetKnown = false; // re-snap next time it appears

            if (_iconRenderer != null && _iconRenderer.enabled != show)
                _iconRenderer.enabled = show;
        }

        private void OnDestroy()
        {
            if (_icon != null)
                Destroy(_icon);
        }

        private bool IsUnchecked()
        {
            try
            {
                int chipId = _enemy.chipId;
                return chipId >= 0
                    && GlossaryManager.Enabled
                    && !NoDropRef(_enemy)
                    && GlossaryManager.TryGetLocation(chipId, out LocationID loc)
                    && !GlossaryManager.IsLocationCollected(loc);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Spawns at this difficulty, alive, not taken out, rendering, and its
        /// SpriteStudio animation is showing.
        /// </summary>
        private bool IsDrawn()
        {
            // Same gate as GroundCharacter.resetParameter: enemies outside their
            // LowestLevel..HardestLevel range never appear at this difficulty.
            int level = NowGameLevelRef(_enemy);
            if (level < _enemy.LowestLevel || level > _enemy.HardestLevel)
                return false;
            if ((_enemy._EventActiveFlag & DeadOrOut) != 0 || !RenderActiveRef(_enemy))
                return false;
            SpriteStudioContainer[] containers = ContainersRef(_enemy);
            return containers != null && containers.Length > 0 && containers[0] != null && containers[0].enabled;
        }

        private bool EnsureIcon()
        {
            if (_icon != null)
                return true;
            // Smooth-filtered copy: the point-filtered map icon gets jagged this small.
            Sprite sprite = ApSpriteLoader.MarkerSprite ?? ApSpriteLoader.MapSprite;
            if (sprite == null)
                return false;

            // Unparented: enemies flip by negative scale, which would mirror the logo.
            _icon = new GameObject("AP_GlossaryEnemyMarker");
            float scale = IconSize / sprite.bounds.size.y;
            _icon.transform.localScale = new Vector3(scale, scale, 1f);
            _iconRenderer = _icon.AddComponent<SpriteRenderer>();
            _iconRenderer.sprite = sprite;
            _iconRenderer.enabled = false;
            return true;
        }

        private void UpdatePosition()
        {
            Vector3 root = _enemy.transform.position;

            Vector2 target;
            if (TryGetHitboxTop(out float centerX, out float top))
            {
                target = new Vector2(centerX - root.x, top + Margin + IconSize * 0.5f - root.y);
                _offset = _offsetKnown ? Vector2.Lerp(_offset, target, Smoothing) : target;
                _offsetKnown = true;
            }
            // else: keep the last offset (or the default above the root)

            _icon.transform.position = new Vector3(root.x + _offset.x, root.y + _offset.y, IconZ(root.z));
        }

        /// <summary>
        /// Just behind the scroll system's front plane, so the logo draws over
        /// level geometry (platforms in front of the enemy) but stays under the
        /// screen-transition wipe that sits on the front plane itself. Falls back
        /// to slightly in front of the enemy.
        /// </summary>
        private float IconZ(float enemyZ)
        {
            try
            {
                L2SystemCore core = SysMainRef(_enemy);
                BGScrollSystem scroll = core != null ? core.ScrollSystem : null;
                if (scroll != null)
                    return Mathf.Min(scroll.FrontZ + FrontPlaneGap, enemyZ - 1f);
            }
            catch
            {
                // fall through
            }
            return enemyZ - 1f;
        }

        /// <summary>
        /// Union of the enemy's damageable ("HIT", red in the debug hitbox
        /// overlay) boxes this frame: horizontal centre and top. Attack/contact
        /// (blue) and defense (green) boxes swing around with attacks, so they're
        /// only used if the enemy has no HIT box at all.
        /// </summary>
        private bool TryGetHitboxTop(out float centerX, out float top)
        {
            return TryGetHitboxTop(true, out centerX, out top)
                || TryGetHitboxTop(false, out centerX, out top);
        }

        /// <summary>
        /// Hitbox union in world space (same maths as MoveCharacterBase's hit
        /// registration). hitflag bits per setHitContainerToTaskSystem: 128
        /// without 8 = HIT (damageable), neither = body ATTACK, 8 = weapon
        /// ATTACK / DEFENSE.
        /// </summary>
        private bool TryGetHitboxTop(bool damageableOnly, out float centerX, out float top)
        {
            centerX = 0f;
            top = float.MinValue;
            float minX = float.MaxValue, maxX = float.MinValue;

            SpriteStudioContainer[] containers = ContainersRef(_enemy);
            int count = Mathf.Min(NumAnimesRef(_enemy), containers != null ? containers.Length : 0);
            for (int i = 0; i < count; i++)
            {
                SpriteStudioContainer c = containers[i];
                if (c == null || !c.enabled || c.MyObject == null)
                    continue;
                Vector3 ls = c.MyObject.transform.localScale;
                SpriteStudioContainer.HITDATA_CONTAINER[] hits = c.getHitList();
                if (hits == null)
                    continue;
                int n = Mathf.Min(c.NumHits, hits.Length);
                for (int j = 0; j < n; j++)
                {
                    int flag = hits[j].hitflag;
                    if (flag == 255)
                        continue;
                    if (damageableOnly && ((flag & 8) != 0 || (flag & 128) == 0))
                        continue;
                    c.getHitPartsParameterInFrame(j, out Vector3 pos, out Vector2 size);
                    float w = Mathf.Abs(size.x * ls.x), h = Mathf.Abs(size.y * ls.y);
                    if (w <= 0f && h <= 0f)
                        continue;
                    top = Mathf.Max(top, pos.y + h * 0.5f);
                    minX = Mathf.Min(minX, pos.x - w * 0.5f);
                    maxX = Mathf.Max(maxX, pos.x + w * 0.5f);
                }
            }

            if (top == float.MinValue)
                return false;
            centerX = (minX + maxX) * 0.5f;
            return true;
        }
    }
}
