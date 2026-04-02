using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using Newtonsoft.Json;
using LaMulana2Archipelago.Archipelago;

namespace LaMulana2Archipelago.Managers
{
    internal static class ShadowSaveManager
    {
        private const int SAVE_VERSION = 1;

        private static int _currentSlot = -1;
        private static ShadowState _state;
        private static ShadowState State => _state ??= Load();

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
            _state = null;
            Plugin.Log.LogInfo($"[Shadow] Slot -> {_currentSlot}");
        }

        /// <summary>
        /// Call after every successful AP item grant.
        /// </summary>
        public static void RecordGranted(ArchipelagoClient.QueuedApItem item)
        {
            var st = State;
            st.GrantedSinceCheckpoint.Add(new PersistedItem
            {
                Index = item.Index,
                ItemId = item.ItemId,
                ItemName = item.ItemName,
                SenderName = item.SenderName
            });
            Persist(st);
        }

        /// <summary>
        /// Call when memSave succeeds — advances the checkpoint.
        /// </summary>
        public static void OnMemSave()
        {
            var st = State;

            // memSave fires during the death→continue flow before the player
            // regains control. If a restore is pending, this is that in-death
            // memSave — ignore it so we don't wipe the restore list.
            if (st.PendingRestore)
            {
                Plugin.Log.LogInfo("[Shadow] memSave during pending restore — checkpoint advance skipped.");
                return;
            }

            st.CheckpointIndex = ArchipelagoClient.ServerData.Index;
            st.GrantedSinceCheckpoint.Clear();
            Persist(st);
            Plugin.Log.LogInfo($"[Shadow] Checkpoint advanced -> {st.CheckpointIndex}");
        }

        /// <summary>
        /// Call when the player dies — marks that a restore is needed.
        /// </summary>
        public static void OnDeath()
        {
            var st = State;
            if (st.GrantedSinceCheckpoint.Count == 0) return; // nothing to restore
            st.PendingRestore = true;
            Persist(st);
            Plugin.Log.LogInfo($"[Shadow] Death recorded, {st.GrantedSinceCheckpoint.Count} items pending restore.");
        }

        /// <summary>
        /// Call on explicit file load from title.
        /// Restores the persisted item index so already-saved items are not
        /// re-granted, and clears transient death-restore state.
        /// </summary>
        public static void OnFileLoad()
        {
            var st = State;

            // Restore the processed item index from the checkpoint so that
            // items already saved in this slot aren't re-granted on reconnect.
            if (st.CheckpointIndex > 0
                && st.CheckpointIndex > ArchipelagoClient.ServerData.Index)
            {
                int oldIndex = ArchipelagoClient.ServerData.Index;
                ArchipelagoClient.ServerData.Index = st.CheckpointIndex;
                DrainProcessedItems(st.CheckpointIndex);
                Plugin.Log.LogInfo(
                    $"[Shadow] Restored Index: {oldIndex} -> {st.CheckpointIndex}");
            }

            // File load resets game state to the checkpoint.  Clear transient
            // tracking — items after the checkpoint will be naturally
            // re-delivered from the AP queue.
            bool dirty = false;
            if (st.PendingRestore)
            {
                st.PendingRestore = false;
                dirty = true;
                Plugin.Log.LogInfo("[Shadow] PendingRestore cleared (explicit file load).");
            }
            if (st.GrantedSinceCheckpoint.Count > 0)
            {
                st.GrantedSinceCheckpoint.Clear();
                dirty = true;
            }
            if (dirty) Persist(st);
        }

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
        /// Call each active Update() frame. Prepends un-checkpointed items back
        /// to the front of the ItemQueue and winds ServerData.Index back.
        /// Returns true if a restore was triggered (skip normal queue processing
        /// this frame to let the queue settle).
        /// </summary>
        public static bool TryRestore()
        {
            var st = State;
            if (!st.PendingRestore) return false;
            if (st.GrantedSinceCheckpoint.Count == 0)
            {
                st.PendingRestore = false;
                Persist(st);
                return false;
            }

            Plugin.Log.LogInfo($"[Shadow] Restoring {st.GrantedSinceCheckpoint.Count} items (checkpoint index={st.CheckpointIndex}).");

            // Snapshot whatever is already pending (new live items that arrived
            // during the death screen), then rebuild: restore items first.
            var pending = ArchipelagoClient.ItemQueue.ToArray();
            ArchipelagoClient.ItemQueue.Clear();

            foreach (var pi in st.GrantedSinceCheckpoint)
            {
                ArchipelagoClient.ItemQueue.Enqueue(new ArchipelagoClient.QueuedApItem(
                    pi.Index, pi.ItemId, pi.ItemName, pi.SenderName));
            }

            foreach (var qi in pending)
                ArchipelagoClient.ItemQueue.Enqueue(qi);

            // Wind the index back so dedup doesn't swallow the restore items.
            ArchipelagoClient.ServerData.Index = st.CheckpointIndex;

            _restoreItemsRemaining = st.GrantedSinceCheckpoint.Count;

            st.PendingRestore = false;
            Persist(st);
            return true;
        }

        // ==========================
        // Persistence
        // ==========================

        private static string SavePath()
        {
            var ap = ArchipelagoClient.ServerData;
            string uri = Sanitize((ap != null && ap.Uri != null) ? ap.Uri : "offline");
            string slot = Sanitize((ap != null && ap.SlotName != null) ? ap.SlotName : "noslot");
            string seed = Sanitize((ap != null && ap.RoomSeed != null) ? ap.RoomSeed : "noseed");
            string lm2 = _currentSlot >= 0 ? $"lm2slot{_currentSlot}" : "staging";
            Directory.CreateDirectory(Paths.ConfigPath);
            return Path.Combine(Paths.ConfigPath, $"LM2AP_Shadow_{uri}_{slot}_{seed}_{lm2}.json");
        }

        private static string Sanitize(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        private static ShadowState Load()
        {
            try
            {
                string path = SavePath();
                if (File.Exists(path))
                {
                    var loaded = JsonConvert.DeserializeObject<ShadowState>(File.ReadAllText(path));
                    if (loaded != null && loaded.Version == SAVE_VERSION)
                    {
                        loaded.GrantedSinceCheckpoint ??= new List<PersistedItem>();
                        return loaded;
                    }
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[Shadow] Load failed: {e}"); }

            return new ShadowState
            {
                Version = SAVE_VERSION,
                CheckpointIndex = 0,
                PendingRestore = false,
                GrantedSinceCheckpoint = new List<PersistedItem>()
            };
        }

        private static void Persist(ShadowState st)
        {
            try { File.WriteAllText(SavePath(), JsonConvert.SerializeObject(st, Formatting.Indented)); }
            catch (Exception e) { Plugin.Log.LogWarning($"[Shadow] Persist failed: {e}"); }
        }

        // ==========================
        // Data model
        // ==========================

        [Serializable]
        private class ShadowState
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