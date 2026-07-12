using HarmonyLib;
using L2Base;
using L2Word;
using LaMulana2Archipelago.Managers;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Glossary-hunt goal: append live "X / Y collected" progress to the Ruins
    /// Encyclopedia ("R Book") app description in the software menu, so the player
    /// can track the goal from their inventory.
    ///
    /// Mirrors AnkhJewelDescriptionPatch: SoftMenu renders each app's info panel
    /// via getMojiText(true, "softText", "&lt;icon name&gt;", mojiScriptType.item)
    /// where the Ruins Encyclopedia's icon/box name is "R Book". We postfix that
    /// lookup and append the count. No-op unless the seed's goal is glossary_hunt.
    /// </summary>
    [HarmonyPatch]
    public static class GlossaryHuntDescriptionPatch
    {
        private const string SoftTextSheet = "softText";
        private const string RuinsEncyclopediaIcon = "R Book";

        // getMojiText(bool firstCall, string sheetName, string id, mojiScriptType type)
        [HarmonyPostfix]
        [HarmonyPatch(typeof(L2System), "getMojiText",
            new System.Type[] {
                typeof(bool), typeof(string), typeof(string), typeof(mojiScriptType)
            })]
        private static void GetMojiText_Postfix(
            string sheet,
            string line,
            ref string __result)
        {
            if (sheet != SoftTextSheet || line != RuinsEncyclopediaIcon)
                return;
            if (!GlossaryGoalTracker.IsGlossaryHuntGoal)
                return;

            string progress = $"Glossary collected: {GlossaryGoalTracker.ObtainedCount} / {GlossaryGoalTracker.TargetCount}";
            __result = string.IsNullOrEmpty(__result) ? progress : __result + "\n" + progress;
        }
    }
}
