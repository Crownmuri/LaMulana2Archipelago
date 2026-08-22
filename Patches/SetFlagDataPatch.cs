using HarmonyLib;
using L2Base;
using L2Flag;
using LaMulana2Archipelago.Archipelago;
using LaMulana2Archipelago.Managers;
using LaMulana2Archipelago.Utils;
using LaMulana2RandomizerShared;
using System.Collections.Generic;

namespace LaMulana2Archipelago.Patches
{
    public static class VirtualFlagManager
    {
        /// <summary>
        /// Covers both AP placeholders (>= ApItemIDs.FlagOffset, 225) and the two
        /// filler families whose ItemDB flags overflow the sheet — NPCMoney01-10
        /// (200-209) and FakeScan01-15 (210-224).
        /// </summary>
        internal const int FirstVirtualFlag = 200;

        private static Dictionary<int, short> _virtualFlags = new Dictionary<int, short>();
        // Persistent L2FlagBase instances returned from getFlagBaseObject.
        // L2FlagBox caches flgBaseL on first call, so we must hand back the
        // same object and mutate its flagValue when the virtual flag changes.
        private static Dictionary<int, L2FlagBase> _virtualBases = new Dictionary<int, L2FlagBase>();

        public static short GetFlag(int flagno)
        {
            if (flagno >= ApItemIDs.FlagOffset)
            {
                int itemId = flagno - ApItemIDs.FlagOffset + ApItemIDs.Placeholder;
                foreach (var kvp in SeedFlagMapBuilder.LocationToItem)
                {
                    if ((int)kvp.Value == itemId)
                    {
                        long apLoc = 430000 + (long)kvp.Key;
                        // reportedLocations covers the current session; CheckedLocations
                        // is repopulated from the server on connect, so it covers items
                        // collected in previous sessions after a fresh launch.
                        if (CheckManager.IsLocationReported(apLoc)
                            || ArchipelagoClient.ServerData.CheckedLocations.Contains(apLoc))
                            return 1;

                        break;
                    }
                }
            }
            else if (TryResolveOverflowFillerLocation(flagno, out LocationID fillerLoc))
            {
                // NPCMoney / FakeScan container flags (200-224). These have no row in
                // sheet 31 and so are never written to the game save; resolve them from
                // AP check state exactly like placeholder flags, otherwise an NPC would
                // hand out its gift again on every relaunch.
                long apLoc = 430000 + (long)fillerLoc;
                if (CheckManager.IsLocationReported(apLoc)
                    || ArchipelagoClient.ServerData.CheckedLocations.Contains(apLoc))
                    return 1;
            }

            if (_virtualFlags.TryGetValue(flagno, out short val))
                return val;

            return 0;
        }

        /// <summary>
        /// Maps an overflow filler container flag (NPCMoney 200-209 / FakeScan 210-224)
        /// back to the location holding it, using the maps SeedFlagMapBuilder already
        /// builds for exactly these two families.
        /// </summary>
        private static bool TryResolveOverflowFillerLocation(int flagno, out LocationID location)
        {
            if (SeedFlagMapBuilder.NpcMoneyFlagToLocation.TryGetValue(flagno, out location)
                || SeedFlagMapBuilder.FakeScanFlagToLocation.TryGetValue(flagno, out location))
                return location != LocationID.None;

            location = LocationID.None;
            return false;
        }

        public static void SetFlag(int flagno, short data)
        {
            _virtualFlags[flagno] = data;
            // Keep any cached L2FlagBase in sync so L2FlagBox's cached flgBaseL
            // reflects the new value on the next getFlagValueL() call.
            if (_virtualBases.TryGetValue(flagno, out L2FlagBase fb) && fb != null)
                fb.flagValue = data;
        }

        public static L2FlagBase GetOrCreateBase(int flagno)
        {
            if (!_virtualBases.TryGetValue(flagno, out L2FlagBase fb) || fb == null)
            {
                fb = new L2FlagBase("AP_Virtual_Flag_" + flagno);
                _virtualBases[flagno] = fb;
            }
            fb.flagValue = GetFlag(flagno);
            return fb;
        }

        public static void Reset()
        {
            _virtualFlags.Clear();
            _virtualBases.Clear();
        }
    }

    [HarmonyPatch]
    internal static class GetFlagSystemPatch
    {
        static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(L2FlagSystem),
                nameof(L2FlagSystem.getFlag),
                new System.Type[] { typeof(int), typeof(int), typeof(short).MakeByRefType() }
            );
        }

        // Verified: getFlag uses "seetno" and "flagno"
        static bool Prefix(L2FlagSystem __instance, int seetno, int flagno, ref short data, ref bool __result)
        {
            if (seetno == 31 && flagno >= VirtualFlagManager.FirstVirtualFlag)
            {
                data = VirtualFlagManager.GetFlag(flagno);
                __result = true;
                return false;
            }

            // Guard against out-of-range lookups that would otherwise crash
            // inside vanilla cellData[seetno][flagno+1][0][0].
            if (!IsFlagIndexValid(__instance, seetno, flagno))
            {
                data = 0;
                __result = false;
                return false;
            }
            return true;
        }

        internal static bool IsFlagIndexValid(L2FlagSystem sys, int seet_no, int flag_no)
        {
            if (sys == null || seet_no < 0 || flag_no < 0) return false;
            var flagData = sys.flag;
            if (flagData == null || flagData.cellData == null) return false;
            if (seet_no >= flagData.cellData.Length) return false;
            var sheet = flagData.cellData[seet_no];
            if (sheet == null || flag_no + 1 >= sheet.Length) return false;
            var row = sheet[flag_no + 1];
            if (row == null || row.Length == 0 || row[0] == null || row[0].Length == 0 || row[0][0] == null)
                return false;
            return true;
        }
    }

    [HarmonyPatch(
        typeof(L2FlagSystem),
        nameof(L2FlagSystem.setFlagData),
        new System.Type[] { typeof(int), typeof(int), typeof(short) }
    )]
    internal static class SetFlagDataFlagSystemPatch
    {
        // Verified: setFlagData uses "seet_no" and "flag_no"
        static bool Prefix(L2FlagSystem __instance, int seet_no, int flag_no, short data, out short __state)
        {
            __state = 0;
            if (seet_no == 31 && flag_no >= VirtualFlagManager.FirstVirtualFlag)
            {
                VirtualFlagManager.SetFlag(flag_no, data);
                return false;
            }

            // Capture prior value for diff reporting, but guard against out-of-range
            // indices that would otherwise crash inside vanilla cellData access.
            if (GetFlagSystemPatch.IsFlagIndexValid(__instance, seet_no, flag_no))
            {
                try { __instance.getFlag(seet_no, flag_no, ref __state); }
                catch { __state = 0; }
            }
            return true;
        }

        static void Postfix(L2FlagSystem __instance, int seet_no, int flag_no, short data, short __state)
        {
            if (data > 0)
                Plugin.Log.LogDebug($"[FLAGSET] sheet={seet_no} flag={flag_no} data={data}");

            CheckManager.NotifyNumericFlag(seet_no, flag_no, data);
            DevUI.RecordFlagChange(seet_no, flag_no, __state, data);

            // Guardian-kill state machine: capture the setFlagData(3, guardianN, 4)
            // ankh-used transition so the scene tracker can confirm the kill
            // on field exit.
            BossKillTracker.NotifyFlagSet(seet_no, flag_no, data);

            // DLC-boss goal: fires CLIENT_GOAL when the post-fight DLC boss
            // dialogue sets (25,59)>=2. Filters internally + gated on slot_data.
            DlcBossGoalTracker.NotifyFlagSet(seet_no, flag_no, data);

            // Glossary-hunt goal: counts sheet-20 unlocks of shuffled entries.
            // Filters internally + gated on slot_data goal==glossary_hunt.
            GlossaryGoalTracker.NotifyFlagSet(seet_no, flag_no, data);

            // Natural-dissonance count → PopTracker datastorage. Filters
            // internally to flag [2,3] and to random_dissonance==false seeds.
            DissonanceTracker.NotifyFlagSet(seet_no, flag_no, data);

            // Rebirth Seal (2,55) obtained → advance DLC story flag (25,5) to 4.
            // Filters internally to the seal flag.
            RebirthSigilFlagSync.OnNumericFlagWrite(__instance, seet_no, flag_no);
        }
    }

    [HarmonyPatch(
        typeof(L2FlagSystem),
        nameof(L2FlagSystem.setFlagData),
        new System.Type[] { typeof(int), typeof(string), typeof(short) }
    )]
    internal static class SetFlagDataFlagSystemStringPatch
    {
        // Verified: setFlagData(string) uses "seet_no"
        static void Postfix(L2FlagSystem __instance, int seet_no, string name, short data)
        {
            if (data <= 0 || string.IsNullOrEmpty(name)) return;

            CheckManager.NotifyStringFlag(seet_no, name, data);
            DevUI.RecordFlagChangeByName(seet_no, name, data);
            BossKillTracker.NotifyFlagSetByName(seet_no, name, data);

            // Rebirth Seal ("02Items"/"Rebirth Seal") obtained via sys.setItem
            // (AP grant / shop) → advance DLC story flag (25,5) to 4.
            RebirthSigilFlagSync.OnNamedFlagWrite(__instance, seet_no, name);
        }
    }
    [HarmonyPatch] // MUST be empty because we use TargetMethod below
    internal static class GetFlagBaseObjectPatch
    {
        static System.Reflection.MethodBase TargetMethod()
        {
            // Verified: The third parameter is "out L2FlagBase", which is L2FlagBase.MakeByRefType()
            return AccessTools.Method(
                typeof(L2FlagSystem),
                nameof(L2FlagSystem.getFlagBaseObject),
                new System.Type[] { typeof(int), typeof(int), typeof(L2FlagBase).MakeByRefType() }
            );
        }

        // Verified parameter names in L2FlagSystem.cs: seet_no, flag_no
        static bool Prefix(L2FlagSystem __instance, int seet_no, int flag_no, ref L2FlagBase flgBase, ref bool __result)
        {
            if (seet_no == 31 && flag_no >= VirtualFlagManager.FirstVirtualFlag)
            {
                flgBase = VirtualFlagManager.GetOrCreateBase(flag_no);
                __result = true;
                return false;
            }

            // Guard: any out-of-range index would crash inside vanilla cellData[][].
            // Also guard against hdb lookup returning null (NRE at callsite).
            if (!GetFlagSystemPatch.IsFlagIndexValid(__instance, seet_no, flag_no))
            {
                flgBase = new L2FlagBase("AP_OOR_Stub_" + seet_no + "_" + flag_no);
                flgBase.flagValue = 0;
                __result = true;
                Plugin.Log.LogWarning($"[FlagGuard] Out-of-range getFlagBaseObject sheet={seet_no} flag={flag_no} — returning stub");
                return false;
            }

            // Pre-resolve to avoid NRE: vanilla hdb.getFlagBaseObject may return null,
            // and caller (L2FlagBox.getFlagValueL) dereferences flgBase.flagValue unconditionally.
            try
            {
                var sheet = __instance.flag.cellData[seet_no];
                string name = sheet[flag_no + 1][0][0];
                var hdbEntry = __instance.hdb[seet_no].getFlagBaseObject(name);
                if (hdbEntry == null)
                {
                    flgBase = new L2FlagBase("AP_Missing_Stub_" + seet_no + "_" + flag_no);
                    flgBase.flagValue = 0;
                    __result = true;
                    Plugin.Log.LogWarning($"[FlagGuard] hdb returned null for sheet={seet_no} flag={flag_no} name='{name}' — returning stub");
                    return false;
                }
                flgBase = hdbEntry;
                __result = true;
                return false;
            }
            catch (System.Exception ex)
            {
                flgBase = new L2FlagBase("AP_Err_Stub_" + seet_no + "_" + flag_no);
                flgBase.flagValue = 0;
                __result = true;
                Plugin.Log.LogWarning($"[FlagGuard] Exception resolving sheet={seet_no} flag={flag_no}: {ex.Message}");
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(L2FlagSystem), nameof(L2FlagSystem.addFlag), new[] { typeof(int), typeof(int), typeof(short), typeof(CALCU) })]
    internal static class AddFlagNumericPatch
    {
        // Verified parameter names in L2FlagSystem.cs: seet_no1, flag_no1
        static bool Prefix(int seet_no1, int flag_no1, short value, CALCU cul)
        {
            if (seet_no1 == 31 && flag_no1 >= VirtualFlagManager.FirstVirtualFlag)
            {
                short current = VirtualFlagManager.GetFlag(flag_no1);
                short nextValue = current;

                switch (cul)
                {
                    case CALCU.EQR: nextValue = value; break;
                    case CALCU.ADD: nextValue = (short)(current + value); break;
                    case CALCU.SUB: nextValue = (short)(current - value); break;
                }

                VirtualFlagManager.SetFlag(flag_no1, nextValue);
                CheckManager.NotifyNumericFlag(seet_no1, flag_no1, nextValue);
                return false;
            }
            return true;
        }

        // In-game item pickups apply their get-flags through setEffectFlag →
        // addFlag (never setFlagData), so the Rebirth Seal (2,55) write from a
        // physical chest/pot lands here. Runs after vanilla applied the value.
        static void Postfix(L2FlagSystem __instance, int seet_no1, int flag_no1)
        {
            RebirthSigilFlagSync.OnNumericFlagWrite(__instance, seet_no1, flag_no1);
        }
    }
}