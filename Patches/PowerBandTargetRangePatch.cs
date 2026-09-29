using System;
using HarmonyLib;
using L2Base;
using L2Hit;
using UnityEngine;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Keeps Power Band-homing Mjolnir bolts on the player's current room.
    ///
    /// <c>NewPlayer.setMyorinrAttack()</c> (Power Band equipped) spends charges in
    /// two passes:
    ///
    ///   1. every <c>ENEMY|HIT</c> hitbox that belongs to a <c>GroundCharacter</c>
    ///      and is more than 80 units away (farthest first, up to 3 charges);
    ///   2. only the charges left over go to <c>MyornirTargetScript</c> switches.
    ///
    /// Pass 1 has no range cap and never checks which screen a hitbox is on.
    /// Every task in the loaded field keeps registering hitboxes, so an unlocked
    /// but unopened chest several screens away (e.g. the EPD A-4 Lava Maze chest,
    /// left for later because it needs the Feather) takes charges from a switch
    /// right next to the player, such as the EPD D-4 Top-Left Mjolnir Switch.
    /// Vanilla rarely shows this because players open chests as soon as they
    /// unlock them. The randomizer makes leaving one unlocked common.
    ///
    /// Fix: while <c>setMyorinrAttack()</c> runs, the <c>getHitDatas()</c> result
    /// it reads is swapped for a copy holding only hitboxes of active objects
    /// inside the current view's room (<c>ViewProperty.isInViewRoom</c>). Inactive
    /// objects, such as superseded vanilla chests, are dropped too. The game's own
    /// buffer is never touched, so collision is unaffected.
    /// </summary>
    internal static class PowerBandTargetRange
    {
        /// <summary>
        /// Frame on which <c>setMyorinrAttack()</c> was entered, or -1 when not
        /// inside it. A frame stamp so a missed disarm can't leak past this frame.
        /// </summary>
        private static int _armedFrame = -1;

        internal static void Arm() => _armedFrame = Time.frameCount;

        internal static void Disarm() => _armedFrame = -1;

        internal static bool Armed => _armedFrame == Time.frameCount;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // NewPlayer.setMyorinrAttack()
    //   Arm the filter only for the duration of the Mjolnir release.
    // ─────────────────────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(NewPlayer), "setMyorinrAttack")]
    internal static class NewPlayerSetMyorinrAttackPatch
    {
        static void Prefix() => PowerBandTargetRange.Arm();

        static void Finalizer() => PowerBandTargetRange.Disarm();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // L2System.getHitDatas(uint)
    // ─────────────────────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(L2System), nameof(L2System.getHitDatas))]
    internal static class PowerBandHitDatasFilterPatch
    {
        static void Postfix(L2System __instance, uint element, ref HITBUFFER __result)
        {
            if (!PowerBandTargetRange.Armed)
                return;
            if (element != (HITELEMENT.ENEMY | HITELEMENT.HIT))
                return;

            try
            {
                ViewProperty view = __instance.getL2SystemCore()?.ScrollSystem?.getCurrentView();
                if (view == null || __result.data == null)
                    return;

                HITBUFFER filtered = new HITBUFFER(Math.Max(__result.count, 1));
                for (int i = 0; i < __result.count; i++)
                {
                    HITDATA hd = __result.data[i];
                    if (hd.go == null || !hd.go.activeInHierarchy)
                        continue;
                    Vector2 c = hd.hit.center;
                    if (!view.isInViewRoom(c.x, c.y))
                        continue;
                    filtered.data[filtered.count++] = hd;
                }

                __result = filtered;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[PowerBand] target filter failed, using vanilla targets: {ex.Message}");
            }
        }
    }
}
