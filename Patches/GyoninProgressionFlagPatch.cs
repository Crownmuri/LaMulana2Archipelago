using HarmonyLib;
using L2Flag;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Fixes the Tower of Oannes → Bailey "outside gyonin" cutscene firing prematurely
    /// under Entrance Randomizer, which permanently skips the two Tower mini-bosses
    /// (Fish Valusa, Fish Slime) and seals their doors into dead ends.
    ///
    /// The DLC mini-boss progression is one strictly-increasing counter (25,14):
    ///   2  first tower gyonin dialogue
    ///   4→6  Fish Valusa (also sets (25,37)=3, opening the door to the Slime room)
    ///   8→10 Fish Slime (opens the door up to the outside)
    ///   11 outside trigger armed → cutscene → 12
    ///
    /// The outside cutscene is driven by a PlayerHitArea in fieldEx2 (Bailey) whose
    /// arming condition (FlagAwakes) is (25,14) &lt;= 10 and whose action (FlagValues)
    /// writes (25,14)=11. In vanilla that was safe: you could only physically stand on
    /// this hit-area after beating Fish Slime (25,14 == 10). In ER you can reach the
    /// spot with (25,14) still at 2/4/6/8, and "&lt;= 10" is satisfied — so the hit-area
    /// jams the counter to 11 → 12, despawning both mini-bosses (past their spawn
    /// windows) and leaving their doors shut forever.
    ///
    /// Fix: rewrite that one condition from (25,14) &lt;= 10 to (25,14) == 10, so the
    /// outside trigger arms only in the legitimate post-Fish-Slime state. This preserves
    /// the entire internal route (reaching the outside early now does nothing), and can't
    /// loop — once it fires, (25,14) becomes 11, no longer == 10. Reaching the outside
    /// before Slime simply does nothing until the player beats it and returns.
    ///
    /// This is a runtime scene-object condition, not something the apworld's logic/
    /// placement layer can touch, so it belongs mod-side — alongside the existing
    /// scene-load rewrites (SceneRandomizer's AnchorGateZ, etc.). The patch re-applies
    /// on every scene load (the object is deserialized fresh from the scene asset each
    /// time) and only touches the object matching the exact outside-trigger signature.
    ///
    /// Note: this prevents NEW corruption; a save whose (25,14) is already past 10 has
    /// already skipped the mini-bosses and can't be repaired by re-arming the trigger.
    /// </summary>
    [HarmonyPatch(typeof(PlayerHitArea), "Init")]
    internal static class GyoninProgressionFlagPatch
    {
        private const int Sheet = 25;
        private const int Flag = 14;
        private const short SlimeBeaten = 10; // arm only in this exact post-Slime state
        private const short ArmsToValue = 11; // signature: the outside trigger writes (25,14)=11

        static void Postfix(PlayerHitArea __instance)
        {
            if (__instance == null) return;
            try
            {
                if (!WritesArmValue(__instance.FlagValues)) return; // not the outside trigger
                RetargetArmCondition(__instance.FlagAwakes, __instance.gameObject.name);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"[DlcOutsideTrigger] fix skipped ({ex.GetType().Name}: {ex.Message})");
            }
        }

        // True if this hit-area's action writes (25,14)=11 — the outside cutscene arm.
        private static bool WritesArmValue(L2FlagBoxEnd[] ends)
        {
            if (ends == null) return false;
            foreach (var e in ends)
            {
                if (e == null) continue;
                if (e.seet_no1 == Sheet && e.flag_no1 == Flag
                    && e.calcu == CALCU.EQR && e.data == ArmsToValue)
                    return true;
            }
            return false;
        }

        // Flip the (25,14) <= 10 box to (25,14) == 10 wherever it appears in the arm condition.
        private static void RetargetArmCondition(L2FlagBoxParent[] parents, string objName)
        {
            if (parents == null) return;
            foreach (var parent in parents)
            {
                if (parent?.BOX == null) continue;
                foreach (var box in parent.BOX)
                {
                    if (box == null) continue;
                    // Left operand must be (25,14); right operand the literal 10 (seet<0);
                    // comparison currently LessEq.
                    if (box.seet_no1 == Sheet && box.flag_no1 == Flag
                        && box.comp == COMPARISON.LessEq
                        && box.seet_no2 < 0 && box.flag_no2 == SlimeBeaten)
                    {
                        box.comp = COMPARISON.Equal;
                        Plugin.Log.LogInfo(
                            $"[DlcOutsideTrigger] '{objName}': re-armed outside cutscene " +
                            $"({Sheet},{Flag}) <= {SlimeBeaten}  →  == {SlimeBeaten} " +
                            "(no longer fires before Fish Slime is beaten).");
                    }
                }
            }
        }
    }
}
