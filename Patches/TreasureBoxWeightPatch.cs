using System;
using System.Reflection;
using HarmonyLib;
using L2Base;
using LaMulana2Archipelago.Managers;
using LaMulana2Archipelago.Utils;
using LaMulana2RandomizerShared;
using UnityEngine;

namespace LaMulana2Archipelago.Patches
{
    [HarmonyPatch(typeof(TreasureBoxScript), "openBox")]
    internal static class TreasureBoxWeightPatch
    {
        [HarmonyPrefix]
        static bool Prefix(TreasureBoxScript __instance)
        {
            if (__instance == null || __instance.itemObj == null) return true;

            var item = __instance.itemObj.GetComponent<AbstractItemBase>();
            if (item == null) return true;

            // Offline + "AP filler off" → let LM2 handle ChestWeight normally
            // (always drops 1 weight) so legacy L2Rando seeds play as intended.
            if (!LaMulana2Archipelago.Archipelago.ArchipelagoClient.ApFillerActive)
                return true;

            // 1. ONLY intercept L2Rando filler chests
            bool isFiller = item.itemLabel.StartsWith("Coin") || item.itemLabel.StartsWith("Weight");
            if (!isFiller) return true; // Let vanilla/other mods handle real items!

            // Setup system access
            L2SystemCore core = GameObject.Find("SystemMain").GetComponent<L2SystemCore>();
            L2System sys = core.L2Sys;

            // 2. Get the specific index for this chest
            int idx = item.itemValue;

            // 3. THE GUARD: Check if already opened (solves the L2Rando clone double-trigger)
            short flagVal = 0;
            sys.getFlag(31, idx, ref flagVal);

            if (flagVal != 0)
            {
                // Kill the loop and skip the vanilla method
                Traverse.Create(__instance).Field("sta").SetValue(7);
                return false;
            }

            // 4. Resolve the Archipelago Location using your dictionary
            LocationID loc = LocationID.None;
            if (SeedFlagMapBuilder.ChestWeightFlagToLocation.TryGetValue(idx, out LocationID foundLoc))
            {
                loc = foundLoc;
            }

            // 5. Set the flag and state IMMEDIATELY
            // This guarantees the clone object firing next frame will hit the guard above.
            sys.setFlagData(31, idx, 1);
            Traverse.Create(__instance).Field("sta").SetValue(7);

            // 6. Replicate vanilla visual side-effects (from your original code)
            var openState = Traverse.Create(__instance).Field("openState").GetValue<string>();
            var myAnime = Traverse.Create(__instance).Field("myAnime").GetValue<Animator>();
            if (myAnime != null)
            {
                if (!string.IsNullOrEmpty(openState))
                {
                    myAnime.enabled = true;
                    myAnime.Play(openState);
                }
                else
                {
                    myAnime.enabled = false;
                }
            }

            // 7. Calculate and Execute Physical Drop
            FillerRewardMap.GetReward(191 + idx, out int coinAmount, out int weightAmount, out _);

            Vector3 pos = Traverse.Create(__instance).Field("actionPosition").GetValue<Vector3>();
            pos.z -= 5f;

            object dropGen = Traverse.Create(core).Property("dropItemGenerator").GetValue()
                          ?? Traverse.Create(core).Field("dropItemGen").GetValue();

            if (dropGen != null)
            {
                if (coinAmount > 0)
                {
                    if (InvokeDrop(dropGen, "dropCoins", pos, coinAmount))
                        Plugin.Log.LogDebug($"[CHEST] Dropped {coinAmount} coin(s) from idx={idx}");
                }
                else if (weightAmount > 0)
                {
                    if (InvokeDrop(dropGen, "dropWeight", pos, weightAmount))
                        Plugin.Log.LogDebug($"[CHEST] Dropped {weightAmount} weight(s) from idx={idx}");
                }
            }

            // 8. Notify Archipelago Server
            if (loc != LocationID.None)
            {
                CheckManager.NotifyLocation(loc);
            }

            // Skip original openBox()
            return false;
        }

        private static bool InvokeDrop(object dropGen, string methodName, Vector3 pos, int amount)
        {
            try
            {
                Type t = dropGen.GetType();
                Type refVec3 = typeof(Vector3).MakeByRefType();

                // Prefer the real LM2 signature: (ref Vector3, int)
                MethodInfo mi = AccessTools.Method(t, methodName, new[] { refVec3, typeof(int) });
                if (mi != null)
                {
                    mi.Invoke(dropGen, new object[] { pos, amount });
                    return true;
                }

                // Fallback if some build uses (Vector3, int)
                mi = AccessTools.Method(t, methodName, new[] { typeof(Vector3), typeof(int) });
                if (mi != null)
                {
                    mi.Invoke(dropGen, new object[] { pos, amount });
                    return true;
                }

                Plugin.Log.LogWarning($"[CHEST] Could not find {t.Name}.{methodName} overload");
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[CHEST] InvokeDrop failed for {methodName}: {ex}");
                return false;
            }
        }
    }
}