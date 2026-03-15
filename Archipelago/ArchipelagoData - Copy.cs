using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace LaMulana2Archipelago.Archipelago
{
    public class ArchipelagoData
    {
        private string _uri;
        public string Uri
        {
            get => _uri;
            set => _uri = NormalizeUri(value);
        }

        /// <summary>
        /// Ensures the URI has the correct WebSocket scheme.
        /// localhost/127.0.0.1 uses unencrypted ws://, everything else uses wss://.
        /// </summary>
        private static string NormalizeUri(string uri)
        {
            if (uri == null || uri.Trim().Length == 0) return uri;

            // Already has an explicit scheme — leave it alone.
            if (uri.StartsWith("ws://") || uri.StartsWith("wss://"))
                return uri;

            // Strip any accidental http/https prefix a user might type.
            if (uri.StartsWith("http://")) uri = uri.Substring(7);
            if (uri.StartsWith("https://")) uri = uri.Substring(8);

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
        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }
}