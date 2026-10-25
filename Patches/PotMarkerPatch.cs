using System;
using System.Collections.Generic;
using HarmonyLib;
using L2Base;
using UnityEngine;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Marks potsanity pots whose AP location hasn't been checked yet: the pot
    /// is tinted red and gets a red outline along its outermost opaque pixels.
    ///
    /// Pots are a 20x20 MeshRenderer quad (1 texel per world unit) using
    /// Sprites/Default, UV-mapped into a small point-filtered "tuboN" texture
    /// (pot art at texels x 1-20, y 11-30).
    ///   - Tint: Sprites/Default's _Color multiplies the texture. The material is
    ///     shared by every pot in the field, so it goes through a per-renderer
    ///     MaterialPropertyBlock.
    ///   - Outline: an outlined copy of the pot texture, swapped in through the
    ///     same property block (_MainTex). The pot keeps rendering in a single
    ///     draw, exactly like vanilla. Two earlier approaches had sorting bugs:
    ///     a separate sprite in front of the pot drew over hit sparks, and a
    ///     second material on the pot mesh (same depth) sometimes drew *under*
    ///     the pot, flickering during screen transitions.
    ///     The pot textures aren't CPU-readable, so the copy goes through a
    ///     RenderTexture. That readback can come back upside down, so it's
    ///     checked against the alpha masks baked in below (decoded from the level
    ///     files with UnityPy, alpha &gt; 0 = solid) and flipped if needed. The
    ///     same masks decide where the outline goes.
    ///
    /// <see cref="PotMarker"/> re-evaluates a few times a second and clears both
    /// once the pot is checked, potsanity is off or AP disconnects.
    /// </summary>
    [HarmonyPatch(typeof(ItemPotScript), "groundInit")]
    internal static class PotMarkerPatch
    {
        /// <summary>Set from the BepInEx config (Gameplay/TintUncheckedPots).</summary>
        public static bool TintEnabled = true;

        /// <summary>Set from the BepInEx config (Gameplay/OutlineUncheckedPots).</summary>
        public static bool OutlineEnabled = true;

        /// <summary>Multiplied into the pot colours: keeps red, dims green/blue.</summary>
        internal static readonly Color TintColor = new Color(1f, 0.70f, 0.70f, 1f);

        private static readonly Color32 OutlineColor = new Color32(170, 0, 0, 255); // 70% red
        private const int Thickness = 1; // outline width in pixels

        internal const string OutlineObjectName = "AP_PotOutline";

        [HarmonyPostfix]
        static void Postfix(ItemPotScript __instance)
        {
            try
            {
                if (__instance == null || __instance.potFlagNo < 0)
                    return;

                // Same lookup vanilla uses for myMesh.
                MeshRenderer mesh = __instance.GetComponentInChildren<MeshRenderer>();
                if (mesh == null || mesh.GetComponent<PotMarker>() != null)
                    return;

                mesh.gameObject.AddComponent<PotMarker>().Setup(__instance, mesh);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[POT] Marker attach failed: {ex.Message}");
            }
        }

        // =================================================================
        // Outlined texture
        // =================================================================

        // Keyed by texture name + UV rect (scene textures get new instances on
        // every load; the names are stable). A null value = failed, don't retry.
        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

        /// <summary>
        /// Returns the (cached) outlined copy of this pot's texture, or null if
        /// the mesh isn't one of the known pot quads or the copy failed.
        /// </summary>
        internal static Texture2D GetOutlinedTexture(MeshRenderer mesh)
        {
            MeshFilter mf = mesh.GetComponent<MeshFilter>();
            Material potMat = mesh.sharedMaterial;
            Texture tex = potMat != null ? potMat.mainTexture : null;
            if (mf == null || mf.sharedMesh == null || tex == null)
                return null;

            Vector2[] uvs = mf.sharedMesh.uv;
            if (uvs == null || uvs.Length < 3)
                return null;

            float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
            for (int i = 0; i < uvs.Length; i++)
            {
                minU = Mathf.Min(minU, uvs[i].x); maxU = Mathf.Max(maxU, uvs[i].x);
                minV = Mathf.Min(minV, uvs[i].y); maxV = Mathf.Max(maxV, uvs[i].y);
            }

            int texW = tex.width, texH = tex.height;
            int x0 = Mathf.RoundToInt(minU * texW), x1 = Mathf.RoundToInt(maxU * texW);
            int y0 = Mathf.RoundToInt(minV * texH), y1 = Mathf.RoundToInt(maxV * texH);
            int w = x1 - x0, h = y1 - y0;
            if (w <= 0 || h <= 0 || x0 < 0 || y0 < 0 || x1 > texW || y1 > texH)
                return null;

            string key = tex.name + "|" + x0 + "," + y0 + "," + w + "," + h;
            Texture2D result;
            if (Cache.TryGetValue(key, out result))
                return result; // may be null (known failure)

            result = BuildOutlinedTexture(tex, x0, y0, w, h);
            Cache[key] = result;
            return result;
        }

        private static Texture2D BuildOutlinedTexture(Texture tex, int x0, int y0, int w, int h)
        {
            bool[] solid = LookupMask(tex.name, x0, y0, w, h);
            if (solid == null)
            {
                Plugin.Log.LogWarning($"[POT] No outline mask for pot texture '{tex.name}' ({x0},{y0} {w}x{h})");
                return null;
            }

            int texW = tex.width, texH = tex.height;
            Color32[] px = ReadPixels(tex);
            if (px == null)
                return null;

            // Work out the readback orientation from the alpha mask.
            int upright = 0, flipped = 0;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool want = solid[y * w + x];
                    if ((px[(y0 + y) * texW + x0 + x].a > 0) == want) upright++;
                    if ((px[(texH - 1 - (y0 + y)) * texW + x0 + x].a > 0) == want) flipped++;
                }
            }
            int needed = w * h * 95 / 100;
            if (upright < needed && flipped < needed)
            {
                Plugin.Log.LogWarning($"[POT] Pot texture '{tex.name}' readback doesn't match its mask " +
                                      $"(upright {upright}, flipped {flipped} of {w * h}); no outline");
                return null;
            }
            if (flipped > upright)
            {
                Color32[] fixedPx = new Color32[px.Length];
                for (int y = 0; y < texH; y++)
                    Array.Copy(px, (texH - 1 - y) * texW, fixedPx, y * texW, texW);
                px = fixedPx;
            }

            // Paint the outline: every solid pixel within Thickness steps
            // (Manhattan) of a transparent one; the region edge counts as
            // transparent.
            Func<int, int, bool> at = (x, y) => x >= 0 && y >= 0 && x < w && y < h && solid[y * w + x];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (!at(x, y))
                        continue;
                    bool edge = false;
                    for (int dy = -Thickness; dy <= Thickness && !edge; dy++)
                        for (int dx = -Thickness; dx <= Thickness && !edge; dx++)
                            if (Math.Abs(dx) + Math.Abs(dy) <= Thickness && !at(x + dx, y + dy))
                                edge = true;
                    if (edge)
                        px[(y0 + y) * texW + x0 + x] = OutlineColor;
                }
            }

            Texture2D outTex = new Texture2D(texW, texH, TextureFormat.ARGB32, false);
            outTex.name = OutlineObjectName + " " + tex.name;
            outTex.filterMode = FilterMode.Point;
            outTex.wrapMode = tex.wrapMode;
            outTex.SetPixels32(px);
            outTex.Apply(false, true);
            outTex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return outTex;
        }

        /// <summary>Copies a non-readable texture's pixels via a RenderTexture.</summary>
        private static Color32[] ReadPixels(Texture tex)
        {
            int texW = tex.width, texH = tex.height;
            RenderTexture rt = RenderTexture.GetTemporary(texW, texH, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            RenderTexture prev = RenderTexture.active;
            FilterMode prevFilter = tex.filterMode;
            try
            {
                tex.filterMode = FilterMode.Point; // exact texels
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                Texture2D tmp = new Texture2D(texW, texH, TextureFormat.ARGB32, false);
                tmp.ReadPixels(new Rect(0, 0, texW, texH), 0, 0, false);
                tmp.Apply(false, false);
                Color32[] px = tmp.GetPixels32();
                UnityEngine.Object.Destroy(tmp);
                return px;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[POT] Pot texture '{tex.name}' readback failed: {ex.Message}");
                return null;
            }
            finally
            {
                tex.filterMode = prevFilter;
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        // Baked region every vanilla pot quad maps (texels, origin bottom-left).
        private const int MaskX = 1, MaskY = 11, MaskW = 20, MaskH = 20;

        // Per pot texture: one int per row, bottom row first; bit i = column MaskX+i.
        private static readonly Dictionary<string, int[]> Masks = new Dictionary<string, int[]>
        {
            { "tubo00", new[] { 0x7FFFE, 0x7FFFE, 0x3FFFC, 0x3FFFC, 0x3FFF8, 0x1FFF8, 0x1FFF8, 0x1FFF8, 0x3FFFC, 0x7FFFE, 0x7FFFE, 0x7FFFE, 0x7FFFE, 0xFFFFE, 0xFFFFE, 0x7FFFE, 0x7FFFE, 0xFFFFF, 0xFFFFF, 0xFFFFF } },
            { "tubo01", new[] { 0x7FFFE, 0x7FFFE, 0x3FFFC, 0x3FFFC, 0x3FFF8, 0x1FFF8, 0x1FFF8, 0x1FFF8, 0x3FFFC, 0x7FFFE, 0x7FFFE, 0x7FFFE, 0x7FFFE, 0xFFFFE, 0xFFFFE, 0x7FFFE, 0x7FFFE, 0xFFFFF, 0xFFFFF, 0xFFFFF } },
            { "tubo10", new[] { 0x0FFF0, 0x1FFF8, 0x1FFF8, 0x3FFF8, 0x3FFFC, 0x3FFFC, 0x7FFFC, 0x7FFFE, 0x7FFFE, 0x7FFFE, 0xFFFFE, 0xFFFFE, 0xFFFFE, 0xFFFFF, 0xFFFFF, 0xFFFFF, 0xFFFFF, 0xFFFFF, 0xFFFFF, 0xFFFFF } },
            { "tubo11", new[] { 0x0FFF0, 0x1FFF8, 0x3FFFC, 0x3FFFC, 0x7FFFE, 0xFFFFE, 0xFFFFF, 0xFFFFF, 0xFFFFF, 0x7FFFF, 0x7FFFE, 0x7FFFE, 0x1FFFC, 0x0FFF8, 0x0FFF0, 0x07FF0, 0x07FF0, 0x07FF8, 0x03FF8, 0x01FE0 } },
            { "tubo12", new[] { 0x07FE0, 0x07FE0, 0x03FC0, 0x0FFF0, 0x1FFF8, 0x3FFFC, 0xFFFFE, 0xFFFFE, 0x7FFFE, 0x7FFFE, 0xFFFFE, 0xFFFFF, 0xFFFFF, 0xFFFFF, 0xFFFF3, 0xFFFFF, 0x7FFFE, 0x07FE0, 0x03FC0, 0x00E00 } },
            { "tubo13", new[] { 0x07FE0, 0x0FFF0, 0x0FFF8, 0x1FFF8, 0x1FFFC, 0x3FFFC, 0x3FFFE, 0x7FFFE, 0x7FFFE, 0x7FFFF, 0xFFFFF, 0xFFFFF, 0xFFFFF, 0xFFFFF, 0x7FFFE, 0x7FFFE, 0x3FFFE, 0x1FFF8, 0x0FFF0, 0x03FC0 } },
            { "tubo14", new[] { 0x07FF8, 0x07FF0, 0x07FF8, 0x07FFC, 0x0FFFC, 0x0FFFE, 0x1FFFE, 0x1FFFE, 0x7FFFF, 0x7FFFF, 0x7FFFF, 0xFFFFF, 0xFFFFF, 0xFFFFF, 0xBFFFF, 0xBFFFF, 0xBFFFF, 0xFFFFF, 0xFFFFF, 0x3FFFF } },
            { "tubo5", new[] { 0x0FFF0, 0x1FFF8, 0x1FFF8, 0x3FFF8, 0x3FFFC, 0x3FFFC, 0x7FFFC, 0x7FFFE, 0x7FFFE, 0x7FFFE, 0xFFFFE, 0xFFFFE, 0xFFFFE, 0xFFFFF, 0xFFFFF, 0xFFFFF, 0xFFFFF, 0xFFFFF, 0xFFFFF, 0xFFFFF } },
            { "tubo6", new[] { 0x07FE0, 0x0FFF0, 0x3FFFC, 0x3FFFC, 0x7FFFE, 0x7FFFF, 0xFFFFF, 0xFFFFF, 0xFFFFF, 0xFFFFF, 0xFFFFF, 0xFFFFF, 0x7FFFE, 0x7FFFE, 0x1FFFC, 0x0FFF0, 0x0FFF0, 0x0FFF0, 0x0FFF0, 0x0FFF0 } },
            { "tubo7", new[] { 0x0FFF0, 0x1FFF8, 0x3FFFC, 0x3FFFC, 0x7FFFE, 0xFFFFE, 0xFFFFF, 0xFFFFF, 0xFFFFF, 0x7FFFF, 0x7FFFE, 0x7FFFE, 0x1FFFC, 0x0FFF8, 0x0FFF0, 0x07FF0, 0x07FF0, 0x07FF8, 0x03FF8, 0x01FE0 } },
            { "tubo8", new[] { 0x07FE0, 0x07FE0, 0x03FC0, 0x0FFF0, 0x1FFF8, 0x3FFFC, 0xFFFFE, 0xFFFFE, 0x7FFFE, 0x7FFFE, 0xFFFFE, 0xFFFFF, 0xFFFFF, 0xFFFFF, 0xFFFF3, 0xFFFFF, 0x7FFFE, 0x07FE0, 0x03FC0, 0x00E00 } },
            { "tubo9", new[] { 0x07FE0, 0x0FFF0, 0x0FFF8, 0x1FFF8, 0x1FFFC, 0x3FFFC, 0x3FFFE, 0x7FFFE, 0x7FFFE, 0x7FFFF, 0xFFFFF, 0xFFFFF, 0xFFFFF, 0xFFFFF, 0x7FFFE, 0x7FFFE, 0x3FFFE, 0x1FFF8, 0x0FFF0, 0x03FC0 } },
        };

        private static bool[] LookupMask(string texName, int x0, int y0, int w, int h)
        {
            int[] rows;
            if (x0 != MaskX || y0 != MaskY || w != MaskW || h != MaskH
                || texName == null || !Masks.TryGetValue(texName, out rows))
                return null;

            bool[] solid = new bool[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    solid[y * w + x] = (rows[y] & (1 << x)) != 0;
            return solid;
        }
    }

    /// <summary>
    /// Lives on a pot's mesh object. Applies/clears the tint and the outlined
    /// texture through one per-renderer property block (both vanish with the
    /// pot mesh while it's broken).
    /// </summary>
    internal class PotMarker : MonoBehaviour
    {
        private const int RecheckInterval = 10; // frames
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

        private ItemPotScript _pot;
        private MeshRenderer _mesh;
        private MaterialPropertyBlock _block;
        private Texture2D _outlined;
        private bool _outlineBuilt;
        private L2System _sys;
        private int _seetNo = -1;
        private bool _tinted;
        private bool _outlineShown;
        private int _nextCheckFrame;

        public void Setup(ItemPotScript pot, MeshRenderer mesh)
        {
            _pot = pot;
            _mesh = mesh;
        }

        private void LateUpdate()
        {
            if (_pot == null || _mesh == null || Time.frameCount < _nextCheckFrame)
                return;
            _nextCheckFrame = Time.frameCount + RecheckInterval;

            bool isUnchecked = IsUnchecked();
            bool hints = isUnchecked && Managers.HintIndicators.PotHints;
            bool tint = hints && PotMarkerPatch.TintEnabled;
            bool outline = hints && PotMarkerPatch.OutlineEnabled && EnsureOutline();
            if (tint == _tinted && outline == _outlineShown)
                return;
            _tinted = tint;
            _outlineShown = outline;

            if (!tint && !outline)
            {
                // Back to the shared material's own colour and texture.
                _mesh.SetPropertyBlock(null);
                return;
            }

            if (_block == null) _block = new MaterialPropertyBlock();
            _block.Clear();
            if (tint)
                _block.SetColor(ColorId, PotMarkerPatch.TintColor);
            if (outline)
                _block.SetTexture(MainTexId, _outlined);
            _mesh.SetPropertyBlock(_block);
        }

        /// <summary>Fetches the outlined texture on first use. One attempt per pot.</summary>
        private bool EnsureOutline()
        {
            if (_outlineBuilt)
                return _outlined != null;
            _outlineBuilt = true;

            try
            {
                _outlined = PotMarkerPatch.GetOutlinedTexture(_mesh);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[POT] Outline build failed: {ex.Message}");
                _outlined = null;
            }
            return _outlined != null;
        }

        private bool IsUnchecked()
        {
            try
            {
                if (_sys == null)
                {
                    _sys = Traverse.Create(_pot).Field("sys").GetValue<L2System>();
                    if (_sys == null) return false;
                }
                if (_seetNo < 0)
                    _seetNo = _sys.SeetNametoNo("21itempot");

                return ItemPotPatch.IsUncheckedApPot(_sys, _seetNo, _pot.potFlagNo);
            }
            catch
            {
                return false;
            }
        }
    }
}
