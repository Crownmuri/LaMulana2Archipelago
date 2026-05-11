using HarmonyLib;
using UnityEngine;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Fixes a stuck curtain/transition animation that can occur in specific
    /// entrance-shuffle situations. When <c>setAsyncScene</c> runs but no
    /// <c>AsyncOperation</c> is actually started (asOpe stays null), the
    /// pre-instantiated curtain object is left behind and never cleaned up,
    /// leaving the screen visually frozen mid-transition. Destroy it in that
    /// case so the transition can recover.
    /// </summary>
    [HarmonyPatch(typeof(L2SystemCore), nameof(L2SystemCore.setAsyncScene))]
    internal static class AsyncScenePatch
    {
        [HarmonyPostfix]
        private static void Postfix(L2SystemCore __instance)
        {
            var T = Traverse.Create(__instance);
            if (T.Field("asOpe").GetValue<AsyncOperation>() is null)
            {
                var cartenObj = T.Field("cartenObj").GetValue<GameObject>();
                if (cartenObj is not null)
                    Object.Destroy(cartenObj);
            }
        }
    }
}
