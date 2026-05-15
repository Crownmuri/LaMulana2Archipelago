using HarmonyLib;
using L2Base;
using L2Menu;
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
    ///   script_run (queues flags + items) → calldialog → StartSwitch (dialog opens)
    ///   ... player dismisses dialog ...
    ///   runToMojiScFlagQ → setFlagData → CheckManager → setItem
    ///
    /// CheckManager fires too late (after dialog dismissed), so we prime here instead.
    /// For AP placeholders (all share BoxName "AP Item"), we read the pending
    /// sheet-31 flag from the MenuSystem flag queue to identify the correct location.
    /// </summary>
    [HarmonyPatch]
    public static class KataribeDialogPatch
    {
        public static long LastPrimedApLocationId = -1L;

        // Reflection cache for MenuSystem.flagq / flagq_count
        private static FieldInfo _menusysField;
        private static FieldInfo _flagqField;
        private static FieldInfo _flagqCountField;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(KataribeScript), "calldialog")]
        private static void CalldialogPrefix(KataribeScript __instance)
        {
            if (!ArchipelagoClient.Authenticated && !ArchipelagoClient.OfflineMode) return;

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

            long apLocationId = -1L;
            LocationID location = LocationID.None;

            if (rawItemName == "AP Item")
            {
                // AP placeholders all share BoxName "AP Item" — BoxNameToLocation
                // only stores the first one so it can't distinguish between NPCs.
                // Instead, read the pending sheet-31 flag from the MenuSystem queue
                // (script_run already queued it before calldialog runs).
                int flagIndex = FindPendingSheet31Flag(sys);
                if (flagIndex < 0) return;

                if (!LocationFlagMap.TryGetNumeric(31, flagIndex, out location))
                    return;

                apLocationId = 430000L + (int)location;
            }
            else
            {
                // NEW: Intercept NPC Money/Filler before BoxName lookup
                int queuedFlag = FindPendingSheet31Flag(sys);

                // Check if the queued flag is in the NPC Money range (80-89)
                if (queuedFlag >= 80 && queuedFlag <= 89)
                {
                    // Resolve the specific NPC directly from the flag
                    if (SeedFlagMapBuilder.NpcMoneyFlagToLocation.TryGetValue(queuedFlag, out location))
                    {
                        apLocationId = 430000L + (int)location;
                    }
                }

                // Fallback: If it's not an NPC money flag, use the standard BoxName lookup
                if (apLocationId == -1L)
                {
                    if (!SeedFlagMapBuilder.BoxNameToLocation.TryGetValue(rawItemName, out location))
                    {
                        // Ankh jewels only: fall back to generic "Ankh Jewel" key.
                        string normalized = null;
                        if (sys.isAnkJewel(rawItemName)) normalized = "Ankh Jewel";

                        if (normalized == null || !SeedFlagMapBuilder.BoxNameToLocation.TryGetValue(normalized, out location))
                        {
                            return;
                        }
                    }
                    apLocationId = 430000L + (int)location;
                }
            }

            // Prefer the scout cache (online) and fall back to seed.lm2ap's
            // location_labels in offline mode, where the cache is empty.
            // Either way we mark LastPrimedApLocationId so CheckManager skips
            // the late prime and we avoid the "another label already pending"
            // collision on a same-frame pickup.
            var scouted = client.GetItemAtLocation(apLocationId);
            string label;

            if (scouted != null)
            {
                bool isForOtherPlayer = scouted.PlayerName != ArchipelagoClient.ServerData.SlotName;

                ItemDialogPatch.PendingDisplayLabel = scouted.ItemName;
                if (isForOtherPlayer)
                    ItemDialogPatch.PendingRecipientName = scouted.PlayerName;

                label = isForOtherPlayer
                    ? scouted.ItemName + " (" + scouted.PlayerName + ")"
                    : scouted.ItemName;
            }
            else if (SceneRandomizer.Instance != null)
            {
                string apLabel = SceneRandomizer.Instance.GetLabelForLocation(location);
                if (string.IsNullOrEmpty(apLabel)) return;

                ItemDialogPatch.PendingDisplayLabel = apLabel;
                label = apLabel;
            }
            else
            {
                return;
            }

            LastPrimedApLocationId = apLocationId;
            Plugin.Log.LogInfo("[KataribePatch] Dialog label primed early: \"" + label + "\"");
        }

        /// <summary>
        /// Scans MenuSystem.flagq for a pending sheet-31 flag entry.
        /// Returns the flag index, or -1 if none found.
        /// </summary>
        private static int FindPendingSheet31Flag(L2System sys)
        {
            try
            {
                // L2System.menusys (private MenuSystem)
                if (_menusysField == null)
                    _menusysField = typeof(L2System).GetField("menusys",
                        BindingFlags.Instance | BindingFlags.NonPublic);

                object menusys = _menusysField?.GetValue(sys);
                if (menusys == null) return -1;

                // MenuSystem.flagq (private MojiScFlagQ[])
                if (_flagqField == null)
                    _flagqField = menusys.GetType().GetField("flagq",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                if (_flagqCountField == null)
                    _flagqCountField = menusys.GetType().GetField("flagq_count",
                        BindingFlags.Instance | BindingFlags.NonPublic);

                var flagq = (MojiScFlagQ[])_flagqField?.GetValue(menusys);
                int count = (int)(_flagqCountField?.GetValue(menusys) ?? 0);

                if (flagq == null || count <= 0) return -1;

                // Scan backwards — the most recent sheet-31 entry is the one for
                // the item that was just queued by script_run.
                for (int i = count - 1; i >= 0; i--)
                {
                    if (flagq[i].flag_sheet == 31)
                        return flagq[i].flag_name;
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("[KataribePatch] Failed to read flag queue: " + ex.Message);
            }

            return -1;
        }
    }
}
