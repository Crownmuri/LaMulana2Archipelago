using System.Collections.Generic;
using LaMulana2Archipelago.Archipelago;

namespace LaMulana2Archipelago.Managers
{
    public static class CheckManagerDeferred
    {
        private static readonly Queue<long> pendingLocations = new();

        public static void QueueLocation(long apLocation)
        {
            pendingLocations.Enqueue(apLocation);
            Plugin.Log.LogDebug($"[CHECK] Queued AP location {apLocation}");
        }

        public static void Flush()
        {
            if (!ArchipelagoClient.Authenticated)
                return;

            var client = ArchipelagoClientProvider.Client;
            if (client == null)
                return;

            while (pendingLocations.Count > 0)
            {
                long loc = pendingLocations.Dequeue();
                client.SendLocationCheck(loc);
            }

            Plugin.Log.LogInfo("[CHECK] Flushed pending locations");
        }
    }
}
