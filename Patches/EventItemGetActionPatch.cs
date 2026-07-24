using HarmonyLib;
using L2Base;
using L2Hit;
using LaMulana2Archipelago.Managers;
using LaMulana2RandomizerShared;
using UnityEngine;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Replaces EventItemScript.itemGetAction so that progressive items
    /// (Whip1/2/3, Shield1/2/3, Mantra, Research, Beherit) show the
    /// correct name and icon when picked up in the world.
    /// Also handles AP placeholder items and filler (Coin/Weight) safely.
    /// Port of the original randomizer's MonoMod patch.
    /// </summary>
    [HarmonyPatch(typeof(EventItemScript), "itemGetAction")]
    internal static class EventItemGetActionPatch
    {
        public static bool Enabled = false;

        static bool Prefix(EventItemScript __instance)
        {
            var sys = Traverse.Create(__instance).Field("sys").GetValue<L2System>();

            // Own glossary ROM → silent filler-style pickup (no hold-up/dialog/AP icon). Must run
            // BEFORE the !Enabled gate: when this replacement patch is enabled, its body below does
            // the get-item hold-up, so the silent pickup has to pre-empt it here (a separate prefix
            // can't — Harmony runs every prefix regardless of return value).
            if (GlossarySilentPickup.TryHandle(__instance, sys, "EventItem")) return false;

            if (!Enabled) return true;

            var pl = Traverse.Create(__instance).Field("pl").GetValue<NewPlayer>();
            string itemLabel = __instance.itemLabel;

            int slotNo = Traverse.Create(__instance).Method("getL2Core").GetValue<L2SystemCore>().seManager.playSE(null, 39);
            Traverse.Create(__instance).Method("getL2Core").GetValue<L2SystemCore>().seManager.releaseGameObjectFromPlayer(slotNo);
            pl.setActionOder(PLAYERACTIONODER.getitem);

            if (itemLabel.Contains("Whip"))
            {
                short data = 0;
                sys.getFlag(2, "Whip", ref data);
                string trueItemName = data == 0 ? "Whip" : data == 1 ? "Whip2" : "Whip3";
                pl.setGetItem(ref trueItemName);
                pl.setGetItemIcon(L2SystemCore.getItemData(trueItemName));
            }
            else if (itemLabel.Contains("Shield"))
            {
                short data = 0;
                sys.getFlag(2, 196, ref data);
                string trueItemName = data == 0 ? "Shield" : data == 1 ? "Shield2" : "Shield3";
                pl.setGetItem(ref trueItemName);
                pl.setGetItemIcon(L2SystemCore.getItemData(trueItemName));
            }
            else if (itemLabel.StartsWith("AP Item"))
            {
                // AP placeholder — lookup via "AP Item" so GetItemDataApItemPatch
                // redirects to Holy Grail and sets LastWasApRedirect, allowing
                // SetGetItemIconApPatch to swap in the custom AP sprite.
                string displayName = "AP Item";
                pl.setGetItem(ref displayName);
                var iconData = L2SystemCore.getItemData("AP Item");
                if (iconData != null)
                    pl.setGetItemIcon(iconData);
            }
            else if (itemLabel.StartsWith("Coin") || itemLabel.StartsWith("Weight"))
            {
                // Filler items — use "Nothing" for safe lookup
                string displayName = itemLabel;
                pl.setGetItem(ref displayName);
                var iconData = L2SystemCore.getItemData("Nothing");
                if (iconData != null)
                    pl.setGetItemIcon(iconData);
            }
            else
            {
                pl.setGetItem(ref itemLabel);
                if (itemLabel.Contains("Mantra"))
                    pl.setGetItemIcon(L2SystemCore.getItemData("Mantra"));
                else if (itemLabel.Contains("Research"))
                    pl.setGetItemIcon(L2SystemCore.getItemData("Research"));
                else if (itemLabel.Contains("Beherit"))
                    pl.setGetItemIcon(L2SystemCore.getItemData("Beherit"));
                else
                {
                    var data = L2SystemCore.getItemData(itemLabel);
                    if (data != null)
                        pl.setGetItemIcon(data);
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Same progressive-item display fix for CostumeSetScript pickups.
    /// </summary>
    [HarmonyPatch(typeof(CostumeSetScript), "itemGetAction")]
    internal static class CostumeItemGetActionPatch
    {
        public static bool Enabled = false;

        static bool Prefix(CostumeSetScript __instance)
        {
            var sys = Traverse.Create(__instance).Field("sys").GetValue<L2System>();

            // Own glossary ROM → silent filler-style pickup (pre-empt the hold-up body below).
            if (GlossarySilentPickup.TryHandle(__instance, sys, "Costume")) return false;

            if (!Enabled) return true;

            var pl = Traverse.Create(__instance).Field("pl").GetValue<NewPlayer>();
            string itemLabel = __instance.itemLabel;

            int slotNo = Traverse.Create(__instance).Method("getL2Core").GetValue<L2SystemCore>().seManager.playSE(null, 39);
            Traverse.Create(__instance).Method("getL2Core").GetValue<L2SystemCore>().seManager.releaseGameObjectFromPlayer(slotNo);
            pl.setActionOder(PLAYERACTIONODER.getitem);

            if (itemLabel.Contains("Whip"))
            {
                short data = 0;
                sys.getFlag(2, "Whip", ref data);
                string trueItemName = data == 0 ? "Whip" : data == 1 ? "Whip2" : "Whip3";
                pl.setGetItem(ref trueItemName);
                pl.setGetItemIcon(L2SystemCore.getItemData(trueItemName));
            }
            else if (itemLabel.Contains("Shield"))
            {
                short data = 0;
                sys.getFlag(2, 196, ref data);
                string trueItemName = data == 0 ? "Shield" : data == 1 ? "Shield2" : "Shield3";
                pl.setGetItem(ref trueItemName);
                pl.setGetItemIcon(L2SystemCore.getItemData(trueItemName));
            }
            else if (itemLabel.StartsWith("AP Item"))
            {
                // AP placeholder — lookup via "AP Item" so GetItemDataApItemPatch
                // redirects to Holy Grail and sets LastWasApRedirect, allowing
                // SetGetItemIconApPatch to swap in the custom AP sprite.
                string displayName = "AP Item";
                pl.setGetItem(ref displayName);
                var iconData = L2SystemCore.getItemData("AP Item");
                if (iconData != null)
                    pl.setGetItemIcon(iconData);
            }
            else if (itemLabel.StartsWith("Coin") || itemLabel.StartsWith("Weight"))
            {
                string displayName = itemLabel;
                pl.setGetItem(ref displayName);
                var iconData = L2SystemCore.getItemData("Nothing");
                if (iconData != null)
                    pl.setGetItemIcon(iconData);
            }
            else
            {
                pl.setGetItem(ref itemLabel);
                if (itemLabel.Contains("Mantra"))
                    pl.setGetItemIcon(L2SystemCore.getItemData("Mantra"));
                else if (itemLabel.Contains("Research"))
                    pl.setGetItemIcon(L2SystemCore.getItemData("Research"));
                else if (itemLabel.Contains("Beherit"))
                    pl.setGetItemIcon(L2SystemCore.getItemData("Beherit"));
                else
                {
                    var data = L2SystemCore.getItemData(itemLabel);
                    if (data != null)
                        pl.setGetItemIcon(data);
                }
            }

            return false;
        }
    }
    /// <summary>
    /// Replaces DropItemScript.itemGetAction specifically for static pot AP drops.
    /// Converts a generic silent pickup into the full chest sequence (Hold up animation, SFX),
    /// but skips the native grant so AP can handle it securely without double-dialog crashing.
    /// </summary>
    [HarmonyPatch(typeof(DropItemScript), "itemGetAction")]
    internal static class DropItemGetActionPatch
    {
        public static bool Enabled = true;

        static bool Prefix(DropItemScript __instance)
        {
            if (!Enabled) return true;

            // Own glossary ROM (any position — incl. chest pop / dropItem): silent filler-style
            // pickup (floating popup, no get-item flow). Must run BEFORE the dropItem/FIX gate
            // below so chest drops are covered.
            var sysG = Traverse.Create(__instance).Field("sys").GetValue<L2System>();
            Plugin.Log.LogDebug($"[GLOSSARY/silent] DropItem label='{__instance.itemLabel}' dropItem={__instance.dropItem} pos={__instance.positionType}");
            if (GlossarySilentPickup.TryHandle(__instance, sysG, "DropItem"))
                return false;

            if (__instance.dropItem || __instance.positionType != AbstractItemBase.PositionType.FIX)
                return true;

            string itemLabel = __instance.itemLabel;
            if (string.IsNullOrEmpty(itemLabel) || itemLabel == "Nothing" || itemLabel == "Gold" || itemLabel == "Weight")
                return true;

            var sys = Traverse.Create(__instance).Field("sys").GetValue<L2System>();
            var pl = Traverse.Create(__instance).Field("pl").GetValue<NewPlayer>();
            var core = Traverse.Create(__instance).Method("getL2Core").GetValue<L2SystemCore>();

            int slotNo = core.seManager.playSE(null, 39);
            core.seManager.releaseGameObjectFromPlayer(slotNo);

            pl.setActionOder(PLAYERACTIONODER.getitem);

            // Strip numbered suffixes so setGetItem finds the correct 3D model
            string lookupName = itemLabel;
            if (lookupName.StartsWith("Ankh Jewel")) lookupName = "Ankh Jewel";
            else if (lookupName.StartsWith("Sacred Orb")) lookupName = "Sacred Orb";
            else if (lookupName.StartsWith("Crystal S")) lookupName = "Crystal S";
            else if (lookupName.StartsWith("Mantra")) lookupName = "Mantra";
            else if (lookupName.StartsWith("Research")) lookupName = "Research";
            else if (lookupName.StartsWith("Beherit")) lookupName = "Beherit";

            if (lookupName.Contains("Whip"))
            {
                short data = 0;
                sys.getFlag(2, "Whip", ref data);
                string trueItemName = data == 0 ? "Whip" : data == 1 ? "Whip2" : "Whip3";
                pl.setGetItem(ref trueItemName);
                pl.setGetItemIcon(L2SystemCore.getItemData(trueItemName));
            }
            else if (lookupName.Contains("Shield"))
            {
                short data = 0;
                sys.getFlag(2, 196, ref data);
                string trueItemName = data == 0 ? "Shield" : data == 1 ? "Shield2" : "Shield3";
                pl.setGetItem(ref trueItemName);
                pl.setGetItemIcon(L2SystemCore.getItemData(trueItemName));
            }
            else if (lookupName.StartsWith("AP Item"))
            {
                string displayName = "AP Item";
                pl.setGetItem(ref displayName);
                var iconData = L2SystemCore.getItemData("AP Item");
                if (iconData != null)
                    pl.setGetItemIcon(iconData);
            }
            else
            {
                // Pass the stripped name so it doesn't default to Shell Horn!
                pl.setGetItem(ref lookupName);

                if (lookupName.Contains("Mantra"))
                    pl.setGetItemIcon(L2SystemCore.getItemData("Mantra"));
                else if (lookupName.Contains("Research"))
                    pl.setGetItemIcon(L2SystemCore.getItemData("Research"));
                else if (lookupName.Contains("Beherit"))
                    pl.setGetItemIcon(L2SystemCore.getItemData("Beherit"));
                else
                {
                    var data = L2SystemCore.getItemData(lookupName);
                    if (data != null)
                        pl.setGetItemIcon(data);
                }
            }

            // CRITICAL: Set flags to trigger the AP Location check natively
            // AP will securely send us the item through the network queue
            if (__instance.itemGetFlags != null && __instance.itemGetFlags.Length > 0)
            {
                sys.setEffectFlag(__instance.itemGetFlags);
            }

            __instance.gameObject.SetActive(false);

            // CRITICAL: We strictly return false here to bypass sys.setItem completely.
            return false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  AP progression capture for pickup animation + item dialog
    //
    //  When a foreign player's item is collected, the pickup animation
    //  (NewPlayer.setGetItemIcon → SetGetItemIconApPatch) and the item dialog
    //  (ItemDialogApItemPatch.SetupDialogManually / ItemDialogPatch) show the
    //  custom AP icon, but they have no location context to pick the progressive
    //  ("up arrow") variant — matching how chests, pots and free-standing items
    //  already differentiate it.
    //
    //  These prefixes run *before* the (vanilla or replacement) itemGetAction
    //  body calls setGetItemIcon, recording whether the item being collected is
    //  a progression item.  They run unconditionally — unlike the Enabled-gated
    //  replacement patches above, which are off in normal AP mode — so the icon
    //  is correct in both normal AP and standalone modes.  They only read the
    //  item, never alter the pickup, so they compose safely with the gated
    //  replacement prefixes on the same methods.
    // ─────────────────────────────────────────────────────────────────────────
    internal static class ApPickupProgressionCapture
    {
        internal static void Capture(AbstractItemBase item)
        {
            if (item == null || string.IsNullOrEmpty(item.itemLabel)
                || !item.itemLabel.StartsWith("AP Item", System.StringComparison.Ordinal))
                return;

            ApIconClass iconClass =
                TreasureBoxSpritePatch.TryGetApLocation(item, out LocationID location)
                    ? CheckManager.GetApIconClassAt(location)
                    : ApIconClass.Plain;

            // Current drives the pickup animation, which plays before the dialog opens.
            // Pending hands the same answer to the dialog, which cannot re-derive it for
            // every pickup kind; ItemDialog's prefix consumes it and clears it.
            ItemDialogApItemPatch.CurrentApPickupIconClass = iconClass;
            ItemDialogApItemPatch.PendingApPickupIconClass = iconClass;
        }

        /// <summary>
        /// Re-applies the correct hold-up sprite AFTER the itemGetAction body has run its
        /// <c>setGetItemIcon</c> (line 27 of EventItemScript). The pickup-anim icon
        /// (<see cref="SetGetItemIconApPatch"/>) reads the pre-captured
        /// <see cref="ItemDialogApItemPatch.CurrentApPickupIconClass"/>, but that static
        /// loses an ordering race — measured behaviour is that the hold-up renders one
        /// pickup stale (icon N shows item N-1's class). Running as a Postfix guarantees we
        /// execute after that icon set, so we resolve the class fresh from THIS item and
        /// overwrite the player's hold-up renderer. No static-timing dependency.
        ///
        /// Resolves the item's AP location from its itemActiveFlag (chests/pots/freestanding).
        /// </summary>
        internal static void FixHoldupIcon(AbstractItemBase item)
        {
            if (!IsApHoldup(item)) return;
            ApIconClass iconClass =
                TreasureBoxSpritePatch.TryGetApLocation(item, out LocationID loc)
                    ? CheckManager.GetApIconClassAt(loc)
                    : ApIconClass.Plain;
            ApplyHoldupSprite(item, iconClass);
        }

        /// <summary>
        /// Same fix for callers that already hold the resolved location (glossary chips,
        /// whose location comes from the chip's book flag, not its itemActiveFlag).
        /// </summary>
        internal static void FixHoldupIconAt(AbstractItemBase item, LocationID loc)
        {
            if (!IsApHoldup(item)) return;
            ApplyHoldupSprite(item, CheckManager.GetApIconClassAt(loc));
        }

        // Only AP placeholders get the AP icon; own items (label = BoxName) and silent
        // pickups (label = "Nothing") keep their real / suppressed sprite.
        private static bool IsApHoldup(AbstractItemBase item) =>
            item != null && ApSpriteLoader.IsLoaded
            && !string.IsNullOrEmpty(item.itemLabel)
            && item.itemLabel.StartsWith("AP Item", System.StringComparison.Ordinal);

        private static void ApplyHoldupSprite(AbstractItemBase item, ApIconClass iconClass)
        {
            var pl = Traverse.Create(item).Field("pl").GetValue<NewPlayer>();
            if (pl == null) return;
            var renderer = Traverse.Create(pl).Field("itemRenderer").GetValue<SpriteRenderer>();
            if (renderer == null) return;
            renderer.sprite = ApSpriteLoader.GetMapSprite(iconClass);
        }
    }

    [HarmonyPatch(typeof(EventItemScript), "itemGetAction")]
    internal static class EventItemProgressionCapturePatch
    {
        static void Prefix(EventItemScript __instance) =>
            ApPickupProgressionCapture.Capture(__instance);

        static void Postfix(EventItemScript __instance) =>
            ApPickupProgressionCapture.FixHoldupIcon(__instance);
    }

    [HarmonyPatch(typeof(DropItemScript), "itemGetAction")]
    internal static class DropItemProgressionCapturePatch
    {
        static void Prefix(DropItemScript __instance) =>
            ApPickupProgressionCapture.Capture(__instance);

        static void Postfix(DropItemScript __instance) =>
            ApPickupProgressionCapture.FixHoldupIcon(__instance);
    }

    [HarmonyPatch(typeof(CostumeSetScript), "itemGetAction")]
    internal static class CostumeProgressionCapturePatch
    {
        static void Prefix(CostumeSetScript __instance) =>
            ApPickupProgressionCapture.Capture(__instance);

        static void Postfix(CostumeSetScript __instance) =>
            ApPickupProgressionCapture.FixHoldupIcon(__instance);
    }
}
