using System.Collections.Generic;

namespace LaMulana2Archipelago.Managers
{
    /// <summary>
    /// Stores flag changes detected during unsafe periods (dialog / scripts).
    /// These are resolved only after gameplay resumes.
    /// </summary>
    public static class FlagQueueDeferred
    {
        public struct NumericFlag
        {
            public int Sheet;
            public int Flag;
            public short Value;
        }

        public struct StringFlag
        {
            public int Sheet;
            public string Name;
            public short Value;
        }

        private static readonly List<NumericFlag> NumericFlags = new();
        private static readonly List<StringFlag> StringFlags = new();

        public static void EnqueueNumeric(int sheet, int flag, short value)
        {
            NumericFlags.Add(new NumericFlag
            {
                Sheet = sheet,
                Flag = flag,
                Value = value
            });
        }

        public static void EnqueueString(int sheet, string name, short value)
        {
            StringFlags.Add(new StringFlag
            {
                Sheet = sheet,
                Name = name,
                Value = value
            });
        }

        /// <summary>
        /// Called only when NOT in KataribeScript.
        /// </summary>
        public static void Flush()
        {
            foreach (var f in NumericFlags)
                CheckManager.NotifyNumericFlag(f.Sheet, f.Flag, f.Value);

            foreach (var f in StringFlags)
                CheckManager.NotifyStringFlag(f.Sheet, f.Name, f.Value);

            NumericFlags.Clear();
            StringFlags.Clear();
        }

        public static bool HasPending =>
            NumericFlags.Count > 0 || StringFlags.Count > 0;
    }
}