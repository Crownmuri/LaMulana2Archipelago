using HarmonyLib;
using L2Base;
using LaMulana2Archipelago.Managers;
using LaMulana2RandomizerShared;
using UnityEngine;

namespace LaMulana2Archipelago.Patches
{
    // Patch the exact overload: setFlagData(int seet_no, int flag_no, short data)
    [HarmonyPatch(typeof(L2System), "setFlagData", new[] { typeof(int), typeof(int), typeof(short) })]
    internal static class NpcMoneyFlagPatch
    {
        private const int NpcMoneySheet = 31;
        private const int NpcMoneyFlagMin = 80;
        private const int NpcMoneyFlagMax = 89;

        static bool Prefix(int seet_no, int flag_no, short data)
        {
            // 1) Report NPCMoney checks when the container flag is set
            if (seet_no == NpcMoneySheet && flag_no >= NpcMoneyFlagMin && flag_no <= NpcMoneyFlagMax && data == 1)
            {
                try
                {
                    if (SeedFlagMapBuilder.NpcMoneyFlagToLocation.TryGetValue(flag_no, out var loc) && loc != LocationID.None)
                    {
                        CheckManager.NotifyLocation(loc);
                    }
                    else
                    {
                        Plugin.Log.LogDebug($"[CHECK] No NpcMoney seed mapping for (31,{flag_no})");
                    }
                }
                catch (System.Exception ex)
                {
                    Plugin.Log.LogWarning("[CHECK] NPCMoney location report failed: " + ex);
                }

                return true; // allow flag write
            }
            return true;
        }
    }
}