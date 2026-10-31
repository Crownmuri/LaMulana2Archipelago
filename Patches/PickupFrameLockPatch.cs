using System.Collections.Generic;
using HarmonyLib;
using L2Base;
using LaMulana2Archipelago.Managers;
using LaMulana2RandomizerShared;
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
        // Sheet-20 book flags of NPC chips whose talk script can start a cutscene,
        // each with a check for "this talk is about to start it". When the check is
        // false the chip behaves like any other NPC glossary drop.
        private static readonly Dictionary<int, System.Func<L2System, bool>> CutsceneNpcChipFlags =
            new Dictionary<int, System.Func<L2System, bool>>
        {
            // Brihaspathi (talk50, talk sheet f10-3). Only the "2nd" page starts
            // the Vritra door cutscene: firstTalk picks it when Vritra (3,11) <= 3
            // and Roki3 (14,30) > 1, and it runs [@setf,14,12,=,1] (D2_slide).
            // In field10 a FlagWatcher then sets D2_event (14,47)=1, and a
            // PlayerActionController plays the scene and sets (14,47)=2 when done.
            { 198, BrihaspathiDoorCutscenePending },
        };

        // True from "this talk will start the door cutscene" until it has finished.
        private static bool BrihaspathiDoorCutscenePending(L2System sys)
        {
            short vritra = 0, roki3 = 0, d2Event = 0;
            sys.getFlag(3, 11, ref vritra);
            sys.getFlag(14, 30, ref roki3);
            sys.getFlag(14, 47, ref d2Event);
            return vritra <= 3 && roki3 > 1 && d2Event < 2;
        }

        static bool Prefix(AbstractItemBase __instance, ref bool __result, out bool __state,
                           bool ___finished, L2System ___sys, NewPlayer ___pl)
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

            // Glossanity chips: MonsterChipGlossaryPatch turns the vanilla floating
            // popup into a full hold-up + item dialog, but vanilla groundBack only
            // gates on sysflag 4128 / GETITEM. An NPC chip (e.g. Brihaspathi's) can be
            // grabbed while that NPC's talk script is kicking off a cutscene (Vritra
            // door) → dialog and cutscene both hold player control → softlock. Leave
            // the chip on the ground until the player is back in free control.
            // Only for CutsceneNpcChipFlags, and only while that NPC's cutscene is
            // still pending: IsSafe includes the post-talk grace window, which would
            // needlessly delay the chip on every other talk.
            if (__instance is MonsterChipScript chip
                && GlossaryManager.Enabled
                && ___sys != null && ___pl != null
                && CutsceneNpcChipFlags.TryGetValue(MonsterChipGlossaryPatch.GetChipBookFlag(chip), out var cutscenePending)
                && cutscenePending(___sys)
                && MonsterChipGlossaryPatch.TryGetChipLocation(chip, out LocationID locId)
                && !GlossaryManager.IsLocationCollected(locId)
                && !ItemGrantStateGuard.IsSafe(___sys, ___pl))
            {
                __result = true;
                return false;
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
