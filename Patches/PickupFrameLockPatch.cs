using HarmonyLib;
using UnityEngine;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Prevents two ground items from being picked up in quick succession.
    ///
    /// The vanilla game guards against concurrent pickups by checking the player
    /// state in <c>groundBack()</c> — if the player is in GETITEM the collision
    /// check is skipped.  However, <c>setActionOder(PLAYERACTIONODER.getitem)</c>
    /// is deferred: the player state doesn't actually transition to GETITEM until
    /// the next update cycle.  When two items overlap the player, both can pass
    /// the GETITEM gate on consecutive frames before the state catches up, both
    /// fire their full pickup sequence, and the player state machine gets
    /// permanently corrupted (frozen in GETITEM).
    ///
    /// This patch adds a pickup lock: once any <c>AbstractItemBase</c> (or
    /// <c>CostumeSetScript</c>) pickup completes, other items' <c>groundBack()</c>
    /// is blocked for a short grace period.  This bridges the gap until the
    /// deferred <c>setActionOder</c> takes effect and the vanilla GETITEM guard
    /// takes over.  The deferred item stays alive (<c>finished</c> remains false)
    /// and will be picked up naturally once the first item's dialog finishes and
    /// the player returns to a normal state.
    /// </summary>
    internal static class PickupFrameLock
    {
        /// <summary>
        /// The frame number on which the most recent item pickup occurred.
        /// Shared across all groundBack patches so that any item type locks
        /// out all other item types.
        /// </summary>
        internal static int LockedFrame = -1;

        /// <summary>
        /// Number of frames to keep the lock active after a pickup.
        /// Bridges the gap between <c>setActionOder(getitem)</c> being called
        /// and the player state actually transitioning to GETITEM.
        /// After this grace period the vanilla GETITEM check in
        /// <c>groundBack()</c> takes over naturally.
        /// </summary>
        private const int GraceFrames = 60;

        internal static bool IsLocked =>
            LockedFrame >= 0 && (Time.frameCount - LockedFrame) <= GraceFrames;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AbstractItemBase.groundBack()
    //   Covers EventItemScript (which does not override groundBack).
    // ─────────────────────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(AbstractItemBase), nameof(AbstractItemBase.groundBack))]
    internal static class AbstractItemBaseGroundBackPatch
    {
        static bool Prefix(ref bool __result, out bool __state, bool ___finished)
        {
            __state = ___finished;

            // If this item is already collected, let the original return early as
            // it normally would — don't interfere.
            if (___finished)
                return true;

            // Another item completed a pickup recently and the player state hasn't
            // caught up yet → skip collision/pickup logic.  The item stays on the
            // ground and will be picked up on a later frame when the player is ready.
            if (PickupFrameLock.IsLocked)
            {
                __result = true; // match normal groundBack return value
                return false;    // skip original method
            }

            return true;
        }

        static void Postfix(bool __state, bool ___finished)
        {
            // finished transitioned false → true  ⇒  a pickup just happened.
            if (!__state && ___finished)
                PickupFrameLock.LockedFrame = Time.frameCount;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CostumeSetScript.groundBack()
    //   CostumeSetScript overrides groundBack so it needs its own patch.
    // ─────────────────────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(CostumeSetScript), nameof(CostumeSetScript.groundBack))]
    internal static class CostumeSetScriptGroundBackPatch
    {
        static bool Prefix(ref bool __result, out bool __state, bool ___finished)
        {
            __state = ___finished;

            if (___finished)
                return true;

            if (PickupFrameLock.IsLocked)
            {
                __result = true;
                return false;
            }

            return true;
        }

        static void Postfix(bool __state, bool ___finished)
        {
            if (!__state && ___finished)
                PickupFrameLock.LockedFrame = Time.frameCount;
        }
    }
}
