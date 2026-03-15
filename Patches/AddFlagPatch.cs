using HarmonyLib;
using L2Flag;
using LaMulana2Archipelago.Managers;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Patches L2FlagSystem.addFlag — this is what fires when chest/event item
    /// itemGetFlags are applied (CALCU.EQR = set, CALCU.ADD = increment).
    /// setFlagData patches do NOT cover this code path.
    /// </summary>
    [HarmonyPatch(typeof(L2FlagSystem), "addFlag",
        new System.Type[] { typeof(int), typeof(int), typeof(short), typeof(CALCU) })]
    internal static class AddFlagPatch
    {
        static void Postfix(int seet_no1, int flag_no1, short value, CALCU cul)
        {
            // Only care about set (EQR) or increment (ADD) — not subtraction
            if (cul == CALCU.SUB) return;
            if (value <= 0) return;
            CheckManager.NotifyNumericFlag(seet_no1, flag_no1, value);
        }
    }
}