using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Packets;
using LaMulana2Archipelago.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>
        /// Set this to true BEFORE connecting on a brand-new game start so that
        /// any items the server already has on record are skipped (their index is
        /// still advanced so they are not re-delivered later).  The game is
        /// expected to hand those starting items out itself.
        /// The flag resets automatically after the first item batch is processed.
        /// </summary>
        public static bool SkipExistingItemsOnConnect = false;

        // =============================
        // Per-LM2-save-slot keying
        // =============================

        private static int _currentSaveSlot = -1;

        public static void SetCurrentSaveSlot(int no)
        {
            if (_currentSaveSlot == no) return;
            _currentSaveSlot = no;
            Plugin.Log.LogInfo($"[AP] Save slot -> {_currentSaveSlot}");
        }

        /// <summary>
        /// Call after an AP item is successfully granted in-game.
        /// </summary>
        public static void MarkItemProcessed(int itemIndex)
        {
            if (itemIndex <= ServerData.Index) return;
            ServerData.Index = itemIndex;
            Plugin.Log.LogInfo($"[AP] Index advanced -> {ServerData.Index}");
        }

        // =============================
        // DeathLink
        // =============================

        public DeathLinkHandler DeathLinkHandler { get; private set; }


        // =============================
        // Connection
        // =============================
        /// <summary>
        /// call to connect to an Archipelago session. Connection info should already be set up on ServerData
        /// </summary>

        public void Connect()
        {
            if (Authenticated || attemptingConnection) return;

            try
            {
                session = ArchipelagoSessionFactory.CreateSession(ServerData.NormalizedUri);
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


        /// <summary>
        /// handle the connection result and do things
        /// </summary>
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

                int APChestColor = ServerData.GetSlotInt("ap_chest_color");
                Plugin.Log.LogInfo($"[AP] ap_chest_color = {APChestColor}");

                bool deathLinkEnabled = ServerData.GetSlotBool("death_link", false);

                DeathLinkHandler = new DeathLinkHandler(
                    session.CreateDeathLinkService(),
                    ServerData.SlotName,
                    deathLinkEnabled
                );

                Plugin.Log.LogInfo("[AP] DeathLink service initialized");

                Authenticated = true;
                GoalReported = false;

                Patches.ShopDialogPatch.Reapply();
                ScoutAllLocations();

                outText = $"Successfully connected to {ServerData.NormalizedUri} as {ServerData.SlotName}!";
                ArchipelagoConsole.LogMessage(outText);
            }
            else
            {
                var failure = (LoginFailure)result;
                outText = $"Failed to connect to {ServerData.NormalizedUri} as {ServerData.SlotName}.";
                outText = failure.Errors.Aggregate(outText, (current, error) => current + $"\n    {error}");

                Plugin.Log.LogError(outText);

                Authenticated = false;
                Disconnect();
            }
            attemptingConnection = false;
        }

        /// <summary>
        /// something went wrong, or we need to properly disconnect from the server. cleanup and re null our session
        /// </summary>
        public void Disconnect()
        {
            Plugin.Log.LogDebug("disconnecting from server...");
            session?.Socket.Disconnect();
            session = null;
            Authenticated = false;
            GoalReported = false;
            ItemQueue.Clear();
        }

        // =============================
        // Location checks
        // =============================
        // 1. Add the cache dictionary near your other fields (e.g., under ServerData)
        private static readonly object cacheLock = new object();
        public static Dictionary<long, ScoutedItem> ScoutedLocationsCache = new Dictionary<long, ScoutedItem>();

        // 2. Replace your existing SendLocationCheck method:
        public void SendLocationCheck(long locationId)
        {
            if (!Authenticated || session == null)
                return;

            if (ServerData.CheckedLocations.Contains(locationId))
                return;

            ServerData.CheckedLocations.Add(locationId);

            // Offload the AP library's socket logic to a background worker thread.
            // This guarantees zero frame drops on the Unity main thread when grabbing an item.
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    session?.Locations?.CompleteLocationChecksAsync(null, locationId);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"[AP] Error sending location check: {e}");
                }
            });

            Plugin.Log.LogInfo($"[AP] Location confirmed: {locationId}");
        }

        // 3. Replace your existing GetItemAtLocation method:
        public ScoutedItem GetItemAtLocation(long locationId)
        {
            if (session == null) return null;

            lock (cacheLock)
            {
                if (ScoutedLocationsCache.TryGetValue(locationId, out var cachedItem))
                {
                    return cachedItem;
                }
            }

            // We intentionally return null here now. 
            // We removed the dynamic session.Locations.ScoutLocationsAsync call because sending 
            // a network request during gameplay was the source of the remaining micro-stutter.
            return null;
        }

        // 4. Add this new method to pre-cache everything on connect:
        private void ScoutAllLocations()
        {
            if (session == null) return;

            var missing = session.Locations.AllMissingLocations;
            if (missing == null || missing.Count == 0) return;

            session.Locations.ScoutLocationsAsync(
                scoutResult =>
                {
                    if (scoutResult == null) return;
                    lock (cacheLock)
                    {
                        foreach (var kvp in scoutResult)
                        {
                            ScoutedLocationsCache[kvp.Key] = new ScoutedItem
                            {
                                ItemName = kvp.Value.ItemName,
                                PlayerName = session.Players.GetPlayerName(kvp.Value.Player),
                                IsOwnItem = kvp.Value.Player == session.ConnectionInfo.Slot
                            };
                        }
                    }
                    Plugin.Log.LogInfo($"[AP] Pre-scouted {scoutResult.Count} locations into cache.");

                    LaMulana2Archipelago.Patches.ShopDialogPatch.Reapply();
                },
                missing.ToArray());
        }

        public void ReportGoalOnce()
        {
            if (GoalReported) return;
            if (!Authenticated || session == null) return;

            GoalReported = true;

            try
            {
                // Protocol: StatusUpdate -> CLIENT_GOAL (30)
                session.Socket.SendPacketAsync(new StatusUpdatePacket { Status = (ArchipelagoClientState)30 });
                Plugin.Log.LogInfo("[AP] Goal reached (CLIENT_GOAL sent)");
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"[AP] Failed to send CLIENT_GOAL: {e}");
                // Allow retry if it failed to send.
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
            public bool IsOwnItem;
        }
        public long? GetLocationIdByName(string locationName)
        {
            // Archipelago.NET exposes this on the session's Locations helper
            return session?.Locations?.GetLocationIdFromName(Game, locationName);
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