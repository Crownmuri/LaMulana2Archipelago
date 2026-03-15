using System.Collections.Generic;
using LaMulana2RandomizerShared;
using LaMulana2Archipelago.Archipelago;

namespace LaMulana2Archipelago.Managers
{
    public static class LocationReporter
    {
        private const long BaseApLocationId = 430000;

        // Optional: per-session dedup by LocationID.
        // (CheckManager already dedups by AP id, but this is harmless if you also call Reporter elsewhere)
        private static readonly HashSet<LocationID> ReportedLocations = new HashSet<LocationID>();

        public static void Report(LocationID location)
        {
            if (!ArchipelagoClient.Authenticated)
            {
                Plugin.Log.LogDebug($"[AP REPORT] Not connected, skipping {location}");
                return;
            }

            if (!ReportedLocations.Add(location))
            {
                Plugin.Log.LogDebug($"[AP REPORT] Already reported: {location}");
                return;
            }

            long apLocationId = BaseApLocationId + (int)location;

            Plugin.Log.LogInfo($"[AP REPORT] Sending location {location} (AP {apLocationId})");
            ArchipelagoClientProvider.Client.SendLocationCheck(apLocationId);
        }

        public static void Reset()
        {
            ReportedLocations.Clear();
            Plugin.Log.LogInfo("[AP REPORT] Reset reported locations");
        }
    }
}
