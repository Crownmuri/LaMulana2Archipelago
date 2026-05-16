using HarmonyLib;
using L2Word;
using LaMulana2Archipelago.Managers;
using UnityEngine;

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

    /// <summary>
    /// Anchors the post-talk grant grace window to the moment Kataribe finishes 
    /// Hooking the 129→10000 transition inside Farst() (where MENUOPEN is
    /// deleted in vanilla code) seeds the grace at the correct moment, giving
    /// the cutscene a full window to take control before any AP grant runs.
    /// </summary>
    [HarmonyPatch(typeof(KataribeScript), nameof(KataribeScript.Farst))]
    internal static class KataribeFarstGracePatch
    {
        // ShopTask.sta is protected int; AccessTools handles the visibility.
        private static readonly AccessTools.FieldRef<KataribeScript, int> StaRef =
            AccessTools.FieldRefAccess<KataribeScript, int>("sta");

        static void Prefix(KataribeScript __instance, out int __state)
        {
            __state = StaRef(__instance);
        }

        static void Postfix(KataribeScript __instance, int __state)
        {
            // 129 is the post-cancel fade state; the transition to 10000 is
            // where Farst() calls delSysFlag(MENUOPEN). 
            if (__state == 129 && StaRef(__instance) == 10000)
            {
                ItemGrantStateGuard.PostTalkGraceUntil =
                    Time.realtimeSinceStartup + ItemGrantStateGuard.PostTalkGraceSeconds;
            }
        }
    }
}
