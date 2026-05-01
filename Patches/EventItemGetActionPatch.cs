using HarmonyLib;
using L2Base;
using L2Hit;

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
            if (!Enabled) return true;

            var sys = Traverse.Create(__instance).Field("sys").GetValue<L2System>();
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
            if (!Enabled) return true;

            var sys = Traverse.Create(__instance).Field("sys").GetValue<L2System>();
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
}
