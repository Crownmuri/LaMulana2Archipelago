using L2Base;
using LaMulana2Archipelago.Archipelago;

namespace LaMulana2Archipelago.Managers
{
    /// <summary>
    /// Mirrors the natural-dissonance count (flag [2,3]) to AP datastorage so
    /// PopTracker can autotrack the Beherit consumable counter when
    /// random_dissonance is OFF.
    ///
    /// With random_dissonance ON, the count is already driven by AP Progressive
    /// Beherit items via onItem (and ItemGrantManager.cs:241 also calls
    /// setFlagData(2,3,...) for those grants). Reporting in that mode would
    /// double-count, so the slot-data guard skips it.
    ///
    /// Key format: lamulana2_dissonance_{team}_{slot}
    /// Value: current value of flag[2,3] (cumulative count).
    /// </summary>
    public static class DissonanceTracker
    {
        private const int DissonanceSheet = 2;
        private const int DissonanceFlag = 3;

        private static int lastReportedCount = -1;

        /// <summary>
        /// Called from SetFlagDataFlagSystemPatch.Postfix and AddFlagPatch.Postfix.
        /// Filters internally to flag [2,3] writes.
        /// </summary>
        public static void NotifyFlagSet(int sheet, int flag, short value)
        {
            if (sheet != DissonanceSheet || flag != DissonanceFlag) return;
            if (value < 0) return;

            // random_dissonance ON: AP Progressive Beherit items drive the
            // beherit counter via onItem. ItemGrantManager re-uses setFlagData
            // to keep the in-game flag in sync, so we'd double-count if we
            // mirrored those writes to datastorage. Skip in that mode.
            if (ArchipelagoClient.ServerData.GetSlotBool("random_dissonance", true))
                return;

            if (value == lastReportedCount) return;
            lastReportedCount = value;

            Plugin.Log.LogInfo($"[DissonanceTracker] flag[2,3]={value}, reporting to datastorage");
            ArchipelagoClientProvider.Client?.RecordDissonanceCount(value);
        }

        /// <summary>
        /// Called from Plugin.OnSceneLoaded so the count is pushed at least once
        /// per session after a save load — the in-game flag system populates
        /// flag[2,3] without going through setFlagData on load, so the patch
        /// hook alone would miss the existing value on reconnect.
        /// </summary>
        public static void NotifySceneLoaded()
        {
            if (ArchipelagoClient.ServerData.GetSlotBool("random_dissonance", true))
                return;

            var sys = UnityEngine.Object.FindObjectOfType<L2System>();
            if (sys == null) return;

            short current = 0;
            try { sys.getFlag(DissonanceSheet, DissonanceFlag, ref current); }
            catch { return; }

            NotifyFlagSet(DissonanceSheet, DissonanceFlag, current);
        }

        public static void Reset()
        {
            lastReportedCount = -1;
        }
    }
}
