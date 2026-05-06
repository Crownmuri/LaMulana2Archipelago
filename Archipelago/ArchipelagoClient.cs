using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using Archipelago.MultiClient.Net.Packets;
using Newtonsoft.Json.Linq;
using LaMulana2Archipelago.Managers;
using LaMulana2Archipelago.Utils;
using LaMulana2RandomizerShared;
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

        /// <summary>
        /// True while the mod is running a solo seed.lm2r locally with no
        /// Archipelago session. Standalone patches are enabled, but all
        /// networking (location checks, item queue, goal, deathlink) stays
        /// dormant since Authenticated remains false.
        /// </summary>
        public static bool OfflineMode;

        /// <summary>
        /// Offline-only preference: when true, the seed was produced by the
        /// AP-aware randomizer and its filler intercepts (variable Coin/Weight
        /// amounts per ChestWeight/FakeItem ID) should run. When false, the
        /// seed is treated as a vanilla L2Rando seed — filler intercepts are
        /// skipped and LM2's original filler behavior applies (ChestWeight
        /// always 1 weight, FakeItem plays the evil tune, etc.).
        /// Ignored while connected to AP (filler is always on in AP mode).
        /// </summary>
        public static bool OfflineApFillerEnabled = false;

        /// <summary>
        /// Offline-only preference: forces guardian-specific Ankh Jewels.
        /// Applied into slot_data at ActivateOffline time so
        /// <see cref="Patches.GuardianSpecificAnkhPatch"/> picks it up.
        /// Ignored while connected to AP (that uses the server's slot value).
        /// </summary>
        public static bool OfflineGuardianAnkhsEnabled = false;

        /// <summary>
        /// True when AP-style filler intercepts should replace the vanilla
        /// LM2 filler behavior. Always on for AP-connected play; in offline
        /// mode it follows <see cref="OfflineApFillerEnabled"/>.
        /// </summary>
        public static bool ApFillerActive => !OfflineMode || OfflineApFillerEnabled;

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

        /// <summary>
        /// Activate offline solo-seed mode: parse seed.lm2r into a slot_data-shaped
        /// dictionary and run the same standalone-mode activation the AP connect
        /// path runs — but without opening a network session.
        /// </summary>
        public bool ActivateOffline()
        {
            if (Authenticated || OfflineMode) return false;

            if (!SeedToSlotData.TryLoad(out var slotData, out string error))
            {
                Plugin.Log.LogError("[AP] Offline activation failed: " + error);
                return false;
            }

            // Offline toggles override whatever the seed baked in.
            slotData["guardian_specific_ankhs"] = OfflineGuardianAnkhsEnabled ? 1 : 0;

            ServerData.SetupSession(slotData, "offline");
            ApplyStandaloneFromSlotData(slotData);

            OfflineMode = true;
            Plugin.Log.LogInfo("[AP] Offline mode activated from seed.lm2r");
            return true;
        }

        /// <summary>
        /// Turn offline mode back off. Only safe from the title screen — the
        /// scene randomizer's cellData rewrites are one-way once gameplay
        /// starts, but the title UI is the only place the toggle is exposed.
        /// </summary>
        public bool DeactivateOffline()
        {
            if (!OfflineMode || Authenticated) return false;

            Patches.SetItemPatch.Enabled = false;
            Patches.IsHaveItemPatch.Enabled = false;
            Patches.GetItemNumPatch.Enabled = false;
            Patches.GameFlagResetsPatch.Enabled = false;
            Patches.EventItemGetActionPatch.Enabled = false;
            Patches.CostumeItemGetActionPatch.Enabled = false;
            Patches.ShopItemCallBackPatch.Enabled = false;
            Patches.ShopSetSoldOutPatch.Enabled = false;
            Patches.MenuSystemFlagQuePatch.Enabled = false;
            Patches.StatusResetPatch.Enabled = false;
            Patches.StatusChangeMainWeaponPatch.Enabled = false;
            Patches.HolyTabretPatch.Enabled = false;
            Patches.SeihaiGetOnHolyNumPatch.Enabled = false;
            Patches.SeihaiGetNowFieldPointPatch.Enabled = false;

            Patches.GuardianSpecificAnkhPatch.GuardianSpecificAnkhsEnabled = false;

            if (SceneRandomizer.Instance != null)
                UnityEngine.Object.Destroy(SceneRandomizer.Instance.gameObject);

            Patches.ItemPotPatch.Reset();
            Patches.VirtualFlagManager.Reset();
            CheckManager.Reset();
            ItemQueue.Clear();
            ServerData?.ClearSessionCache();
            if (ServerData != null)
            {
                ServerData.CheckedLocations.Clear();
                ServerData.Index = 0;
            }

            // Rebuild the default flag map from legacy seed.lm2r or defaults
            // so the mod returns to its pre-activation state.
            LocationFlagMap.InitializeFromSeed();

            OfflineMode = false;
            Plugin.Log.LogInfo("[AP] Offline mode deactivated");
            return true;
        }

        /// <summary>
        /// Shared activation path for both AP connect (slot_data from the server)
        /// and offline solo seeds (slot_data synthesized from seed.lm2r).
        /// Assumes ServerData.SetupSession has already been called.
        /// </summary>
        private static void ApplyStandaloneFromSlotData(Dictionary<string, object> slotData)
        {
            // Rebuild flag maps from slot_data (standalone mode) or seed.lm2r (legacy fallback).
            LocationFlagMap.InitializeFromSlotData(slotData);

            // Enable standalone patches if slot_data contains full placement data.
            bool standaloneMode = slotData.ContainsKey("item_placements");
            Patches.SetItemPatch.Enabled = standaloneMode;
            Patches.IsHaveItemPatch.Enabled = standaloneMode;
            Patches.GetItemNumPatch.Enabled = standaloneMode;
            Patches.GameFlagResetsPatch.Enabled = standaloneMode;
            Patches.EventItemGetActionPatch.Enabled = standaloneMode;
            Patches.CostumeItemGetActionPatch.Enabled = standaloneMode;
            Patches.ShopItemCallBackPatch.Enabled = standaloneMode;
            Patches.ShopSetSoldOutPatch.Enabled = standaloneMode;
            Patches.MenuSystemFlagQuePatch.Enabled = standaloneMode;
            Patches.StatusResetPatch.Enabled = standaloneMode;
            Patches.StatusChangeMainWeaponPatch.Enabled = standaloneMode;
            Patches.HolyTabretPatch.Enabled = standaloneMode;
            Patches.SeihaiGetOnHolyNumPatch.Enabled = standaloneMode;
            Patches.SeihaiGetNowFieldPointPatch.Enabled = standaloneMode;

            if (standaloneMode)
            {
                Patches.GameFlagResetsPatch.LoadFromSlotData(ServerData);

                bool autoScan = ServerData.GetSlotBool("auto_scan_tablets", false);
                Patches.HolyTabretPatch.AutoScanTablets = autoScan;
                Plugin.Log.LogInfo($"[AP] auto_scan_tablets = {autoScan}");

                SceneRandomizer.Create();
                SceneRandomizer.Instance.LoadFromSlotData(slotData, ServerData);
            }
            Plugin.Log.LogInfo($"[AP] Standalone mode = {standaloneMode}");

            bool guardianAnkhs = ServerData.GetSlotBool("guardian_specific_ankhs");
            Patches.GuardianSpecificAnkhPatch.GuardianSpecificAnkhsEnabled = guardianAnkhs;

            Plugin.Log.LogInfo("[AP] === Slot Settings ===");
            Plugin.Log.LogInfo($"[AP]   starting_area       = {ServerData.GetSlotInt("starting_area")}");
            Plugin.Log.LogInfo($"[AP]   starting_weapon     = {ServerData.GetSlotInt("starting_weapon")}");
            Plugin.Log.LogInfo($"[AP]   starting_money      = {ServerData.GetSlotInt("starting_money", 200)}");
            Plugin.Log.LogInfo($"[AP]   starting_weights    = {ServerData.GetSlotInt("starting_weights", 10)}");
            Plugin.Log.LogInfo($"[AP]   random_dissonance   = {ServerData.GetSlotBool("random_dissonance", true)}");
            Plugin.Log.LogInfo($"[AP]   required_guardians  = {ServerData.GetSlotInt("required_guardians", 5)}");
            Plugin.Log.LogInfo($"[AP]   required_skulls     = {ServerData.GetSlotInt("required_skulls", 6)}");
            Plugin.Log.LogInfo($"[AP]   echidna             = {ServerData.GetSlotInt("echidna", 4)}");
            Plugin.Log.LogInfo($"[AP]   auto_scan_tablets   = {ServerData.GetSlotBool("auto_scan_tablets")}");
            Plugin.Log.LogInfo($"[AP]   auto_place_skull    = {ServerData.GetSlotBool("auto_place_skull", true)}");
            Plugin.Log.LogInfo($"[AP]   remove_it_statue    = {ServerData.GetSlotBool("remove_it_statue", true)}");
            Plugin.Log.LogInfo($"[AP]   guardian_specific_ankhs = {guardianAnkhs}");
            Plugin.Log.LogInfo($"[AP]   death_link          = {ServerData.GetSlotBool("death_link")}");
            Plugin.Log.LogInfo($"[AP]   item_chest_color    = {ServerData.GetSlotInt("item_chest_color")}");
            Plugin.Log.LogInfo($"[AP]   filler_chest_color  = {ServerData.GetSlotInt("filler_chest_color", 4)}");
            Plugin.Log.LogInfo($"[AP]   ap_chest_color      = {ServerData.GetSlotInt("ap_chest_color", 1)}");
            Plugin.Log.LogInfo("[AP] === End Settings ===");

            // Initialize Potsanity (pot_flag_map from slot_data); offline seeds
            // set potsanity=0 so this is a no-op in that path.
            Patches.ItemPotPatch.Initialize();
        }

        public void Connect()
        {
            if (Authenticated || attemptingConnection || OfflineMode) return;

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

                ApplyStandaloneFromSlotData(success.SlotData);

                bool deathLinkEnabled = ServerData.GetSlotBool("death_link", false);

                DeathLinkHandler = new DeathLinkHandler(
                    session.CreateDeathLinkService(),
                    ServerData.SlotName,
                    deathLinkEnabled
                );

                Plugin.Log.LogInfo("[AP] DeathLink service initialized");

                Authenticated = true;
                GoalReported = false;

                // Populate CheckedLocations from the server's authoritative list.
                // Needed so VirtualFlagManager can correctly identify already-collected
                // AP placeholder items after a fresh session start (their sheet-31 flags
                // are in-memory only and don't survive a relaunch).
                var alreadyChecked = session.Locations.AllLocationsChecked;
                if (alreadyChecked != null)
                {
                    foreach (long loc in alreadyChecked)
                    {
                        if (!ServerData.CheckedLocations.Contains(loc))
                            ServerData.CheckedLocations.Add(loc);
                    }
                    Plugin.Log.LogInfo($"[AP] Restored {alreadyChecked.Count} checked locations from server.");
                }

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
            attemptingConnection = false;
            GoalReported = false;
            ItemQueue.Clear();

            // Ensure next Connect() re-requests slot_data. Without this, slotData
            // from the previous session is still non-null, so NeedSlotData=false,
            // and the new server returns empty SlotData → NRE in HandleConnectResult.
            ServerData?.ClearSessionCache();

            // Wipe slot-specific state so reconnecting as a different slot in the
            // same game session doesn't leak the previous slot's progress.
            // VirtualFlagManager holds in-memory sheet-31 flags (incl. shop sold-out
            // state for AP slots). CheckedLocations and Index are slot-keyed.
            // CheckManager.reportedLocations and ItemPotPatch state are session-scoped.
            Patches.VirtualFlagManager.Reset();
            Patches.ItemPotPatch.Reset();
            CheckManager.Reset();
            if (ServerData != null)
            {
                ServerData.CheckedLocations.Clear();
                ServerData.Index = 0;
            }
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

        /// <summary>
        /// Mirror a guardian kill to AP datastorage. Boss locations are
        /// event-only in the AP world (loc.address = None), so the
        /// SendLocationCheck call doesn't reach the server's checked_locations
        /// broadcast. PopTracker reads these slot-scoped keys via SetNotify
        /// to mark the boss as dead.
        ///
        /// Key format: lamulana2_kill_{LocationID enum name}_{team}_{slot}
        /// Value: 1 (idempotent set)
        /// </summary>
        public void RecordBossKill(LocationID guardian)
        {
            if (!Authenticated || session == null) return;

            try
            {
                var conn = session.ConnectionInfo;
                string key = $"lamulana2_kill_{guardian}_{conn.Team}_{conn.Slot}";

                // Send a raw Set packet rather than relying on DataStorage indexer
                // semantics. Equivalent to the Python ctx.send_msg pattern other
                // AP clients use; lets PopTracker pick up the change via its
                // SetNotify subscription on the same key.
                var packet = new SetPacket
                {
                    Key = key,
                    DefaultValue = JToken.FromObject(0),
                    WantReply = false,
                    Operations = new[]
                    {
                        new OperationSpecification
                        {
                            OperationType = OperationType.Replace,
                            Value = JToken.FromObject(1)
                        }
                    }
                };

                session.Socket.SendPacketAsync(packet);
                Plugin.Log.LogInfo($"[AP] Datastorage Set sent: {key} = 1");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[AP] Datastorage write failed for {guardian}: {ex.Message}");
            }
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

            // Scout ALL locations (not just missing) so that shop labels for
            // already-collected items are still available in the cache.
            var all = session.Locations.AllLocations;
            if (all == null || all.Count == 0) return;

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
                                ItemId = kvp.Value.ItemId,
                                ItemName = kvp.Value.ItemName,
                                PlayerName = session.Players.GetPlayerName(kvp.Value.Player),
                                IsOwnItem = kvp.Value.Player == session.ConnectionInfo.Slot
                            };
                        }
                    }
                    Plugin.Log.LogInfo($"[AP] Pre-scouted {scoutResult.Count} locations into cache.");

                    LaMulana2Archipelago.Patches.ShopDialogPatch.Reapply();
                },
                all.ToArray());
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
            public long ItemId;
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
            Patches.ItemPotPatch.Reset();
        }
    }
}