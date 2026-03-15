using System.Collections.Generic;
using LaMulana2RandomizerShared;
using LaMulana2Archipelago.Managers;

namespace LaMulana2Archipelago
{
    public static class LocationFlagMap
    {
        // Numeric: (sheet, flag) => LocationID
        private static readonly Dictionary<int, LocationID> NumericFlagMap = new Dictionary<int, LocationID>();

        // String: (sheet, name) => LocationID
        // Use stable composite string key rather than GetHashCode.
        private static readonly Dictionary<string, LocationID> StringFlagMap = new Dictionary<string, LocationID>();

        public static void InitializeFromSeed()
        {
            NumericFlagMap.Clear();
            StringFlagMap.Clear();

            RegisterNumericFlags();
            RegisterStringFlags();

            Plugin.Log.LogInfo($"[LocationFlagMap] Loaded {NumericFlagMap.Count} numeric flags, {StringFlagMap.Count} string flags");
        }

        // ----------------------------
        // USER-FACING REGISTRATION API
        // ----------------------------

        // This is now seed-driven. No hardcoded entries here.
        private static void RegisterNumericFlags()
        {
            // NumericFlags come from seed.lm2r (rigid, correct, includes shops)
            SeedFlagMapBuilder.BuildIntoMap(
                addNumeric: AddNumeric,
                logInfo: Plugin.Log.LogInfo,
                logWarn: Plugin.Log.LogWarning
            );
        }

        private static void RegisterStringFlags()
        {
            // String flags are optional. Keep this for future edge cases.
            // Example:
            // AddString(sheet: 5, name: "TalkedToXelpud", location: LocationID.XelpudItem);
        }

        // ----------------------------
        // LOOKUP
        // ----------------------------

        public static bool TryGetNumeric(int sheet, int flag, out LocationID location)
        {
            return NumericFlagMap.TryGetValue(MakeNumericKey(sheet, flag), out location);
        }

        public static bool TryGetString(int sheet, string name, out LocationID location)
        {
            return StringFlagMap.TryGetValue(MakeStringKey(sheet, name), out location);
        }

        // ----------------------------
        // INTERNAL ADDERS (called by builder or optional manual map)
        // ----------------------------

        private static void AddNumeric(int sheet, int flag, LocationID location)
        {
            int key = MakeNumericKey(sheet, flag);

            if (NumericFlagMap.ContainsKey(key))
            {
                Plugin.Log.LogWarning($"[LocationFlagMap] Duplicate numeric key seet={sheet} flag={flag} (existing={NumericFlagMap[key]}, new={location}) — keeping first");
                return;
            }

            NumericFlagMap.Add(key, location);
            Plugin.Log.LogDebug($"[LocationFlagMap] Numeric seet={sheet} flag={flag} → {location}");
        }

        private static void AddString(int sheet, string name, LocationID location)
        {
            string key = MakeStringKey(sheet, name);

            if (StringFlagMap.ContainsKey(key))
            {
                Plugin.Log.LogWarning($"[LocationFlagMap] Duplicate string key seet={sheet} name={name} (existing={StringFlagMap[key]}, new={location}) — keeping first");
                return;
            }

            StringFlagMap.Add(key, location);
            Plugin.Log.LogDebug($"[LocationFlagMap] String seet={sheet} name={name} → {location}");
        }

        // ----------------------------
        // KEY HELPERS
        // ----------------------------

        private static int MakeNumericKey(int sheet, int flag)
        {
            return (sheet << 16) | (flag & 0xFFFF);
        }

        private static string MakeStringKey(int sheet, string name)
        {
            // stable composite key
            return sheet.ToString() + ":" + (name ?? "");
        }
    }
}
