using HarmonyLib;
using L2Base;
using LaMulana2Archipelago.Archipelago;
using LaMulana2Archipelago.Managers;
using LaMulana2RandomizerShared;
using System.Reflection;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Primes ItemDialogPatch.PendingDisplayLabel before KataribeScript.calldialog
    /// calls StartSwitch(), so NPC-given items show their AP label correctly.
    ///
    /// For NPC items, the flow is:
    ///   calldialog → StartSwitch (dialog opens) → setItem → flag set → CheckManager
    /// CheckManager fires too late (dialog already open), so we prime here instead.
    /// </summary>
    [HarmonyPatch]
    public static class KataribeDialogPatch
    {
        public static long LastPrimedApLocationId = -1L;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(KataribeScript), "calldialog")]
        private static void CalldialogPrefix(KataribeScript __instance)
        {
            if (!ArchipelagoClient.Authenticated) return;

            // Mirror the game's own condition — only act when the player confirms.
            // calldialog is called every frame; without this gate we prime on every frame.
            L2System sys = (L2System)ItemDialogPatch
                .FindFieldInHierarchy(typeof(KataribeScript), "sys")
                ?.GetValue(__instance);
            if (sys == null) return;
            if (!sys.getL2Keys(L2KEYS.ok, KEYSTATE.DOWN)
                && !sys.getL2Keys(L2KEYS.cancel, KEYSTATE.DOWN))
                return;

            var client = ArchipelagoClientProvider.Client;
            if (client == null) return;

            // Read itemIdBuff and itemIdBuff_counter via reflection.
            string[] itemIdBuff = (string[])ItemDialogPatch
                .FindFieldInHierarchy(typeof(KataribeScript), "itemIdBuff")
                ?.GetValue(__instance);

            if (itemIdBuff == null) return;

            FieldInfo counterField = ItemDialogPatch
                .FindFieldInHierarchy(typeof(KataribeScript), "itemIdBuff_counter");
            if (counterField == null) return;

            int counter = (int)counterField.GetValue(__instance);
            if (counter < 0 || counter >= itemIdBuff.Length) return;

            string rawItemName = itemIdBuff[counter];
            if (string.IsNullOrEmpty(rawItemName)) return;

            // Look up the AP location for this item name.
            LocationID location;
            if (!SeedFlagMapBuilder.BoxNameToLocation.TryGetValue(rawItemName, out location))
            {
                // Ankh jewels only: fall back to generic "Ankh Jewel" key.
                // Maps must NOT fall back since all maps share BoxName "Map" — enum name is the correct key.
                string normalized = null;
                if (sys.isAnkJewel(rawItemName)) normalized = "Ankh Jewel";

                if (normalized == null || !SeedFlagMapBuilder.BoxNameToLocation.TryGetValue(normalized, out location))
                {
                    return;
                }
            }

            long apLocationId = 430000L + (int)location;

            // Don't prime if this location was already reported (dedup).
            // CheckManager will still send the check; we only skip the dialog prime.
            var scouted = client.GetItemAtLocation(apLocationId);
            if (scouted == null) return;

            string label = scouted.PlayerName != ArchipelagoClient.ServerData.SlotName
                ? scouted.ItemName + " (" + scouted.PlayerName + ")"
                : scouted.ItemName;

            LastPrimedApLocationId = apLocationId;
            ItemDialogPatch.PendingDisplayLabel = label;
            Plugin.Log.LogInfo("[KataribePatch] Dialog label primed early: \"" + label + "\"");
        }
    }
}