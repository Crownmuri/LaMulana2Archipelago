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