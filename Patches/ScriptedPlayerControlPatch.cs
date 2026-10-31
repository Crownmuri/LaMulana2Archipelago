using System.Collections.Generic;
using HarmonyLib;
using L2Base;
using L2Flag;
using UnityEngine;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Blocks AP grants around PlayerActionController cutscenes.
    ///
    /// These cutscenes (e.g. the Valhalla ankh demo, E2_ankh (14,43) 1→2→3)
    /// drive the player with fake key presses: no ANIME_MODE / EVENTWAIT, only
    /// SYSTEMFLAG.DUMMYINPUT while a controller is running. A chain of controllers
    /// (trigger zone → walk to x → hold UP) drops DUMMYINPUT for a frame between
    /// links. A grant landing in that gap starts the item-get pose, the next
    /// controller switches input to fake keys, then the item dialog opens. The
    /// dialog pauses the controller (systemStopFlg includes ITEMDIALOG) and
    /// ignores real input, so neither can finish: softlock.
    ///
    /// Blocked while:
    ///   * any controller is running, plus a short grace after (covers gaps
    ///     between chained controllers);
    ///   * any idle controller's CheckFlags are already true, i.e. it starts on
    ///     its next tick (covers the trigger → first controller gap). Capped by
    ///     ArmedMaxSeconds in case that controller never ticks (out of view).
    /// </summary>
    internal static class ScriptedPlayerControlGuard
    {
        private const int StaIdle = 0;
        private const int StaWait = 1;
        private const int StaAction = 2;

        public const float GraceSeconds = 0.75f;
        public const float ArmedMaxSeconds = 3f;

        private static readonly AccessTools.FieldRef<L2MobTask.MoveCharacterBase, int> StaRef =
            AccessTools.FieldRefAccess<L2MobTask.MoveCharacterBase, int>("sta");

        private static readonly List<PlayerActionController> Live = new List<PlayerActionController>();
        private static readonly Dictionary<PlayerActionController, float> ArmedSince =
            new Dictionary<PlayerActionController, float>();

        private static float _activeUntil;

        internal static void Register(PlayerActionController c)
        {
            if (c == null || c.controllCommands == null || c.controllCommands.Length < 1) return;
            if (!Live.Contains(c)) Live.Add(c);
            ArmedSince.Remove(c);
        }

        internal static void Unregister(PlayerActionController c)
        {
            Live.Remove(c);
            ArmedSince.Remove(c);
        }

        internal static void NoteTick(PlayerActionController c)
        {
            int sta = StaRef(c);
            if (sta == StaWait || sta == StaAction)
                _activeUntil = Time.realtimeSinceStartup + GraceSeconds;
        }

        public static bool IsBlocking(L2System sys)
        {
            float now = Time.realtimeSinceStartup;
            if (now < _activeUntil) return true;

            bool blocking = false;
            for (int i = Live.Count - 1; i >= 0; i--)
            {
                var c = Live[i];
                if (c == null) // destroyed with its scene
                {
                    Live.RemoveAt(i);
                    continue;
                }

                if (StaRef(c) != StaIdle || !sys.checkStartFlag(c.CheckFlags))
                {
                    ArmedSince.Remove(c);
                    continue;
                }

                float since;
                if (!ArmedSince.TryGetValue(c, out since))
                {
                    since = now;
                    ArmedSince[c] = now;
                }
                if (now - since < ArmedMaxSeconds)
                    blocking = true;
            }

            // Drop dictionary entries for destroyed controllers.
            if (ArmedSince.Count > Live.Count)
            {
                var stale = new List<PlayerActionController>();
                foreach (var k in ArmedSince.Keys)
                    if (k == null || !Live.Contains(k)) stale.Add(k);
                foreach (var k in stale) ArmedSince.Remove(k);
            }

            return blocking;
        }
    }

    [HarmonyPatch(typeof(PlayerActionController), nameof(PlayerActionController.Init))]
    internal static class PlayerActionControllerInitPatch
    {
        static void Postfix(PlayerActionController __instance)
        {
            ScriptedPlayerControlGuard.Register(__instance);
        }
    }

    [HarmonyPatch(typeof(PlayerActionController), nameof(PlayerActionController.Final))]
    internal static class PlayerActionControllerFinalPatch
    {
        static void Postfix(PlayerActionController __instance)
        {
            ScriptedPlayerControlGuard.Unregister(__instance);
        }
    }

    /// <summary>
    /// Other half of the same-frame race: the grant runs first, its GETITEM order
    /// is only queued (player state still normal), the trigger zone fires anyway,
    /// and the controller would start on top of the item sequence. Hold an idle
    /// controller until the item sequence is over, the same way the vanilla
    /// trigger zones refuse to fire during GETITEM.
    /// </summary>
    [HarmonyPatch(typeof(PlayerActionController), nameof(PlayerActionController.ActionCharacterFarst))]
    internal static class PlayerActionControllerFarstPatch
    {
        private const int OrderSettleFrames = 3;

        private static readonly AccessTools.FieldRef<L2MobTask.MoveCharacterBase, int> StaRef =
            AccessTools.FieldRefAccess<L2MobTask.MoveCharacterBase, int>("sta");
        private static readonly AccessTools.FieldRef<L2Task.L2TaskSystemBase, L2System> SysRef =
            AccessTools.FieldRefAccess<L2Task.L2TaskSystemBase, L2System>("sys");

        static bool Prefix(PlayerActionController __instance, ref bool __result)
        {
            if (StaRef(__instance) != 0) return true; // only gate the start

            if (Time.frameCount - Managers.ItemGrantManager.LastPresentationFrame <= OrderSettleFrames)
            {
                __result = true;
                return false;
            }

            var sys = SysRef(__instance);
            var pl = sys?.getPlayer();
            if (pl == null) return true;

            var ps = pl.getSta();
            if (ps == NewPlayer.PLAYER_MST.GETITEM || ps == NewPlayer.PLAYER_MST.GETITEM_J
                || sys.getSysFlag(SYSTEMFLAG.ITEMDIALOG) != 0)
            {
                __result = true;
                return false;
            }
            return true;
        }

        static void Postfix(PlayerActionController __instance)
        {
            ScriptedPlayerControlGuard.NoteTick(__instance);
        }
    }
}
