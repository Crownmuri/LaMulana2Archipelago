#if LEGACY
using HarmonyLib;
using L2Base;
using LaMulana2Archipelago.Archipelago;
using LaMulana2Archipelago.Managers;
using LaMulana2RandomizerShared;
using LM2RandomiserMod;
using UnityEngine;
using System.Collections.Generic;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Intercepts mural scans (Snap Software) to prevent duplicate item grants
    /// for locations already collected in Archipelago.
    /// </summary>
    [HarmonyPatch(typeof(SnapMenu), "Farst")]
    public static class SnapMenuPatch
    {
        private const long BaseApLocationId = 430000;

        [HarmonyPrefix]
        public static bool Prefix(SnapMenu __instance, ref bool __result)
        {
            var t = Traverse.Create(__instance);

            // State 2 is the 'Scanning' state. We intercept when the animation finishes.
            int sta = t.Field("sta").GetValue<int>();
            if (sta != 2) return true;

            // Wait for the scan animation to reach the end
            var con = t.Field("con").GetValue<SnapMenuController>();
            if (con == null || con.anime == null) return true;
            if (con.anime.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f) return true;

            // In your game version, the target is resolved via the ScrollSystem
            var l2Core = t.Field("L2Core").GetValue<L2SystemCore>();
            if (l2Core == null || l2Core.ScrollSystem == null) return true;

            // Resolve the mural being scanned
            var snapTarget = l2Core.ScrollSystem.getSnapShotTarget();
            if (snapTarget == null || snapTarget.mode != SnapShotTargetScript.SnapShotMode.SOFTWARE) return true;

            // Only apply AP logic if the randomizer is active
            var l2Rando = Object.FindObjectOfType<L2Rando>();
            if (l2Rando == null || !l2Rando.IsRandomising) return true;

            // Determine the Archipelago Location ID
            LocationID locationId = l2Rando.GetLocationIDForMural(snapTarget);
            if (locationId == (LocationID)0) return true; // (LocationID)0 is Nothing

            long apLocationId = BaseApLocationId + (int)locationId;

            // Check if already collected on server OR reported locally in this session
            bool alreadyCollected = ArchipelagoClient.ServerData.CheckedLocations.Contains(apLocationId);

            if (!alreadyCollected)
            {
                // Access CheckManager's private reported cache to catch immediate double-scans
                var reported = Traverse.Create(typeof(CheckManager)).Field("reportedLocations").GetValue<HashSet<long>>();
                if (reported != null && reported.Contains(apLocationId))
                {
                    alreadyCollected = true;
                }
            }

            if (alreadyCollected)
            {
                Plugin.Log.LogInfo($"[SnapPatch] Mural {locationId} (AP {apLocationId}) already collected. Suppressing duplicate grant.");

                // Replicate the visual transition for "Already Have"
                var sys = t.Field("sys").GetValue<L2System>();
                if (sys != null) sys.setKeyBlock(false);

                con.anime.Play("snap scanEnd");
                l2Core.seManager.playSE(null, 37); // Success beep

                // Set SnapMenu fields to trigger the "Already Have" UI sequence
                t.Field("SnapShotTargetSc").SetValue(snapTarget);
                t.Field("HaveItems").SetValue(true);
                t.Field("DrawBinalyCount").SetValue(0);
                t.Field("sta").SetValue(5); // Move to binary text animation

                __result = true; // Set return value for Farst()
                return false;    // Skip original method to prevent setItem call
            }

            // Not collected yet: let the original code run and call setItem
            return true;
        }
    }
}
#else
using HarmonyLib;
using L2Base;
using L2Word;
using LaMulana2Archipelago.Archipelago;
using LaMulana2Archipelago.Managers;
using LaMulana2RandomizerShared;
using LM2RandomiserMod;
using UnityEngine;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Standalone mural scan patch — intercepts SnapMenu.Farst() at state 2
    /// (scan animation finished, SOFTWARE mural detected) to grant the
    /// randomized item instead of the vanilla hardcoded one.
    ///
    /// Replicates the original randomizer's patched_SnapMenu.Farst() case 2
    /// SOFTWARE branch, using SceneRandomizer for item lookups.
    /// </summary>
    [HarmonyPatch(typeof(SnapMenu), "Farst")]
    public static class SnapMenuPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(SnapMenu __instance, ref bool __result)
        {
            if (SceneRandomizer.Instance == null || !SceneRandomizer.Instance.IsRandomising)
                return true;

            var t = Traverse.Create(__instance);
            int sta = t.Field("sta").GetValue<int>();
            if (sta != 2) return true;

            // Wait for scan animation to finish
            var con = t.Field("con").GetValue<SnapMenuController>();
            if (con == null || con.anime == null) return true;
            if (con.anime.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f) return true;

            var l2Core = t.Field("L2Core").GetValue<L2SystemCore>();
            if (l2Core == null || l2Core.ScrollSystem == null) return true;

            // Execute the shared scan-end steps that vanilla does before mode check
            var sys = t.Field("sys").GetValue<L2System>();
            sys.setKeyBlock(false);
            con.anime.Play("snap scanEnd");

            var snapTarget = l2Core.ScrollSystem.getSnapShotTarget();
            l2Core.seManager.playSE(null, 37);

            if (snapTarget == null)
            {
                // No target — set sta=3 (NoContents) like vanilla
                t.Field("sta").SetValue(3);
                __result = true;
                return false;
            }

            // MESSAGE mode: let vanilla handle (but we already did the shared steps)
            if (snapTarget.mode == SnapShotTargetScript.SnapShotMode.MESSAGE)
            {
                con.Contents.SetActive(true);
                con.ContentsText.text = sys.getMojiText(false, snapTarget.sheetName, snapTarget.cellName, L2Word.mojiScriptType.sekihi);
                t.Field("sta").SetValue(4);
                __result = true;
                return false;
            }

            // Only handle SOFTWARE mode
            if (snapTarget.mode != SnapShotTargetScript.SnapShotMode.SOFTWARE)
            {
                t.Field("sta").SetValue(3);
                __result = true;
                return false;
            }

            // --- SOFTWARE branch: grant randomized item ---
            sys.setKeyBlock(true);

            // Look up randomized item for this mural
            LocationID locationID = SceneRandomizer.Instance.GetLocationIDForMural(snapTarget);

            // --- Duplicate prevention: skip grant if already collected ---
            if (locationID != LocationID.None &&
                (ArchipelagoClient.Authenticated || ArchipelagoClient.OfflineMode))
            {
                long apLocationId = 430000L + (int)locationID;
                bool alreadyCollected = ArchipelagoClient.ServerData.CheckedLocations.Contains(apLocationId)
                    || CheckManager.IsLocationReported(apLocationId);

                if (alreadyCollected)
                {
                    Plugin.Log.LogInfo($"[SnapPatch] Mural {locationID} (AP {apLocationId}) already collected. Suppressing duplicate grant.");

                    con.anime.Play("snap scanEnd");
                    l2Core.seManager.playSE(null, 37);

                    t.Field("SnapShotTargetSc").SetValue(snapTarget);
                    t.Field("HaveItems").SetValue(true);
                    t.Field("DrawBinalyCount").SetValue(0);
                    t.Field("sta").SetValue(5);

                    __result = true;
                    return false;
                }
            }

            // If unknown mural or missing item data, run vanilla SOFTWARE logic
            ItemID itemID = locationID != LocationID.None
                ? SceneRandomizer.Instance.GetItemIDForLocation(locationID)
                : ItemID.None;
            ItemInfo itemInfo = itemID != ItemID.None ? ItemDB.GetItemInfo(itemID) : null;

            if (itemInfo == null)
            {
                // Vanilla SOFTWARE path: grant the hardcoded item
                var item = L2SystemCore.getItemData(snapTarget.itemName);
                string vanillaId = item.getItemId();
                bool vanillaHave = sys.isHaveItem(vanillaId) > 0;
                if (!vanillaHave)
                    sys.setItem(vanillaId, 1, true, false, true);

                t.Field("SnapShotTargetSc").SetValue(snapTarget);
                t.Field("GetItemID").SetValue(vanillaId);
                t.Field("HaveItems").SetValue(vanillaHave);
                t.Field("DrawBinalyCount").SetValue(0);
                t.Field("sta").SetValue(5);
                __result = true;
                return false;
            }

            // Resolve the display name for the dialog
            string getItemID = itemInfo.BoxName;

            // Check if the player already has this item
            bool haveItems = false;
            int flagValue = 0;
            if (itemID == ItemID.MobileSuperx3P)
                flagValue = 1;

            if (itemInfo.BoxName.Contains("Research") || itemInfo.BoxName.Equals("Nothing") || itemInfo.BoxName.Contains("Beherit"))
            {
                short data = 0;
                sys.getFlag(itemInfo.ItemSheet, itemInfo.ItemFlag, ref data);
                if (data > 0)
                    haveItems = true;
            }
            else if (itemInfo.BoxName.StartsWith("AP Item"))
            {
                // AP placeholder items are never "already owned"
                haveItems = false;
            }
            else if (itemInfo.BoxName.StartsWith("Coin") || itemInfo.BoxName.StartsWith("Weight"))
            {
                // Filler items are never "already owned"
                haveItems = false;
            }
            else if (sys.isHaveItem(itemInfo.ShopName) > flagValue)
            {
                haveItems = true;
            }

            // Grant the item if not already owned
            if (!haveItems)
            {
                if (!itemInfo.BoxName.Equals("Nothing"))
                    sys.setItem(getItemID, 1, false, false, true);

                sys.setEffectFlag(SceneRandomizer.Instance.CreateGetFlags(itemID, itemInfo));
            }

            // Set up fields for the binary text animation (state 5→6→dialog)
            t.Field("SnapShotTargetSc").SetValue(snapTarget);
            t.Field("GetItemID").SetValue(getItemID);
            t.Field("HaveItems").SetValue(haveItems);
            t.Field("DrawBinalyCount").SetValue(0);
            t.Field("sta").SetValue(5);

            __result = true;
            return false; // skip original Farst — we handled state 2
        }
    }
}
#endif
