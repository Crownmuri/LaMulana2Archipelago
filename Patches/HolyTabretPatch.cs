using HarmonyLib;
using L2Base;
using UnityEngine;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Replaces HolyTabretScript.memorySave to support the auto-scan tablets
    /// setting without requiring the original L2Rando MonoBehaviour.
    /// </summary>
    [HarmonyPatch(typeof(HolyTabretScript), nameof(HolyTabretScript.memorySave))]
    internal static class HolyTabretPatch
    {
        public static bool Enabled = false;
        public static bool AutoScanTablets = false;

        static bool Prefix(HolyTabretScript __instance, ref bool __result)
        {
            if (!Enabled) return true;

            var sys = Traverse.Create(__instance).Field("sys").GetValue<L2System>();
            var sysCore = Traverse.Create(__instance).Field("sysCore").GetValue<L2SystemCore>();
            var memsaveParticle = Traverse.Create(__instance).Field("memsaveParticle").GetValue<ParticleSystem>();

            short num = 0;
            if (!sys.getFlag(__instance.sheetNo, __instance.flagNo, ref num))
            {
                __result = false;
                return false;
            }

            if (num < 1)
            {
                if (AutoScanTablets)
                {
                    sys.setFlagData(__instance.sheetNo, __instance.flagNo, 1);
                }
                else
                {
                    __result = false;
                    return false;
                }
            }

            if (sys.getItemNum("Holy Grail") <= 0)
            {
                __result = false;
                return false;
            }

            sysCore.seManager.playSE(__instance.gameObject, 143);
            sys.memSave(__instance.transform.position.x, __instance.transform.position.y, __instance.warpPointNo);

            __instance.clearSaveParticle();
            memsaveParticle.Play();

            __result = true;
            return false;
        }
    }
}
