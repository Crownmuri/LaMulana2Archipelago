using System.Collections.Generic;
using LaMulana2RandomizerShared;

namespace LaMulana2Archipelago.Managers
{
    /// <summary>
    /// Detects guardian kills by tracking the scene-transition pattern around
    /// a boss fight. Used for guardians whose room/RCD script doesn't emit a
    /// unique post-kill flag (Fafnir confirmed silent; Hel is the lone known
    /// exception and uses the per-area "Hel" name flag — see LocationFlagMap).
    ///
    /// State machine:
    ///   1. setFlagData(3, guardianN, 4) → arm pending guardian, capture the
    ///      currently-loaded scene as originatingScene.
    ///   2. Scene loads matching originatingScene while still armed → kill
    ///      confirmed (the only way to leave a boss field is the post-kill
    ///      auto-transition; grail-warp is blocked).
    ///   3. Game-over (any death, including in-field) resets state before the
    ///      load-from-save scene transition fires, preventing a false positive.
    /// </summary>
    public static class BossKillTracker
    {
        // Sheet 3 ("03world") flag index → guardian LocationID.
        // Same mapping used by GuardianSpecificAnkhPatch (guardian00..08).
        private static readonly Dictionary<int, LocationID> GuardianFlagToLocation =
            new Dictionary<int, LocationID>
            {
                { 10, LocationID.Fafnir },
                { 11, LocationID.Vritra },
                { 12, LocationID.Kujata },
                { 13, LocationID.AtenRa },
                { 14, LocationID.Jormungand },
                { 15, LocationID.Anu },
                { 16, LocationID.Surtr },
                { 17, LocationID.Echidna },
                { 18, LocationID.Hel },
            };

        // Same mapping by flag NAME, for callers that hit setFlagData(int, string, short).
        private static readonly Dictionary<string, LocationID> GuardianNameToLocation =
            new Dictionary<string, LocationID>
            {
                { "guardian00", LocationID.Fafnir },
                { "guardian01", LocationID.Vritra },
                { "guardian02", LocationID.Kujata },
                { "guardian03", LocationID.AtenRa },
                { "guardian04", LocationID.Jormungand },
                { "guardian05", LocationID.Anu },
                { "guardian06", LocationID.Surtr },
                { "guardian07", LocationID.Echidna },
                { "guardian08", LocationID.Hel },
            };

        private static LocationID? pendingGuardian;
        private static string originatingScene;
        private static string lastSceneName;

        /// <summary>
        /// Called from SetFlagDataFlagSystemPatch.Postfix on any numeric flag
        /// set. Filters internally for the (sheet=3, value=4) ankh-used
        /// transition.
        /// </summary>
        public static void NotifyFlagSet(int sheet, int flag, short value)
        {
            if (sheet != 3 || value != 4) return;

            LocationID guardian;
            if (!GuardianFlagToLocation.TryGetValue(flag, out guardian)) return;

            Arm(guardian);
        }

        /// <summary>
        /// Called from SetFlagDataFlagSystemStringPatch.Postfix for the
        /// string-keyed setFlagData overload (e.g. setFlagData(3, "guardian00", 4)).
        /// In practice this is the path the game actually uses for the 2/3/4
        /// guardian transitions during the ankh interaction.
        /// </summary>
        public static void NotifyFlagSetByName(int sheet, string name, short value)
        {
            if (sheet != 3 || value != 4 || string.IsNullOrEmpty(name)) return;

            LocationID guardian;
            if (!GuardianNameToLocation.TryGetValue(name, out guardian)) return;

            Arm(guardian);
        }

        private static void Arm(LocationID guardian)
        {
            pendingGuardian = guardian;
            originatingScene = lastSceneName;
            Plugin.Log.LogInfo($"[BossKillTracker] Armed for {guardian} (origin scene='{originatingScene ?? "<null>"}')");
        }

        /// <summary>
        /// Called from Plugin.OnSceneLoaded for every scene transition.
        /// </summary>
        public static void NotifySceneLoaded(string sceneName)
        {
            if (pendingGuardian.HasValue
                && originatingScene != null
                && sceneName == originatingScene
                && lastSceneName != null
                && lastSceneName != originatingScene)
            {
                LocationID guardian = pendingGuardian.Value;
                Plugin.Log.LogInfo($"[BossKillTracker] Boss field exit '{lastSceneName}' → '{sceneName}'; confirming {guardian} kill");

                CheckManager.NotifyLocation(guardian);

                // Boss locations are event-only in the AP world (loc.address = None),
                // so the LocationCheck above never reaches the server's checked-locations
                // broadcast. Mirror to slot-scoped datastorage so PopTracker can pick up
                // the kill via SetNotify.
                ArchipelagoClientProvider.Client?.RecordBossKill(guardian);

                // TODO: trigger forced memSave once the planned feature lands.
                // The check fires here regardless — server-side it's durable
                // even if the client crashes before the next save.

                Clear();
            }

            lastSceneName = sceneName;
        }

        /// <summary>
        /// Called from GameOverTaskPatch when the death screen activates.
        /// Resets pending state so the subsequent load-save scene transition
        /// doesn't false-fire the check.
        /// </summary>
        public static void NotifyGameOver()
        {
            if (pendingGuardian.HasValue)
                Plugin.Log.LogInfo($"[BossKillTracker] Game over while armed for {pendingGuardian} — clearing");
            Clear();
        }

        public static void Reset()
        {
            Clear();
            lastSceneName = null;
        }

        private static void Clear()
        {
            pendingGuardian = null;
            originatingScene = null;
        }
    }
}
