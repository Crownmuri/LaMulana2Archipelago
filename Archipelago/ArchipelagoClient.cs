using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Exceptions;
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
using System.Reflection;
using System.Threading;

namespace LaMulana2Archipelago.Archipelago
{
    public class ArchipelagoClient
    {
        public const string APVersion = "1.0.4";
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
        /// Player opted in to the UAT autotracking server (title-screen
        /// toggle). Off by default: the server is a listening socket, so it
        /// only opens when asked for.
        /// </summary>
        public static bool UatEnabled = false;

        /// <summary>slot_data from the last offline activation, for PublishUat.</summary>
        public static Dictionary<string, object> LastOfflineSlotData;

        /// <summary>
        /// Bring the UAT server up and hand it the current seed. Safe to call
        /// whether offline mode came first or the toggle did. ResetRun before
        /// the seed: a different save must publish a shorter items array so the
        /// tracker rebuilds rather than stacking onto the previous run.
        /// </summary>
        public static bool PublishUat()
        {
            if (!UAT.UATServer.Start()) return false;
            UAT.UATServer.ResetRun();
            if (LastOfflineSlotData != null) UAT.UATServer.SetSlotData(LastOfflineSlotData);
            return true;
        }

        /// <summary>
        /// True when AP-style filler intercepts should replace the vanilla
        /// LM2 filler behavior. Always on for AP-connected play; in offline
        /// mode it follows <see cref="OfflineApFillerEnabled"/>.
        /// </summary>
        public static bool ApFillerActive => !OfflineMode || OfflineApFillerEnabled;

        // Written by the background connect worker, read on the Unity main
        // thread (Connect's guard, PumpConnectResult). volatile so the main
        // thread cannot keep reading a stale cached value.
        private volatile bool attemptingConnection;

        // Result handed back by the connect worker, consumed on the main
        // thread by PumpConnectResult. See TryConnect for why.
        private volatile LoginResult pendingLoginResult;

        // =============================
        // Connection progress (for title-screen UI)
        // =============================
        // Set from the background connect worker, read from the Unity main
        // thread in Plugin.OnGUI. A volatile enum + immutable string are safe
        // to share without a lock (reference/word-sized reads are atomic).

        public enum ConnectionPhase
        {
            Idle,
            Connecting,       // opening the socket
            WaitingForServer, // socket open, waiting for RoomInfo
            LoadingData,      // DataPackage streaming/parsing
            Authenticating,   // Connect sent, waiting for login result
            Scouting,         // logged in, pre-scouting locations
            Connected,
            Failed
        }

        private static volatile ConnectionPhase _phase = ConnectionPhase.Idle;
        public static ConnectionPhase Phase => _phase;

        // Short extra detail shown after a failure (first server error line).
        public static string PhaseDetail = "";

        private static void SetPhase(ConnectionPhase phase, string detail = "")
        {
            _phase = phase;
            PhaseDetail = detail ?? "";
        }

        /// <summary>Human-readable label for the current connection phase.</summary>
        public static string PhaseText()
        {
            switch (_phase)
            {
                case ConnectionPhase.Connecting:       return "Connecting…";
                case ConnectionPhase.WaitingForServer: return "Waiting for server…";
                case ConnectionPhase.LoadingData:      return "Loading data package…";
                case ConnectionPhase.Authenticating:   return "Authenticating…";
                case ConnectionPhase.Scouting:         return "Scouting locations…";
                case ConnectionPhase.Connected:        return "Connected";
                case ConnectionPhase.Failed:
                    return string.IsNullOrEmpty(PhaseDetail) ? "Failed" : "Failed: " + PhaseDetail;
                default:                               return "";
            }
        }

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

        // =============================
        // Item queue
        // =============================
        // Filled from the MultiClient socket thread (OnItemReceived) and
        // drained on the Unity main thread (Plugin.Update, ShadowSaveManager).
        // Queue<T> is not thread-safe and net35 has no ConcurrentQueue, so the
        // instance is private and every operation goes through the accessors
        // below, all of which take _itemQueueLock. Left public and unguarded,
        // an item arriving mid-drain could be lost outright -- ShadowSaveManager
        // snapshots with ToArray() and then Clear()s, and anything the socket
        // enqueued between those two calls vanished.
        private static readonly Queue<QueuedApItem> _itemQueue = new();
        private static readonly object _itemQueueLock = new object();

        public static int ItemQueueCount
        {
            get { lock (_itemQueueLock) { return _itemQueue.Count; } }
        }

        public static void EnqueueItem(QueuedApItem item)
        {
            lock (_itemQueueLock) { _itemQueue.Enqueue(item); }
        }

        public static bool TryPeekItem(out QueuedApItem item)
        {
            lock (_itemQueueLock)
            {
                if (_itemQueue.Count == 0) { item = default(QueuedApItem); return false; }
                item = _itemQueue.Peek();
                return true;
            }
        }

        public static bool TryDequeueItem(out QueuedApItem item)
        {
            lock (_itemQueueLock)
            {
                if (_itemQueue.Count == 0) { item = default(QueuedApItem); return false; }
                item = _itemQueue.Dequeue();
                return true;
            }
        }

        public static void ClearItemQueue()
        {
            lock (_itemQueueLock) { _itemQueue.Clear(); }
        }

        /// <summary>
        /// Rewrite the queue under the lock. <paramref name="transform"/> is
        /// handed the current contents in order and returns the replacement,
        /// so a read-modify-write cannot lose an item the socket thread
        /// enqueues partway through.
        /// </summary>
        public static void MutateItemQueue(
            Func<List<QueuedApItem>, List<QueuedApItem>> transform)
        {
            if (transform == null) return;
            lock (_itemQueueLock)
            {
                var next = transform(new List<QueuedApItem>(_itemQueue));
                if (next == null) return;
                _itemQueue.Clear();
                foreach (var item in next) _itemQueue.Enqueue(item);
            }
        }

        private static bool GoalReported;

        // Set when the player has reached a goal scene but the CLIENT_GOAL packet
        // hasn't landed yet (e.g. socket silently closed). Persists across a
        // reconnect so HandleConnectResult can retry the send.
        public static bool GoalPending;

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
        // Game Difficulty (Normal / Hard / Hardest)
        // =============================
        // Initialized from slot_data on connect / offline activation. Drives
        // the title-screen Difficulty cycle in Plugin.OnGUI and is re-applied
        // by GameDifficultyPatch on every save-load / new-game hook. ApplyTo
        // shifts G_Difficulty by (toggleState - saveState) * StepSize and
        // marks hard1/hard2, preserving natural progression and Voluspa.

        public static GameDifficultyHandler GameDifficultyHandler { get; private set; }


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


            // Remembered so the "Enable UAT" button can publish the seed even
            // when it is switched on after offline mode is already live.
            LastOfflineSlotData = slotData;

            // Stand in for the server's scout replies. Must run before the
            // standalone patches below, which read the cache as they apply.
            BuildOfflineScoutCache(slotData);

            ServerData.SetupSession(slotData, "offline");
            ApplyStandaloneFromSlotData(slotData);

            // Slot value pins the per-seed authority (slotState); the
            // title-screen cycle can still override the live toggle.
            GameDifficultyHandler = new GameDifficultyHandler(
                ServerData.GetSlotInt("game_difficulty", 0));

            OfflineMode = true;

            // Any shadow state cached against the pre-activation "noseed" key
            // must be discarded so the next file load resolves against
            // ..._offline_* files.
            ShadowSaveManager.InvalidateCaches();

            // L2ShopDataBase constructed before offline activation, so its
            // ctor postfix ran with no slot_data and skipped Apply. Trigger
            // the same Reapply() the online scout-cache callback uses, now
            // that OfflineMode is true and seed.lm2ap labels are loaded.
            LaMulana2Archipelago.Patches.ShopDialogPatch.Reapply();

            // Offline there is no AP room for PopTracker to watch, so a UAT
            // server can host for it -- but only when the player has asked for
            // one. Starting it unconditionally makes PopTracker latch on the
            // moment the pack loads, which is not wanted by default.
            if (UatEnabled) PublishUat();

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

            UAT.UATServer.Stop();

            TearDownStandaloneState();

            Patches.ItemPotPatch.Reset();
            Managers.GlossaryManager.Reset();
            Managers.CostumeManager.Reset();   // stop X-blocking costumes once AP is no longer active
            Patches.VirtualFlagManager.Reset();
            CheckManager.Reset();
            ClearItemQueue();
            ServerData?.ClearSessionCache();
            if (ServerData != null)
            {
                ServerData.CheckedLocations.Clear();
                ServerData.Index = 0;
            }

            GameDifficultyHandler = null;

            OfflineMode = false;
            ShadowSaveManager.InvalidateCaches();

            Plugin.Log.LogInfo("[AP] Offline mode deactivated");
            return true;
        }

        /// <summary>
        /// Undo the world-altering state that <see cref="ApplyStandaloneFromSlotData"/>
        /// installs, returning the mod to its pre-connection / pre-activation state:
        /// standalone patches off, the SceneRandomizer torn down, the default
        /// (non-AP) flag map rebuilt, and the previous session's scout cache dropped.
        ///
        /// Shared by <see cref="DeactivateOffline"/> and <see cref="DisconnectAndRevert"/>
        /// so the offline and online teardown paths can't drift apart.
        ///
        /// Only safe from the title screen — the SceneRandomizer's cellData
        /// rewrites are one-way once a scene has been played. Both callers are
        /// reachable only from the title UI, which is where those constraints hold.
        /// </summary>
        private static void TearDownStandaloneState()
        {
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

            // Revert the rewrites that outlive a scene: the shop/talk moji script
            // databases (L2System field initialisers, so process-lifetime) and the
            // chest item prefabs. Without this the next session still sees the last
            // seed's shop stock, NPC gifts and chest contents.
            Managers.WorldDataRestore.RestoreAll();
            Patches.ShopDialogPatch.Reset();

            if (SceneRandomizer.Instance != null)
                UnityEngine.Object.Destroy(SceneRandomizer.Instance.gameObject);

            // Persistent inventory is a per-seed setting; make sure a seed that
            // had it on doesn't leak the replay behavior into the next one.
            Managers.PersistentInventoryManager.Enabled = false;
            Managers.PersistentInventoryManager.Reset();

            // Rebuild the default flag map from legacy seed.lm2r or defaults
            // so the mod returns to its pre-activation state.
            LocationFlagMap.InitializeFromSeed();

            // Drop the previous session's pre-scouted placements so stale labels
            // and chest colors can't leak into a connection to a different server.
            lock (cacheLock)
                ScoutedLocationsCache.Clear();
            ScoutCacheReady = false;
        }

        /// <summary>
        /// Manual disconnect from the title screen that also reverts every
        /// world-altering change the connection installed, so the player can
        /// connect to a different Archipelago server without restarting the game.
        ///
        /// This is the full teardown; the plain <see cref="Disconnect"/> is the
        /// lightweight one used for unexpected socket drops and goal-send retries,
        /// where the standalone patches must stay live (mid-game) or a deferred
        /// goal must survive to be re-sent to the same server.
        /// </summary>
        public void DisconnectAndRevert()
        {
            Disconnect();

            TearDownStandaloneState();

            // A manual disconnect is a clean slate: any deferred goal belonged to
            // the server we just left and must not be re-sent to the next one.
            GoalPending = false;

            Plugin.Log.LogInfo("[AP] Disconnected and reverted to pre-connection state");
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

            // Persistent Inventory. Off by default: without it, only foreign
            // items survive a death/load, because own-world finds live in the
            // save state that memLoad rewinds.
            bool persistentInventory = ServerData.GetSlotBool("persistent_inventory", false);
            Managers.PersistentInventoryManager.Enabled = persistentInventory;
            Managers.PersistentInventoryManager.Reset();

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
            Plugin.Log.LogInfo($"[AP]   greedy_charon       = {ServerData.GetSlotBool("greedy_charon", true)}");
            Plugin.Log.LogInfo($"[AP]   remove_it_statue    = {ServerData.GetSlotBool("remove_it_statue", true)}");
            Plugin.Log.LogInfo($"[AP]   guardian_specific_ankhs = {guardianAnkhs}");
            Plugin.Log.LogInfo($"[AP]   persistent_inventory = {persistentInventory}");
            Plugin.Log.LogInfo($"[AP]   death_link          = {ServerData.GetSlotBool("death_link")}");
            Plugin.Log.LogInfo($"[AP]   item_chest_color    = {ServerData.GetSlotInt("item_chest_color")}");
            Plugin.Log.LogInfo($"[AP]   filler_chest_color  = {ServerData.GetSlotInt("filler_chest_color", 4)}");
            Plugin.Log.LogInfo($"[AP]   ap_chest_color      = {ServerData.GetSlotInt("ap_chest_color", 1)}");
            Plugin.Log.LogInfo("[AP] === End Settings ===");

            // Initialize Potsanity (pot_flag_map from slot_data); offline seeds
            // set potsanity=0 so this is a no-op in that path.
            Patches.ItemPotPatch.Initialize();

            // Initialize Glossanity (glossary_flag_map from slot_data); registers
            // (sheet 20, bookFlagNo) → LocationID. glossanity=0 → no-op.
            Managers.GlossaryManager.Initialize();
        }

        public void Connect()
        {
            if (Authenticated || attemptingConnection || OfflineMode) return;

            SetPhase(ConnectionPhase.Connecting);

            try
            {
                session = ArchipelagoSessionFactory.CreateSession(ServerData.NormalizedUri);
                SetupSession();
            }
            catch (Exception e)
            {
                // A malformed address reaches UriFormatException here. Falling
                // through to TryConnect would queue a worker against a null
                // session, and ManualConnectAndLogin dereferences it on its
                // first line -- the NRE escapes onto the ThreadPool, so
                // attemptingConnection is never cleared and the guard above
                // turns every later Connect into a silent no-op.
                Plugin.Log.LogError(e);
                SetPhase(ConnectionPhase.Failed, e.Message);
                session = null;
                return;
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
            attemptingConnection = true;
            pendingLoginResult = null;

            try
            {
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    // Only the socket handshake belongs on this thread.
                    // HandleConnectResult builds a GameObject, loads prefabs
                    // and rewrites the shop/talk script databases -- Unity API
                    // that is only legal on the main thread -- so the result is
                    // parked here and picked up by PumpConnectResult from
                    // Plugin.Update instead.
                    LoginResult result;
                    try
                    {
                        result = ManualConnectAndLogin(
                            ServerData.SlotName,
                            ServerData.Password,
                            ServerData.NeedSlotData);
                    }
                    catch (Exception e)
                    {
                        // An unhandled exception on a ThreadPool thread takes
                        // the process down and would strand
                        // attemptingConnection at true either way.
                        Plugin.Log.LogError(e);
                        result = new LoginFailure(e.ToString());
                    }
                    pendingLoginResult = result;
                });
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(e);
                pendingLoginResult = new LoginFailure(e.ToString());
            }
        }

        // Set from the scout callback (socket thread), consumed by
        // PumpMainThreadWork.
        private static volatile bool _scoutReapplyPending;

        /// <summary>
        /// Main-thread half of everything the networking threads hand back.
        /// Called every frame from <see cref="Plugin.Update"/>; does nothing
        /// until a background worker parks some work.
        /// </summary>
        public void PumpMainThreadWork()
        {
            if (_scoutReapplyPending)
            {
                _scoutReapplyPending = false;
                try
                {
                    Patches.ShopDialogPatch.Reapply();
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError("[AP] Deferred shop reapply failed: " + e);
                }
            }

            LoginResult result = pendingLoginResult;
            if (result == null) return;
            pendingLoginResult = null;

            if (!attemptingConnection)
            {
                // Disconnect ran while the handshake was in flight -- the
                // result belongs to a session we have already torn down.
                Plugin.Log.LogInfo("[AP] Discarding login result from a cancelled connect.");
                return;
            }

            try
            {
                HandleConnectResult(result);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("[AP] Connect handling failed: " + e);
                Authenticated = false;
                Disconnect();
                SetPhase(ConnectionPhase.Failed, e.Message);
            }
            finally
            {
                // Cleared on every path. HandleConnectResult's own assignment
                // is not enough: if anything above throws, the guard in
                // Connect() would block every retry until the game restarts.
                attemptingConnection = false;
            }
        }

        // Seconds to wait for RoomInfo after the socket opens. RoomInfo is the
        // first packet a live server sends, so this only needs to cover network
        // latency + the socket's own ws/wss handshake fallback.
        private const double RoomInfoTimeoutSeconds = 15.0;

        // Seconds to wait for the Connected/ConnectionRefused packet after we
        // send Connect. This is the wait the stock library caps at 4s — far too
        // short here, because the server streams the full DataPackage for every
        // checksum-mismatched game in the room first, and parsing that large
        // JSON on Unity 2017's single websocket receive thread routinely takes
        // longer than 4s (worse when our own frequently-rebuilt apworld's
        // checksum never matches the on-disk cache). The parse blocks the same
        // thread that would surface Connected, so the stock wait false-negatives
        // with "Connection timed out" and tears down an otherwise-good socket.
        private const double LoginResultTimeoutSeconds = 60.0;

        /// <summary>
        /// Drop-in replacement for the bundled MultiClient.Net
        /// <c>ArchipelagoSession.TryConnectAndLogin</c>, which hardcodes a 4-second
        /// wait for the Connected packet. We replicate the same handshake on the
        /// same single socket (one lobby join, no reconnect spam) but wait with a
        /// generous, bounded timeout so the DataPackage has time to parse.
        ///
        /// The session's own helpers (DataPackageCache, ConnectionInfoHelper,
        /// ReceivedItemsHelper, …) are already subscribed to Socket.PacketReceived,
        /// so driving the packets through the socket populates them exactly as the
        /// stock path would. We only add our own observer to time the two waits and
        /// to build the LoginResult via the public LoginResult.FromPacket.
        /// </summary>
        private LoginResult ManualConnectAndLogin(string name, string password, bool requestSlotData)
        {
            // The library keys item/location name resolution and the outgoing
            // Connect packet off ConnectionInfo. SetConnectionParameters is
            // internal, so mirror what TryConnectAndLogin does via reflection.
            // (Game/Slot/Team are re-derived from the Connected packet anyway.)
            string uuid = Guid.NewGuid().ToString();
            var connInfo = session.ConnectionInfo;
            var setParams = connInfo.GetType().GetMethod(
                "SetConnectionParameters",
                BindingFlags.Instance | BindingFlags.NonPublic);
            setParams?.Invoke(connInfo, new object[]
            {
                Game, new string[0], ItemsHandlingFlags.RemoteItems, uuid
            });

            RoomInfoPacket roomInfo = null;
            LoginResult loginResult = null;

            void Observer(ArchipelagoPacketBase packet)
            {
                if (packet is RoomInfoPacket ri)
                    roomInfo = ri;
                else if (packet is DataPackagePacket)
                    SetPhase(ConnectionPhase.LoadingData);
                else if (packet is ConnectedPacket || packet is ConnectionRefusedPacket)
                    loginResult = LoginResult.FromPacket(packet);
            }

            session.Socket.PacketReceived += Observer;
            try
            {
                SetPhase(ConnectionPhase.Connecting);
                session.Socket.Connect();

                SetPhase(ConnectionPhase.WaitingForServer);
                DateTime start = DateTime.UtcNow;
                while (roomInfo == null)
                {
                    if (DateTime.UtcNow - start > TimeSpan.FromSeconds(RoomInfoTimeoutSeconds))
                    {
                        session.Socket.Disconnect();
                        return new LoginFailure("Connection timed out waiting for room info.");
                    }
                    Thread.Sleep(25);
                }

                session.Socket.SendPacket(new ConnectPacket
                {
                    Game = Game,
                    Name = name,
                    Password = password,
                    Uuid = uuid,
                    Tags = new string[0],
                    Version = new NetworkVersion(new Version(APVersion)),
                    ItemsHandling = ItemsHandlingFlags.RemoteItems,
                    RequestSlotData = requestSlotData
                });

                // Only move to "Authenticating" if the DataPackage step hasn't
                // already taken over — that stream can arrive after Connect and
                // is the slow part worth surfacing.
                if (_phase != ConnectionPhase.LoadingData)
                    SetPhase(ConnectionPhase.Authenticating);
                start = DateTime.UtcNow;
                while (loginResult == null)
                {
                    if (DateTime.UtcNow - start > TimeSpan.FromSeconds(LoginResultTimeoutSeconds))
                    {
                        session.Socket.Disconnect();
                        return new LoginFailure("Connection timed out waiting for login result.");
                    }
                    Thread.Sleep(25);
                }

                // Give the session's own PacketReceived subscribers a moment to
                // finish applying the Connected packet (ConnectionInfo slot/team,
                // ReceivedItems) before HandleConnectResult reads them.
                Thread.Sleep(50);
                return loginResult;
            }
            catch (ArchipelagoSocketClosedException)
            {
                return new LoginFailure("Socket closed unexpectedly.");
            }
            finally
            {
                session.Socket.PacketReceived -= Observer;
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

                GameDifficultyHandler = new GameDifficultyHandler(
                    ServerData.GetSlotInt("game_difficulty", 0));
                Plugin.Log.LogInfo($"[AP] Game difficulty initial = {GameDifficultyHandler.State} (offset={GameDifficultyHandler.Level})");

                Authenticated = true;
                GoalReported = false;

                // Discard any shadow state cached before RoomSeed was known
                // (e.g. a file load that happened pre-connect resolved its
                // SeedKey to "noseed"). Subsequent OnFileLoad calls will
                // reload from the correct ..._<seed>_* files.
                ShadowSaveManager.InvalidateCaches();

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

                SetPhase(ConnectionPhase.Scouting);
                Patches.ShopDialogPatch.Reapply();
                ScoutAllLocations();

                SetPhase(ConnectionPhase.Connected);
                outText = $"Successfully connected to {ServerData.NormalizedUri} as {ServerData.SlotName}!";
                ArchipelagoConsole.LogMessage(outText);

                // If a previous CLIENT_GOAL send failed (socket dropped at credits),
                // retry now that we're authenticated again.
                if (GoalPending && !GoalReported)
                {
                    Plugin.Log.LogInfo("[AP] Retrying deferred CLIENT_GOAL after reconnect.");
                    ReportGoalOnce();
                }
            }
            else
            {
                var failure = (LoginFailure)result;
                outText = $"Failed to connect to {ServerData.NormalizedUri} as {ServerData.SlotName}.";
                outText = failure.Errors.Aggregate(outText, (current, error) => current + $"\n    {error}");

                Plugin.Log.LogError(outText);

                Authenticated = false;
                Disconnect();

                // Set AFTER Disconnect (which resets the phase to Idle) so the
                // failure reason stays visible on the title screen.
                SetPhase(ConnectionPhase.Failed,
                    failure.Errors != null ? failure.Errors.FirstOrDefault() : null);
            }
            attemptingConnection = false;
        }

        /// <summary>
        /// something went wrong, or we need to properly disconnect from the server. cleanup and re null our session
        /// </summary>
        public void Disconnect()
        {
            Plugin.Log.LogDebug("disconnecting from server...");
            SetPhase(ConnectionPhase.Idle);
            session?.Socket.Disconnect();
            session = null;
            Authenticated = false;
            attemptingConnection = false;
            GoalReported = false;
            ClearItemQueue();

            // The next Connect() re-runs ScoutAllLocations; until its async
            // callback lands, scout answers belong to the previous session.
            ScoutCacheReady = false;

            GameDifficultyHandler = null;

            // The DeathLink service is bound to the session we just dropped; a
            // stale handler would keep Update()-ing against a dead socket. The
            // next Connect() builds a fresh one from the new slot_data.
            DeathLinkHandler = null;

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
            Managers.GlossaryManager.Reset();
            Managers.CostumeManager.Reset();   // stop X-blocking costumes after disconnect
            CheckManager.Reset();
            if (ServerData != null)
            {
                ServerData.CheckedLocations.Clear();
                ServerData.Index = 0;
            }

            // RoomSeed is cleared above; cached shadow state is now keyed
            // against a stale seed and must be dropped.
            ShadowSaveManager.InvalidateCaches();
        }

        // =============================
        // Location checks
        // =============================
        // Scout results, pre-cached once on connect (see ScoutAllLocations). Read by
        // GetItemAtLocation so gameplay never blocks on a network scout.
        // AP id windows, mirroring worlds/lamulana2/ids.py. Foreign players'
        // items are represented by placeholders in [410000, 420000); our own
        // items are BASE_ITEM_ID + game id.
        private const int ApItemPlaceholderMin = 410000;
        private const int ApItemPlaceholderMax = 420000;
        private const long OfflineBaseApItemId = 420000;
        private const long OfflineBaseApLocationId = 430000;

        private static readonly object cacheLock = new object();
        public static Dictionary<long, ScoutedItem> ScoutedLocationsCache = new Dictionary<long, ScoutedItem>();

        /// <summary>
        /// True once ScoutAllLocations' async callback has filled the cache.
        /// Until then GetItemAtLocation returns null for everything, which is
        /// indistinguishable from "no scout data exists" (offline). Callers that
        /// would silently draw the wrong conclusion from a null scout — e.g.
        /// PersistentInventoryManager, which cannot identify an own glossary ROM
        /// without one — must wait on this rather than treat null as an answer.
        /// </summary>
        // volatile: set from the scout callback on the socket thread, polled on
        // the main thread by PersistentInventoryManager and friends.
        private static volatile bool _scoutCacheReady;
        public static bool ScoutCacheReady
        {
            get { return _scoutCacheReady; }
            private set { _scoutCacheReady = value; }
        }

        public void SendLocationCheck(long locationId)
        {
            // Offline autotracking. Deliberately ABOVE the guard below, which
            // always trips offline (no session), and in this funnel rather than
            // at the call sites: CheckManager reports through two separate
            // paths -- ReportLocation and the silent shop auto-collect in
            // NotifyApLocationId -- and hooking only the first meant a shop
            // slot never reached the tracker, so it stayed an unchecked blank
            // instead of turning into its Weights/ammo icon. UATServer dedupes,
            // so a location offered twice is harmless.
            UAT.UATServer.AddCheckedLocation(locationId);

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

        /// <summary>
        /// Mirror the natural-dissonance count (flag [2,3]) to AP datastorage so
        /// PopTracker can drive the Beherit consumable counter when
        /// random_dissonance is OFF. Driven by DissonanceTracker, which guards
        /// the random_dissonance==true mode (those grants come through onItem).
        ///
        /// Key format: lamulana2_dissonance_{team}_{slot}
        /// Value: current cumulative count from flag[2,3].
        /// </summary>
        public void RecordDissonanceCount(int count)
        {
            if (!Authenticated || session == null) return;

            try
            {
                var conn = session.ConnectionInfo;
                string key = $"lamulana2_dissonance_{conn.Team}_{conn.Slot}";

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
                            Value = JToken.FromObject(count)
                        }
                    }
                };

                session.Socket.SendPacketAsync(packet);
                Plugin.Log.LogInfo($"[AP] Datastorage Set sent: {key} = {count}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[AP] Datastorage write failed for dissonance count: {ex.Message}");
            }
        }

        // Returns the pre-scouted item at a location, or null if it wasn't cached on
        // connect. Cache-only by design — see the note inside about the removed live scout.
        public ScoutedItem GetItemAtLocation(long locationId)
        {
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

        /// <summary>
        /// Offline equivalent of ScoutAllLocations: fills the same cache from the
        /// seed instead of from the server.
        ///
        /// The seed already carries everything a scout reply would: which item
        /// sits at each location (item_placements / shop_placements) and its
        /// display name (location_labels, written by seed.py). Filling the cache
        /// here means every consumer works offline unchanged, rather than each
        /// patch needing its own seed-reading fallback.
        ///
        /// Ownership: AP placeholders for other players' items live in
        /// [410000, 420000); anything below that is one of ours, and its AP item
        /// id is BASE_ITEM_ID + game id.
        /// </summary>
        public static void BuildOfflineScoutCache(Dictionary<string, object> slotData)
        {
            if (slotData == null) return;

            // Read straight from slotData, not ServerData: this runs before
            // SetupSession so the session dict is not populated yet. Offline the
            // labels arrive as Dictionary<int,string> from SeedToSlotData; the
            // online slot_data shape is a JObject with string keys.
            var labels = new Dictionary<int, string>();
            object labelObj;
            if (slotData.TryGetValue("location_labels", out labelObj))
            {
                var typed = labelObj as Dictionary<int, string>;
                if (typed != null)
                {
                    foreach (var kvp in typed) labels[kvp.Key] = kvp.Value;
                }
                else
                {
                    var asJson = labelObj as JObject;
                    if (asJson != null)
                    {
                        foreach (var prop in asJson)
                        {
                            int locId;
                            if (int.TryParse(prop.Key, out locId) && prop.Value != null)
                                labels[locId] = prop.Value.ToString();
                        }
                    }
                }
            }

            // Our OWN glossary ROMs and pot filler are written into the
            // placements as per-location AP placeholders (410000+n) so the
            // location's sheet-31 machinery fires the check -- see
            // randomizer.get_own_placeholder_items. Taken at face value they
            // read as another player's item, which offline is never true.
            var ownBehindPlaceholder = SeedToSlotData.GetOwnPlaceholderItems(slotData);

            int cached = 0;
            lock (cacheLock)
            {
                ScoutedLocationsCache.Clear();

                foreach (string key in new[] { "item_placements", "shop_placements" })
                {
                    var placements = slotData.ContainsKey(key) ? slotData[key] as JArray : null;
                    if (placements == null) continue;

                    foreach (var entry in placements)
                    {
                        var locTok = entry["location"];
                        var itemTok = entry["item"];
                        if (locTok == null || itemTok == null) continue;

                        int gameLocation = (int)locTok;
                        int rawItem = (int)itemTok;
                        bool foreign = rawItem >= ApItemPlaceholderMin
                                       && rawItem < ApItemPlaceholderMax;

                        // Resolve a placeholder that is really one of ours back
                        // to the item it stands for, so ownership-sensitive
                        // consumers (IsOwnGlossaryRom, the dialog's
                        // "Sent ... to ..." framing, UAT's item feed) see the
                        // truth the scout reply would have carried online.
                        int ownItem;
                        if (foreign && ownBehindPlaceholder.TryGetValue(gameLocation, out ownItem))
                        {
                            rawItem = ownItem;
                            foreign = false;
                        }

                        string name;
                        if (!labels.TryGetValue(gameLocation, out name))
                            name = foreign ? "AP Item" : null;

                        ScoutedLocationsCache[OfflineBaseApLocationId + gameLocation] = new ScoutedItem
                        {
                            ItemId = foreign ? rawItem : OfflineBaseApItemId + rawItem,
                            ItemName = name,
                            PlayerName = "Player",
                            IsOwnItem = !foreign,
                            // Offline there is no AP classification; nothing that
                            // matters offline reads Flags.
                            Flags = default(ItemFlags),
                        };
                        cached++;
                    }
                }
            }

            Plugin.Log.LogInfo($"[AP] Offline scout cache built from seed: {cached} locations");
        }

        // Pre-caches every location's scout result on connect so shop/chest labels are
        // available instantly during gameplay without per-frame network requests.
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
                                IsOwnItem = kvp.Value.Player == session.ConnectionInfo.Slot,
                                Flags = kvp.Value.Flags
                            };
                        }
                    }
                    ScoutCacheReady = true;
                    Plugin.Log.LogInfo($"[AP] Pre-scouted {scoutResult.Count} locations into cache.");

                    // Reapply rewrites the shared L2ShopDataBase cellData that
                    // the main thread reads while a shop is open. Hand it over
                    // rather than racing it from the socket callback.
                    _scoutReapplyPending = true;
                },
                all.ToArray());
        }

        public void ReportGoalOnce()
        {
            if (GoalReported) return;

            // Offline there is no AP session and never will be for this run, so
            // there is no goal to owe anyone. Bail BEFORE recording intent:
            // GoalPending would otherwise survive DeactivateOffline and make the
            // next Connect() fire a CLIENT_GOAL at an unrelated server on login
            // (HandleConnectResult's deferred-goal retry).
            if (OfflineMode) return;

            // Record intent up-front so any retry path (reconnect, Ending2
            // fallback) knows we still owe the server a CLIENT_GOAL.
            GoalPending = true;

            if (!Authenticated || session == null) return;

            GoalReported = true;

            try
            {
                // Protocol: StatusUpdate -> CLIENT_GOAL (30)
                session.Socket.SendPacketAsync(new StatusUpdatePacket { Status = (ArchipelagoClientState)30 });
                Plugin.Log.LogInfo("[AP] Goal reached (CLIENT_GOAL sent)");
                GoalPending = false;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"[AP] Failed to send CLIENT_GOAL: {e}");
                // Send failed (commonly because the socket silently closed).
                // Tear the session down so the next Connect() rebuilds it;
                // GoalPending stays true so HandleConnectResult retries the
                // CLIENT_GOAL once we're reconnected.
                GoalReported = false;
                Disconnect();
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

            /// <summary>AP item classification flags (Advancement / NeverExclude / Trap).</summary>
            public ItemFlags Flags;

            /// <summary>
            /// True when the item carries the AP progression (Advancement) flag —
            /// i.e. something the logic considers important. Drives the "up arrow"
            /// progressive icon.
            /// </summary>
            public bool IsProgression => (Flags & ItemFlags.Advancement) != 0;

            /// <summary>True when the item carries the AP Trap flag.</summary>
            public bool IsTrap => (Flags & ItemFlags.Trap) != 0;

            /// <summary>
            /// Visual icon class for this item, with progression outranking trap so
            /// the precedence matches <see cref="ClassificationColor"/>.
            /// </summary>
            public ApIconClass IconClass =>
                IsProgression ? ApIconClass.Progression
                : IsTrap ? ApIconClass.Trap
                : ApIconClass.Plain;

            /// <summary>
            /// TextMeshPro RRGGBBAA colour for this item's name in the "Sent … to …"
            /// acquisition dialog, chosen from the AP classification flags:
            ///   progression + useful → F8E426, progression → AD8EE3,
            ///   useful → 6C74C5, trap → F98072, filler → 36D7D9.
            /// </summary>
            public string ClassificationColorHex => ClassificationColor(Flags);
        }

        /// <summary>
        /// Maps AP item classification flags to the "Sent … to …" dialog colour
        /// (TextMeshPro RRGGBBAA hex, no leading '#').
        /// </summary>
        public static string ClassificationColor(ItemFlags flags)
        {
            bool advancement = (flags & ItemFlags.Advancement) != 0;
            bool useful = (flags & ItemFlags.NeverExclude) != 0;
            bool trap = (flags & ItemFlags.Trap) != 0;

            if (advancement && useful) return "F8E426FF"; // progression + useful
            if (advancement) return "AD8EE3FF";            // progression
            if (trap) return "F98072FF";                   // trap
            if (useful) return "6C74C5FF";                 // useful
            return "36D7D9FF";                             // filler
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

                EnqueueItem(new QueuedApItem(itemIndex, item.ItemId, item.ItemName, senderName));
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
            ClearItemQueue();
            GoalReported = false;
            GoalPending = false;
            Patches.ItemPotPatch.Reset();
            Managers.GlossaryManager.Reset();
        }
    }
}