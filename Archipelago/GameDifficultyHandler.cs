using L2Base;

namespace LaMulana2Archipelago.Archipelago
{
    /// <summary>
    /// AP "Game Difficulty" option: lets the player start a seed in hard mode
    /// without scanning the Mausoleum tablet (which entrance shuffle may hide).
    ///
    /// Two parts:
    ///   • Flat hard-mode markers (see <see cref="HardMarkerFlags"/>) — written
    ///     to a fixed value for Hard, cleared to 0 for Normal. These also record
    ///     which difficulty the save was last played on.
    ///   • G_Difficulty (flag 0,53), the value bosses / mob spawners read via
    ///     L2System.getGameLevel. The Mausoleum tablet's vanilla effect is +3.
    ///     It also climbs above that baseline during play to scale enemies, so
    ///     ApplyTo only SHIFTS it by ±StepSize when the toggle differs from the
    ///     last-played state — never pins it — preserving natural progression.
    /// </summary>
    public class GameDifficultyHandler
    {
        public enum DifficultyState
        {
            Normal = 0,
            Hard = 1,
        }

        /// <summary>G_Difficulty offset for Hard (the tablet's vanilla effect).</summary>
        public const int StepSize = 3;

        /// <summary>
        /// Flat hard-mode marker flags, each row being { sheet, flag, value }.
        /// Written to their value for Hard, cleared to 0 for Normal. Reading the
        /// first one tells us the difficulty the save was last played on (unlike
        /// G_Difficulty, which climbs during play and can't distinguish tiers).
        /// G_Difficulty (0,53) is handled separately via setGameLevel.
        /// </summary>
        private static readonly short[][] HardMarkerFlags =
        {
            new short[] { 5, 23, 2 },
            new short[] { 19, 318, 2 },
        };

        // Per-seed authority for whether the save was set up in hard mode.
        // Retained for logging / the title-screen toggle baseline.
        private readonly DifficultyState slotState;
        private DifficultyState state;

        public DifficultyState State => state;
        public DifficultyState SlotState => slotState;
        public bool IsHardMode => state != DifficultyState.Normal;
        public int Level => (int)state * StepSize;

        /// <param name="slotDataValue">0 = Normal, 1 = Hard. Out-of-range defaults to Normal.</param>
        public GameDifficultyHandler(int slotDataValue)
        {
            state = slotState = slotDataValue == 1 ? DifficultyState.Hard : DifficultyState.Normal;
        }

        /// <summary>Toggle Normal ↔ Hard and re-apply live.</summary>
        public void ToggleHardMode()
        {
            state = IsHardMode ? DifficultyState.Normal : DifficultyState.Hard;
            ApplyToLiveSystem();
            Plugin.Log.LogInfo($"[AP] Game difficulty toggled -> {state} (offset={Level})");
        }

        /// <summary>
        /// Align the save to the current difficulty: shift G_Difficulty by the
        /// toggle delta (±StepSize) and write the flat markers. No-op when the
        /// save is already at the target difficulty, so a hard save's climbed
        /// G_Difficulty is preserved across reloads. Safe to call from save-load
        /// / new-game hooks.
        /// </summary>
        public void ApplyTo(L2System sys)
        {
            if (sys == null) return;

            // Detect the difficulty the save was last played on from a flat
            // marker — G_Difficulty itself can't tell us, since it climbs.
            short marker = 0;
            sys.getFlag(HardMarkerFlags[0][0], HardMarkerFlags[0][1], ref marker);
            DifficultyState saveState = marker != 0 ? DifficultyState.Hard : DifficultyState.Normal;

            int currentLevel = sys.getGameLevel();

            if (state == saveState)
            {
                Plugin.Log.LogInfo($"[AP] Difficulty already aligned: {state} (slot={slotState}, G_Difficulty={currentLevel})");
                return;
            }

            // Shift G_Difficulty by the toggle delta so natural in-run
            // progression (which raises it above the hard baseline) survives.
            int delta = ((int)state - (int)saveState) * StepSize;
            int newLevel = currentLevel + delta;
            if (newLevel < 0) newLevel = 0;
            sys.setGameLevel(newLevel);

            // Record the new last-played difficulty in the flat markers.
            foreach (short[] f in HardMarkerFlags)
            {
                sys.setFlagData(f[0], f[1], IsHardMode ? f[2] : (short)0);
            }

            Plugin.Log.LogInfo($"[AP] Difficulty changed: {saveState} -> {state}, G_Difficulty {currentLevel} -> {newLevel} (delta={delta:+0;-0;0}, slot={slotState})");
        }

        private void ApplyToLiveSystem()
        {
            var sys = UnityEngine.Object.FindObjectOfType<L2System>();
            if (sys != null) ApplyTo(sys);
        }
    }
}
