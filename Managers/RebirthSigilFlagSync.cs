using L2Flag;

namespace LaMulana2Archipelago.Managers
{
    /// <summary>
    /// When the player obtains the Rebirth Seal (item flag sheet 2 / flag 55,
    /// BoxName "Rebirth Seal"), advance the DLC story flag (25,5) to 4.
    ///
    /// The seal's flag is written through two independent mechanisms depending on
    /// how it's acquired, so we hook both:
    ///   • In-game pickup  → TreasureBox/item setEffectFlag → addFlag(2, 55, 1, EQR)
    ///                        (AddFlagNumericPatch.Postfix → OnNumericFlagWrite)
    ///   • AP grant / shop → sys.setItem("Rebirth Seal") → SetItemPatch →
    ///                        setFlagData(2, "Rebirth Seal", 1) [string overload]
    ///                        (SetFlagDataFlagSystemStringPatch.Postfix → OnNamedFlagWrite)
    /// A numeric setFlagData(2, 55, …) write is covered too, for completeness.
    ///
    /// The write is monotonic-from-below: (25,5) is only ever raised to 4, never
    /// lowered. Save-load re-applies the seal flag, and the DLC may push (25,5)
    /// past 4 later in its questline — the guard makes both cases inert, so
    /// re-application can never regress story progress.
    /// </summary>
    public static class RebirthSigilFlagSync
    {
        // Rebirth Seal item flag ("02Items" sheet).
        private const int SealSheet = 2;
        private const int SealFlag = 55;
        private const string SealName = "Rebirth Seal";

        // DLC story flag to advance on obtain.
        private const int StorySheet = 25;
        private const int StoryFlag = 5;
        private const short StoryValue = 4;

        /// <summary>Numeric flag write (addFlag / setFlagData by number).</summary>
        public static void OnNumericFlagWrite(L2FlagSystem flagSys, int seet, int flag)
        {
            if (seet != SealSheet || flag != SealFlag) return;
            Advance(flagSys);
        }

        /// <summary>Named flag write (setFlagData by name, from sys.setItem).</summary>
        public static void OnNamedFlagWrite(L2FlagSystem flagSys, int seet, string name)
        {
            if (seet != SealSheet || name != SealName) return;
            Advance(flagSys);
        }

        private static void Advance(L2FlagSystem flagSys)
        {
            if (flagSys == null) return;

            // Confirm the seal is actually held (guards against a reset-to-0 write).
            short have = 0;
            flagSys.getFlag(SealSheet, SealFlag, ref have);
            if (have <= 0) return;

            short cur = 0;
            flagSys.getFlag(StorySheet, StoryFlag, ref cur);
            if (cur >= StoryValue) return; // already at/past target — never regress

            flagSys.setFlagData(StorySheet, StoryFlag, StoryValue);
            Plugin.Log.LogInfo($"[REBIRTH] Rebirth Seal obtained → set ({StorySheet},{StoryFlag})={StoryValue} (was {cur})");
        }
    }
}
