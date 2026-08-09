using System.Threading;

namespace LaMulana2Archipelago.Managers
{
    /// <summary>
    /// DIAGNOSTIC (temporary): background watchdog that detects a frozen main
    /// thread and logs WHERE it stalled. A hang stops Plugin.Update, so no
    /// in-frame logging can fire during the freeze — this runs on its own thread
    /// (which the hang never touches) and reports the last breadcrumb plus the
    /// current game moji-script position the moment the main thread stops ticking.
    ///
    /// Purpose: pin the intermittent shop / NPC-glossary exit freeze.
    ///   • If the loop is a moji-script jump, RunScriptLineWatchdogPatch keeps
    ///     NoteScriptLine firing at a huge rate while the heartbeat is frozen —
    ///     the logged (sheet, id) is the spinning branch.
    ///   • If the script-call rate is ~0 during the stall, the loop is native
    ///     code elsewhere and LastBreadcrumb still narrows it down.
    ///
    /// Note: a long legitimate operation (scene load, DataPackage parse on
    /// connect) also freezes Update briefly and can trip a one-shot "stalled"
    /// line — but it is followed by "resumed". A real freeze never resumes.
    /// Remove this class (and RunScriptLineWatchdogPatch + its call sites) once
    /// the freeze is fixed.
    /// </summary>
    public static class HangWatchdog
    {
        // Main-thread liveness: a monotonic counter bumped every Plugin.Update.
        // Single writer (main), single reader (watchdog) → volatile int is safe.
        private static volatile int _heartbeat;

        // runScriptLine call counter + last script position (single main-thread
        // writer). The watchdog reads these when it detects a stall.
        private static volatile int _scriptCalls;
        private static volatile string _lastScriptSheet;
        private static volatile string _lastScriptId;
        private static volatile int _lastScriptSheetNo;
        private static volatile int _lastScriptLineNo;

        // Generic breadcrumb any patch can drop on a suspect path.
        private static volatile string _lastBreadcrumb = "(none)";

        private static Thread _thread;
        private static volatile bool _running;

        // Poll cadence and the stall threshold (~4s of no frame → declare a stall).
        private const int PollMs = 500;
        private const int StallSamples = 8;

        public static void Heartbeat() => _heartbeat++;

        public static void NoteScriptLine(int sheetNo, int lineNo, string sheet, string id)
        {
            _scriptCalls++;
            _lastScriptSheetNo = sheetNo;
            _lastScriptLineNo = lineNo;
            _lastScriptSheet = sheet;
            _lastScriptId = id;
        }

        public static string LastBreadcrumb
        {
            get => _lastBreadcrumb;
            set => _lastBreadcrumb = value;
        }

        // Method enter/exit trail. A method that enters but never exits (the hang)
        // leaves "IN:<name>" as the standing breadcrumb, because nothing overwrote
        // it; a completed method leaves "OUT:<name>". Used by BunsyouWatchdogPatch.
        public static void Enter(string name) => _lastBreadcrumb = "IN:" + name;
        public static void Exit(string name) => _lastBreadcrumb = "OUT:" + name;

        public static void Start()
        {
            if (_thread != null) return;
            _running = true;
            _thread = new Thread(WatchLoop) { IsBackground = true, Name = "AP-HangWatchdog" };
            _thread.Start();
            Plugin.Log.LogInfo("[Watchdog] Hang watchdog started");
        }

        public static void Stop() => _running = false;

        private static void WatchLoop()
        {
            int lastHeartbeat = _heartbeat;
            int lastScriptCalls = _scriptCalls;
            int stallCount = 0;
            bool reported = false;

            while (_running)
            {
                try { Thread.Sleep(PollMs); }
                catch { return; }

                int hb = _heartbeat;
                int calls = _scriptCalls;

                if (hb == lastHeartbeat)
                {
                    // No frame since the last sample. Only meaningful once the
                    // game has begun ticking at all (hb > 0).
                    if (hb > 0) stallCount++;

                    if (stallCount == StallSamples && !reported)
                    {
                        reported = true;
                        int callDelta = calls - lastScriptCalls; // ~0 = native loop; large = script spin
                        Plugin.Log.LogError(
                            "[Watchdog] MAIN THREAD STALLED (~" + (stallCount * PollMs) + "ms, no frame). " +
                            "runScriptLine calls in last ~" + PollMs + "ms=" + callDelta + ". " +
                            "lastScript sheet='" + _lastScriptSheet + "' id='" + _lastScriptId +
                            "' sheetNo=" + _lastScriptSheetNo + " lineNo=" + _lastScriptLineNo + ". " +
                            "lastBreadcrumb='" + _lastBreadcrumb + "'.");
                    }
                }
                else
                {
                    if (reported)
                        Plugin.Log.LogWarning("[Watchdog] Main thread resumed after stall.");
                    stallCount = 0;
                    reported = false;
                }

                lastHeartbeat = hb;
                lastScriptCalls = calls;
            }
        }
    }
}
