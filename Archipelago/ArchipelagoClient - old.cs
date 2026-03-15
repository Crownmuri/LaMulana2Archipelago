using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Packets;
using BepInEx;
using LaMulana2Archipelago.Patches;
using LaMulana2Archipelago.Utils;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace LaMulana2Archipelago.Archipelago
{
    public class ArchipelagoClient
    {
        public const string APVersion = "0.5.0";
        private const string Game = "La-Mulana 2";

        public static bool Authenticated;
        private bool attemptingConnection;

        public static ArchipelagoData ServerData = new();
        private ArchipelagoSession session;

        // ============
        // Queue payload
        // ============

        public struct QueuedApItem
        {
            public int Index;
            public long ItemId;
            public string ItemName;

            /// <summary>
            /// Display name of the player whose location sent this item.
            /// Null or empty = item came from this player's own world.
            /// "Server" = sent via server console (!getitem command).
            /// </summary>
            public string SenderName;

            public QueuedApItem(int index, long itemId, string itemName, string senderName)
            {
                Index = index;
                ItemId = itemId;
                ItemName = itemName;
                SenderName = senderName;
            }
        }

        // Main-thread consumed queue (index + item id)
        public static Queue<QueuedApItem> ItemQueue = new();

        private static bool GoalReported;

        public static bool SkipExistingItemsOnConnect = false;

        // =============================
        // Per-LM2-save-slot keying
        // =============================

        private static int _currentSaveSlot = -1;

        public static void SetCurrentSaveSlot(int no)
        {
            if (_currentSaveSlot == no) return;

            int old = _currentSaveSlot;
            _currentSaveSlot = no;

            // If we are moving from staging (-1/unknown) to a real slot,
            // migrate the persisted state forward so it doesn't get orphaned.
            if (old < 0 && no >= 0)
            {
                TryMigrateState(fromSlot: old, toSlot: no);
            }

            // Load the target slot state (if any). While connected, do not rewind Index.
            int before = ServerData.Index;
            LoadPersistentState();
            if (Authenticated && ServerData.Index < before)
                ServerData.Index = before;

            Plugin.Log.LogInfo($"[AP] Current LM2 save slot set -> {_currentSaveSlot} (Index={ServerData.Index})");
        }

        // =============================
        // Persistence (Index + checks)
        // =============================

        [Serializable]
        private class PersistedState
        {
            public int Index;
            public List<long> CheckedLocations;
        }

        private static bool IsNullOrWhiteSpaceCompat(string s)
        {
            return s == null || s.Trim().Length == 0;
        }

        private static string SanitizeForFilename(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s;
        }

        private static string SeedHash8()
        {
            // Prefer AP room seed if available; if not connected yet, empty.
            string seed = (ServerData != null && !IsNullOrWhiteSpaceCompat(ServerData.RoomSeed)) ? ServerData.RoomSeed : "";
            try
            {
                using (var md5 = MD5.Create())
                {
                    byte[] data = Encoding.UTF8.GetBytes(seed);
                    byte[] hash = md5.ComputeHash(data);
                    // first 4 bytes = 8 hex chars
                    return BitConverter.ToString(hash, 0, 4).Replace("-", "").ToLowerInvariant();
                }
            }
            catch
            {
                return "seed00000";
            }
        }

        private static string PersistPathForSlot(int lm2Slot)
        {
            string uri = (ServerData != null && !IsNullOrWhiteSpaceCompat(ServerData.Uri)) ? ServerData.Uri : "offline";
            string apslot = (ServerData != null && !IsNullOrWhiteSpaceCompat(ServerData.SlotName)) ? ServerData.SlotName : "noslot";

            uri = SanitizeForFilename(uri);
            apslot = SanitizeForFilename(apslot);

            string seedHash = SeedHash8();
            string lm2 = (lm2Slot >= 0) ? $"lm2slot{lm2Slot}" : "lm2slot_staging";

            Directory.CreateDirectory(Paths.ConfigPath);
            return Path.Combine(Paths.ConfigPath, $"LM2AP_ClientState_{uri}_{apslot}_{seedHash}_{lm2}.json");
        }

        private static string PersistPath => PersistPathForSlot(_currentSaveSlot);

        public static void LoadPersistentState()
        {
            try
            {
                string path = PersistPath;
                if (!File.Exists(path))
                    return;

                var json = File.ReadAllText(path);
                var st = JsonConvert.DeserializeObject<PersistedState>(json);
                if (st == null) return;

                ServerData.Index = st.Index;
                ServerData.CheckedLocations = st.CheckedLocations ?? new List<long>();

                Plugin.Log.LogInfo($"[AP] Loaded persisted state: Index={ServerData.Index}, Checked={ServerData.CheckedLocations.Count} ({Path.GetFileName(path)})");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[AP] Failed to load persisted client state: {e}");
            }
        }

        private static void SavePersistentState()
        {
            try
            {
                var st = new PersistedState
                {
                    Index = ServerData.Index,
                    CheckedLocations = ServerData.CheckedLocations ?? new List<long>()
                };

                var json = JsonConvert.SerializeObject(st, Formatting.Indented);
                File.WriteAllText(PersistPath, json);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[AP] Failed to save persisted client state: {e}");
            }
        }

        private static PersistedState TryLoadStateForSlot(int lm2Slot)
        {
            try
            {
                string path = PersistPathForSlot(lm2Slot);
                if (!File.Exists(path)) return null;

                var json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<PersistedState>(json);
            }
            catch
            {
                return null;
            }
        }

        private static void TrySaveStateForSlot(int lm2Slot, PersistedState st)
        {
            try
            {
                string path = PersistPathForSlot(lm2Slot);
                Directory.CreateDirectory(Paths.ConfigPath);
                File.WriteAllText(path, JsonConvert.SerializeObject(st, Formatting.Indented));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[AP] Failed to write migrated state: {e}");
            }
        }

        private static void TryDeleteStateForSlot(int lm2Slot)
        {
            try
            {
                string path = PersistPathForSlot(lm2Slot);
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch { }
        }

        private static void TryMigrateState(int fromSlot, int toSlot)
        {
            // fromSlot may be -1 (staging) and toSlot a real number.
            var from = TryLoadStateForSlot(fromSlot);
            if (from == null) return;

            var to = TryLoadStateForSlot(toSlot) ?? new PersistedState { Index = 0, CheckedLocations = new List<long>() };

            to.Index = Math.Max(to.Index, from.Index);

            // union checked locations
            var set = new HashSet<long>(to.CheckedLocations ?? new List<long>());
            if (from.CheckedLocations != null)
                foreach (var l in from.CheckedLocations)
                    set.Add(l);
            to.CheckedLocations = set.ToList();

            TrySaveStateForSlot(toSlot, to);
            TryDeleteStateForSlot(fromSlot);

            Plugin.Log.LogInfo($"[AP] Migrated persisted client state {fromSlot} -> {toSlot} (Index={to.Index}, Checked={to.CheckedLocations.Count})");
        }

        /// <summary>
        /// Call after an AP item is successfully granted in-game.
        /// </summary>
        public static void MarkItemProcessed(int itemIndex)
        {
            if (itemIndex <= ServerData.Index)
                return;

            ServerData.Index = itemIndex;
            SavePersistentState();

            Plugin.Log.LogInfo($"[AP] Processed item index advanced -> {ServerData.Index} (LM2 slot {_currentSaveSlot})");
        }

        // =============================
        // Connection
        // =============================

        public void Connect()
        {
            if (Authenticated || attemptingConnection) return;

            // Load persistence BEFORE connecting so we can dedupe immediately.
            LoadPersistentState();

            try
            {
                session = ArchipelagoSessionFactory.CreateSession(ServerData.Uri);
                SetupSession();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(e);
            }

            TryConnect();
        }

        private void SetupSession()
        {
            session.MessageLog.OnMessageReceived += message => ArchipelagoConsole.LogMessage(message.ToString());
            session.Items.ItemReceived += OnItemReceived;
            session.Socket.ErrorReceived += OnSessionErrorReceived;
            session.Socket.SocketClosed += OnSessionSocketClosed;
        }

        private void TryConnect()
        {
            try
            {
                attemptingConnection = true;

                ThreadPool.QueueUserWorkItem(
                    _ => HandleConnectResult(
                        session.TryConnectAndLogin(
                            Game,
                            ServerData.SlotName,
                            ItemsHandlingFlags.RemoteItems,
                            new Version(APVersion),
                            password: ServerData.Password,
                            requestSlotData: ServerData.NeedSlotData
                        )));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(e);
                HandleConnectResult(new LoginFailure(e.ToString()));
                attemptingConnection = false;
            }
        }

        private void HandleConnectResult(LoginResult result)
        {
            string outText;

            if (result.Successful)
            {
                var success = (LoginSuccessful)result;

                ServerData.SetupSession(success.SlotData, session.RoomState.Seed);

                // Apply slot-data settings to any active Harmony patches.
                bool guardianAnkhs = ServerData.GetSlotBool("guardian_specific_ankhs");
                Patches.GuardianSpecificAnkhPatch.GuardianSpecificAnkhsEnabled = guardianAnkhs;
                Plugin.Log.LogInfo($"[AP] guardian_specific_ankhs = {guardianAnkhs}");

                // Important: RoomSeed just became available; load per-seed persistence now.
                // Do not rewind index while connected.
                int before = ServerData.Index;
                LoadPersistentState();
                if (ServerData.Index < before) ServerData.Index = before;

                Authenticated = true;
                GoalReported = false;

                outText = $"Successfully connected to {ServerData.Uri} as {ServerData.SlotName}!";
                ArchipelagoConsole.LogMessage(outText);

                SavePersistentState();
            }
            else
            {
                var failure = (LoginFailure)result;
                outText = $"Failed to connect to {ServerData.Uri} as {ServerData.SlotName}.";
                outText = failure.Errors.Aggregate(outText, (current, error) => current + $"\n    {error}");

                Plugin.Log.LogError(outText);

                Authenticated = false;
                Disconnect();
            }

            ArchipelagoConsole.LogMessage(outText);
            attemptingConnection = false;
        }

        private void Disconnect()
        {
            Plugin.Log.LogDebug("disconnecting from server...");
            session?.Socket.Disconnect();
            session = null;
            Authenticated = false;
            GoalReported = false;
        }

        // =============================
        // Location checks
        // =============================

        public void SendLocationCheck(long locationId)
        {
            if (!Authenticated || session == null)
                return;

            if (ServerData.CheckedLocations.Contains(locationId))
                return;

            ServerData.CheckedLocations.Add(locationId);
            SavePersistentState();

            session.Locations.CompleteLocationChecks(locationId);
            Plugin.Log.LogInfo($"[AP] Location confirmed: {locationId} (LM2 slot {_currentSaveSlot})");
        }

        public void ReportGoalOnce()
        {
            if (GoalReported) return;
            if (!Authenticated || session == null) return;

            GoalReported = true;

            try
            {
                session.Socket.SendPacketAsync(new StatusUpdatePacket { Status = (ArchipelagoClientState)30 });
                Plugin.Log.LogInfo("[AP] Goal reached (CLIENT_GOAL sent)");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[AP] Failed to send CLIENT_GOAL: {e}");
                GoalReported = false;
            }
        }

        public void SendMessage(string message)
        {
            if (!Authenticated || session == null)
                return;

            session.Socket.SendPacketAsync(new SayPacket { Text = message });
        }

        // =============================
        // Pull AP item info for dialog patching
        // =============================
        public class ScoutedItem
        {
            public string ItemName;
            public string PlayerName;
        }
        public long? GetLocationIdByName(string locationName)
        {
            // Archipelago.NET exposes this on the session's Locations helper
            return session?.Locations?.GetLocationIdFromName(Game, locationName);
        }

        public ScoutedItem GetItemAtLocation(long locationId)
        {
            if (session == null) return null;

            ScoutedItem result = null;
            var done = new System.Threading.ManualResetEvent(false);

            session.Locations.ScoutLocationsAsync(
                scoutResult =>
                {
                    if (scoutResult != null && scoutResult.ContainsKey(locationId))
                    {
                        var info = scoutResult[locationId];
                        result = new ScoutedItem
                        {
                            ItemName = info.ItemName,
                            PlayerName = session.Players.GetPlayerName(info.Player)
                        };
                    }
                    done.Set();
                },
                locationId);

            done.WaitOne(5000);
            return result;
        }

        // =============================
        // Item reception (dedupe by processed index)
        // =============================

        private void OnItemReceived(ReceivedItemsHelper helper)
        {
            bool skipBatch = SkipExistingItemsOnConnect;

            while (helper.PeekItem() != null)
            {
                var item = helper.DequeueItem();
                int itemIndex = helper.Index;

                // Already processed/granted -> ignore
                if (itemIndex <= ServerData.Index)
                    continue;

                if (skipBatch)
                {
                    Plugin.Log.LogInfo($"[AP] Skipped pre-existing item: {item.ItemName} (ID: {item.ItemId}) at Index: {itemIndex}");
                    MarkItemProcessed(itemIndex);
                    continue;
                }

                // Resolve the sending player's display name.
                // item.Player == 0 means the item was sent by the server console.
                // item.Player == session.ConnectionInfo.Slot means sent from own world (show nothing).
                string senderName = null;
                if (item.Player == 0)
                {
                    senderName = "Server";
                }
                else if (item.Player != session.ConnectionInfo.Slot)
                {
                    try { senderName = session.Players.GetPlayerName(item.Player); }
                    catch { /* session may not be fully ready */ }
                }

                ItemQueue.Enqueue(new QueuedApItem(itemIndex, item.ItemId, item.ItemName, senderName));
                Plugin.Log.LogInfo($"[AP] Queued item: {item.ItemName} (ID: {item.ItemId}) from {senderName ?? "self"} at Index: {itemIndex}");

            }

            if (skipBatch)
            {
                SkipExistingItemsOnConnect = false;
                Plugin.Log.LogInfo("[AP] Pre-existing item skip complete.");
            }
        }

        private void OnSessionErrorReceived(Exception e, string message)
        {
            Plugin.Log.LogError(e);
            ArchipelagoConsole.LogMessage(message);
        }

        private void OnSessionSocketClosed(string reason)
        {
            Plugin.Log.LogError($"Connection to Archipelago lost: {reason}");
            Disconnect();
        }

        public static void ResetSession()
        {
            ItemQueue.Clear();
            GoalReported = false;
        }
    }
}