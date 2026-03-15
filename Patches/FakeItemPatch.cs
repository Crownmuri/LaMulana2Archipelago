using HarmonyLib;
using L2Base;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// When a FakeItem is collected, LM2 sets sheet 31 flags 40-79.
    /// We mark a one-shot context so the *next* grant uses DropItem pickup SFX (23).
    /// </summary>
    internal static class FakeItemPickupContext
    {
        public static bool UseDropPickupSeOnce;
    }

    [HarmonyPatch(typeof(L2System), nameof(L2System.setFlagData), new[] { typeof(int), typeof(int), typeof(short) })]
    internal static class FakeItemSetFlagPatch
    {
        static void Postfix(int sheetNo, int idNo, short data)
        {
            // FakeItem01-40 are sheet31 flags 40-79 (per your own comment block)
            if (sheetNo == 31 && idNo >= 40 && idNo <= 79 && data == 1)
            {
                FakeItemPickupContext.UseDropPickupSeOnce = true;
            }
        }
    }
}