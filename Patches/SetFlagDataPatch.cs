using HarmonyLib;
using L2Base;
using L2Flag;
using LaMulana2Archipelago.Managers;

namespace LaMulana2Archipelago.Patches
{
    [HarmonyPatch(
        typeof(L2FlagSystem),
        nameof(L2FlagSystem.setFlagData),
        new System.Type[] { typeof(int), typeof(int), typeof(short) }
    )]
    internal static class SetFlagDataFlagSystemPatch
    {
        static void Postfix(int seet_no, int flag_no, short data)
        {
            if (data > 0)
                Plugin.Log.LogDebug($"[FLAGSET] sheet={seet_no} flag={flag_no} data={data}");

            CheckManager.NotifyNumericFlag(seet_no, flag_no, data);
        }
    }

    [HarmonyPatch(
        typeof(L2FlagSystem),
        nameof(L2FlagSystem.setFlagData),
        new System.Type[] { typeof(int), typeof(string), typeof(short) }
    )]
    internal static class SetFlagDataFlagSystemStringPatch
    {
        static void Postfix(int seet_no, string name, short data)
        {
            if (data <= 0 || string.IsNullOrEmpty(name)) return;

            CheckManager.NotifyStringFlag(seet_no, name, data);
        }
    }
}