using HarmonyLib;
using L2Base;
using L2Flag;
using L2Hit;
using LaMulana2Archipelago.Archipelago;
using LaMulana2Archipelago.Managers;
using LaMulana2RandomizerShared;
using UnityEngine;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Sprites for the glossary chip, used to show a recognisable chip icon instead of the
    /// generic AP / Shell Horn filler icon. The chip tile matches the ROM's rarity
    /// (N/R/SR/UR), keyed off the glossary game_id; callers that lack the id get the N tile.
    /// All providers cache so we don't re-scan Resources:
    ///   • ShopIcon(id)   → rarity chip tile (atlas cut) for shop slots, else "R Book".
    ///   • DialogIcon(id) → rarity chip tile, else the real pixel chip if seen, else "R Book".
    ///   • FloorIcon(id)  → rarity chip tile for freestanding + chest floor renderers.
    ///                      The floor/map atlas (icon_map) has no chip tile, so we slice the
    ///                      tile out of the shop atlas texture; falls back to Shell Horn.
    /// </summary>
    internal static class GlossaryChipSprite
    {
        // The real pixelated chip sprite, cached the first time a live glossary chip is seen
        // (its sprite is assigned at runtime, not on the prefab). Used for the item dialog only.
        internal static Sprite LiveChip;

        private static Sprite _rbook;
        private static Sprite _shell;

        // Chip tiles cut from the shop atlas, cached per atlas column (8=N,9=R,10=SR,11=UR).
        private static readonly System.Collections.Generic.Dictionary<int, Sprite> _chipTiles
            = new System.Collections.Generic.Dictionary<int, Sprite>();

        // Glossary game_id → chip rarity. Only non-N ids are listed (everything else is N).
        // game_id == 2000 + entry number. Source: apworld ids.py LocationID comments, whose
        // rarity tag in parens ("… Bat (N)", "… (R)/(SR)/(UR)") gives each entry's tier.
        private static readonly System.Collections.Generic.HashSet<int> _rChip = new System.Collections.Generic.HashSet<int> { 2005, 2006, 2007, 2021, 2022, 2025, 2033, 2034, 2040, 2049, 2050, 2063, 2075, 2076, 2077, 2078, 2079, 2080, 2091, 2105, 2106, 2107, 2119, 2120, 2121, 2131, 2132, 2133, 2140, 2143, 2144, 2145, 2158, 2169, 2170, 2171, 2172, 2174, 2176, 2185, 2192, 2196, 2199, 2219, 2220, 2221, 2222, 2233, 2234, 2235, 2236 };
        private static readonly System.Collections.Generic.HashSet<int> _srChip = new System.Collections.Generic.HashSet<int> { 2023, 2024, 2035, 2048, 2051, 2081, 2139, 2146, 2159, 2160, 2173, 2175, 2177, 2178, 2179, 2191, 2216, 2218, 2227, 2228, 2229, 2230, 2231 };
        private static readonly System.Collections.Generic.HashSet<int> _urChip = new System.Collections.Generic.HashSet<int> { 2013, 2180, 2193, 2204, 2205, 2206, 2213, 2214, 2215, 2217, 2223, 2232 };

        private const int NChipCol = 8, RChipCol = 9, SRChipCol = 10, URChipCol = 11;

        /// <summary>Shop-atlas column of the chip tile for a glossary ROM game_id, by rarity.
        /// Unknown / non-glossary ids (incl. -1) fall through to the N tile.</summary>
        private static int ChipColumnForRom(int gameId)
        {
            if (_urChip.Contains(gameId)) return URChipCol;
            if (_srChip.Contains(gameId)) return SRChipCol;
            if (_rChip.Contains(gameId)) return RChipCol;
            return NChipCol;
        }

        // "R Book" from the shop atlas (used for shop slots + dialog fallback).
        private static Sprite RBook()
        {
            if (_rbook != null) return _rbook;
            var all = Resources.LoadAll<Sprite>("Textures/icons_shops");
            _rbook = System.Array.Find(all, sp => sp != null && sp.name == "R Book");
            return _rbook;
        }

        // Chip tile cut straight out of the shop atlas texture, by column.
        //
        // The chip tiles (N/R/SR/UR) aren't exposed as individually-named sprites in this
        // atlas, so Resources can't resolve them by name the way "R Book" resolves — and the
        // floor/map atlas (icon_map) has no chip tile at all. So we slice directly. icons_shops
        // is a clean 12×12 grid of 52×52 tiles (624×624); the chip tiles sit on row 6:
        // N=col 8, R=9, SR=10, UR=11 (counting from the top-left). Unity texture space is
        // bottom-left origin, so the row is flipped in the Rect.
        private static Sprite ChipTile(int col)
        {
            if (_chipTiles.TryGetValue(col, out var cachedTile)) return cachedTile;

            Sprite result = null;
            try
            {
                var all = Resources.LoadAll<Sprite>("Textures/icons_shops");
                if (all != null && all.Length > 0)
                {
                    Texture2D tex = all[0].texture; // shared atlas texture
                    if (tex != null)
                    {
                        // 12×12 grid; derive tile size from the texture so it stays correct
                        // even if the atlas is ever imported at a scaled resolution.
                        const int cols = 12;
                        const int rowFromTop = 6;
                        int tw = tex.width / cols;
                        int th = tex.height / cols;
                        var rect = new Rect(col * tw, tex.height - (rowFromTop + 1) * th, tw, th);

                        // Base PPU off the Shell Horn floor sprite it replaces (fall back to 100),
                        // then double it so the chip renders at half that footprint — higher PPU =
                        // smaller world size. Chips are small, so half-size reads better on the floor.
                        var shell = Shell();
                        float ppu = (shell != null ? shell.pixelsPerUnit : 100f) * 2.5f;

                        result = Sprite.Create(tex, rect, new Vector2(0.5f, 0.5f), ppu);
                        result.name = "Chip col " + col + " (atlas cut)";
                    }
                }
            }
            catch { result = null; }

            if (result != null) _chipTiles[col] = result; // only cache successes (atlas may load late)
            return result;
        }

        /// <summary>Rarity-correct chip tile for a glossary ROM game_id (defaults to the N tile).</summary>
        private static Sprite ChipForRom(int gameId) => ChipTile(ChipColumnForRom(gameId));

        // Shell Horn map icon (used for freestanding + chest floor sprites, like filler).
        private static Sprite Shell()
        {
            if (_shell != null) return _shell;
            var d = L2SystemCore.getItemData("Shell Horn") ?? L2SystemCore.getItemData("ShellHorn");
            if (d != null) _shell = L2SystemCore.getMapIconSprite(d);
            return _shell;
        }

        // gameId selects the rarity tile (N/R/SR/UR); -1 (unknown) yields the N tile.
        internal static Sprite DialogIcon(int gameId = -1) => ChipForRom(gameId) ?? LiveChip ?? RBook(); // chip tier, else live chip, else R Book
        internal static Sprite ShopIcon(int gameId = -1) => ChipForRom(gameId) ?? RBook();    // shop slots: chip tier, else R Book
        internal static Sprite FloorIcon(int gameId = -1) => ChipForRom(gameId) ?? Shell();   // freestanding + chest floor: chip tier, else Shell Horn
    }

    /// <summary>
    /// Filler-style pickup for glossary ROMs at non-chip freestanding / chest locations.
    ///
    /// Goal (per user): walk-over auto-pickup like coins/weights — bottom-right popup, no
    /// get-item animation/dialog/AP icon. We fire the item's collected flag (sheet 31), which
    /// reports the AP check and delivers the entry via the CheckManager hook (→ DeliverGlossaryRom,
    /// the floating popup), then consume the object and skip the vanilla get-item flow.
    /// Chip-based glossary already does this via MonsterChipGlossaryPatch. Foreign / non-glossary
    /// items are never touched.
    /// </summary>
    internal static class GlossarySilentPickup
    {
        /// <summary>True if the item was an own glossary ROM and was consumed silently.</summary>
        internal static bool TryHandle(AbstractItemBase item, L2System sys, string caller)
        {
            if (!GlossaryManager.Enabled || item == null || sys == null)
                return false;

            string label = item.itemLabel;
            if (string.IsNullOrEmpty(label) || !label.StartsWith("AP Item", System.StringComparison.Ordinal))
                return false;

            if (!TreasureBoxSpritePatch.TryGetApLocation(item, out LocationID loc))
            {
                Plugin.Log.LogDebug($"[GLOSSARY/silent] {caller}: '{label}' — no AP location from itemActiveFlag");
                return false;
            }

            var scouted = ArchipelagoClientProvider.Client?.GetItemAtLocation(430000L + (int)loc);
            if (!GlossaryManager.IsOwnGlossaryRom(scouted))
            {
                Plugin.Log.LogDebug($"[GLOSSARY/silent] {caller}: loc={loc} — not own glossary ROM, skip");
                return false;
            }

            // Popup-only: skip the dialog prime (reuse the pot-filler skip in ReportLocation).
            ItemPotPatch.PotFillerDialog = true;

            // Fire the collected flag (sheet 31) → AP check → DeliverGlossaryRom (floating popup)
            // + marks the spot collected so it won't respawn.
            if (item.itemGetFlags != null && item.itemGetFlags.Length > 0)
                sys.setEffectFlag(item.itemGetFlags);

            // CRITICAL: the base groundBack() calls setItem(itemLabel) AFTER itemGetAction, and
            // "AP Item" triggers the hold-up animation + item dialog even though the grant is
            // suppressed. Neutralise to "Nothing" (and clear the flags we already fired) so the
            // pickup is truly silent — same trick MonsterChipGlossaryPatch uses.
            item.itemLabel = "Nothing";
            item.itemGetFlags = new L2FlagBoxEnd[0];
            Plugin.Log.LogInfo($"[GLOSSARY/silent] {caller}: SILENT pickup at {loc}: {scouted.ItemName}");
            return true;
        }
    }

    // NOTE: the actual interception is embedded at the TOP of EventItemGetActionPatch /
    // CostumeItemGetActionPatch / DropItemGetActionPatch (see EventItemGetActionPatch.cs). A
    // separate sibling prefix can't suppress the hold-up because Harmony runs every prefix
    // regardless of return value, so the enabled replacement patch would still run its body.
}
