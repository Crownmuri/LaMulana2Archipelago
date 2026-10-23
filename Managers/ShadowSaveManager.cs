using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using Newtonsoft.Json;
using LaMulana2Archipelago.Archipelago;

namespace LaMulana2Archipelago.Managers
{
    // Shadow persistence is split into three files per seed:
    //
    //   ..._master.json     — every AP item ever granted for this seed across
    //                         every save slot. Append-only log keyed by Index.
    //   ..._staging.json    — current in-memory checkpoint (the L2 autosave /
    //                         memSave state). Tracks what death-continue would
    //                         restore to. Lives across slots; reset on dataLoad.
    //   ..._lm2slotN.json    — checkpoint marker for save slot N. Updated only
    //                         on dataSave (hardsave) — never on memSave —
    //                         because autosave progress is in-memory only and
    //                         is lost the moment any slot is hardloaded.
    //
    // Restore rules:
    //   Death-continue: regrant master items where Index > staging.CheckpointIndex.
    //   Hardload slotN: regrant master items where Index > slotN.CheckpointIndex.
    internal static class ShadowSaveManager
    {
        private const int SAVE_VERSION = 1;

        private static int _currentSlot = -1;
        private static SlotState _slot;
        private static MemState _mem;
        private static MasterState _master;

        private static SlotState Slot => _slot ??= LoadSlot();
        private static MemState Mem => _mem ??= LoadMem();
        private static MasterState Master => _master ??= LoadMaster();

        // SeedKey() resolves to "noseed" until ServerData.RoomSeed is populated
        // (AP connect) or set to "offline" (ActivateOffline). Any persistence
        // before that point would land in noseed_* files and stomp ServerData.Index,
        // so all destructive entry points short-circuit until we have a real key.
        private static bool IsActive =>
            ArchipelagoClient.Authenticated || ArchipelagoClient.OfflineMode;

        // Detached: the socket dropped mid-run. Disconnect() clears RoomSeed and
        // zeroes ServerData.Index, but the player keeps playing the same seed and
        // the game keeps saving — with every item granted before the drop in it.
        // If the checkpoints froze at their pre-drop values, the next reconnect +
        // Continue/Load would rewind Index to them and re-grant those items.
        // So remember the seed's key and the game-side item count, keep the save
        // hooks advancing (and rewinding) checkpoints against them, and hand the
        // count back on reconnect to the same seed.
        private static string _detachedKey;
        private static int _detachedIndex;
        private static bool IsDetached => !IsActive && _detachedKey != null;

        /// <summary>Key the shadow files are read/written under right now.</summary>
        private static string CurrentKey() => IsDetached ? _detachedKey : SeedKey();

        /// <summary>Items the game state currently holds (AP index).</summary>
        private static int GameIndex => IsDetached ? _detachedIndex : ArchipelagoClient.ServerData.Index;

        /// <summary>
        /// Call at the top of ArchipelagoClient.Disconnect(), before RoomSeed and
        /// Index are cleared. No-op unless an online session was live.
        /// </summary>
        public static void OnSessionDropped()
        {
            if (!ArchipelagoClient.Authenticated || ArchipelagoClient.ServerData?.RoomSeed == null)
                return;

            _detachedKey = SeedKey();
            _detachedIndex = ArchipelagoClient.ServerData.Index;
            Plugin.Log.LogInfo($"[Shadow] Session dropped; tracking saves for {_detachedKey} at index {_detachedIndex}.");
        }

        /// <summary>
        /// Call once an online session is authenticated. Reconnecting to the seed
        /// we were detached from restores the game-side Index, so a mid-run
        /// reconnect doesn't treat every already-granted item as new.
        /// </summary>
        public static void OnSessionAttached()
        {
            if (_detachedKey != null && _detachedKey == SeedKey()
                && ArchipelagoClient.ServerData.Index < _detachedIndex)
            {
                ArchipelagoClient.ServerData.Index = _detachedIndex;
                Plugin.Log.LogInfo($"[Shadow] Reattached to {_detachedKey}; Index -> {_detachedIndex}.");
            }
            ClearDetached();
        }

        /// <summary>True while a dropped online session's seed is still being played.</summary>
        public static bool HasDetachedSession => IsDetached;

        /// <summary>
        /// True when a session dropped earlier and the session now being set up
        /// (ServerData.RoomSeed already set, not yet Authenticated) is a different
        /// seed or slot.
        /// </summary>
        public static bool DetachedFromDifferentSeed => _detachedKey != null && _detachedKey != SeedKey();

        /// <summary>
        /// Remember a location check that could not be sent (socket down). Stored in
        /// the seed's master file so it survives a restart; main thread only.
        /// </summary>
        public static void AddUnsentCheck(long apLocationId)
        {
            if (!IsActive && !IsDetached) return;

            var m = Master;
            m.UnsentChecks ??= new List<long>();
            if (m.UnsentChecks.Contains(apLocationId)) return;

            m.UnsentChecks.Add(apLocationId);
            PersistMaster(m);
            Plugin.Log.LogInfo($"[Shadow] Queued unsent location check {apLocationId} ({m.UnsentChecks.Count} pending).");
        }

        /// <summary>Returns and clears the current seed's unsent checks.</summary>
        public static List<long> TakeUnsentChecks()
        {
            var result = new List<long>();
            if (!IsActive) return result;

            var m = Master;
            if (m.UnsentChecks == null || m.UnsentChecks.Count == 0) return result;

            result.AddRange(m.UnsentChecks);
            m.UnsentChecks.Clear();
            PersistMaster(m);
            return result;
        }

        /// <summary>Forget any detached session (offline activation, full teardown).</summary>
        public static void ClearDetached()
        {
            if (_detachedKey == null) return;
            _detachedKey = null;
            InvalidateCaches();
        }

        // Called by ArchipelagoClient on connect/disconnect/offline toggle so
        // any cached state captured against an old (or "noseed") SeedKey is
        // discarded; the next access reloads from the file that matches the
        // current seed.
        public static void InvalidateCaches()
        {
            _slot = null;
            _mem = null;
            _master = null;
        }

        // ==========================
        // Public API
        // ==========================

        public static void SetCurrentSaveSlot(int no)
        {
            if (_currentSlot == no) return;
            _currentSlot = no;
            _slot = null; // force reload from the new slot's file
            Plugin.Log.LogInfo($"[Shadow] Slot -> {_currentSlot}");
        }

        /// <summary>
        /// Call after every successful AP item grant. Idempotent by Index, so a
        /// re-granted (restored) item that is already in master is a no-op.
        /// </summary>
        public static void RecordGranted(ArchipelagoClient.QueuedApItem item)
        {
            if (!IsActive) return;

            var m = Master;

            // Idempotent: skip if we've already recorded this index.
            for (int i = m.ReceivedItems.Count - 1; i >= 0; i--)
            {
                if (m.ReceivedItems[i].Index == item.Index) return;
            }

            m.ReceivedItems.Add(new PersistedItem
            {
                Index = item.Index,
                ItemId = item.ItemId,
                ItemName = item.ItemName,
                SenderName = item.SenderName
            });
            if (item.Index > m.HighestIndex) m.HighestIndex = item.Index;
            PersistMaster(m);
        }

        /// <summary>
        /// Remember that the AP item at this queue index was a surplus collapsed
        /// copy paid for with an own-world placement (CollapsedItemResolver pass 2).
        /// That location's check survives every rewind, so a later re-grant of the
        /// same index must reuse this placement rather than spend another one.
        /// </summary>
        public static void RecordSurplusClaim(int index, int location)
        {
            if (!IsActive || index < 0) return;

            var m = Master;
            m.SurplusClaims ??= new Dictionary<int, int>();
            if (m.SurplusClaims.TryGetValue(index, out int existing) && existing == location) return;

            m.SurplusClaims[index] = location;
            PersistMaster(m);
        }

        public static bool TryGetSurplusClaim(int index, out int location)
        {
            location = 0;
            if (!IsActive || index < 0) return false;

            var claims = Master.SurplusClaims;
            return claims != null && claims.TryGetValue(index, out location);
        }

        /// <summary>
        /// Call when memSave succeeds (autosave / grail). Updates the in-memory
        /// checkpoint only — never the slot file, because autosave progress is
        /// not persisted to a slot until dataSave runs.
        /// </summary>
        public static void OnMemSave()
        {
            if (!IsActive && !IsDetached) return;

            var mem = Mem;

            // memSave fires during the death→continue flow before the player
            // regains control. If a restore is pending, this is that in-death
            // memSave — ignore it so the next continue still triggers restore.
            if (mem.PendingRestore)
            {
                Plugin.Log.LogInfo("[Shadow] memSave during pending restore — checkpoint advance skipped.");
                return;
            }

            int newIdx = GameIndex;
            if (newIdx == mem.CheckpointIndex) return;

            // The checkpoint only advances. A drop without PendingRestore set
            // means ServerData.Index got out of sync upstream (e.g. a
            // reconnect zeroed it and OnMemLoad didn't run before this fired).
            // Refusing the regress preserves the player's actual progress.
            if (newIdx < mem.CheckpointIndex)
            {
                Plugin.Log.LogWarning($"[Shadow] memSave: refusing to regress checkpoint {mem.CheckpointIndex} -> {newIdx}");
                return;
            }

            mem.CheckpointIndex = newIdx;
            PersistMem(mem);
            Plugin.Log.LogInfo($"[Shadow] Mem checkpoint advanced -> {mem.CheckpointIndex}");
        }

        /// <summary>
        /// Call when dataSave succeeds (hardsave to a slot). Updates the slot
        /// file's checkpoint AND the in-memory checkpoint (since memory and
        /// disk are in sync immediately after a hardsave).
        /// </summary>
        public static void OnDataSave()
        {
            if (!IsActive && !IsDetached) return;

            int newIdx = GameIndex;

            var st = Slot;
            if (st.CheckpointIndex != newIdx)
            {
                if (newIdx < st.CheckpointIndex)
                {
                    Plugin.Log.LogWarning($"[Shadow] dataSave: refusing to regress slot {_currentSlot} checkpoint {st.CheckpointIndex} -> {newIdx}");
                }
                else
                {
                    st.CheckpointIndex = newIdx;
                    PersistSlot(st);
                    Plugin.Log.LogInfo($"[Shadow] Slot {_currentSlot} checkpoint -> {st.CheckpointIndex}");
                }
            }

            var mem = Mem;
            bool memDirty = false;
            if (mem.CheckpointIndex != newIdx)
            {
                if (newIdx < mem.CheckpointIndex)
                {
                    Plugin.Log.LogWarning($"[Shadow] dataSave: refusing to regress mem checkpoint {mem.CheckpointIndex} -> {newIdx}");
                }
                else
                {
                    mem.CheckpointIndex = newIdx;
                    memDirty = true;
                }
            }
            if (mem.PendingRestore) { mem.PendingRestore = false; memDirty = true; }
            if (memDirty) PersistMem(mem);
        }

        /// <summary>
        /// Call when the player dies (gameOverStart). Flags a pending restore
        /// before the in-death memSave fires so that memSave is correctly
        /// skipped. Required because the title-Continue path uses memLoad
        /// alone, but the death-Continue path runs memSave→reInitSystem→memLoad
        /// — gameOverStart is the only hook that lands before that memSave.
        /// </summary>
        public static void OnDeath()
        {
            if (!IsActive && !IsDetached) return;

            FlagPendingRestore("Death");
        }

        /// <summary>
        /// Call when L2System.memLoad runs. Covers every "restore in-memory
        /// state to last autosave" code path — title-Continue, death-Continue,
        /// any future variant — and is harmless when redundant (the death
        /// flow has already flagged via OnDeath; this is a no-op then).
        /// </summary>
        public static void OnMemLoad()
        {
            if (IsDetached)
            {
                // Rewound to the last autosave: the game now holds only what that
                // checkpoint holds. Anything after it is re-granted by TryRestore
                // once we're connected again (PendingRestore survives on disk).
                _detachedIndex = Mem.CheckpointIndex;
                FlagPendingRestore("memLoad (disconnected)");
                return;
            }

            if (!IsActive) return;

            // memLoad reverts in-memory state to the last memSave checkpoint,
            // so ServerData.Index must follow. Without this, a Disconnect
            // (which zeroes Index) → reconnect → title-Continue would leave
            // Index stuck at 0 while mem.CheckpointIndex still reads the
            // saved value; the next memSave (any autosave) would then
            // clobber the checkpoint back to 0 and the AP item queue would
            // re-grant every item the player already had.
            //
            // OnFileLoad does the equivalent for the dataLoad path. The
            // death-restore path also lands here via Shadow_MemLoad_Patch,
            // and is idempotent (Index already equals CheckpointIndex when
            // OnDeath flagged the restore; queue is typically empty).
            var mem = Mem;
            int oldIndex = ArchipelagoClient.ServerData.Index;
            if (oldIndex != mem.CheckpointIndex)
            {
                ArchipelagoClient.ServerData.Index = mem.CheckpointIndex;
                Plugin.Log.LogInfo($"[Shadow] memLoad: Index sync {oldIndex} -> {mem.CheckpointIndex}");
            }
            DrainProcessedItems(mem.CheckpointIndex);

            FlagPendingRestore("memLoad");

            // memLoad is the single choke point for every "rewind to the last
            // autosave" path (title-Continue and both death-Continue variants),
            // so it's also where own-world items need re-granting.
            PersistentInventoryManager.RequestReplay("memLoad");
        }

        private static void FlagPendingRestore(string reason)
        {
            var mem = Mem;
            int pending = CountItemsAfter(mem.CheckpointIndex);
            if (pending == 0) return;
            if (mem.PendingRestore) return;

            mem.PendingRestore = true;
            PersistMem(mem);
            Plugin.Log.LogInfo($"[Shadow] {reason}: {pending} items pending restore.");
        }

        /// <summary>
        /// Call when the player starts a New Game from the title (L2System.gameStat).
        /// Resets the in-memory checkpoint to 0 and re-queues every master item, so a
        /// fresh L2 run replays the full AP item history. Without this, staging.json
        /// carries the previous session's CheckpointIndex into the new game and the
        /// player ends up with none of their AP items.
        /// </summary>
        public static void OnNewGame()
        {
            if (IsDetached)
            {
                var dmem = Mem;
                dmem.CheckpointIndex = 0;
                dmem.PendingRestore = CountItemsAfter(0) > 0; // replayed after reconnect
                PersistMem(dmem);
                _detachedIndex = 0;
                return;
            }

            if (!IsActive) return;

            var mem = Mem;
            mem.CheckpointIndex = 0;
            mem.PendingRestore = false;
            PersistMem(mem);

            int oldIndex = ArchipelagoClient.ServerData.Index;
            if (oldIndex != 0)
            {
                ArchipelagoClient.ServerData.Index = 0;
                Plugin.Log.LogInfo($"[Shadow] New game: Index reset {oldIndex} -> 0");
            }

            DrainProcessedItems(0);
            EnqueueMasterItemsAfter(0);

            // A fresh run starts with an empty save state, so every check the
            // player has already sent this seed needs its item handed back.
            PersistentInventoryManager.RequestReplay("New game");
        }

        /// <summary>
        /// Call on explicit hardload from the title menu. Resets the in-memory
        /// checkpoint to the slot's hardsave state, then re-queues every master
        /// item received after that checkpoint.
        /// </summary>
        public static void OnFileLoad()
        {
            if (IsDetached)
            {
                var dst = Slot;
                var dmem = Mem;
                dmem.CheckpointIndex = dst.CheckpointIndex;
                dmem.PendingRestore = CountItemsAfter(dst.CheckpointIndex) > 0; // replayed after reconnect
                PersistMem(dmem);
                _detachedIndex = dst.CheckpointIndex;
                return;
            }

            if (!IsActive) return;

            var st = Slot;

            // Hardload wipes the in-memory autosave state — reset memCheckpoint
            // to match the slot's hardsave.
            var mem = Mem;
            mem.CheckpointIndex = st.CheckpointIndex;
            mem.PendingRestore = false;
            PersistMem(mem);

            int oldIndex = ArchipelagoClient.ServerData.Index;
            if (oldIndex != st.CheckpointIndex)
            {
                ArchipelagoClient.ServerData.Index = st.CheckpointIndex;
                Plugin.Log.LogInfo($"[Shadow] Restored Index: {oldIndex} -> {st.CheckpointIndex}");
            }

            DrainProcessedItems(st.CheckpointIndex);
            EnqueueMasterItemsAfter(st.CheckpointIndex);

            // Hardload rolls the save state back to whatever the slot held —
            // possibly far behind the checks already sent for this seed.
            PersistentInventoryManager.RequestReplay("Hardload");
        }

        /// <summary>
        /// Call each active Update() frame. Triggers a death-restore if the
        /// in-memory checkpoint has its PendingRestore flag set. Returns true
        /// if a restore was triggered (caller should skip normal queue
        /// processing for one frame to let the queue settle).
        /// </summary>
        public static bool TryRestore()
        {
            var mem = Mem;
            if (!mem.PendingRestore) return false;

            if (Master.HighestIndex <= mem.CheckpointIndex)
            {
                mem.PendingRestore = false;
                PersistMem(mem);
                return false;
            }

            int oldIndex = ArchipelagoClient.ServerData.Index;
            if (oldIndex != mem.CheckpointIndex)
            {
                ArchipelagoClient.ServerData.Index = mem.CheckpointIndex;
                Plugin.Log.LogInfo($"[Shadow] Restored Index: {oldIndex} -> {mem.CheckpointIndex}");
            }

            DrainProcessedItems(mem.CheckpointIndex);
            EnqueueMasterItemsAfter(mem.CheckpointIndex);

            mem.PendingRestore = false;
            PersistMem(mem);
            return true;
        }

        // ==========================
        // Queue helpers
        // ==========================

        /// <summary>
        /// Removes items from the AP queue that have already been processed
        /// (index &lt;= upToIndex).
        /// </summary>
        private static void DrainProcessedItems(int upToIndex)
        {
            int drained = 0;

            // Under the queue lock: an item the socket thread delivers partway
            // through must not be dropped on the floor.
            ArchipelagoClient.MutateItemQueue(current =>
            {
                var keep = new List<ArchipelagoClient.QueuedApItem>(current.Count);
                foreach (var item in current)
                {
                    if (item.Index <= upToIndex) drained++;
                    else keep.Add(item);
                }
                return keep;
            });

            if (drained > 0)
                Plugin.Log.LogInfo($"[Shadow] Drained {drained} already-processed items from queue.");
        }

        /// <summary>
        /// Prepends every master item with Index > afterIndex to the front of
        /// the AP queue (in index order), preserving any other live items
        /// already queued (deduped by index).
        /// </summary>
        private static void EnqueueMasterItemsAfter(int afterIndex)
        {
            var m = Master;
            var restoreItems = new List<PersistedItem>();
            foreach (var pi in m.ReceivedItems)
            {
                if (pi.Index > afterIndex) restoreItems.Add(pi);
            }
            if (restoreItems.Count == 0) return;

            // Replay in the order they were originally received.
            restoreItems.Sort((a, b) => a.Index.CompareTo(b.Index));

            // Rebuild the queue with restore items first and any live items
            // appended (deduped). Done under the queue lock as one operation:
            // the old snapshot-then-clear pair silently lost anything the
            // socket thread delivered between the two calls.
            ArchipelagoClient.MutateItemQueue(pending =>
            {
                var rebuilt = new List<ArchipelagoClient.QueuedApItem>(
                    restoreItems.Count + pending.Count);

                foreach (var pi in restoreItems)
                {
                    rebuilt.Add(new ArchipelagoClient.QueuedApItem(
                        pi.Index, pi.ItemId, pi.ItemName, pi.SenderName));
                }

                foreach (var qi in pending)
                {
                    if (qi.Index <= afterIndex) continue;

                    bool dup = false;
                    foreach (var pi in restoreItems)
                    {
                        if (pi.Index == qi.Index) { dup = true; break; }
                    }
                    if (!dup) rebuilt.Add(qi);
                }

                return rebuilt;
            });

            Plugin.Log.LogInfo($"[Shadow] Re-queued {restoreItems.Count} items from master (afterIndex={afterIndex}).");
        }

        private static int CountItemsAfter(int afterIndex)
        {
            int c = 0;
            foreach (var pi in Master.ReceivedItems)
                if (pi.Index > afterIndex) c++;
            return c;
        }

        // ==========================
        // Persistence
        // ==========================

        private static string SeedKey()
        {
            var ap = ArchipelagoClient.ServerData;
            string slot = Sanitize((ap != null && ap.SlotName != null) ? ap.SlotName : "noslot");
            string seed = Sanitize((ap != null && ap.RoomSeed != null) ? ap.RoomSeed : "noseed");
            // NOTE: the URI is deliberately NOT part of the key. archipelago.gg
            // reassigns the game-room port on re-host, and a port change must not
            // start a fresh save.
            return $"{slot}_{seed}";
        }

        private static string SlotPath()
        {
            // Pre-slot-bind state has nowhere to land except staging — but
            // staging is the mem file, not a slot file. If no slot is bound,
            // route slot writes to a "nostage" sentinel name so we never write
            // slot data into the staging mem file.
            string lm2 = _currentSlot >= 0 ? $"lm2slot{_currentSlot}" : "noslot";
            Directory.CreateDirectory(Paths.ConfigPath);
            return Path.Combine(Paths.ConfigPath, $"LM2AP_Shadow_{CurrentKey()}_{lm2}.json");
        }

        private static string MemPath()
        {
            Directory.CreateDirectory(Paths.ConfigPath);
            return Path.Combine(Paths.ConfigPath, $"LM2AP_Shadow_{CurrentKey()}_staging.json");
        }

        private static string MasterPath()
        {
            Directory.CreateDirectory(Paths.ConfigPath);
            return Path.Combine(Paths.ConfigPath, $"LM2AP_Shadow_{CurrentKey()}_master.json");
        }

        private static string Sanitize(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        private static SlotState LoadSlot()
        {
            try
            {
                string path = SlotPath();
                if (File.Exists(path))
                {
                    var raw = JsonConvert.DeserializeObject<LegacyOrSlotState>(File.ReadAllText(path));
                    if (raw != null && raw.Version == SAVE_VERSION)
                    {
                        // Legacy schema migration: old per-slot files held
                        // GrantedSinceCheckpoint. Fold those into master so
                        // the restore rule has access to them.
                        if (raw.GrantedSinceCheckpoint != null && raw.GrantedSinceCheckpoint.Count > 0)
                        {
                            var m = Master;
                            int migrated = 0;
                            foreach (var pi in raw.GrantedSinceCheckpoint)
                            {
                                bool dup = false;
                                foreach (var existing in m.ReceivedItems)
                                {
                                    if (existing.Index == pi.Index) { dup = true; break; }
                                }
                                if (!dup)
                                {
                                    m.ReceivedItems.Add(pi);
                                    migrated++;
                                }
                                if (pi.Index > m.HighestIndex) m.HighestIndex = pi.Index;
                            }
                            if (migrated > 0)
                            {
                                PersistMaster(m);
                                Plugin.Log.LogInfo($"[Shadow] Migrated {migrated} legacy items into master.");
                            }
                        }

                        var migratedSlot = new SlotState
                        {
                            Version = SAVE_VERSION,
                            CheckpointIndex = raw.CheckpointIndex
                        };

                        // Rewrite slot file in the new schema so the legacy
                        // field doesn't keep round-tripping on disk.
                        if (raw.GrantedSinceCheckpoint != null && raw.GrantedSinceCheckpoint.Count > 0)
                            PersistSlot(migratedSlot);

                        return migratedSlot;
                    }
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[Shadow] LoadSlot failed: {e}"); }

            return new SlotState
            {
                Version = SAVE_VERSION,
                CheckpointIndex = 0
            };
        }

        private static MemState LoadMem()
        {
            try
            {
                string path = MemPath();
                if (File.Exists(path))
                {
                    var raw = JsonConvert.DeserializeObject<LegacyOrSlotState>(File.ReadAllText(path));
                    if (raw != null && raw.Version == SAVE_VERSION)
                    {
                        // Legacy staging files may also carry GrantedSinceCheckpoint
                        // (the pre-rework schema). Fold into master.
                        if (raw.GrantedSinceCheckpoint != null && raw.GrantedSinceCheckpoint.Count > 0)
                        {
                            var m = Master;
                            int migrated = 0;
                            foreach (var pi in raw.GrantedSinceCheckpoint)
                            {
                                bool dup = false;
                                foreach (var existing in m.ReceivedItems)
                                {
                                    if (existing.Index == pi.Index) { dup = true; break; }
                                }
                                if (!dup)
                                {
                                    m.ReceivedItems.Add(pi);
                                    migrated++;
                                }
                                if (pi.Index > m.HighestIndex) m.HighestIndex = pi.Index;
                            }
                            if (migrated > 0)
                            {
                                PersistMaster(m);
                                Plugin.Log.LogInfo($"[Shadow] Migrated {migrated} legacy staging items into master.");
                            }
                        }

                        var mem = new MemState
                        {
                            Version = SAVE_VERSION,
                            CheckpointIndex = raw.CheckpointIndex,
                            PendingRestore = raw.PendingRestore
                        };

                        if (raw.GrantedSinceCheckpoint != null && raw.GrantedSinceCheckpoint.Count > 0)
                            PersistMem(mem);

                        return mem;
                    }
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[Shadow] LoadMem failed: {e}"); }

            return new MemState
            {
                Version = SAVE_VERSION,
                CheckpointIndex = 0,
                PendingRestore = false
            };
        }

        private static MasterState LoadMaster()
        {
            try
            {
                string path = MasterPath();
                if (File.Exists(path))
                {
                    var loaded = JsonConvert.DeserializeObject<MasterState>(File.ReadAllText(path));
                    if (loaded != null && loaded.Version == SAVE_VERSION)
                    {
                        loaded.ReceivedItems ??= new List<PersistedItem>();
                        return loaded;
                    }
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[Shadow] LoadMaster failed: {e}"); }

            return new MasterState
            {
                Version = SAVE_VERSION,
                HighestIndex = 0,
                ReceivedItems = new List<PersistedItem>()
            };
        }

        private static void PersistSlot(SlotState st)
        {
            try { File.WriteAllText(SlotPath(), JsonConvert.SerializeObject(st, Formatting.Indented)); }
            catch (Exception e) { Plugin.Log.LogWarning($"[Shadow] PersistSlot failed: {e}"); }
        }

        private static void PersistMem(MemState mem)
        {
            try { File.WriteAllText(MemPath(), JsonConvert.SerializeObject(mem, Formatting.Indented)); }
            catch (Exception e) { Plugin.Log.LogWarning($"[Shadow] PersistMem failed: {e}"); }
        }

        private static void PersistMaster(MasterState m)
        {
            try { File.WriteAllText(MasterPath(), JsonConvert.SerializeObject(m, Formatting.Indented)); }
            catch (Exception e) { Plugin.Log.LogWarning($"[Shadow] PersistMaster failed: {e}"); }
        }

        // ==========================
        // Data model
        // ==========================

        [Serializable]
        private class SlotState
        {
            public int Version { get; set; }
            public int CheckpointIndex { get; set; }
        }

        [Serializable]
        private class MemState
        {
            public int Version { get; set; }
            public int CheckpointIndex { get; set; }
            public bool PendingRestore { get; set; }
        }

        [Serializable]
        private class MasterState
        {
            public int Version { get; set; }
            public int HighestIndex { get; set; }
            public List<PersistedItem> ReceivedItems { get; set; }

            /// <summary>AP queue index → LocationID of the own placement a surplus
            /// collapsed copy consumed. Absent in older master files.</summary>
            public Dictionary<int, int> SurplusClaims { get; set; }

            /// <summary>AP location ids checked while the socket was down (or whose
            /// send failed), re-sent on the next connect to this seed.</summary>
            public List<long> UnsentChecks { get; set; }
        }

        // Used by LoadSlot / LoadMem to absorb legacy files that still carry
        // GrantedSinceCheckpoint. The field is migrated into master and the
        // file is rewritten in the new schema.
        [Serializable]
        private class LegacyOrSlotState
        {
            public int Version { get; set; }
            public int CheckpointIndex { get; set; }
            public bool PendingRestore { get; set; }
            public List<PersistedItem> GrantedSinceCheckpoint { get; set; }
        }

        [Serializable]
        public class PersistedItem
        {
            public int Index { get; set; }
            public long ItemId { get; set; }
            public string ItemName { get; set; }
            public string SenderName { get; set; }
        }
    }
}
