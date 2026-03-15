using BepInEx;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace LaMulana2Archipelago.Archipelago
{
    public class ArchipelagoData
    {
        private string _uri;

        /// <summary>
        /// Raw URI exactly as the user typed it (for UI display and saving).
        /// </summary>
        public string Uri
        {
            get => _uri;
            set => _uri = value;
        }

        /// <summary>
        /// URI normalized for WebSocket connection.
        /// </summary>
        public string NormalizedUri => NormalizeUri(_uri);

        /// <summary>
        /// Ensures the URI has the correct WebSocket scheme.
        /// localhost/127.0.0.1 uses unencrypted ws://, everything else uses wss://.
        /// </summary>
        private static string NormalizeUri(string uri)
        {
            if (uri == null || uri.Trim().Length == 0)
                return uri;

            uri = uri.Trim();

            // Already has an explicit scheme — leave it alone.
            if (uri.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) ||
                uri.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
                return uri;

            // Strip accidental http/https
            if (uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                uri = uri.Substring(7);
            if (uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                uri = uri.Substring(8);

            string host = uri.Split(':')[0].ToLowerInvariant();
            bool isLocal = host == "localhost" || host == "127.0.0.1";

            return (isLocal ? "ws://" : "wss://") + uri;
        }

        public string SlotName;
        public string Password;

        /// <summary>
        /// Persisted "last processed/granted item index".
        /// We advance this after successful in-game grants (or skip batches).
        /// </summary>
        public int Index;

        public List<long> CheckedLocations;

        /// <summary>
        /// Room seed string (from the AP session). Used for per-seed persistence keying.
        /// </summary>
        private string seed;

        /// <summary>
        /// Expose room seed for persistence keying.
        /// </summary>
        public string RoomSeed => seed;

        private Dictionary<string, object> slotData;
        public bool NeedSlotData => slotData == null;

        public ArchipelagoData()
        {
            Uri = "localhost:30485";
            SlotName = "CrownLumisa";
            Password = "";
            Index = 0;
            CheckedLocations = new();
        }

        public ArchipelagoData(string uri, string slotName, string password)
        {
            Uri = uri;
            SlotName = slotName;
            Password = password;
            Index = 0;
            CheckedLocations = new();
        }

        public void SetupSession(Dictionary<string, object> roomSlotData, string roomSeed)
        {
            slotData = roomSlotData;
            seed = roomSeed;
        }
        public bool GetSlotBool(string key, bool defaultValue = false)
        {
            if (slotData == null || !slotData.TryGetValue(key, out object raw))
                return defaultValue;

            // AP sends JSON numbers as long or int depending on the library version.
            try { return Convert.ToInt32(raw) != 0; }
            catch { return defaultValue; }
        }
        public int GetSlotInt(string key, int defaultValue = 0)
        {
            if (slotData == null || !slotData.TryGetValue(key, out object raw))
                return defaultValue;
            try { return Convert.ToInt32(raw); }
            catch { return defaultValue; }
        }
        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }
}