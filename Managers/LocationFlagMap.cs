using System.Collections.Generic;
using LaMulana2RandomizerShared;
using LaMulana2Archipelago.Managers;

namespace LaMulana2Archipelago
{
    public static class LocationFlagMap
    {
        // Numeric: (sheet, flag) => LocationID
        private static readonly Dictionary<int, LocationID> NumericFlagMap = new Dictionary<int, LocationID>();

        // Optional per-entry minimum value gate. If present, the reported value
        // must be >= the stored threshold or the location is not reported.
        private static readonly Dictionary<int, short> NumericFlagMinValues = new Dictionary<int, short>();

        // String: (sheet, name) => LocationID
        // Use stable composite string key rather than GetHashCode.
        private static readonly Dictionary<string, LocationID> StringFlagMap = new Dictionary<string, LocationID>();

        /// <summary>
        /// Legacy: initialize from seed.lm2r file (called at Awake before AP connection).
        /// </summary>
        public static void InitializeFromSeed()
        {
            Initialize(slotData: null);
        }

        /// <summary>
        /// Initialize from AP slot_data (called after successful AP connection).
        /// Falls back to seed.lm2r if slot_data lacks item_placements.
        /// </summary>
        public static void InitializeFromSlotData(Dictionary<string, object> slotData)
        {
            Initialize(slotData);
        }

        private static void Initialize(Dictionary<string, object> slotData)
        {
            NumericFlagMap.Clear();
            NumericFlagMinValues.Clear();
            StringFlagMap.Clear();

            RegisterNumericFlags(slotData);
            RegisterStringFlags();

            Plugin.Log.LogInfo($"[LocationFlagMap] Loaded {NumericFlagMap.Count} numeric flags, {StringFlagMap.Count} string flags");
        }

        // ----------------------------
        // USER-FACING REGISTRATION API
        // ----------------------------

        private static void RegisterNumericFlags(Dictionary<string, object> slotData)
        {
            SeedFlagMapBuilder.BuildIntoMap(
                addNumeric: AddNumeric,
                logInfo: Plugin.Log.LogInfo,
                logWarn: Plugin.Log.LogWarning,
                slotData: slotData
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

        public static bool TryGetNumeric(int sheet, int flag, short value, out LocationID location)
        {
            int key = MakeNumericKey(sheet, flag);

            if (!NumericFlagMap.TryGetValue(key, out location))
                return false;

            if (NumericFlagMinValues.TryGetValue(key, out short minValue) && value < minValue)
            {
                location = default(LocationID);
                return false;
            }

            return true;
        }

        // Overload for callers that just want the mapping without applying any
        // value threshold (callers outside the flag-set notification path that
        // don't have a meaningful "current value" to test against).
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

        private static void AddNumeric(int sheet, int flag, LocationID location, short minValue)
        {
            int key = MakeNumericKey(sheet, flag);

            if (NumericFlagMap.ContainsKey(key))
            {
                Plugin.Log.LogWarning($"[LocationFlagMap] Duplicate numeric key seet={sheet} flag={flag} (existing={NumericFlagMap[key]}, new={location}) — keeping first");
                return;
            }

            NumericFlagMap.Add(key, location);
            NumericFlagMinValues[key] = minValue;
            Plugin.Log.LogDebug($"[LocationFlagMap] Numeric seet={sheet} flag={flag} → {location} (minValue={minValue})");
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
        // PUBLIC REGISTRATION (for subsystems like Potsanity)
        // ----------------------------

        /// <summary>
        /// Register an additional numeric flag mapping after initial build.
        /// Used by ItemPotPatch to add pot flag → LocationID entries.
        /// </summary>
        public static void RegisterNumeric(int sheet, int flag, LocationID location)
        {
            AddNumeric(sheet, flag, location);
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
