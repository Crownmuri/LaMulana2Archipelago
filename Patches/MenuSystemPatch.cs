using HarmonyLib;
using L2Menu;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Replaces MenuSystem.setMojiFlagQue to handle stacking items correctly.
    /// Without this, picking up a second Sacred Orb/Crystal Skull/etc. in the
    /// same mojiscript sequence would overwrite instead of add.
    /// Port of the original randomizer's MonoMod patch.
    /// </summary>
    [HarmonyPatch(typeof(MenuSystem), nameof(MenuSystem.setMojiFlagQue))]
    internal static class MenuSystemFlagQuePatch
    {
        public static bool Enabled = false;

        static bool Prefix(MenuSystem __instance, int sheet, int id, short vale)
        {
            if (!Enabled) return true;

            var trav = Traverse.Create(__instance);
            int flagq_count = trav.Field("flagq_count").GetValue<int>();
            var flagq = trav.Field("flagq").GetValue<MojiScFlagQ[]>();
            var sys = trav.Field("sys").GetValue<L2Base.L2System>();

            if (flagq_count >= flagq.Length)
            {
                return false;
            }

            for (int i = 0; i < flagq_count; i++)
            {
                if (flagq[i].flag_sheet == sheet && flagq[i].flag_name == id)
                {
                    // Stacking items: increment instead of overwrite
                    if (sheet == 3 && id == 30)
                        flagq[i].flag_vale += 4;      // Score
                    else if (sheet == 0 && id == 2)
                        flagq[i].flag_vale++;          // Sacred Orb count
                    else if (sheet == 0 && id == 32)
                        flagq[i].flag_vale++;          // Crystal Skull count
                    else if (sheet == 5 && id == 47)
                        flagq[i].flag_vale++;          // Auto-placed skull count
                    return false;
                }
            }

            flagq[flagq_count].flag_sheet = sheet;
            flagq[flagq_count].flag_name = id;
            flagq[flagq_count].flag_vale = vale;
            trav.Field("flagq_count").SetValue(flagq_count + 1);

            return false;
        }
    }
}
