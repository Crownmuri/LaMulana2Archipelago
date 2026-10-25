using System.Collections.Generic;
using L2Base;
using LaMulana2Archipelago.Managers;
using UnityEngine;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Icons for the Guardian Soul items (shuffle_guardian_souls), taken from the
    /// guardian's own glossary portrait — the same Resources texture BookMenu
    /// shows ("Textures/book/" + BookIndex column 2). The whole 280×311 portrait
    /// (book frame included) is pre-downsampled once to 58×64 and centred in a
    /// transparent 64×64 texture, so square UI slots don't stretch it: a 311→64
    /// shrink sampled straight off the source would alias badly on the small
    /// floor / hold-up renderers.
    ///
    ///   • DialogIcon(id) → item dialog (UI Image, size set by the dialog).
    ///   • ShopIcon(id)   → shop slots (UI Image).
    ///   • FloorIcon(id)  → chest/pot/freestanding floor + the hold-up above the
    ///                      player (SpriteRenderers): PPU matched to the Ankh Jewel
    ///                      map icon so the soul is the size of a normal item.
    /// Every getter returns null on failure so callers fall back to their old icon.
    /// </summary>
    internal static class GuardianSoulSprite
    {
        // Glossary texture per soul, in GuardianSoulManager bit order
        // (guardian00..08: Fafnir, Vritra, Kujata, Aten-Ra, Jormungand, Anu, Surtr, Echidna, Hel).
        private static readonly string[] BookTextures =
        {
            "f00-8", "f10-9", "f02-11", "f11-8", "f03-8", "f12-7", "f04-8", "f13-15", "f14-9",
        };

        // Full portrait (280×311) scaled to OutSize tall, aspect kept, padded to a square.
        private const int OutSize = 64;
        private const int PortraitW = 58; // 280/311 × 64 ≈ 57.6

        private static readonly Dictionary<int, Texture2D> _tex = new Dictionary<int, Texture2D>();
        private static readonly Dictionary<int, Sprite> _ui = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, Sprite> _floor = new Dictionary<int, Sprite>();

        /// <summary>
        /// Soul to show in the next non-placeholder item dialog (direct grants of a
        /// received soul use the "Ankh Jewel" dialog). -1 when none. Consumed by
        /// ItemDialogPatch when the dialog opens, cleared when it closes.
        /// </summary>
        internal static int PendingDialogSoul = -1;

        internal static Sprite DialogIcon(int gameId) => Ui(gameId);
        internal static Sprite ShopIcon(int gameId) => Ui(gameId);

        internal static Sprite FloorIcon(int gameId)
        {
            if (_floor.TryGetValue(gameId, out var s) && s != null) return s;
            var tex = Texture(gameId);
            if (tex == null) return null;

            float ppu = 100f;
            try
            {
                // Match the world size of a normal item's map icon.
                var jewel = L2SystemCore.getItemData("Ankh Jewel");
                var refSprite = jewel != null ? L2SystemCore.getMapIconSprite(jewel) : null;
                if (refSprite != null && refSprite.rect.width > 0f)
                    ppu = OutSize / (refSprite.rect.width / refSprite.pixelsPerUnit);
            }
            catch { }

            s = Sprite.Create(tex, new Rect(0, 0, OutSize, OutSize), new Vector2(0.5f, 0.5f), ppu);
            s.name = "Guardian Soul " + gameId + " (floor)";
            _floor[gameId] = s;
            return s;
        }

        private static Sprite Ui(int gameId)
        {
            if (_ui.TryGetValue(gameId, out var s) && s != null) return s;
            var tex = Texture(gameId);
            if (tex == null) return null;

            s = Sprite.Create(tex, new Rect(0, 0, OutSize, OutSize), new Vector2(0.5f, 0.5f), 100f);
            s.name = "Guardian Soul " + gameId;
            _ui[gameId] = s;
            return s;
        }

        private static Texture2D Texture(int gameId)
        {
            if (_tex.TryGetValue(gameId, out var cached) && cached != null) return cached;

            int idx = gameId - (int)LaMulana2RandomizerShared.ItemID.FafnirSoul;
            if (!GuardianSoulManager.IsSoul(gameId) || idx < 0 || idx >= BookTextures.Length) return null;

            Texture2D result = null;
            RenderTexture prev = RenderTexture.active;
            RenderTexture big = null, small = null;
            try
            {
                var src = Resources.Load<Texture>("Textures/book/" + BookTextures[idx]);
                if (src == null)
                {
                    Plugin.Log.LogWarning("[SOULS] Glossary texture not found: " + BookTextures[idx]);
                    return null;
                }

                // Two bilinear passes: full portrait → 116×128, then an exact 2:1
                // (a 2×2 box filter) → 58×64.
                big = RenderTexture.GetTemporary(PortraitW * 2, OutSize * 2, 0, RenderTextureFormat.ARGB32);
                small = RenderTexture.GetTemporary(PortraitW, OutSize, 0, RenderTextureFormat.ARGB32);
                big.filterMode = FilterMode.Bilinear;
                Graphics.Blit(src, big);
                Graphics.Blit(big, small);

                // Transparent 64×64 canvas, portrait centred horizontally.
                result = new Texture2D(OutSize, OutSize, TextureFormat.RGBA32, false);
                result.SetPixels32(new Color32[OutSize * OutSize]);
                RenderTexture.active = small;
                result.ReadPixels(new Rect(0, 0, PortraitW, OutSize), (OutSize - PortraitW) / 2, 0);
                result.Apply(false, false);
                result.filterMode = FilterMode.Bilinear;
                result.wrapMode = TextureWrapMode.Clamp;
                result.name = "Guardian Soul " + gameId;
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("[SOULS] Building soul icon failed: " + ex.Message);
                result = null;
            }
            finally
            {
                RenderTexture.active = prev;
                if (big != null) RenderTexture.ReleaseTemporary(big);
                if (small != null) RenderTexture.ReleaseTemporary(small);
            }

            if (result != null) _tex[gameId] = result;
            return result;
        }
    }
}
