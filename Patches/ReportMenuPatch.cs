using HarmonyLib;
using LaMulana2Archipelago.Managers;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Last line of defence for the journal counter: ReportMenu.StartSwitch is
    /// the exact point where (0,38) report-b is read to decide how many FILE
    /// entries to list, so recount the papers immediately before it.
    ///
    /// The flag-write hooks in SetFlagDataPatch / AddFlagPatch already keep the
    /// counter current; this makes the menu correct even for a paper that
    /// arrived through a route none of them observed.
    /// </summary>
    [HarmonyPatch(typeof(ReportMenu), nameof(ReportMenu.StartSwitch))]
    internal static class ReportMenuStartSwitchPatch
    {
        static void Prefix()
        {
            ResearchReportSync.Sync(null);
        }
    }
}
