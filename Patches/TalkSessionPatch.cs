using HarmonyLib;
using L2Word;
using LaMulana2Archipelago.Managers;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Tracks when a NPC talk script has called [@exit] but not yet [@out].
    ///
    /// After [@exit] fires, the dialog window closes (MENUOPEN cleared) but the NPC
    /// script keeps executing. Pages that follow may run [@anifla,mnext,wait2] which
    /// suspends the script waiting for player button input — but the player state is
    /// WALK and no system flag indicates this. If an AP item from the queue fires
    /// during this window it competes with the NPC wait for the same button press,
    /// causing a deadlock.
    ///
    /// Solution: set IsPostExitTalkActive = true on [@exit], false on [@out].
    /// ItemGrantStateGuard.IsSafe returns false while the flag is true.
    /// </summary>
    [HarmonyPatch(typeof(MojiScript), "com_ntex")]
    internal static class TalkExitPatch
    {
        static void Prefix()
        {
            ItemGrantStateGuard.IsPostExitTalkActive = true;
        }
    }

    [HarmonyPatch(typeof(MojiScript), "com_out")]
    internal static class TalkOutPatch
    {
        static void Prefix()
        {
            ItemGrantStateGuard.IsPostExitTalkActive = false;
        }
    }
}
