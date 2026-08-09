using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using L2Menu;
using LaMulana2Archipelago.Managers;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// DIAGNOSTIC (temporary): enter/exit breadcrumbs for <see cref="HangWatchdog"/>
    /// over the bunsyouemon (lore/glossary-message) family and the two menu-nav
    /// while-loop methods in MenuSystem.
    ///
    /// The watchdog already showed the shop / NPC-glossary exit freeze is a NATIVE
    /// loop (runScriptLine calls = 0 during the stall), not a moji-script jump.
    /// The prime suspect is a cyclic bunsyouemon linked list making
    /// getNowBunsyouemonNum's `.next` walk never terminate. Whichever method here
    /// enters and never returns leaves "IN:<name>" as the standing breadcrumb in
    /// the next stall report, pinning the loop exactly.
    ///
    /// Pure observation — records only. Remove with HangWatchdog once fixed.
    /// </summary>
    [HarmonyPatch]
    internal static class BunsyouWatchdogPatch
    {
        // Non-overloaded MenuSystem methods on/around the glossary-give path.
        private static readonly string[] MethodNames =
        {
            "getNowBunsyouemonNum",
            "writeBunsyouemon",
            "writeBunsyouemon_sub",
            "delBunsyouemon",
            "bunsyouSort",
            "bunsyouSortLink",
            "bunsyouemonOpen",
            "setBunsyouData",
            "getRightSideMenu",
            "getLeftSideMenu",
        };

        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string name in MethodNames)
            {
                MethodBase m = AccessTools.Method(typeof(MenuSystem), name);
                if (m != null) yield return m;
                else Plugin.Log.LogWarning("[Watchdog] BunsyouWatchdogPatch: method not found: " + name);
            }
        }

        static void Prefix(MethodBase __originalMethod) => HangWatchdog.Enter(__originalMethod.Name);
        static void Postfix(MethodBase __originalMethod) => HangWatchdog.Exit(__originalMethod.Name);
    }
}
