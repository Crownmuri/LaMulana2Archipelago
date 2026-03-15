using System;
using HarmonyLib;
using L2Base;
using LaMulana2Archipelago.Managers;
using UnityEngine;

namespace LaMulana2Archipelago.Patches
{
    [HarmonyPatch(typeof(EventItemScript), "itemGetAction")]
    internal static class EventItemFillerPatch
    {
        private const int SeFillerCoin = 109;
        private const int SeFillerWeight = 23;

        static bool Prefix(EventItemScript __instance)
        {
            try
            {
                if (__instance == null) return true;

                // EventItemScript uses base fields "sys" and "pl" (protected in AbstractItemBase)
                var sys = Traverse.Create(__instance).Field("sys").GetValue<L2System>();
                var pl = Traverse.Create(__instance).Field("pl").GetValue<NewPlayer>();
                if (sys == null || pl == null) return true;

                // Only intercept L2Rando filler remaps (CoinX / WeightX)
                string label = __instance.itemLabel;
                if (string.IsNullOrEmpty(label)) return true;

                int coinAmount = 0;
                int weightAmount = 0;

                switch (label)
                {
                    case "Coin1": coinAmount = 1; break;
                    case "Coin10": coinAmount = 10; break;
                    case "Coin30": coinAmount = 30; break;

                    case "Weight1": weightAmount = 1; break;
                    case "Weight5": weightAmount = 5; break;
                    case "Weight10": weightAmount = 10; break;

                    default:
                        return true; // not filler; AP Item and traps stay vanilla
                }

                // Guarded (same philosophy as your AP grant)
                if (!ItemGrantStateGuard.IsSafe(sys, pl))
                    return false; // swallow to avoid breaking state; item will try again

                // Play desired SE (no get-item jingle)
                PlaySe(sys, coinAmount > 0 ? SeFillerCoin : SeFillerWeight);

                // Open item dialog with no image (your ItemDialogApItemPatch should redirect)
                var dlg = sys.getMenuObjectNF(1);
                dlg.setMess(label);  // e.g. "Coin30" / "Weight5"
                dlg.setMess("");
                sys.openItemDialog();

                // Apply reward without GETITEM animation
                using (ItemGrantRecursiveGuard.Begin())
                {
                    if (coinAmount > 0)
                    {
                        // matches your existing no-animation grant approach
                        sys.setItem("Gold", coinAmount, direct: false, loadcall: false, sub_add: false);
                    }
                    else
                    {
                        sys.setItem("Weight", weightAmount, direct: false, loadcall: false, sub_add: false);
                    }
                }

                // Skip vanilla EventItemScript.itemGetAction (prevents animation + SE 39)
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[FILLER] EventItemFillerPatch failed: " + ex);
                return true; // fail open (better to get vanilla than break pickup)
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