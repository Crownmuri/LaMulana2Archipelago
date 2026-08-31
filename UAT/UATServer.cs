using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LaMulana2Archipelago.UAT
{
    /// <summary>
    /// UAT (Universal Auto Tracking) server for offline play.
    ///
    /// Offline there is no Archipelago room, so PopTracker has nothing to
    /// connect to. UAT fills that gap: the game hosts, the tracker connects,
    /// and the tracker's "Map Tracker + Entrances (Offline)" variant maps the
    /// variables published here onto the same handlers the AP backend drives.
    ///
    /// Protocol (github.com/black-sliver/UAT): each packet is a JSON ARRAY of
    /// command objects. On connect the server sends Info; the client answers
    /// with Sync; the server replies with one Var per variable and then pushes
    /// a Var whenever one changes.
    ///
    /// It is a state store, not an event stream: every variable always carries
    /// the whole current value. That is what makes a reconnect lossless -- the
    /// tracker rebuilds from the Sync dump rather than replaying history.
    ///
    /// Variables published (see scripts/uat.lua in the PopTracker pack, which
    /// documents the consuming side of this exact contract):
    ///   slot_data        object  same shape as the apworld's fill_slot_data()
    ///   items            array   game ItemIDs held, repeated per copy
    ///   locations        array   game LocationIDs checked
    ///   guardian_kills   array   LocationID enum names, e.g. "Fafnir"
    ///   dissonance_count number  cumulative natural dissonances
    ///   shop_items       object  LocationID (string key) -> ItemID
    ///
    /// IDs are GAME ids here (the LocationID/ItemID enums, what seed.lm2r
    /// speaks). The tracker adds its 420000/430000 AP bases on the way in.
    /// </summary>
    internal static class UATServer
    {
        // Mirrors CheckManager's constants; AP ids are BASE + game id.
        private const long BaseApItemId = 420000;
        private const long BaseApLocationId = 430000;

        private static readonly UATWebSocketServer Socket = new UATWebSocketServer();
        private static readonly object StateLock = new object();

        private static readonly Dictionary<string, JToken> Vars =
            new Dictionary<string, JToken>();

        // items/locations are append-only within a run and must stay in step:
        // the tracker tail-applies new items rather than replaying the list, and
        // falls back to a full rebuild only when the array stops being an
        // extension of what it already applied. ResetRun is what deliberately
        // triggers that rebuild on a new file.
        private static readonly JArray Items = new JArray();
        private static readonly JArray Locations = new JArray();
        private static readonly JArray GuardianKills = new JArray();
        private static readonly HashSet<long> SeenLocations = new HashSet<long>();
        private static readonly HashSet<string> SeenGuardians = new HashSet<string>();

        // location game id -> item game id, from the seed's placements.
        private static readonly Dictionary<int, int> Placements = new Dictionary<int, int>();

        public static bool Running { get { return Socket.Running; } }

        // ============================================================
        // Lifecycle
        // ============================================================

        public static bool Start()
        {
            Socket.OnConnect = OnClientConnected;
            Socket.OnMessage = OnClientMessage;
            return Socket.Start();
        }

        public static void Stop()
        {
            Socket.Stop();
        }

        /// <summary>
        /// Drop everything derived from the previous run. Called when offline
        /// mode activates, so loading a different save publishes a shorter
        /// items array and the tracker rebuilds from scratch instead of
        /// stacking the new run onto the old counts.
        /// </summary>
        public static void ResetRun()
        {
            lock (StateLock)
            {
                Items.Clear();
                Locations.Clear();
                GuardianKills.Clear();
                SeenLocations.Clear();
                SeenGuardians.Clear();
                Vars["items"] = Items;
                Vars["locations"] = Locations;
                Vars["guardian_kills"] = GuardianKills;
                Vars["dissonance_count"] = 0;
            }
            BroadcastVars("items", "locations", "guardian_kills", "dissonance_count");
        }

        // ============================================================
        // State in
        // ============================================================

        /// <summary>
        /// Publish the seed. Also indexes item_placements and shop_placements,
        /// which is what lets the run be reconstructed from checked locations
        /// alone -- see AddCheckedLocation.
        /// </summary>
        public static void SetSlotData(Dictionary<string, object> slotData)
        {
            if (slotData == null) return;

            JObject json;
            try
            {
                json = JObject.FromObject(slotData);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[UAT] slot_data is not serialisable: " + ex.Message);
                return;
            }

            // Own glossary ROMs and own pot filler are written into the
            // placements as per-location AP placeholders (410000+n) so the
            // location's sheet-31 machinery fires the check. The tracker keys
            // ITEM_MAPPING on real ids, so a placeholder published here is
            // simply unmappable -- glossary_count would never move offline.
            var ownBehindPlaceholder =
                Managers.SeedToSlotData.GetOwnPlaceholderItems(slotData);

            var shops = new JObject();
            lock (StateLock)
            {
                Placements.Clear();
                IndexPlacements(json["item_placements"] as JArray, ownBehindPlaceholder);
                IndexPlacements(json["shop_placements"] as JArray, ownBehindPlaceholder);

                var shopPlacements = json["shop_placements"] as JArray;
                if (shopPlacements != null)
                {
                    foreach (var entry in shopPlacements)
                    {
                        var loc = entry["location"];
                        var item = entry["item"];
                        if (loc == null || item == null) continue;
                        shops[((int)loc).ToString()] =
                            ResolveItem((int)loc, (int)item, ownBehindPlaceholder);
                    }
                }

                Vars["slot_data"] = json;
                Vars["shop_items"] = shops;

                // The seed also hands over what the player starts with; those
                // never correspond to a checked location.
                Items.Clear();
                var starting = json["starting_items"] as JArray;
                if (starting != null)
                    foreach (var id in starting) Items.Add((int)id);

                // starting_weapon is its OWN seed field, not a member of
                // starting_items -- online AP precollects it so it still
                // arrives as a received item, but offline nothing else would
                // ever publish it and the tracker would never mark it held.
                var startingWeapon = json["starting_weapon"];
                if (startingWeapon != null && (int)startingWeapon > 0)
                    Items.Add((int)startingWeapon);

                Vars["items"] = Items;
            }

            BroadcastVars("slot_data", "shop_items", "items");
            Plugin.Log.LogInfo(
                $"[UAT] Published slot_data ({Placements.Count} placements, {shops.Count} shop slots)");
        }

        private static void IndexPlacements(
            JArray placements, Dictionary<int, int> ownBehindPlaceholder)
        {
            if (placements == null) return;
            foreach (var entry in placements)
            {
                var loc = entry["location"];
                var item = entry["item"];
                if (loc == null || item == null) continue;
                Placements[(int)loc] =
                    ResolveItem((int)loc, (int)item, ownBehindPlaceholder);
            }
        }

        /// <summary>
        /// Substitutes the real own-world item id for a per-location AP
        /// placeholder. Anything not listed is published unchanged -- a pre-v5
        /// seed has no map, and offline that is the best answer available.
        /// </summary>
        private static int ResolveItem(
            int gameLocation, int rawItem, Dictionary<int, int> ownBehindPlaceholder)
        {
            int ownItem;
            if (ownBehindPlaceholder != null
                && ownBehindPlaceholder.TryGetValue(gameLocation, out ownItem))
                return ownItem;
            return rawItem;
        }

        /// <summary>
        /// Record a checked location, and with it whatever the seed placed
        /// there.
        ///
        /// Items are derived rather than hooked at the grant sites: a grant has
        /// half a dozen exit paths and does not replay on a save load, whereas
        /// the checked-location set is authoritative and restored with the
        /// save. Deriving also keeps items and locations appending in lockstep,
        /// which is exactly the invariant the tracker's tail-apply relies on.
        /// </summary>
        public static void AddCheckedLocation(long apLocationId)
        {
            if (!Socket.Running) return;

            int gameLocation = (int)(apLocationId - BaseApLocationId);
            bool addedItem = false;

            lock (StateLock)
            {
                if (!SeenLocations.Add(apLocationId)) return;
                Locations.Add(gameLocation);

                int itemId;
                if (Placements.TryGetValue(gameLocation, out itemId))
                {
                    Items.Add(itemId);
                    addedItem = true;
                }
            }

            if (addedItem) BroadcastVars("locations", "items");
            else BroadcastVars("locations");
        }

        /// <summary>
        /// Boss locations are event-only in the AP world, so online they are
        /// mirrored to datastorage instead of the checked-locations broadcast.
        /// Offline this variable takes that role; the name matches the
        /// LocationID enum, as the datastorage key does.
        /// </summary>
        public static void AddGuardianKill(string guardianName)
        {
            if (!Socket.Running || string.IsNullOrEmpty(guardianName)) return;

            lock (StateLock)
            {
                if (!SeenGuardians.Add(guardianName)) return;
                GuardianKills.Add(guardianName);
                Vars["guardian_kills"] = GuardianKills;
            }
            BroadcastVars("guardian_kills");
        }

        /// <summary>
        /// Cumulative natural dissonance count (flag[2,3]). Only meaningful
        /// when the seed left dissonance vanilla; with random_dissonance on,
        /// Progressive Beherit items carry the count and the tracker ignores
        /// this. DissonanceTracker already applies that guard before calling.
        /// </summary>
        public static void SetDissonanceCount(int count)
        {
            if (!Socket.Running) return;
            lock (StateLock)
            {
                var current = Vars.ContainsKey("dissonance_count") ? Vars["dissonance_count"] : null;
                if (current != null && (int)current == count) return;
                Vars["dissonance_count"] = count;
            }
            BroadcastVars("dissonance_count");
        }

        // ============================================================
        // Protocol out
        // ============================================================

        private static void OnClientConnected(object client)
        {
            var info = new JObject
            {
                ["cmd"] = "Info",
                ["protocol"] = 0,
                ["name"] = "La-Mulana 2",
                ["version"] = Plugin.PluginVersion,
                ["features"] = new JArray(),
                ["slots"] = new JArray(),
            };
            Socket.Send(client, Wrap(info));
        }

        private static void OnClientMessage(object client, string text)
        {
            JArray packet;
            try
            {
                packet = JArray.Parse(text);
            }
            catch (JsonException ex)
            {
                Plugin.Log.LogWarning("[UAT] Malformed packet ignored: " + ex.Message);
                return;
            }

            foreach (var command in packet)
            {
                var cmd = command["cmd"];
                if (cmd == null) continue;

                // Sync is the only client command UAT defines. Answer with the
                // full store; the tracker rebuilds its whole state from it.
                if ((string)cmd == "Sync") SendAllVars(client);
                else Plugin.Log.LogDebug("[UAT] Ignoring unknown command: " + (string)cmd);
            }
        }

        private static void SendAllVars(object client)
        {
            var commands = new JArray();
            lock (StateLock)
            {
                foreach (var pair in Vars) commands.Add(VarCommand(pair.Key, pair.Value));
            }
            if (commands.Count == 0) return;
            Socket.Send(client, commands.ToString(Formatting.None));
            Plugin.Log.LogInfo($"[UAT] Sync: sent {commands.Count} variables");
        }

        private static void BroadcastVars(params string[] names)
        {
            if (!Socket.Running || Socket.ClientCount == 0) return;

            var commands = new JArray();
            lock (StateLock)
            {
                foreach (string name in names)
                {
                    JToken value;
                    if (Vars.TryGetValue(name, out value)) commands.Add(VarCommand(name, value));
                }
            }
            if (commands.Count > 0) Socket.Broadcast(commands.ToString(Formatting.None));
        }

        private static JObject VarCommand(string name, JToken value)
        {
            // Deep-clone: Items/Locations keep mutating after this packet is
            // built, and a live reference would let the JSON writer race the
            // game thread appending to it.
            return new JObject
            {
                ["cmd"] = "Var",
                ["name"] = name,
                ["value"] = value == null ? JValue.CreateNull() : value.DeepClone(),
            };
        }

        private static string Wrap(JObject command)
        {
            return new JArray { command }.ToString(Formatting.None);
        }
    }
}
