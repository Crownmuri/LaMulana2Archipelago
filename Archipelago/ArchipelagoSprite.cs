using System;
using System.IO;
using UnityEngine;

namespace LaMulana2Archipelago
{
    /// <summary>
    /// Loads custom Archipelago icons from the plugin directory and creates
    /// reusable Unity Sprite objects for every context:
    ///
    ///   • MapSprite  — "color-icon.png"
    ///                  chest drops, free-standing items, pickup animation
    ///                  (SpriteRenderer contexts, 25 PPU for ~4× size)
    ///
    ///   • ShopSprite — "original-logo.png"
    ///                  shop UI slots (Image component contexts)
    ///
    /// Call <see cref="Load"/> once from Plugin.Awake().
    /// Every sprite-override patch can then do:
    ///   <c>sprite = ApSpriteLoader.MapSprite ?? fallbackSprite;</c>
    /// </summary>
    internal static class ApSpriteLoader
    {
        /// <summary>Sprite for SpriteRenderer contexts (chests, ground items, pickup anim).</summary>
        public static Sprite MapSprite { get; private set; }

        /// <summary>Sprite for UI Image contexts (shop slots).</summary>
        public static Sprite ShopSprite { get; private set; }

        /// <summary>
        /// Progression ("up arrow") variant for SpriteRenderer contexts.
        /// Null when "ap-iconp.png" is absent — callers then fall back to <see cref="MapSprite"/>.
        /// </summary>
        public static Sprite MapSpriteProgression { get; private set; }

        /// <summary>
        /// Progression ("up arrow") variant for UI Image contexts (shop slots).
        /// Null when "ap-iconp.png" is absent — callers then fall back to <see cref="ShopSprite"/>.
        /// </summary>
        public static Sprite ShopSpriteProgression { get; private set; }

        /// <summary>True once at least the map sprite has been created successfully.</summary>
        public static bool IsLoaded => MapSprite != null;

        /// <summary>
        /// Map-context sprite for an AP item, choosing the progression ("up arrow")
        /// variant when the item carries the AP Advancement flag and that icon loaded.
        /// </summary>
        public static Sprite GetMapSprite(bool progression)
            => (progression && MapSpriteProgression != null) ? MapSpriteProgression : MapSprite;

        /// <summary>
        /// Shop-context sprite for an AP item, choosing the progression ("up arrow")
        /// variant when the item carries the AP Advancement flag and that icon loaded.
        /// </summary>
        public static Sprite GetShopSprite(bool progression)
            => (progression && ShopSpriteProgression != null) ? ShopSpriteProgression : ShopSprite;

        /// <summary>
        /// Loads icon PNGs from <paramref name="pluginDir"/> and creates the
        /// cached sprite objects.  Safe to call multiple times (no-ops after
        /// the first successful load).
        /// </summary>
        /// <param name="pluginDir">
        /// Directory containing the plugin DLL — typically
        /// <c>Path.GetDirectoryName(Info.Location)</c>.
        /// </param>
        public static void Load(string pluginDir)
        {
            if (IsLoaded) return;

            // Map sprite — used by SpriteRenderer (chests, ground items, pickup anim)
            MapSprite = LoadSprite(pluginDir, "ap-icon.png", "AP_Icon_Map", 25f);

            // Shop sprite — used by UI Image (shop slots)
            ShopSprite = LoadSprite(pluginDir, "ap-icon.png", "AP_Icon_Shop", 100f);

            // Progression variants ("up arrow") — optional. When "ap-iconp.png" is
            // missing these stay null and GetMapSprite/GetShopSprite fall back to
            // the plain icon, so the feature degrades cleanly on older installs.
            MapSpriteProgression = LoadSprite(pluginDir, "ap-iconp.png", "AP_Icon_Map_Prog", 25f);
            ShopSpriteProgression = LoadSprite(pluginDir, "ap-iconp.png", "AP_Icon_Shop_Prog", 100f);
        }

        private static Sprite LoadSprite(string dir, string fileName, string spriteName, float ppu)
        {
            string path = Path.Combine(dir, fileName);

            if (!File.Exists(path))
            {
                Plugin.Log.LogWarning($"[AP] Icon not found: {path}");
                return null;
            }

            try
            {
                byte[] pngData = File.ReadAllBytes(path);

                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };

                if (!ImageConversion.LoadImage(tex, pngData))
                {
                    Plugin.Log.LogWarning($"[AP] Failed to decode {fileName}");
                    UnityEngine.Object.Destroy(tex);
                    return null;
                }

                UnityEngine.Object.DontDestroyOnLoad(tex);

                var sprite = Sprite.Create(
                    tex,
                    new Rect(0f, 0f, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f),
                    ppu);

                sprite.name = spriteName;
                UnityEngine.Object.DontDestroyOnLoad(sprite);

                Plugin.Log.LogInfo(
                    $"[AP] Loaded {spriteName} from {fileName} ({tex.width}×{tex.height}, PPU={ppu})");

                return sprite;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[AP] Error loading {fileName}: {ex}");
                return null;
            }
        }
    }
}