using L2Flag;

namespace LaMulana2Archipelago.Managers
{
    /// <summary>
    /// Spawns the DLC Ixtab rematch (the fight that opens the way to Eden)
    /// off the Heimdall kill instead of off the Gyonin King's conversation.
    ///
    /// Vanilla only ever writes (25,2)=1 from one place: row "2nd-5" of the
    /// Gyonin King's Annwfn dialogue (moji sheet "f02-gyonin"), which does
    /// [@setf,25,1,=,1][@setf,25,2,=,1]. That row is only reachable while his
    /// conversation stage (25,4) is still early, and (25,4) is force-advanced to
    /// 4/5 by the Eden and Gate of Guidance FlagWatchers the moment the player
    /// merely *holds* the Rebirth Seal ((2,55) >= 1) — which is exactly what we
    /// want to keep, since (25,4)=4 is what opens the top entrance of Spring in
    /// the Sky without doing the four-part DLC puzzle. Under Entrance Randomizer
    /// the Sigil routinely arrives before the King has ever been spoken to, so
    /// his branch table skips straight past "2nd-5", (25,2) is never written,
    /// Ixtab never respawns and Eden becomes unreachable.
    ///
    /// Rather than fight the (25,4) fast-forward, we decouple the Ixtab respawn
    /// from the dialogue entirely and hang it off the Heimdall kill, which is
    /// the other half of the vanilla requirement anyway (the apworld already
    /// gates Eden-side logic on "IsDead(Ixtab) and IsDead(Heimdall)").
    ///
    /// Trigger flag: (25,0), the Annwfn DLC-ladder counter — the ladder up to
    /// the Gyonin King's C-4 alcove. Its only writer is the "Ex_ladder"
    /// FlagWatcherScript in field02, whose condition is
    /// (6,26) &gt;= 1 AND (25,0) == 0  →  (25,0) = 1,
    /// where (6,26) is "B4_roomguarder", the Heimdall room-guarder flag. So
    /// (25,0) &gt;= 1 is precisely "Heimdall is dead and the ladder is up".
    /// (The counter then runs 1 → 2 → 3 as the ladder animates in; anything
    /// &gt;= 1 means the gate has been passed.)
    ///
    /// The write is monotonic-from-below: (25,2) is only ever raised to 1 and
    /// only when it is currently unset, so replaying it on every flag write and
    /// every scene load is inert once it has fired, and it can never regress a
    /// player who reached the respawn the vanilla way.
    /// </summary>
    public static class IxtabRespawnSync
    {
        // Annwfn DLC ladder counter ("25ext2"), set to 1 by field02's
        // "Ex_ladder" FlagWatcherScript once Heimdall (6,26) is down.
        private const int LadderSheet = 25;
        private const int LadderFlag = 0;
        private const short LadderArmed = 1;

        // Ixtab respawn flag ("25ext2"), vanilla-written only by f02-gyonin "2nd-5".
        private const int IxtabSheet = 25;
        private const int IxtabFlag = 2;
        private const short IxtabValue = 1;

        /// <summary>
        /// Numeric flag write (addFlag from setEffectFlag, or setFlagData by
        /// number). Filters internally to the ladder counter.
        /// </summary>
        public static void OnNumericFlagWrite(L2FlagSystem flagSys, int seet, int flag)
        {
            if (seet != LadderSheet || flag != LadderFlag) return;
            Apply(flagSys, "ladder flag write");
        }

        /// <summary>
        /// Called from Plugin.OnSceneLoaded. Recovers saves whose Heimdall kill
        /// (and therefore the (25,0) write) happened in a prior session or
        /// before this fix existed — the engine restores flags on load without
        /// replaying setFlagData, so the live hook alone would never see it.
        /// </summary>
        public static void NotifySceneLoaded()
        {
            var sys = UnityEngine.Object.FindObjectOfType<L2Base.L2System>();
            if (sys == null) return;

            var flagSys = sys.getFlagSys();
            if (flagSys == null) return;

            Apply(flagSys, "scene load");
        }

        private static void Apply(L2FlagSystem flagSys, string source)
        {
            if (flagSys == null) return;

            short ladder = 0;
            flagSys.getFlag(LadderSheet, LadderFlag, ref ladder);
            if (ladder < LadderArmed) return; // Heimdall not down / ladder not up yet

            short cur = 0;
            flagSys.getFlag(IxtabSheet, IxtabFlag, ref cur);
            if (cur >= IxtabValue) return; // already spawned — never rewrite

            flagSys.setFlagData(IxtabSheet, IxtabFlag, IxtabValue);
            Plugin.Log.LogInfo(
                $"[IXTAB] Heimdall down (({LadderSheet},{LadderFlag})={ladder}, {source}) → " +
                $"set ({IxtabSheet},{IxtabFlag})={IxtabValue} (Ixtab rematch spawned; was {cur})");
        }
    }
}
