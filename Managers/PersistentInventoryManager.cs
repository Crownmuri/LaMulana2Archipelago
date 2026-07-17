using System.Collections.Generic;
using L2Base;
using LaMulana2Archipelago.Archipelago;
using LaMulana2RandomizerShared;
using LM2RandomiserMod;

namespace LaMulana2Archipelago.Managers
{
    // Persistent Inventory (slot_data["persistent_inventory"]).
    //
    // The problem this solves is an asymmetry between the two kinds of item:
    //
    //   • Foreign items (another player's world) arrive over the AP socket, so
    //     ShadowSaveManager can log them in master.json and re-queue whatever
    //     the save state is missing. They already survive death/load.
    //
    //   • Own-world items are NOT sent by the server at all: ArchipelagoClient
    //     connects with ItemsHandlingFlags.RemoteItems, which asks only for
    //     other worlds' items. Our own finds are granted locally — by the
    //     get-flags SceneRandomizer.CreateGetFlags bakes onto the chest/shop,
    //     or by a direct delivery (DeliverGlossaryRom, pot filler). Either way
    //     the result lands in the L2 save state, which memLoad rewinds.
    //
    // So dying gives back every item a stranger sent you and takes back
    // everything you found yourself. This manager closes that half.
    //
    // Source of truth is AP's own checked-locations set, not a local ledger.
    // ServerData.CheckedLocations is repopulated from the server on connect and
    // is never rewound by memLoad, so "locations you have checked" already
    // outlives any death, load or fresh New Game. Replay walks that set and
    // re-grants whatever the save state is missing.
    //
    // Two invariants make re-granting safe:
    //
    //   1. The probe is per-item, not a checkpoint. Each location owns exactly
    //      one registered (sheet, flag) — its item's get-flag, or a book/pot
    //      flag — so flag == 0 reliably means "the save state lacks this". This
    //      matters most for Sacred Orbs, whose grant bumps a CALCU.ADD running
    //      total (sheet 0 flag 2): a blind re-apply would stack phantom orbs.
    //
    //   2. Probe and reward always rewind together. Both live in the save state,
    //      so a rewind that takes the item away also clears the flag, and one
    //      that keeps the item keeps the flag set. That is why replaying
    //      coins/weights can't be farmed: spending them is part of the same
    //      state, so the only thing replay ever restores is what the rewind
    //      actually removed.
    //
    // Locations whose flag is NOT in the save state are skipped for free, which
    // is exactly right: a foreign item's sheet-31 flag (>= 225) is virtualised
    // by VirtualFlagManager, which resolves it from this same CheckedLocations
    // set. The probe reads 1 and the location is left alone — the server re-sends
    // those items itself.
    internal static class PersistentInventoryManager
    {
        private const long BaseApLocationId = 430000;
        private const long BaseApItemId = 420000;

        /// <summary>
        /// From slot_data["persistent_inventory"]. When false this manager is
        /// inert and only foreign items are restored (the pre-existing behavior).
        /// </summary>
        public static bool Enabled = false;

        private struct ReplayEntry
        {
            public LocationID Location;
            public long ApItemId;
            public int Sheet;
            public int Flag;
        }

        // Resolved at scan time and drained one per frame by Update, so a big
        // replay can't spike a single frame.
        private static readonly Queue<ReplayEntry> _pending = new Queue<ReplayEntry>();

        // Retry budget per location, so one item that can never be granted
        // can't hold the replay open indefinitely.
        private static readonly Dictionary<LocationID, int> _attempts = new Dictionary<LocationID, int>();
        private const int MaxAttemptsPerLocation = 120;

        private static bool _replayQueued;
        private static string _replayReason;

        public static bool IsReplaying => _pending.Count > 0;

        /// <summary>
        /// Ask for a replay on the next safe frame. Called from the save/load
        /// lifecycle points; the actual scan is deferred because at request time
        /// the L2 flag state is still mid-rebuild (dataLoad hasn't run memLoad
        /// yet) and would probe garbage.
        /// </summary>
        public static void RequestReplay(string reason)
        {
            if (!Enabled) return;

            _replayQueued = true;
            _replayReason = reason;
        }

        public static void Reset()
        {
            _pending.Clear();
            _attempts.Clear();
            _replayQueued = false;
        }

        /// <summary>
        /// Drives the replay. Returns true if it did work this frame, in which
        /// case Plugin.Update skips normal AP queue processing — own-world items
        /// go back in before any foreign item opens a dialog.
        /// </summary>
        public static bool Update(L2System sys, NewPlayer pl)
        {
            if (!Enabled) return false;

            if (_replayQueued)
            {
                _replayQueued = false;
                BuildPending(sys);
            }

            if (_pending.Count == 0) return false;

            GrantSilently(sys, pl, _pending.Dequeue());

            if (_pending.Count == 0)
                Plugin.Log.LogInfo("[Persist] Replay complete.");

            return true;
        }

        // ==========================
        // Scan
        // ==========================

        private static void BuildPending(L2System sys)
        {
            _pending.Clear();

            // Online, the scout cache is filled by an async callback on connect.
            // Until it lands every scout reads null, which would be silently
            // misread as "not our item" and skip every own glossary ROM and pot
            // filler. Re-arm and try again next frame instead. Offline never sets
            // this and never needs to: it has no placeholders to resolve.
            if (ArchipelagoClient.Authenticated && !ArchipelagoClient.ScoutCacheReady)
            {
                _replayQueued = true;
                return;
            }

            var checkedLocations = ArchipelagoClient.ServerData?.CheckedLocations;
            if (checkedLocations == null || checkedLocations.Count == 0) return;

            int missing = 0;

            foreach (long apLocation in checkedLocations)
            {
                LocationID location = (LocationID)(int)(apLocation - BaseApLocationId);

                long apItemId = ResolveOwnApItemId(location, apLocation);
                if (apItemId <= BaseApItemId) continue;

                if (!TryGetProbeFlag(location, apItemId, out int sheet, out int flag))
                    continue;

                if (HasFlag(sys, sheet, flag))
                    continue;

                _pending.Enqueue(new ReplayEntry
                {
                    Location = location,
                    ApItemId = apItemId,
                    Sheet = sheet,
                    Flag = flag
                });
                missing++;
            }

            if (missing > 0)
                Plugin.Log.LogInfo($"[Persist] {_replayReason}: {missing} own item(s) to restore.");
        }

        /// <summary>
        /// The AP item id of THIS player's item at a location, or 0 if the
        /// location holds someone else's item (the server re-sends those itself).
        ///
        /// The scout is authoritative and is the only thing that can answer this
        /// for a location written as an AP placeholder — which is how the apworld
        /// encodes own glossary ROMs and own pot filler (randomizer.py routes
        /// them "through the per-location placeholder so the location's AP
        /// mechanism fires the check"). For those, item_placements holds the
        /// placeholder, not the real item, so LocationToItem cannot identify the
        /// ROM. This mirrors CheckManager.TryDeliverOwnGlossaryRom.
        ///
        /// The LocationToItem fallback covers offline seeds, which have no scout
        /// cache. Offline is solo, so a non-placeholder placement is by
        /// definition ours.
        /// </summary>
        private static long ResolveOwnApItemId(LocationID location, long apLocation)
        {
            var scouted = ArchipelagoClientProvider.Client?.GetItemAtLocation(apLocation);
            if (scouted != null)
                return scouted.IsOwnItem ? scouted.ItemId : 0;

            if (SeedFlagMapBuilder.LocationToItem.TryGetValue(location, out ItemID item)
                && !ApItemIDs.IsApPlaceholder((int)item)
                && (int)item > 0)
                return BaseApItemId + (int)item;

            return 0;
        }

        /// <summary>
        /// The save-state flag that says whether this item has already been
        /// granted.
        ///
        /// Glossary ROMs are special-cased: a ROM's book flag belongs to the ROM,
        /// not to the location it was found at (a shuffled ROM can turn up
        /// anywhere), so the location's own registered flag would probe the wrong
        /// entry. Every other item is identified by its location, whose single
        /// registered flag is the one a physical pickup sets.
        /// </summary>
        private static bool TryGetProbeFlag(LocationID location, long apItemId, out int sheet, out int flag)
        {
            int gameId = (int)(apItemId - BaseApItemId);

            if (GlossaryManager.TryGetBookFlagForItem(gameId, out int bookFlag))
            {
                sheet = GlossaryManager.BookSheet;
                flag = bookFlag;
                return true;
            }

            return LocationFlagMap.TryGetFlagForLocation(location, out sheet, out flag);
        }

        private static bool HasFlag(L2System sys, int sheet, int flag)
        {
            short data = 0;
            if (!sys.getFlag(sheet, flag, ref data))
                return true; // unreadable flag: assume present rather than risk a double grant

            return data > 0;
        }

        // ==========================
        // Grant
        // ==========================

        private static void GrantSilently(L2System sys, NewPlayer pl, ReplayEntry entry)
        {
            // Distinct negative key per location so ItemGrantManager's per-index
            // backoff can't collide with a live AP queue index (always >= 0).
            int queueIndex = -((int)entry.Location) - 1;

            ItemGrantManager.SuppressPresentation = true;
            try
            {
                bool granted = ItemGrantManager.TryGrantItem(sys, pl, queueIndex, entry.ApItemId);

                if (!granted)
                {
                    // Backoff or a state guard refused it — requeue and retry.
                    // Capped: a permanently failing item would otherwise hold
                    // Update at "did work" forever and starve the AP queue,
                    // silently blocking every incoming foreign item.
                    if (Bump(entry.Location) <= MaxAttemptsPerLocation)
                        _pending.Enqueue(entry);
                    else
                        Plugin.Log.LogWarning(
                            $"[Persist] Giving up on AP item {entry.ApItemId} from {entry.Location} " +
                            $"after {MaxAttemptsPerLocation} attempts.");
                    return;
                }

                _attempts.Remove(entry.Location);
                StampProbeFlag(sys, entry);
                Plugin.Log.LogInfo($"[Persist] Restored AP item {entry.ApItemId} from {entry.Location}");
            }
            finally
            {
                ItemGrantManager.SuppressPresentation = false;
            }
        }

        /// <summary>
        /// Make the probe flag true after a grant that didn't set it itself.
        ///
        /// Needed wherever the reward and the location's flag are set by
        /// different code paths:
        ///
        ///   • Progressives. An AP grant of a Progressive Whip/Shield/Beherit
        ///     deliberately leaves the per-level markers (sheet 2: whip 190-192,
        ///     shield 193-195, beherit 170-176) alone — only a physical chest
        ///     pickup sets those — and bumps a shared count instead.
        ///   • Filler. FillerRewardMap drops coins/weights; the sheet-31 flag is
        ///     stamped by TreasureBoxWeightPatch at the chest, not by the grant.
        ///
        /// Without this the probe would still read 0 after the restore, and the
        /// item would be re-granted after every *subsequent* rewind that lands on
        /// a save made post-restore — walking whip level, or coin count, up one
        /// step at a time.
        ///
        /// Stamping is semantically honest: the registered flag is the one a
        /// physical pickup sets, and the check really has been sent, so the chest
        /// is spent. Wrapped in the recursive guard so the write can't be
        /// mistaken for a fresh location check. Grants that DO set their own flag
        /// (DeliverGlossaryRom, Sacred Orbs, maps, skulls) no-op here.
        /// </summary>
        private static void StampProbeFlag(L2System sys, ReplayEntry entry)
        {
            if (HasFlag(sys, entry.Sheet, entry.Flag))
                return;

            using (ItemGrantRecursiveGuard.Begin())
                sys.setFlagData(entry.Sheet, entry.Flag, 1);

            Plugin.Log.LogInfo(
                $"[Persist] Stamped flag ({entry.Sheet},{entry.Flag}) for {entry.Location} — grant path leaves it to the pickup.");
        }

        private static int Bump(LocationID location)
        {
            _attempts.TryGetValue(location, out int n);
            n++;
            _attempts[location] = n;
            return n;
        }
    }
}
