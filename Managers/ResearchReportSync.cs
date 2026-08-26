using L2Base;

namespace LaMulana2Archipelago.Managers
{
    /// <summary>
    /// Keeps the journal ("Kosugi Research Papers" app) in step with how many
    /// research papers the player actually holds.
    ///
    /// ReportMenu never looks at the papers themselves: it reads the single
    /// counter flag (0,38) "report-b" (memo: 小杉研究誌総数, "total Kosugi
    /// research journals"), clamps it to 12, and lists FILE 01..N, pulling each
    /// body from the moji key "reportText{N}". Vanilla drives that counter from
    /// each paper's itemGetFlags on the map.
    ///
    /// The randomizer replaces every placed item's get-flags wholesale
    /// (SceneRandomizer.CreateGetFlags), so the counter write is dropped: papers
    /// only stamp their own copy flag (2,180..189), which unlocks the app but
    /// leaves report-b at 0 — hence an app that opens to an empty list.
    ///
    /// Rather than re-attach a counter write to ten different acquisition paths
    /// (world pickup, chest, shop, AP grant), recompute the counter from state
    /// that every path already maintains:
    ///   • (2,84) "Research"  — vanilla setItem count, bumped by every route
    ///                          that hands over a paper, including any paper
    ///                          the randomizer left alone.
    ///   • (2,180..189)       — the randomizer's per-copy flags for Research1-10.
    /// The larger of the two wins, so neither a vanilla-only nor a rando-only
    /// paper can be missed. Raising to the total (rather than incrementing) makes
    /// this idempotent and self-healing: a save made before this fix picks the
    /// correct count back up the next time it runs.
    /// </summary>
    public static class ResearchReportSync
    {
        // "02Items"
        private const int ItemSheet = 2;
        // "Research" — the app itself; its value is the number of papers held.
        private const int ResearchCountFlag = 84;
        // Research1..Research10 copy flags (unnamed d180..d189 scratch slots).
        private const int FirstPaperFlag = 180;
        private const int PaperCount = 10;

        // "00system" / "report-b"
        private const int SystemSheet = 0;
        private const int ReportTotalFlag = 38;
        // ReportMenu only has 12 list slots and clamps to 12 itself.
        private const short MaxReports = 12;

        /// <summary>
        /// Numeric flag write (setFlagData / addFlag). Filters internally to the
        /// research flags, so it is safe to call on every flag write.
        /// </summary>
        public static void NotifyFlagSet(int sheet, int flag, short value)
        {
            if (sheet != ItemSheet) return;
            if (flag != ResearchCountFlag
                && (flag < FirstPaperFlag || flag >= FirstPaperFlag + PaperCount))
                return;

            Sync(null);
        }

        /// <summary>
        /// Named flag write — sys.setItem("Research", …) writes the count flag by
        /// name, which is the path an AP grant and a shop purchase both take.
        /// </summary>
        public static void NotifyNamedFlagSet(int sheet, string name)
        {
            if (sheet != ItemSheet || name != "Research") return;
            Sync(null);
        }

        /// <summary>
        /// Catch papers that were already held before this hook could see the
        /// write — a save loaded mid-run, or a run started before this fix.
        /// </summary>
        public static void NotifySceneLoaded()
        {
            Sync(null);
        }

        public static void Sync(L2System sys)
        {
            if (sys == null)
                sys = UnityEngine.Object.FindObjectOfType<L2System>();
            if (sys == null) return;

            try
            {
                short counted = 0;
                sys.getFlag(ItemSheet, ResearchCountFlag, ref counted);

                short copies = 0;
                for (int i = 0; i < PaperCount; i++)
                {
                    short held = 0;
                    sys.getFlag(ItemSheet, FirstPaperFlag + i, ref held);
                    if (held > 0) copies++;
                }

                short total = counted > copies ? counted : copies;
                if (total < 0) total = 0;
                if (total > MaxReports) total = MaxReports;

                // Only ever raise it. Not every research report in the game comes
                // from the ten shuffled papers -- the counter is also fed by
                // sources the randomizer leaves alone (field09's own "report"
                // flag at (13,40), the Xelputter "oyajirepo" at (23,56)), which
                // is how vanilla reaches all 12 FILEs. Those still add through
                // their untouched get-flags, so writing the total unconditionally
                // would take entries back off the player.
                short current = 0;
                sys.getFlag(SystemSheet, ReportTotalFlag, ref current);
                if (current >= total) return;

                sys.setFlagData(SystemSheet, ReportTotalFlag, total);
                Plugin.Log.LogInfo($"[RESEARCH] journal entries {current} -> {total} "
                                 + $"(count flag={counted}, copy flags={copies})");
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"[RESEARCH] report-b sync failed: {ex.Message}");
            }
        }
    }
}
