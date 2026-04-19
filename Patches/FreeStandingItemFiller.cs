using HarmonyLib;
using L2Base;
using LaMulana2Archipelago.Managers;
using LaMulana2Archipelago.Utils;
using LaMulana2RandomizerShared;
using System;
using UnityEngine;

namespace LaMulana2Archipelago.Patches
{
    [HarmonyPatch]
    internal static class FakeItemFillerIntercept
    {
        static Type TargetType() => AccessTools.TypeByName("LM2RandomiserMod.FakeItem");

        static System.Reflection.MethodBase TargetMethod()
            => AccessTools.Method(TargetType(), "Update");

        // We run BEFORE the original Update and skip it only for filler
        static bool Prefix(object __instance)
        {
            // Offline + "AP filler off" → let LM2's vanilla FakeItem behavior run
            // (evil tune, disappears) so legacy L2Rando seeds play as intended.
            if (!LaMulana2Archipelago.Archipelago.ArchipelagoClient.ApFillerActive)
                return true;

            try
            {
                if (__instance == null) return true;

                // Read required fields from FakeItem
                var sys = Traverse.Create(__instance).Field("sys").GetValue<L2System>();
                if (sys == null) return true;

                int flagNo = Traverse.Create(__instance).Field("flagNo").GetValue<int>();

                if (flagNo < 40 || flagNo > 79) return true;

                // If this FakeItem isn't currently "active" (spawned/visible), let original handle
                // The original checks sys.checkStartFlag(activeFlags) and overlap.
                // We replicate the exact same checks before intercepting.
                var activeFlags = Traverse.Create(__instance).Field("activeFlags").GetValue<object>();
                if (activeFlags != null)
                {
                    object okObj = Traverse.Create(sys).Method("checkStartFlag", activeFlags).GetValue();
                    if (okObj is bool ok && !ok)
                        return true;
                }

                // Replicate overlap logic (from FakeItem.cs)
                var player = sys.getPlayer();
                if (player == null) return true;

                Vector3 p = player.getPlayerPositon();
                p.y += 15f;

                Rect playerRect = Traverse.Create(__instance).Field("playerRect").GetValue<Rect>();
                Rect bounds = Traverse.Create(__instance).Field("bounds").GetValue<Rect>();

                playerRect.center = p;
                bounds.center = ((MonoBehaviour)__instance).transform.position;

                // Write back updated rects (original does this each update)
                Traverse.Create(__instance).Field("playerRect").SetValue(playerRect);
                Traverse.Create(__instance).Field("bounds").SetValue(bounds);

                if (!playerRect.Overlaps(bounds))
                    return true; // not picked up this frame

                // --- Filler Logic ---
                // FakeItems in AP use Sheet 31, Flags 231-270 (mapped from ItemIDs 231-270)
                int itemId = flagNo + 191;
                FillerRewardMap.GetReward(itemId, out int coinAmount, out int weightAmount, out string label);

                // Guarded dialog + grant
                var pl = sys.getPlayer();
                if (!ItemGrantStateGuard.IsSafe(sys, pl))
                    return false; // swallow; will try again next frame

                // Non-blocking popup above the player + SFX (no dialog, no pause).
                var popType = (coinAmount > 0)
                    ? ItemPopUpController.PopUpType.Coin
                    : ItemPopUpController.PopUpType.Weight;
                int popAmount = (coinAmount > 0) ? coinAmount : weightAmount;
                int se = (coinAmount > 0) ? 109 : 23;
                ItemGrantManager.ShowFillerPopUp(sys, popType, popAmount, se);

                // direct: false: we don't set the value, but only add.
                using (ItemGrantRecursiveGuard.Begin())
                {
                    if (coinAmount > 0)
                        sys.setItem("Gold", coinAmount, direct: false, loadcall: false, sub_add: true);
                    else
                        sys.setItem("Weight", weightAmount, direct: false, loadcall: false, sub_add: true);
                }

                // Mark collected so the FakeItem disappears permanently
                sys.setFlagData(31, flagNo, (short)1);

                // IMPORTANT: do NOT set (sheet=1, flag=12) for filler,
                // it triggers the vanilla fake-item pickup SFX after the dialog closes.
                // sys.setFlagData(1, 12, (short)1);

                // Explicit AP location report for filler FakeItems:
                // LocationFlagMap is missing entries because generator skips Coin/Weight BoxNames.
                try
                {
                    LocationID loc = LocationID.None;

                    // 1) best: seed-derived mapping
                    if (SeedFlagMapBuilder.FakeItemFlagToLocation.TryGetValue(flagNo, out var seeded))
                        loc = seeded;

                    // 2) fallback: existing numeric map (usually missing for filler)
                    if (loc == LocationID.None && LocationFlagMap.TryGetNumeric(31, flagNo, out var mapped))
                        loc = mapped;

                    if (loc != LocationID.None)
                        CheckManager.NotifyLocation(loc);
                    else
                        Plugin.Log.LogDebug($"[CHECK] Could not resolve location for FakeItem flag (31,{flagNo})");
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning("[CHECK] FakeItem filler location report failed: " + ex);
                }
                // Disable the object like original
                ((MonoBehaviour)__instance).gameObject.SetActive(false);

                // Skip original Update (we already handled it)
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[FILLER] FakeItemFillerIntercept failed: " + ex);
                return true; // fail open
            }
        }

        private static void PlaySe(L2System sys, int seNo)
        {
            try
            {
                var core = sys.getL2SystemCore();
                if (core?.seManager == null) return;

                int handle = core.seManager.playSE(null, seNo);
                core.seManager.releaseGameObjectFromPlayer(handle);
            }
            catch { }
        }
    }
}