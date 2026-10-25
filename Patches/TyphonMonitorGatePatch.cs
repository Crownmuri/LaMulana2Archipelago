using System;
using HarmonyLib;
using L2Flag;
using L2Task;
using LaMulana2Archipelago.Managers;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Typhon's room (Hell Temple, field09) vs. random_dissonance.
    ///
    /// (13,39) = minusGus, the HL dissonance state: 0 = not released, 1 = released,
    /// 2 = absorbed. Vanilla flow (decoded from level11 / sharedassets11):
    ///   - Scanning (UseItemArea, USE_SCANA, active while (13,12)&gt;=1 AND (13,39)&lt;=1)
    ///     sets (1,9)=1, which brings up the monitor: TalkGate (2) "f09-5", whose
    ///     shadow "f09 monitor C3" is live while (1,9)==1 AND (13,39)&lt;=1.
    ///     Picking "destroy Typhon" there releases the dissonance ((13,39)=1).
    ///   - Echidna's glossary chip (chipSymUR-Guardian07, at the monitor) has the
    ///     shadow "f09 minusSmokeDown": live while (13,39)&gt;=2, i.e. only after the
    ///     dissonance is absorbed with a Beherit.
    ///
    /// With random_dissonance the Beherit absorb is replaced by the dissonance chest,
    /// whose openActionFlags stamp the 2. So after releasing the dissonance the scan
    /// kept reopening the monitor and the chip only appeared once the chest was
    /// opened, which needs a Beherit. Logic only needs the dissonance released.
    ///
    /// Fix, on the live shadow clones and only with random_dissonance on: the monitor
    /// retires once the dissonance is released ((13,39)&lt;=1 → &lt;1), and the chip
    /// appears once released ((13,39)&gt;=2 → &gt;=1). The chest still unlocks on &gt;=1 and
    /// still stamps 2 when opened.
    /// </summary>
    [HarmonyPatch(typeof(L2TaskShadow), "childbackup")]
    internal static class TyphonMonitorGatePatch
    {
        private const string MonitorShadow = "f09 monitor C3";
        private const string ChipShadow = "f09 minusSmokeDown";
        private const int DissonanceSheet = 13, DissonanceFlag = 39;

        static void Postfix(L2TaskShadow __instance)
        {
            try
            {
                if (__instance == null || __instance.startflag == null)
                    return;
                if (SceneRandomizer.Instance == null || !SceneRandomizer.Instance.RandomDissonance)
                    return;

                string name = __instance.name;
                if (name.StartsWith(MonitorShadow, StringComparison.Ordinal))
                    Rewrite(__instance, COMPARISON.LessEq, 1, COMPARISON.Less, 1,
                        "Typhon monitor retires on dissonance release ((13,39)<1)");
                else if (name.StartsWith(ChipShadow, StringComparison.Ordinal))
                    Rewrite(__instance, COMPARISON.GreaterEq, 2, COMPARISON.GreaterEq, 1,
                        "Echidna glossary chip appears on dissonance release ((13,39)>=1)");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[SceneRando] Typhon shadow gate failed: {ex.Message}");
            }
        }

        private static void Rewrite(L2TaskShadow shadow, COMPARISON fromComp, int fromValue,
            COMPARISON toComp, int toValue, string log)
        {
            foreach (L2FlagBoxParent parent in shadow.startflag)
            {
                if (parent?.BOX == null) continue;
                foreach (L2FlagBox box in parent.BOX)
                {
                    if (box != null && box.seet_no1 == DissonanceSheet && box.flag_no1 == DissonanceFlag
                        && box.seet_no2 < 0 && box.comp == fromComp && box.flag_no2 == fromValue)
                    {
                        box.comp = toComp;
                        box.flag_no2 = toValue;
                        Plugin.Log.LogInfo("[SceneRando] " + log);
                    }
                }
            }
        }
    }
}
