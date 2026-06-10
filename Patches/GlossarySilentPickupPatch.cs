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
    /// Sprites for the glossary chip ("N Chip"). Icon() pulls the high-res shop-atlas sprite
    /// (Textures/icons_shops) by name for UI (shop slots, dialog); World() pulls the in-world
    /// sprite off the chip prefab (DropItemGeneratorScript.nchipPrefab) for floor renderers.
    /// Both cache so we don't re-scan Resources. Used to show the chip instead of the AP icon.
    /// </summary>
    internal static class GlossaryChipSprite
    {
        // The real pixelated chip sprite, cached the first time a live glossary chip is seen
        // (its sprite is assigned at runtime, not on the prefab). Used for the item dialog only.
        internal static Sprite LiveChip;

        private static Sprite _rbook;
        private static Sprite _shell;

        // "R Book" from the shop atlas (used for shop slots + dialog fallback).
        private static Sprite RBook()
        {
            if (_rbook != null) return _rbook;
            var all = Resources.LoadAll<Sprite>("Textures/icons_shops");
            _rbook = System.Array.Find(all, sp => sp != null && sp.name == "R Book");
            return _rbook;
        }

        // Shell Horn map icon (used for freestanding + chest floor sprites, like filler).
        private static Sprite Shell()
        {
            if (_shell != null) return _shell;
            var d = L2SystemCore.getItemData("Shell Horn") ?? L2SystemCore.getItemData("ShellHorn");
            if (d != null) _shell = L2SystemCore.getMapIconSprite(d);
            return _shell;
        }

        internal static Sprite DialogIcon() => LiveChip ?? RBook(); // pixelated chip if seen, else R Book
        internal static Sprite ShopIcon() => RBook();               // shop slots
        internal static Sprite FloorIcon() => Shell();              // freestanding + chest floor
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
