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

        private static int _restoreItemsRemaining = 0;
        public static bool IsRestoringItem => _restoreItemsRemaining > 0;

        public static void OnRestoreItemGranted()
        {
            if (_restoreItemsRemaining > 0)
                _restoreItemsRemaining--;
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
        /// Call after every successful first-time AP item grant.
        /// Callers must skip this when IsRestoringItem is true so master stays
        /// a record of first grants only.
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
        /// Call when memSave succeeds (autosave / grail). Updates the in-memory
        /// checkpoint only — never the slot file, because autosave progress is
        /// not persisted to a slot until dataSave runs.
        /// </summary>
        public static void OnMemSave()
        {
            if (!IsActive) return;

            var mem = Mem;

            // memSave fires during the death→continue flow before the player
            // regains control. If a restore is pending, this is that in-death
            // memSave — ignore it so the next continue still triggers restore.
            if (mem.PendingRestore)
            {
                Plugin.Log.LogInfo("[Shadow] memSave during pending restore — checkpoint advance skipped.");
                return;
            }

            int newIdx = ArchipelagoClient.ServerData.Index;
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
            if (!IsActive) return;

            int newIdx = ArchipelagoClient.ServerData.Index;

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
            if (!IsActive) return;

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
        }

        /// <summary>
        /// Call on explicit hardload from the title menu. Resets the in-memory
        /// checkpoint to the slot's hardsave state, then re-queues every master
        /// item received after that checkpoint.
        /// </summary>
        public static void OnFileLoad()
        {
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
            var queue = ArchipelagoClient.ItemQueue;
            if (queue.Count == 0) return;

            var keep = new Queue<ArchipelagoClient.QueuedApItem>();
            int drained = 0;

            while (queue.Count > 0)
            {
                var item = queue.Dequeue();
                if (item.Index <= upToIndex)
                    drained++;
                else
                    keep.Enqueue(item);
            }

            while (keep.Count > 0)
                queue.Enqueue(keep.Dequeue());

            if (drained > 0)
                Plugin.Log.LogInfo($"[Shadow] Drained {drained} already-processed items from queue.");
        }

        /// <summary>
        /// Prepends every master item with Index > afterIndex to the front of
        /// the AP queue (in index order), preserving any other live items
        /// already queued (deduped by index). Bumps the restore counter so
        /// Plugin.Update routes the next N grants through OnRestoreItemGranted
        /// instead of RecordGranted.
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

            // Snapshot any live items already queued (e.g. items the AP
            // server delivered after our last record), then rebuild the queue
            // with restore items first and live items appended (deduped).
            var pending = ArchipelagoClient.ItemQueue.ToArray();
            ArchipelagoClient.ItemQueue.Clear();

            foreach (var pi in restoreItems)
            {
                ArchipelagoClient.ItemQueue.Enqueue(new ArchipelagoClient.QueuedApItem(
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
                if (!dup) ArchipelagoClient.ItemQueue.Enqueue(qi);
            }

            _restoreItemsRemaining += restoreItems.Count;
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
            string uri = Sanitize((ap != null && ap.Uri != null) ? ap.Uri : "offline");
            string slot = Sanitize((ap != null && ap.SlotName != null) ? ap.SlotName : "noslot");
            string seed = Sanitize((ap != null && ap.RoomSeed != null) ? ap.RoomSeed : "noseed");
            return $"{uri}_{slot}_{seed}";
        }

        private static string SlotPath()
        {
            // Pre-slot-bind state has nowhere to land except staging — but
            // staging is the mem file, not a slot file. If no slot is bound,
            // route slot writes to a "nostage" sentinel name so we never write
            // slot data into the staging mem file.
            string lm2 = _currentSlot >= 0 ? $"lm2slot{_currentSlot}" : "noslot";
            Directory.CreateDirectory(Paths.ConfigPath);
            return Path.Combine(Paths.ConfigPath, $"LM2AP_Shadow_{SeedKey()}_{lm2}.json");
        }

        private static string MemPath()
        {
            Directory.CreateDirectory(Paths.ConfigPath);
            return Path.Combine(Paths.ConfigPath, $"LM2AP_Shadow_{SeedKey()}_staging.json");
        }

        private static string MasterPath()
        {
            Directory.CreateDirectory(Paths.ConfigPath);
            return Path.Combine(Paths.ConfigPath, $"LM2AP_Shadow_{SeedKey()}_master.json");
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
