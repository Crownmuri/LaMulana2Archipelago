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
        /// URI prepared for handoff to MultiClient.Net's CreateSession.
        /// Strips accidental http(s):// prefixes; otherwise passes the user's
        /// input through. If the user did not specify a scheme, the AP library
        /// will negotiate ws/wss automatically (its ParseUri wraps schemeless
        /// input in the sentinel "unspecified://" scheme that the socket layer
        /// handles internally).
        /// </summary>
        public string NormalizedUri => NormalizeUri(_uri);

        /// <summary>
        /// Strips accidental http(s):// prefixes and otherwise leaves the URI
        /// alone. We deliberately do NOT pre-pick ws:// or wss:// here — the
        /// previous version of this method forced wss:// onto everything
        /// non-localhost, which broke connections to plain-ws servers
        /// (self-hosted, Hamachi, LAN, archipelago.gg dynamic game-room ports)
        /// with TLS handshake failures. Letting the AP library negotiate the
        /// scheme matches the Archipelago convention used by archipelago.js
        /// and other .NET clients (Hollow Knight, Risk of Rain 2, etc.).
        /// </summary>
        private static string NormalizeUri(string uri)
        {
            if (uri == null || uri.Trim().Length == 0)
                return uri;

            uri = uri.Trim();

            // Explicit scheme — pass through untouched. Players who legitimately
            // need wss:// (rare reverse-proxied custom setups) can opt in here.
            if (uri.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) ||
                uri.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
                return uri;

            // Strip accidental http(s):// from copy-paste.
            if (uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                uri = uri.Substring(7);
            if (uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                uri = uri.Substring(8);

            // No scheme — hand the bare host:port to MultiClient.Net and let
            // its socket layer negotiate ws/wss.
            return uri;
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
            Uri = "localhost:38281"; // 38281
            SlotName = "Lumisa";
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

        /// <summary>
        /// Clear cached slot data / seed so the next Connect() requests slot data
        /// again. Called on Disconnect so reconnecting (same or new seed) works.
        /// </summary>
        public void ClearSessionCache()
        {
            slotData = null;
            seed = null;
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
        public object GetSlotRaw(string key)
        {
            if (slotData == null || !slotData.TryGetValue(key, out object raw))
                return null;
            return raw;
        }

        /// <summary>
        /// Reads a dictionary from slot_data (e.g., pot_flag_map).
        /// AP sends nested JSON objects as Dictionary&lt;string, object&gt; or
        /// Newtonsoft JObject depending on the library version.
        /// </summary>
        public Dictionary<string, object> GetSlotDict(string key)
        {
            if (slotData == null || !slotData.TryGetValue(key, out object raw))
                return null;

            if (raw is Dictionary<string, object> dict)
                return dict;

            if (raw is Newtonsoft.Json.Linq.JObject jObj)
            {
                var result = new Dictionary<string, object>();
                foreach (var prop in jObj)
                    result[prop.Key] = prop.Value;
                return result;
            }

            return null;
        }
        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }
}