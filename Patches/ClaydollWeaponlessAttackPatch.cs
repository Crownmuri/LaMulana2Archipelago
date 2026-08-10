using System;
using HarmonyLib;
using L2Hit;
using UnityEngine;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Restores the melee attack's normal combat properties when Lumisa wears the
    /// Claydoll Suit while owning no main weapon (only reachable in a randomised
    /// game — vanilla can never take your last whip away).
    ///
    /// The Claydoll Suit replaces the main attack with the punch, and the game
    /// implements that by coercing the *equipped weapon* to <c>LWHIP</c> in two
    /// places:
    ///
    ///   NewPlayer.setAttackFlag()      — <c>if (dogooOn &amp;&amp; flag != NON) flag = LWHIP;</c>
    ///   NewPlayer.setMainAttackAnime() — <c>if (dogooOn) mainweapon = LWHIP;</c>
    ///
    /// The animation side is unconditional, but the flag side is guarded on
    /// <c>flag != NON</c>.  With no weapon equipped the only caller,
    /// <c>checkKeyActions()</c>, passes <c>sys.getMainWeapon()</c> == <c>NON</c>,
    /// so the guard rejects it and <c>attackflag</c> stays <c>NON</c> for the whole
    /// punch: the animation plays, but the game never believes an attack is
    /// running.  Everything that keys off <c>attackflag</c> then misbehaves:
    ///
    ///   • <c>isAttackMoveStop()</c> returns false, so walking isn't locked out
    ///     during the swing (she can attack while moving).
    ///   • <c>isMainAttack()</c> returns false, so the jump / ladder / turn gates
    ///     that normally wait for the swing to finish let the player cancel it.
    ///   • In the air it cancels the punch outright: <c>inAirAnimation()</c> pushes
    ///     the jump pose at <c>PLAYER_ANIMETION_LEVEL.LEG</c>, and
    ///     <c>PlayerAnimetion.setAnime()</c> only spares the head/body sprites
    ///     (<c>set1</c>/<c>set2</c>) from a LEG-level animation when
    ///     <c>isMainAttack() || isSubAttack()</c>.  With the flag unset the punch
    ///     animation — and its hitbox — is overwritten the same frame it starts.
    ///
    /// Fix: when <c>checkKeyActions()</c> starts a main attack and the suit is on,
    /// substitute <c>LWHIP</c> for the missing weapon, exactly as vanilla would
    /// have done had any weapon been equipped.  Damage is unaffected —
    /// <c>NewPlayer.getMainWeaponDamageValue()</c> short-circuits to
    /// <c>DMG_Punch</c> whenever <c>dogooOn</c> is set, and the hitbox reads
    /// <c>sys.getMainWeapon()</c> rather than <c>attackflag</c>.
    ///
    /// The substitution is scoped to the <c>checkKeyActions()</c> call so that the
    /// many <c>setAttackFlag(NON)</c> calls that *end* an attack keep working.
    /// </summary>
    internal static class ClaydollWeaponlessAttack
    {
        /// <summary>
        /// Frame on which <c>checkKeyActions()</c> was entered, or -1 when we are
        /// not inside it.  A frame stamp rather than a plain bool so that a missed
        /// un-arm can never leak past the current frame and turn attack-clearing
        /// calls into attack-starting ones (which would freeze the player in an
        /// attack state permanently).
        /// </summary>
        private static int _armedFrame = -1;

        internal static void Arm() => _armedFrame = Time.frameCount;

        internal static void Disarm() => _armedFrame = -1;

        internal static bool Armed => _armedFrame == Time.frameCount;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // NewPlayer.checkKeyActions(bool)
    //   The sole caller that starts a main attack.  Arm the substitution only
    //   for the duration of this call.
    // ─────────────────────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(NewPlayer), "checkKeyActions", new Type[] { typeof(bool) })]
    internal static class NewPlayerCheckKeyActionsPatch
    {
        static void Prefix() => ClaydollWeaponlessAttack.Arm();

        // Finalizer rather than Postfix: it also runs if the original throws, so
        // the substitution can't stay armed past the call.
        static void Finalizer() => ClaydollWeaponlessAttack.Disarm();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // NewPlayer.setAttackFlag(MAINWEAPON)
    // ─────────────────────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(NewPlayer), nameof(NewPlayer.setAttackFlag))]
    internal static class NewPlayerSetAttackFlagPatch
    {
        static void Prefix(NewPlayer __instance, ref MAINWEAPON flag)
        {
            // Only while checkKeyActions() is starting an attack.
            if (!ClaydollWeaponlessAttack.Armed)
                return;

            // Any real weapon already takes the vanilla dogoo coercion below.
            if (flag != MAINWEAPON.NON)
                return;

            // No weapon equipped — only meaningful with the suit on.  isDogooOn()
            // is already false while the Claydoll Suit is sealed (dogoSeal).
            if (__instance == null || !__instance.isDogooOn())
                return;

            // Same value vanilla's own `dogooOn` branch in setAttackFlag() assigns.
            flag = MAINWEAPON.LWHIP;
        }
    }
}
