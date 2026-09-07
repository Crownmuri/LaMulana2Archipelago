using System.Collections.Generic;
using LaMulana2Archipelago.Archipelago;

namespace LaMulana2Archipelago.Managers
{
    /// <summary>
    /// Glossary-hunt goal (slot_data["goal"] == 2): the seed is won by collecting
    /// a target number of AP-shuffled glossary entries. When the target is reached
    /// the AP goal (CLIENT_GOAL) is sent and the credits scene is rolled.
    ///
    /// What counts: only AP-SHUFFLED glossary entries (those registered in
    /// GlossaryManager from glossary_flag_map). In the decoupled glossanity model
    /// a shuffled entry's sheet-20 "book" flag is set ONLY when its ROM is
    /// received (ItemGrantManager.DeliverGlossaryRom) — finding the in-world chip
    /// just fires the AP check. Vanilla, non-shuffled entries also set sheet-20
    /// flags when unlocked in-world, but they aren't registered, so they don't
    /// count.
    ///
    /// Live path: DeliverGlossaryRom's setFlagData(20, flag, 1) flows through
    /// SetFlagDataFlagSystemPatch.Postfix → NotifyFlagSet (fires even while an item
    /// grant is in progress, unlike CheckManager.NotifyNumericFlag).
    ///
    /// Recovery path: a save loaded with entries already unlocked restores the
    /// sheet-20 flags without replaying setFlagData, so NotifySceneLoaded rebuilds
    /// the obtained set from the live flags after every load/reconnect.
    /// </summary>
    public static class GlossaryGoalTracker
    {
        private const int BookSheet = 20;
        private const string GoalSlotKey = "goal";
        private const int GoalGlossaryHunt = 2; // Goal.option_glossary_hunt
        private const string CountSlotKey = "glossary_hunt_count";

        // sheet-20 flags of shuffled entries the player has unlocked so far.
        private static readonly HashSet<int> obtained = new HashSet<int>();

        private static bool goalFired;
        private static bool creditsRequested;

        /// <summary>True when the connected seed's goal is glossary_hunt.</summary>
        public static bool IsGlossaryHuntGoal =>
            ArchipelagoClient.ServerData.GetSlotInt(GoalSlotKey, 0) == GoalGlossaryHunt;

        /// <summary>Number of shuffled glossary entries required to win.</summary>
        public static int TargetCount =>
            ArchipelagoClient.ServerData.GetSlotInt(CountSlotKey, 0);

        /// <summary>Shuffled glossary entries unlocked so far (for progress display).</summary>
        public static int ObtainedCount => obtained.Count;

        /// <summary>
        /// Called from SetFlagDataFlagSystemPatch.Postfix on every numeric flag
        /// set. Filters to sheet-20 writes of registered (shuffled) entries.
        /// </summary>
        public static void NotifyFlagSet(int sheet, int flag, short value)
        {
            if (!IsGlossaryHuntGoal || sheet != BookSheet || value <= 0)
                return;

            if (!GlossaryManager.TryGetLocation(flag, out _))
                return; // not an AP-shuffled entry

            if (obtained.Add(flag))
                CheckThreshold("ROM received");
        }

        /// <summary>
        /// Called from Plugin.OnSceneLoaded. Rebuilds the obtained set from the
        /// live sheet-20 flags so entries unlocked in a prior session (restored on
        /// load without a setFlagData call) are counted.
        /// </summary>
        public static void NotifySceneLoaded()
        {
            if (goalFired || !IsGlossaryHuntGoal)
                return;

            var sys = UnityEngine.Object.FindObjectOfType<L2Base.L2System>();
            if (sys == null) return;

            foreach (int flag in GlossaryManager.RegisteredBookFlags)
            {
                short v = 0;
                try { sys.getFlag(BookSheet, flag, ref v); }
                catch { continue; }
                if (v != 0)
                    obtained.Add(flag);
            }

            CheckThreshold("scene load");
        }

        private static void CheckThreshold(string source)
        {
            if (goalFired) return;

            int target = TargetCount;
            if (target <= 0 || obtained.Count < target)
                return;

            goalFired = true;
            creditsRequested = true;
            Plugin.Log.LogInfo($"[GlossaryGoalTracker] Glossary hunt target reached " +
                $"({obtained.Count}/{target}, via {source}) — reporting goal + rolling credits.");
            ArchipelagoClientProvider.Client?.ReportGoalOnce();
        }

        /// <summary>
        /// True once after the target is reached (resets on read). Plugin.Update
        /// consumes it at a safe point to trigger the credits transition.
        /// </summary>
        public static bool TryConsumeCreditsRequest()
        {
            if (!creditsRequested) return false;
            creditsRequested = false;
            return true;
        }

        public static void Reset()
        {
            obtained.Clear();
            goalFired = false;
            creditsRequested = false;
        }
    }
}
