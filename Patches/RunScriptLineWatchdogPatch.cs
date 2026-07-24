using HarmonyLib;
using L2Base;
using LaMulana2Archipelago.Managers;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// DIAGNOSTIC (temporary): breadcrumb feed for <see cref="HangWatchdog"/>.
    /// Records each moji-script line dispatch so a spinning shop / NPC script
    /// (the intermittent glossary-exit freeze) exposes its looping (sheet, id)
    /// in the watchdog's stall report. Both ShopScript and KataribeScript drive
    /// their do/while script loops through this L2System wrapper, so one patch
    /// covers both.
    ///
    /// Pure observation — records only, never blocks or alters flow. Remove
    /// together with HangWatchdog once the freeze is fixed.
    /// </summary>
    [HarmonyPatch(typeof(L2System), nameof(L2System.runScriptLine))]
    internal static class RunScriptLineWatchdogPatch
    {
        [HarmonyPrefix]
        private static void Prefix(int sheet_no, int line_no, ref string nx_sheet_name, ref string nx_id_name)
        {
            HangWatchdog.NoteScriptLine(sheet_no, line_no, nx_sheet_name, nx_id_name);
        }
    }
}
