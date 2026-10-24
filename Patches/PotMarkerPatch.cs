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
    ///   - Outline: the textures aren't CPU-readable (and a RenderTexture copy
    ///     came back flipped), so the alpha masks of all 12 pot textures are
    ///     baked in below, decoded from the level files with UnityPy (alpha &gt; 0
    ///     counts as solid). The outline is a child sprite just in front of the
    ///     pot quad, cached per texture name.
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
        internal static readonly Color TintColor = new Color(1f, 0.7f, 0.7f, 1f);

        private static readonly Color32 OutlineColor = new Color32(150, 0, 0, 255); // 70% red
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
        // Outline sprite
        // =================================================================

        internal sealed class OutlineInfo
        {
            public Sprite Sprite;
            public Vector2 Center; // quad centre in mesh-local space
            public bool FlipX;
            public bool FlipY;
        }

        // Keyed by texture name + UV rect (scene textures get new instances on
        // every load; the names are stable).
        private static readonly Dictionary<string, OutlineInfo> Cache = new Dictionary<string, OutlineInfo>();

        /// <summary>
        /// Returns the (cached) outline for this pot mesh, or null if the mesh
        /// isn't one of the known pot quads.
        /// </summary>
        internal static OutlineInfo GetOutline(MeshRenderer mesh)
        {
            MeshFilter mf = mesh.GetComponent<MeshFilter>();
            Material mat = mesh.sharedMaterial;
            Texture tex = mat != null ? mat.mainTexture : null;
            if (mf == null || mf.sharedMesh == null || tex == null)
                return null;

            Vector3[] verts = mf.sharedMesh.vertices;
            Vector2[] uvs = mf.sharedMesh.uv;
            if (verts == null || uvs == null || verts.Length < 3 || uvs.Length != verts.Length)
                return null;

            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
            int minXIdx = 0, minYIdx = 0;
            for (int i = 0; i < verts.Length; i++)
            {
                if (verts[i].x < minX) { minX = verts[i].x; minXIdx = i; }
                if (verts[i].y < minY) { minY = verts[i].y; minYIdx = i; }
                maxX = Mathf.Max(maxX, verts[i].x);
                maxY = Mathf.Max(maxY, verts[i].y);
                minU = Mathf.Min(minU, uvs[i].x); maxU = Mathf.Max(maxU, uvs[i].x);
                minV = Mathf.Min(minV, uvs[i].y); maxV = Mathf.Max(maxV, uvs[i].y);
            }

            int x0 = Mathf.RoundToInt(minU * tex.width), x1 = Mathf.RoundToInt(maxU * tex.width);
            int y0 = Mathf.RoundToInt(minV * tex.height), y1 = Mathf.RoundToInt(maxV * tex.height);
            int w = x1 - x0, h = y1 - y0;
            float quadW = maxX - minX;
            if (w <= 0 || h <= 0 || quadW <= 0f)
                return null;

            string key = tex.name + "|" + x0 + "," + y0 + "," + w + "," + h;
            OutlineInfo info;
            if (Cache.TryGetValue(key, out info) && info.Sprite != null)
                return info;

            bool[] solid = LookupMask(tex.name, x0, y0, w, h);
            if (solid == null)
            {
                Plugin.Log.LogWarning($"[POT] No outline mask for pot texture '{tex.name}' ({x0},{y0} {w}x{h})");
                return null;
            }

            Texture2D outTex = BuildOutline(solid, w, h);
            Sprite sprite = Sprite.Create(outTex, new Rect(0, 0, outTex.width, outTex.height),
                new Vector2(0.5f, 0.5f), w / quadW);
            sprite.name = OutlineObjectName + " " + tex.name;
            sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;

            info = new OutlineInfo
            {
                Sprite = sprite,
                Center = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f),
                // The left-most vertex should carry the smallest U (bottom-most the
                // smallest V); if not, the art is mirrored on that axis.
                FlipX = uvs[minXIdx].x > (minU + maxU) * 0.5f,
                FlipY = uvs[minYIdx].y > (minV + maxV) * 0.5f,
            };
            Cache[key] = info;
            return info;
        }

        // Outline materials keyed by render queue.
        private static readonly Dictionary<int, Material> OutlineMaterials = new Dictionary<int, Material>();

        /// <summary>Sprite material copy drawn in the same render queue as the pot.</summary>
        internal static Material GetOutlineMaterial(Material spriteMat, Material potMat)
        {
            if (spriteMat == null || potMat == null)
                return spriteMat;

            int queue = potMat.renderQueue;
            Material m;
            if (OutlineMaterials.TryGetValue(queue, out m) && m != null)
                return m;

            m = new Material(spriteMat) { renderQueue = queue, name = OutlineObjectName + " q" + queue };
            m.hideFlags = HideFlags.DontUnloadUnusedAsset;
            OutlineMaterials[queue] = m;
            Plugin.Log.LogDebug($"[POT] Outline material: pot '{potMat.name}' shader '{potMat.shader?.name}' queue {queue}, sprite queue {spriteMat.renderQueue}");
            return m;
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

        /// <summary>
        /// Builds a w x h texture that is red on every opaque pixel within
        /// Thickness steps of a transparent one (Manhattan distance, quad edge
        /// counts as transparent). Everything else is transparent.
        /// </summary>
        private static Texture2D BuildOutline(bool[] solid, int w, int h)
        {
            Func<int, int, bool> at = (x, y) => x >= 0 && y >= 0 && x < w && y < h && solid[y * w + x];

            Color32 clear = new Color32(0, 0, 0, 0);
            Color32[] dst = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool edge = false;
                    if (at(x, y))
                    {
                        for (int dy = -Thickness; dy <= Thickness && !edge; dy++)
                            for (int dx = -Thickness; dx <= Thickness && !edge; dx++)
                                if (Math.Abs(dx) + Math.Abs(dy) <= Thickness && !at(x + dx, y + dy))
                                    edge = true;
                    }
                    dst[y * w + x] = edge ? OutlineColor : clear;
                }
            }

            Texture2D tex = new Texture2D(w, h, TextureFormat.ARGB32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.SetPixels32(dst);
            tex.Apply(false, true);
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return tex;
        }
    }

    /// <summary>
    /// Lives on a pot's mesh object. Applies/clears the tint and shows/hides the
    /// outline child (which also follows the pot mesh's visibility, so it
    /// disappears while the pot is broken).
    /// </summary>
    internal class PotMarker : MonoBehaviour
    {
        private const int RecheckInterval = 10; // frames
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private ItemPotScript _pot;
        private MeshRenderer _mesh;
        private MaterialPropertyBlock _block;
        private SpriteRenderer _outline;
        private bool _outlineBuilt;
        private L2System _sys;
        private int _seetNo = -1;
        private bool _unchecked;
        private bool _tinted;
        private int _nextCheckFrame;

        public void Setup(ItemPotScript pot, MeshRenderer mesh)
        {
            _pot = pot;
            _mesh = mesh;
        }

        private void LateUpdate()
        {
            if (_pot == null || _mesh == null)
                return;

            if (Time.frameCount >= _nextCheckFrame)
            {
                _nextCheckFrame = Time.frameCount + RecheckInterval;
                _unchecked = IsUnchecked();
                UpdateTint(_unchecked && PotMarkerPatch.TintEnabled);
            }

            bool showOutline = _unchecked && PotMarkerPatch.OutlineEnabled && _mesh.enabled && EnsureOutline();
            if (_outline != null && _outline.enabled != showOutline)
                _outline.enabled = showOutline;
        }

        private void UpdateTint(bool want)
        {
            if (want == _tinted)
                return;
            _tinted = want;

            if (want)
            {
                if (_block == null) _block = new MaterialPropertyBlock();
                _mesh.GetPropertyBlock(_block);
                _block.SetColor(ColorId, PotMarkerPatch.TintColor);
                _mesh.SetPropertyBlock(_block);
            }
            else
            {
                // Back to the shared material's own colour.
                _mesh.SetPropertyBlock(null);
            }
        }

        /// <summary>Creates the outline child on first use. One attempt per pot.</summary>
        private bool EnsureOutline()
        {
            if (_outlineBuilt)
                return _outline != null;
            _outlineBuilt = true;

            try
            {
                var info = PotMarkerPatch.GetOutline(_mesh);
                if (info == null)
                    return false;

                GameObject go = new GameObject(PotMarkerPatch.OutlineObjectName);
                go.layer = _mesh.gameObject.layer;
                go.transform.SetParent(_mesh.transform, false);
                // Slightly nearer the camera (world -z) than the pot quad so the
                // line paints over the pot's edge pixels. Done in world space: the
                // pot mesh object is rotated 180° about Y, so a local -z offset
                // would push the outline behind the pot.
                go.transform.position = _mesh.transform.TransformPoint(info.Center.x, info.Center.y, 0f)
                    + new Vector3(0f, 0f, -0.25f);

                SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = info.Sprite;
                sr.flipX = info.FlipX;
                sr.flipY = info.FlipY;
                sr.sortingLayerID = _mesh.sortingLayerID;
                sr.sortingOrder = _mesh.sortingOrder;
                // Keep the outline in the pot's render queue so depth alone decides
                // the order (both are 3000 in vanilla; this guards against changes).
                sr.sharedMaterial = PotMarkerPatch.GetOutlineMaterial(sr.sharedMaterial, _mesh.sharedMaterial);
                sr.enabled = false;
                _outline = sr;
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[POT] Outline build failed: {ex.Message}");
                return false;
            }
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
