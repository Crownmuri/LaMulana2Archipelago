using HarmonyLib;
using L2Base;
using L2Word;
using LaMulana2Archipelago.Managers;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// shuffle_guardian_souls: append "Souls obtained: X" to the Mobile Super X3 /
    /// X3+ item description in the inventory menu, so players can see how many
    /// souls the soul gates will count.
    ///
    /// Mirrors AnkhJewelDescriptionPatch / GlossaryHuntDescriptionPatch: ItemMenu
    /// RefreshInformation renders the tablet via getMojiText(true, "itemText",
    /// "MSX", item); its script_run may redirect the id to "MSX3p" for the X3+.
    /// </summary>
    [HarmonyPatch]
    public static class GuardianSoulDescriptionPatch
    {
        // getMojiText(bool firstCall, string sheetName, string id, mojiScriptType type)
        [HarmonyPostfix]
        [HarmonyPatch(typeof(L2System), "getMojiText",
            new System.Type[] {
                typeof(bool), typeof(string), typeof(string), typeof(mojiScriptType)
            })]
        private static void GetMojiText_Postfix(
            L2System __instance,
            string sheet,
            string line,
            ref string __result)
        {
            if (sheet != "itemText" || (line != "MSX" && line != "MSX3p")) return;
            if (!GuardianSoulManager.Enabled) return;

            string progress = $"Souls obtained: {GuardianSoulManager.ObtainedCount(__instance)} / 9";
            __result = string.IsNullOrEmpty(__result) ? progress : __result + "\n" + progress;
        }
    }
}
