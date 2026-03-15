using L2Base;

namespace LaMulana2Archipelago
{
    internal static class SaveData
    {
        private const int Sheet = 30;   // unused sheet
        private const int Flag = 0;     // single flag for AP index

        public static void SaveIndex(int index, L2System sys)
        {
            if (sys == null) return;
            sys.setFlagData(Sheet, Flag, (short)index);
        }

        public static int LoadIndex(L2System sys)
        {
            if (sys == null) return 0;
            short val = 0;
            sys.getFlag(Sheet, Flag, ref val);
            return val;
        }

        public static void ResetIndex(L2System sys)
        {
            if (sys == null) return;
            sys.setFlagData(Sheet, Flag, 0);
        }
    }
}