using L2Base;

namespace LaMulana2Archipelago.Archipelago
{
    /// <summary>
    /// AP "Game Difficulty" option: lets the player start a seed in Hard or
    /// Hardest without scanning the Mausoleum tablet (which entrance shuffle
    /// may hide). Drives the "G_Difficulty" system flag that bosses and
    /// mob spawners read via L2System.getGameLevel.
    ///
    /// Each step is +3 to G_Difficulty (the tablet's vanilla effect): Normal=0,
    /// Hard=3, Hardest=6. sheet-19 "hard1"/"hard2" are written/read purely as
    /// the "is this save in any hard mode" marker for ApplyTo's delta math —
    /// they do NOT actually gate the in-game tablet (scanning it in AP hard
    /// mode will still stack another +3, untested for full behaviour).
    /// </summary>
    public class GameDifficultyHandler
    {
        public enum DifficultyState
        {
            Normal = 0,
            Hard = 1,
            Hardest = 2,
        }

        /// <summary>G_Difficulty offset per step. Hardest is two steps.</summary>
        public const int StepSize = 3;

        // Slot value is the per-seed authority for which non-Normal tier the
        // save was set up at — hard1/hard2 alone can't tell Hard from Hardest.
        private readonly DifficultyState slotState;
        private DifficultyState state;

        public DifficultyState State => state;
        public DifficultyState SlotState => slotState;
        public bool IsHardMode => state != DifficultyState.Normal;
        public int Level => (int)state * StepSize;

        /// <param name="slotDataValue">0 = Normal, 1 = Hard, 2 = Hardest. Out-of-range defaults to Normal.</param>
        public GameDifficultyHandler(int slotDataValue)
        {
            if (slotDataValue < 0) slotDataValue = 0;
            if (slotDataValue > 2) slotDataValue = 2;
            slotState = state = (DifficultyState)slotDataValue;
        }

        /// <summary>Cycle Normal → Hard → Hardest → Normal and re-apply live.</summary>
        public void ToggleHardMode()
        {
            state = (DifficultyState)(((int)state + 1) % 3);
            ApplyToLiveSystem();
            Plugin.Log.LogInfo($"[AP] Game difficulty toggled -> {state} (offset={Level})");
        }

        /// <summary>
        /// Shift G_Difficulty by (state - saveState) * StepSize, where
        /// saveState is Normal if hard1/hard2 are both clear, else slotState.
        /// No-op when already aligned. Safe to call from save-load / new-game
        /// hooks; preserves natural progression / Voluspa adjustments by
        /// only shifting the baseline.
        /// </summary>
        public void ApplyTo(L2System sys)
        {
            if (sys == null) return;

            int currentLevel = sys.getGameLevel();

            short hard1Val = 0, hard2Val = 0;
            sys.getFlag(19, "hard1", ref hard1Val);
            sys.getFlag(19, "hard2", ref hard2Val);
            bool saveInHardMode = hard1Val != 0 || hard2Val != 0;

            DifficultyState saveState = saveInHardMode ? slotState : DifficultyState.Normal;

            if (state == saveState)
            {
                Plugin.Log.LogInfo($"[AP] Difficulty already aligned: {state} (slot={slotState}, G_Difficulty={currentLevel})");
                return;
            }

            int delta = ((int)state - (int)saveState) * StepSize;
            int newLevel = currentLevel + delta;
            if (newLevel < 0) newLevel = 0;

            sys.setGameLevel(newLevel);

            // hard1/hard2 are our "in any hard mode" marker for the next
            // ApplyTo's detection — the level value carries Hard vs Hardest.
            // (They don't actually gate the in-game tablet; scanning it on
            // top of AP hard mode will still stack another +3.)
            short hardMarker = (short)(state == DifficultyState.Normal ? 0 : 1);
            sys.setFlagData(19, "hard1", hardMarker);
            sys.setFlagData(19, "hard2", hardMarker);

            Plugin.Log.LogInfo($"[AP] Difficulty changed: {saveState} -> {state}, G_Difficulty {currentLevel} -> {newLevel} (delta={delta:+0;-0;0}, slot={slotState})");
        }

        private void ApplyToLiveSystem()
        {
            var sys = UnityEngine.Object.FindObjectOfType<L2System>();
            if (sys != null) ApplyTo(sys);
        }
    }
}
