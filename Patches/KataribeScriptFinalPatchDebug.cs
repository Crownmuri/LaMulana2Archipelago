using HarmonyLib;
using L2Base;
using L2Task;
using UnityEngine;

namespace LaMulana2Archipelago.Debug
{
    [HarmonyPatch(typeof(KataribeScript), "Farst")]
    internal static class KataribeScriptStateDebugPatch
    {
        private static int lastSta = -1;

        static void Prefix(KataribeScript __instance)
        {
            int sta = (int)AccessTools
                .Field(typeof(KataribeScript), "sta")
                .GetValue(__instance);

            if (sta != lastSta)
            {
                lastSta = sta;
                Plugin.Log.LogInfo($"[KATARIBE DEBUG] sta = {sta}");
            }
        }
    }

    [HarmonyPatch(typeof(KataribeScript), nameof(KataribeScript.setMess))]
    internal static class KataribeScriptSetMessDebugPatch
    {
        static void Prefix(string mess)
        {
            Plugin.Log.LogInfo($"[KATARIBE DEBUG] setMess('{mess}')");
        }
    }

    [HarmonyPatch(typeof(KataribeScript), "script_run")]
    internal static class KataribeScriptRunDebugPatch
    {
        static int counter = 0;

        static void Prefix()
        {
            counter++;
            if (counter % 100 == 0)
            {
                Plugin.Log.LogInfo($"[KATARIBE DEBUG] script_run iterations = {counter}");
            }
        }
    }

    [HarmonyPatch(typeof(KataribeScript), "Farst")]
    internal static class KataribeScriptFlagDebugPatch
    {
        static void Postfix(KataribeScript __instance)
        {
            int sta = (int)AccessTools
                .Field(typeof(KataribeScript), "sta")
                .GetValue(__instance);

            if (sta == 200 || sta == 205)
            {
                var sys = (L2System)AccessTools
                    .Field(typeof(L2TaskSystemBase), "sys")
                    .GetValue(__instance);

                Plugin.Log.LogInfo(
                    $"[KATARIBE DEBUG] ITDLRBLOCK={sys.getSysFlag(SYSTEMFLAG.ITDLRBLOCK)} " +
                    $"DRAMATEJI={sys.getSysFlag(SYSTEMFLAG.DRAMATEJI)}"
                );
            }
        }
    }
}