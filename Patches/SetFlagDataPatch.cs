using HarmonyLib;
using L2Base;
using L2Flag;
using LaMulana2Archipelago.Archipelago;
using LaMulana2Archipelago.Managers;
using LaMulana2Archipelago.Utils;
using System.Collections.Generic;

namespace LaMulana2Archipelago.Patches
{
    public static class VirtualFlagManager
    {
        private static Dictionary<int, short> _virtualFlags = new Dictionary<int, short>();
        // Persistent L2FlagBase instances returned from getFlagBaseObject.
        // L2FlagBox caches flgBaseL on first call, so we must hand back the
        // same object and mutate its flagValue when the virtual flag changes.
        private static Dictionary<int, L2FlagBase> _virtualBases = new Dictionary<int, L2FlagBase>();

        public static short GetFlag(int flagno)
        {
            int itemId = flagno - 105 + ApItemIDs.Placeholder;
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

            if (_virtualFlags.TryGetValue(flagno, out short val))
                return val;

            return 0;
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
            if (seetno == 31 && flagno >= 105)
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
            if (seet_no == 31 && flag_no >= 105)
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

        static void Postfix(int seet_no, int flag_no, short data, short __state)
        {
            if (data > 0)
                Plugin.Log.LogDebug($"[FLAGSET] sheet={seet_no} flag={flag_no} data={data}");

            CheckManager.NotifyNumericFlag(seet_no, flag_no, data);
            DevUI.RecordFlagChange(seet_no, flag_no, __state, data);

            // Guardian-kill state machine: capture the setFlagData(3, guardianN, 4)
            // ankh-used transition so the scene tracker can confirm the kill
            // on field exit.
            BossKillTracker.NotifyFlagSet(seet_no, flag_no, data);
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
        static void Postfix(int seet_no, string name, short data)
        {
            if (data <= 0 || string.IsNullOrEmpty(name)) return;

            CheckManager.NotifyStringFlag(seet_no, name, data);
            DevUI.RecordFlagChangeByName(seet_no, name, data);
            BossKillTracker.NotifyFlagSetByName(seet_no, name, data);
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
            if (seet_no == 31 && flag_no >= 105)
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
            if (seet_no1 == 31 && flag_no1 >= 105)
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
    }
}