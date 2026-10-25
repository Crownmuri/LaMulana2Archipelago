using System.Collections.Generic;
using L2Base;
using L2Flag;
using LaMulana2Archipelago.Archipelago;
using LaMulana2RandomizerShared;

namespace LaMulana2Archipelago.Managers
{
    /// <summary>
    /// Shuffle Guardian Souls (slot_data["shuffle_guardian_souls"]).
    ///
    /// Soul gates normally open on the guardian-kill count (3,0) "guardians".
    /// With this option they count Guardian Souls RECEIVED instead (AP items
    /// 610-618, no in-game equivalent), and each confirmed guardian kill sends
    /// an extra "&lt;Guardian&gt; Obliterated" check (LocationID 314-322).
    ///
    /// State lives in free sheet-13 scratch flags (13,190..199 are unreferenced
    /// by every scene and script; (13,199) is taken by the Orpheus rewrite):
    ///   (13,190) bitmask of received souls, bit i = SoulItems[i]
    ///   (13,191) number of distinct souls received (0-9) — what soul gates read
    ///   (13,192) bitmask of Obliterated checks whose own item was delivered
    /// Being game flags, they rewind with the save exactly like a real item:
    /// network souls newer than the save are re-granted by the shadow log, and
    /// own souls / Obliterated items are restored by <see cref="ScanForMissing"/>.
    ///
    /// Only the soul-gate machinery is redirected to (13,191) — SceneRandomizer's
    /// randomized gates plus the vanilla "sgateCheck" FlagWatchers and the two
    /// PlayerHitAreas that arm them. Every other (3,0) reader (jewel mails, ankh
    /// shop, funeral, hints) keeps counting real kills.
    /// </summary>
    public static class GuardianSoulManager
    {
        private const long BaseApLocationId = 430000;
        private const long BaseApItemId = 420000;

        public const int StateSheet = 13;
        public const int SoulMaskFlag = 190;
        public const int SoulCountFlag = 191;
        public const int DeliveredMaskFlag = 192;

        // The guardian-kill count soul gates read in vanilla: (3,0) "guardians".
        public const int KillCountSheet = 3;
        public const int KillCountFlag = 0;

        /// <summary>Soul items, in bit order.</summary>
        private static readonly ItemID[] SoulItems =
        {
            ItemID.FafnirSoul, ItemID.VritraSoul, ItemID.KujataSoul,
            ItemID.AtenRaSoul, ItemID.JormungandSoul, ItemID.AnuSoul,
            ItemID.SurtrSoul, ItemID.EchidnaSoul, ItemID.HelSoul,
        };

        private static readonly string[] SoulNames =
        {
            "Fafnir's Soul", "Vritra's Soul", "Kujata's Soul",
            "Aten-Ra's Soul", "Jormungand's Soul", "Anu's Soul",
            "Surtr's Soul", "Echidna's Soul", "Hel's Soul",
        };

        /// <summary>Guardian kill event → its Obliterated check, by name (the
        /// kill LocationIDs are not in soul order), in delivered-mask bit order.</summary>
        private static readonly LocationID[] ObliteratedLocations =
        {
            LocationID.FafnirObliterated, LocationID.VritraObliterated, LocationID.KujataObliterated,
            LocationID.AtenRaObliterated, LocationID.JormungandObliterated, LocationID.AnuObliterated,
            LocationID.SurtrObliterated, LocationID.EchidnaObliterated, LocationID.HelObliterated,
        };

        private static readonly Dictionary<LocationID, LocationID> KillToObliterated =
            new Dictionary<LocationID, LocationID>
            {
                { LocationID.Fafnir,     LocationID.FafnirObliterated },
                { LocationID.Vritra,     LocationID.VritraObliterated },
                { LocationID.Kujata,     LocationID.KujataObliterated },
                { LocationID.AtenRa,     LocationID.AtenRaObliterated },
                { LocationID.Jormungand, LocationID.JormungandObliterated },
                { LocationID.Anu,        LocationID.AnuObliterated },
                { LocationID.Surtr,      LocationID.SurtrObliterated },
                { LocationID.Echidna,    LocationID.EchidnaObliterated },
                { LocationID.Hel,        LocationID.HelObliterated },
            };

        /// <summary>From slot_data["shuffle_guardian_souls"].</summary>
        public static bool Enabled { get; set; }

        // Obliterated locations whose own item still has to be handed over,
        // drained one per frame from Plugin.Update.
        private static readonly Queue<LocationID> _pending = new Queue<LocationID>();
        private static readonly HashSet<LocationID> _pendingSet = new HashSet<LocationID>();
        private static readonly Dictionary<LocationID, int> _attempts = new Dictionary<LocationID, int>();
        private const int MaxAttempts = 120;

        // Live kill deliveries present the item; rewind restores stay silent.
        private static readonly HashSet<LocationID> _silent = new HashSet<LocationID>();

        private static bool _scanQueued;

        // =====================================================================
        // Ids
        // =====================================================================

        public static bool IsSoul(int gameId) => SoulIndex(gameId) >= 0;

        private static int SoulIndex(int gameId)
        {
            for (int i = 0; i < SoulItems.Length; i++)
                if ((int)SoulItems[i] == gameId) return i;
            return -1;
        }

        /// <summary>
        /// True when the location holds one of THIS player's souls (always behind an
        /// AP placeholder). Scout-based, so it works offline too (the offline scout
        /// cache resolves own placeholders). Used to pick the soul icon.
        /// </summary>
        public static bool TryGetOwnSoulAt(LocationID location, out int gameId)
        {
            gameId = -1;
            if (location == LocationID.None) return false;
            var sc = ArchipelagoClientProvider.Client?.GetItemAtLocation(BaseApLocationId + (int)location);
            if (sc == null || !sc.IsOwnItem) return false;
            int id = (int)(sc.ItemId - BaseApItemId);
            if (!IsSoul(id)) return false;
            gameId = id;
            return true;
        }

        public static bool IsObliteratedLocation(LocationID location) =>
            System.Array.IndexOf(ObliteratedLocations, location) >= 0;

        /// <summary>Sheet/flag soul gates must compare against for this seed.</summary>
        public static int GateCountSheet => Enabled ? StateSheet : KillCountSheet;
        public static int GateCountFlag => Enabled ? SoulCountFlag : KillCountFlag;

        // =====================================================================
        // Souls
        // =====================================================================

        /// <summary>Record a received soul and refresh the gate count. Idempotent.</summary>
        public static void MarkReceived(L2System sys, int gameId)
        {
            int idx = SoulIndex(gameId);
            if (idx < 0 || sys == null) return;

            int mask = ReadFlag(sys, SoulMaskFlag) | (1 << idx);
            using (ItemGrantRecursiveGuard.Begin())
                sys.setFlagData(StateSheet, SoulMaskFlag, (short)mask);
            SyncCount(sys);

            Plugin.Log.LogInfo($"[SOULS] Received {SoulNames[idx]} → {PopCount(mask)}/9 souls");
        }

        /// <summary>Number of souls received (bits set in (13,190)).</summary>
        public static int ObtainedCount(L2System sys) =>
            sys == null ? 0 : PopCount(ReadFlag(sys, SoulMaskFlag));

        /// <summary>Keep (13,191) equal to the number of bits in (13,190).</summary>
        public static void SyncCount(L2System sys)
        {
            if (sys == null) return;
            short want = (short)PopCount(ReadFlag(sys, SoulMaskFlag));
            if (ReadFlag(sys, SoulCountFlag) == want) return;
            using (ItemGrantRecursiveGuard.Begin())
                sys.setFlagData(StateSheet, SoulCountFlag, want);
        }

        /// <summary>
        /// Own souls normally ride a per-location AP placeholder; called when
        /// any location's check is reported. Silent: the location's own pickup
        /// dialog already names the soul. Obliterated locations are skipped —
        /// <see cref="Update"/> delivers those with their own presentation.
        /// </summary>
        public static void TryDeliverOwnSoulOnCheck(long apLocation)
        {
            if (!Enabled) return;
            LocationID location = (LocationID)(int)(apLocation - BaseApLocationId);
            if (IsObliteratedLocation(location)) return;

            long apItemId = PersistentInventoryManager.ResolveOwnApItemId(location, apLocation);
            int gameId = (int)(apItemId - BaseApItemId);
            if (apItemId <= BaseApItemId || !IsSoul(gameId)) return;

            MarkReceived(UnityEngine.Object.FindObjectOfType<L2System>(), gameId);
        }

        // =====================================================================
        // Obliterated checks
        // =====================================================================

        /// <summary>
        /// BossKillTracker confirmed a kill: send its Obliterated check and queue
        /// the own item there (if any) for delivery.
        /// </summary>
        public static void OnGuardianKillConfirmed(LocationID guardian)
        {
            if (!Enabled) return;
            if (!KillToObliterated.TryGetValue(guardian, out LocationID oblit)) return;

            Plugin.Log.LogInfo($"[SOULS] {guardian} confirmed → reporting {oblit}");
            CheckManager.NotifyLocation(oblit);
            Enqueue(oblit, silent: false);
        }

        /// <summary>Ask for a restore scan on the next safe frame (scene load, reconnect).</summary>
        public static void RequestScan()
        {
            if (Enabled) _scanQueued = true;
        }

        /// <summary>Drop queued work (disconnect / new slot). Enabled is owned by slot_data loading.</summary>
        public static void Reset()
        {
            _pending.Clear();
            _pendingSet.Clear();
            _attempts.Clear();
            _silent.Clear();
            _scanQueued = false;
        }

        /// <summary>
        /// Per-frame driver, called from Plugin.Update after the safety guards.
        /// Returns true if it did work this frame (Plugin.Update then skips the
        /// AP queue so two item dialogs never stack).
        /// </summary>
        public static bool Update(L2System sys, NewPlayer pl)
        {
            if (!Enabled || sys == null || pl == null) return false;

            if (_scanQueued)
            {
                // Online, scouts read null until the async scout reply lands —
                // that would be misread as "not our item". Wait for it.
                if (ArchipelagoClient.Authenticated && !ArchipelagoClient.ScoutCacheReady)
                    return false;
                _scanQueued = false;
                ScanForMissing(sys);
            }

            if (_pending.Count == 0) return false;

            LocationID oblit = _pending.Dequeue();
            _pendingSet.Remove(oblit);
            bool silent = _silent.Remove(oblit);

            if (!Deliver(sys, pl, oblit, silent))
            {
                int n = _attempts.TryGetValue(oblit, out int a) ? a + 1 : 1;
                _attempts[oblit] = n;
                if (n <= MaxAttempts) Enqueue(oblit, silent);
                else Plugin.Log.LogWarning($"[SOULS] Giving up delivering {oblit} after {MaxAttempts} attempts.");
            }
            else
            {
                _attempts.Remove(oblit);
            }
            return true;
        }

        private static void Enqueue(LocationID oblit, bool silent)
        {
            // Already queued: a live kill may upgrade a restore to presented, but a
            // restore scan (same scene load as the kill) must not silence a live one.
            if (_pendingSet.Contains(oblit))
            {
                if (!silent) _silent.Remove(oblit);
                return;
            }

            if (silent) _silent.Add(oblit); else _silent.Remove(oblit);
            _pendingSet.Add(oblit);
            _pending.Enqueue(oblit);
        }

        /// <summary>
        /// Restore what a rewind took away: Obliterated items whose check is
        /// done but whose delivered bit is clear, and own souls (any location)
        /// whose check is done but whose soul bit is clear. Without the latter,
        /// an own soul behind a placeholder would be lost forever — the
        /// placeholder's virtual sheet-31 flag keeps the chest spent.
        /// </summary>
        private static void ScanForMissing(L2System sys)
        {
            SyncCount(sys);

            var serverChecked = ArchipelagoClient.ServerData?.CheckedLocations;
            bool IsChecked(long ap) =>
                CheckManager.IsLocationReported(ap) || (serverChecked != null && serverChecked.Contains(ap));

            int delivered = ReadFlag(sys, DeliveredMaskFlag);
            for (int i = 0; i < ObliteratedLocations.Length; i++)
            {
                if ((delivered & (1 << i)) != 0) continue;
                if (!IsChecked(BaseApLocationId + (int)ObliteratedLocations[i])) continue;
                Enqueue(ObliteratedLocations[i], silent: true);
            }

            if (serverChecked == null) return;
            int restored = 0;
            foreach (long apLocation in serverChecked)
            {
                LocationID location = (LocationID)(int)(apLocation - BaseApLocationId);
                if (IsObliteratedLocation(location)) continue; // handled above

                long apItemId = PersistentInventoryManager.ResolveOwnApItemId(location, apLocation);
                int gameId = (int)(apItemId - BaseApItemId);
                int idx = SoulIndex(gameId);
                if (idx < 0 || (ReadFlag(sys, SoulMaskFlag) & (1 << idx)) != 0) continue;

                MarkReceived(sys, gameId);
                restored++;
            }
            if (restored > 0)
                Plugin.Log.LogInfo($"[SOULS] Restored {restored} own soul(s) the save state lost.");
        }

        /// <summary>Hand over the own item at an Obliterated location. True when done.</summary>
        private static bool Deliver(L2System sys, NewPlayer pl, LocationID oblit, bool silent)
        {
            int bit = System.Array.IndexOf(ObliteratedLocations, oblit);
            if (bit < 0) return true;
            if ((ReadFlag(sys, DeliveredMaskFlag) & (1 << bit)) != 0) return true;

            long apLocation = BaseApLocationId + (int)oblit;
            long apItemId = PersistentInventoryManager.ResolveOwnApItemId(oblit, apLocation);
            int gameId = (int)(apItemId - BaseApItemId);

            if (apItemId > BaseApItemId)
            {
                if (!ItemGrantStateGuard.IsSafe(sys, pl)) return false;

                string label = ArchipelagoClientProvider.Client?.GetItemAtLocation(apLocation)?.ItemName;
                if (IsSoul(gameId) && string.IsNullOrEmpty(label))
                    label = SoulNames[SoulIndex(gameId)];

                if (GlossaryManager.TryGetBookFlagForItem(gameId, out int _))
                {
                    // Idempotent; CheckManager already delivered it on report.
                    ItemGrantManager.DeliverGlossaryRom(sys, gameId);
                }
                else if (CostumeManager.IsCostume((ItemID)gameId))
                {
                    CostumeManager.MarkReceived(sys, (ItemID)gameId);
                }
                else
                {
                    // Distinct negative backoff key, clear of AP queue indices (>= 0)
                    // and of PersistentInventoryManager's -(loc)-1 keys.
                    int queueIndex = -100000 - (int)oblit;

                    if (!silent) Patches.ItemDialogPatch.PendingDisplayLabel = label;
                    ItemGrantManager.SuppressPresentation = silent;
                    bool granted;
                    try { granted = ItemGrantManager.TryGrantItem(sys, pl, queueIndex, apItemId); }
                    finally { ItemGrantManager.SuppressPresentation = false; }

                    if (!granted || silent || ItemGrantManager.LastGrantUsedPopupOnly)
                        Patches.ItemDialogPatch.PendingDisplayLabel = null;
                    if (!granted) return false;
                }

                Plugin.Log.LogInfo($"[SOULS] Delivered own item {apItemId} ({label}) at {oblit}"
                    + (silent ? " (restore)" : ""));
            }
            else
            {
                Plugin.Log.LogInfo($"[SOULS] {oblit} holds no own item; nothing to deliver.");
            }

            int mask = ReadFlag(sys, DeliveredMaskFlag) | (1 << bit);
            using (ItemGrantRecursiveGuard.Begin())
                sys.setFlagData(StateSheet, DeliveredMaskFlag, (short)mask);
            return true;
        }

        // =====================================================================
        // Soul gate rewrites (vanilla gates; randomized gates use GateCount*)
        // =====================================================================

        /// <summary>
        /// Point the vanilla soul-gate machinery at the soul count. Matches only:
        ///   • FlagWatchers that set an "sgateCheck" flag (3,19..27) — the
        ///     per-gate "(3,0) &gt;= N" watchers in every field;
        ///   • PlayerHitAreas reading (3,0) — the two that arm the AC (16,47)
        ///     and HoM (17,60) gate watchers.
        /// </summary>
        public static void RewriteSceneSoulGates()
        {
            if (!Enabled) return;
            int rewritten = 0;

            foreach (FlagWatcherScript fw in UnityEngine.Object.FindObjectsOfType<FlagWatcherScript>())
            {
                if (fw.ActionFlags == null || fw.CheckFlags == null) continue;
                bool setsGateCheck = false;
                foreach (L2FlagBoxEnd end in fw.ActionFlags)
                    if (end != null && end.seet_no1 == 3 && end.flag_no1 >= 19 && end.flag_no1 <= 27)
                        setsGateCheck = true;
                if (setsGateCheck)
                    rewritten += Redirect(fw.CheckFlags);
            }

            foreach (PlayerHitArea area in UnityEngine.Object.FindObjectsOfType<PlayerHitArea>())
                rewritten += Redirect(area.FlagAwakes);

            if (rewritten > 0)
                Plugin.Log.LogInfo($"[SOULS] Redirected {rewritten} soul-gate check(s) to souls received.");
        }

        private static int Redirect(L2FlagBoxParent[] parents)
        {
            if (parents == null) return 0;
            int n = 0;
            foreach (L2FlagBoxParent p in parents)
            {
                if (p?.BOX == null) continue;
                foreach (L2FlagBox box in p.BOX)
                {
                    if (box != null && box.seet_no1 == KillCountSheet && box.flag_no1 == KillCountFlag)
                    {
                        box.seet_no1 = StateSheet;
                        box.flag_no1 = SoulCountFlag;
                        n++;
                    }
                }
            }
            return n;
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        private static int ReadFlag(L2System sys, int flag)
        {
            short v = 0;
            sys.getFlag(StateSheet, flag, ref v);
            return (ushort)v;
        }

        private static int PopCount(int mask)
        {
            int c = 0;
            for (int i = 0; i < SoulItems.Length; i++)
                if ((mask & (1 << i)) != 0) c++;
            return c;
        }
    }
}
