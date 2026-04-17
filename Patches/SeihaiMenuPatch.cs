using HarmonyLib;
using L2Menu;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Fixes Holy Grail warp point counting and current-field detection
    /// for the randomizer. The original randomizer's patches ensure that
    /// ura (backside) warps only show when the player has the ura warp ability,
    /// and field-point lookups work for all scenes.
    /// </summary>
    [HarmonyPatch(typeof(SeihaiMenu), "getOnHolyNum")]
    internal static class SeihaiGetOnHolyNumPatch
    {
        public static bool Enabled = false;

        static bool Prefix(SeihaiMenu __instance, ref int omote, ref int ura)
        {
            if (!Enabled) return true;

            var sys = Traverse.Create(__instance).Field("sys").GetValue<L2Base.L2System>();
            uint fieldWarpFlag = sys.getFieldWarpFlag();
            omote = 0;
            ura = 0;

            for (int i = 0; i < 10; i++)
            {
                if ((fieldWarpFlag >> i & 1u) == 1u)
                    omote++;
            }
            for (int j = 10; j < 20; j++)
            {
                if ((fieldWarpFlag >> j & 1u) == 1u)
                    ura++;
            }

            if (!sys.getPlayer()._uraWarp)
                ura = 0;

            return false;
        }
    }

    [HarmonyPatch(typeof(SeihaiMenu), "getNowFieldPoint")]
    internal static class SeihaiGetNowFieldPointPatch
    {
        public static bool Enabled = false;

        static bool Prefix(SeihaiMenu __instance, int omotenum, int uranum, ref int uraomote, ref int __result)
        {
            if (!Enabled) return true;

            var sys = Traverse.Create(__instance).Field("sys").GetValue<L2Base.L2System>();
            var warppointbuffer_omote = Traverse.Create(__instance).Field("warppointbuffer_omote").GetValue<int[]>();
            var warppointbuffer_ura = Traverse.Create(__instance).Field("warppointbuffer_ura").GetValue<int[]>();

            ViewProperty currentView = sys.getL2SystemCore().ScrollSystem.getCurrentView();
            int sceaneNo = sys.getL2SystemCore().SceaneNo;
            bool hasUra = sys.getPlayer()._uraWarp && uranum > 0;

            int num;
            switch (sceaneNo)
            {
                case 0: uraomote = 0; num = 1; break;
                case 1: uraomote = 0; num = 0; break;
                case 2: uraomote = 0; num = 2; break;
                case 3:
                    if (currentView.ViewY >= 5 && hasUra) { uraomote = 1; num = 12; }
                    else { uraomote = 0; num = 3; }
                    break;
                case 4:
                    if (currentView.ViewX >= 4 && hasUra) { uraomote = 1; num = 13; }
                    else { uraomote = 0; num = 4; }
                    break;
                case 5: uraomote = 0; num = 5; break;
                case 6: uraomote = 0; num = 6; break;
                case 7: uraomote = 0; num = 7; break;
                case 8: uraomote = 0; num = 8; break;
                case 9: uraomote = 0; num = 9; break;
                case 10:
                    if (hasUra) { uraomote = 1; num = 14; }
                    else { uraomote = 0; num = 0; }
                    break;
                case 11:
                    if (hasUra) { uraomote = 1; num = 16; }
                    else { uraomote = 0; num = 0; }
                    break;
                case 12:
                    if (hasUra) { uraomote = 1; num = 17; }
                    else { uraomote = 0; num = 0; }
                    break;
                case 13:
                    if (hasUra) { uraomote = 1; num = 18; }
                    else { uraomote = 0; num = 0; }
                    break;
                case 14:
                    if (hasUra) { uraomote = 1; num = 10; }
                    else { uraomote = 0; num = 0; }
                    break;
                case 15:
                    if (hasUra) { uraomote = 1; num = 11; }
                    else { uraomote = 0; num = 0; }
                    break;
                case 28:
                    if (hasUra) { uraomote = 1; num = 15; }
                    else { uraomote = 0; num = 0; }
                    break;
                case 32: uraomote = 0; num = 0; break;
                default: uraomote = 0; num = -1; break;
            }

            if (omotenum == 0 && uraomote == 0)
            {
                num = -1;
                uraomote = 1;
            }

            if (num != -1)
            {
                if (uraomote == 0)
                {
                    for (int i = 0; i < omotenum; i++)
                    {
                        if (warppointbuffer_omote[i] == num)
                        {
                            __result = i;
                            return false;
                        }
                    }
                }
                else if (uraomote == 1)
                {
                    for (int i = 0; i < uranum; i++)
                    {
                        if (warppointbuffer_ura[i] == num)
                        {
                            __result = i;
                            return false;
                        }
                    }
                }
            }

            __result = 0;
            return false;
        }
    }
}
