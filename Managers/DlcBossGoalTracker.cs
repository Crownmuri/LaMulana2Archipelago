using LaMulana2Archipelago.Archipelago;

namespace LaMulana2Archipelago.Managers
{
    /// <summary>
    /// Fires the AP goal (CLIENT_GOAL) when the DLC boss is beaten, for seeds
    /// whose goal is "defeat the DLC boss".
    ///
    /// Flag investigation (see 04field.. talk data, L2TalkDataBase entry 16/77):
    /// the post-fight dialogue with the DLC boss runs
    ///     [@setf,1,44,=,1][@setf,25,59,=,2]
    /// setting two flags atomically:
    ///   - (1,44)=1   sheet "01View": a generic, unnamed boolean, set only here
    ///                but living on the volatile View sheet.
    ///   - (25,59)=2  sheet "25ext2" (DLC extension sheet): the DLC's own
    ///                progression flag "d059". An RCD script sets it to 1 (the
    ///                gyonin/merfolk NPC gates dialogue on ==1); the post-boss
    ///                talk bumps it to 2.
    ///
    /// We key off (25,59) &gt;= 2 because it is persistent DLC story data (saved,
    /// read across sessions) and its value is specific to the post-kill state,
    /// so the intermediate ==1 can't false-fire.
    ///
    /// Live path: the talk [@setf] write is queued via MenuSystem.setMojiFlagQue
    /// and flushed through L2System.setFlagData(int,int,short) →
    /// L2FlagSystem.setFlagData(int,int,short), which SetFlagDataFlagSystemPatch
    /// hooks — so NotifyFlagSet catches the kill the moment it happens.
    ///
    /// Recovery path: a save loaded with the boss already beaten populates the
    /// flag WITHOUT replaying setFlagData (same as DissonanceTracker), so
    /// NotifySceneLoaded re-checks the current value after every load/reconnect.
    /// </summary>
    public static class DlcBossGoalTracker
    {
        private const int DlcGoalSheet = 25;
        private const int DlcGoalFlag = 59;
        private const short DlcGoalValue = 2;

        // Victory condition, echoed by the apworld into slot_data["goal"]
        // (worlds/lamulana2/options.py Goal). Default 0 keeps this inert for
        // every non-DLC-goal seed.
        private const string GoalSlotKey = "goal";
        private const int GoalBeatTheDlc = 1; // Goal.option_beat_the_dlc

        private static bool goalFired;

        /// <summary>
        /// True when the connected seed selected "beat_the_dlc" as its goal.
        /// Also consulted by Plugin.OnSceneLoaded to suppress the normal
        /// ending-scene goal trigger in that mode.
        /// </summary>
        public static bool IsDlcBossGoal =>
            ArchipelagoClient.ServerData.GetSlotInt(GoalSlotKey, 0) == GoalBeatTheDlc;

        /// <summary>
        /// Called from SetFlagDataFlagSystemPatch.Postfix on every numeric flag
        /// set. Filters internally to the DLC-boss completion flag.
        /// </summary>
        public static void NotifyFlagSet(int sheet, int flag, short value)
        {
            if (sheet != DlcGoalSheet || flag != DlcGoalFlag || value < DlcGoalValue)
                return;

            TryReportGoal("flag set");
        }

        /// <summary>
        /// Called from Plugin.OnSceneLoaded. Catches the case where the boss was
        /// already beaten in a prior session — the engine restores the flag on
        /// load without a setFlagData call, so the live hook would miss it.
        /// </summary>
        public static void NotifySceneLoaded()
        {
            if (goalFired || !IsDlcBossGoal)
                return;

            var sys = UnityEngine.Object.FindObjectOfType<L2Base.L2System>();
            if (sys == null) return;

            short current = 0;
            try { sys.getFlag(DlcGoalSheet, DlcGoalFlag, ref current); }
            catch { return; }

            if (current >= DlcGoalValue)
                TryReportGoal("scene load");
        }

        private static void TryReportGoal(string source)
        {
            if (goalFired || !IsDlcBossGoal)
                return;

            goalFired = true;
            Plugin.Log.LogInfo($"[DlcBossGoalTracker] DLC boss defeat detected ({source}), reporting goal.");
            ArchipelagoClientProvider.Client?.ReportGoalOnce();
        }

        public static void Reset()
        {
            goalFired = false;
        }
    }
}
