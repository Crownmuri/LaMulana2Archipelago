using System;
using HarmonyLib;
using L2Base;
using L2Hit;
using L2STATUS;
using L2Task;
using UnityEngine;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// How the Gale Fibula shortcut behaves.  Cycled Off -> Toggle -> Trigger from
    /// the title screen and persisted to the BepInEx config.
    /// </summary>
    internal enum QuickGaleMode
    {
        /// <summary>No shortcut at all — the Fibula is equipped from the menu only.</summary>
        Off = 0,

        /// <summary>
        /// Previous Sub-Weapon + Next Sub-Weapon together equip or remove the Fibula
        /// in place, and it stays that way.  See <see cref="QuickToggleGaleFibulaChordPatch"/>.
        /// </summary>
        Toggle = 1,

        /// <summary>
        /// Tapping Left or Right three times kicks off one dash and takes the Fibula
        /// straight back off.  See <see cref="QuickToggleGaleFibulaPatch"/>.
        /// </summary>
        Trigger = 2,
    }

    /// <summary>
    /// Quality-of-life, <see cref="QuickGaleMode.Trigger"/>: tapping Left or Right
    /// three times in quick succession starts a Gale Fibula dash, without the
    /// equipment-menu round trip.
    ///
    /// The Fibula can't be steered or stopped while it is worn, so the way it is
    /// actually used is: equip it, take one step to kick the dash off, unequip it.
    ///
    /// Shape of it: the triple tap equips "G Band" and refreshes the player's cached
    /// equip state (<c>NewPlayer.checkEquipItem()</c>, which is what sets
    /// <c>dashOn</c> and is normally only called after a menu equip).  From there the
    /// vanilla movement code does the work — <c>moveHorizontal()</c> sets
    /// <c>dashMode</c> the moment there is horizontal input while <c>dashOn</c> is
    /// set.  We then poll for <c>isDash()</c> and take the Fibula straight back off.
    ///
    /// Hooked on <c>Status.Farst()</c>, the same per-frame task that reads the
    /// vanilla weapon-change keys, and gated on the same conditions its input block
    /// sits behind — so menu navigation, the title screen and scripted
    /// (<c>DUMMYINPUT</c>) sequences can't be mistaken for a dash gesture — plus
    /// <c>ONGURDIAN</c> and <see cref="InBossArena"/>, which between them switch the
    /// gesture off for every boss room: the nine guardians, the final boss and the
    /// DLC boss.
    /// </summary>
    [HarmonyPatch(typeof(Status), nameof(Status.Farst))]
    internal static class QuickToggleGaleFibulaPatch
    {
        /// <summary>
        /// Shared master switch for both shortcut shapes — <c>Plugin</c> binds this to
        /// a BepInEx config entry and the title screen cycles it.  This patch acts on
        /// <see cref="QuickGaleMode.Trigger"/>; <see cref="QuickToggleGaleFibulaChordPatch"/>
        /// acts on <see cref="QuickGaleMode.Toggle"/>, so the two can never both be live.
        /// </summary>
        internal static QuickGaleMode Mode = QuickGaleMode.Toggle;

        /// <summary>Item id of the Gale Fibula, as the equip flags key it.</summary>
        private const string GBandId = "G Band";

        private const int TapsToDash = 3;

        /// <summary>
        /// Frame range between consecutive inputs. Adjust if necessary.
        /// </summary>
        private const int TapGapFrames = 12;

        /// <summary>
        /// Safeguard for how long the Fibula stays equipped to trigger the dash.
        /// </summary>
        private const int EquipWindowFrames = 30;

        // sys is `protected L2System` on L2TaskSystemBase with no accessor. Resolved
        // once into a compiled delegate — this runs every frame.
        private static readonly AccessTools.FieldRef<L2TaskSystemBase, L2System> SysRef = ResolveSys();

        // Tap gesture state.
        private static L2KEYS _tapKey = L2KEYS.non;
        private static int _tapCount;
        private static int _lastTapFrame = -1;

        // Temporary-equip state. _armed is only ever true for a band *we* put on.
        private static bool _armed;
        private static int _armedFrame;

        // Memo for InBossArena(), keyed on the scene number it was computed for.
        private static int _bossArenaSceaneNo = int.MinValue;
        private static bool _bossArenaCached;

        private static AccessTools.FieldRef<L2TaskSystemBase, L2System> ResolveSys()
        {
            try
            {
                return AccessTools.FieldRefAccess<L2TaskSystemBase, L2System>("sys");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[DASHTAP] L2TaskSystemBase.sys unavailable, tap-to-dash disabled: " + ex.Message);
                return null;
            }
        }

        static void Prefix(Status __instance)
        {
            // _armed is still serviced in any mode: the window has to be able to close
            // and take the band back off even if the mode were flipped mid-flight.
            if ((Mode != QuickGaleMode.Trigger && !_armed) || SysRef == null || __instance == null)
                return;

            try
            {
                L2System sys = SysRef(__instance);
                if (sys == null)
                    return;

                // Same gate the vanilla weapon-change keys sit behind in this method
                // — not the title screen, and the status bar isn't raised for a menu,
                // a dramatic scene or scripted input — plus the two boss checks below.
                //
                // ONGURDIAN is flagged when the player is fighting a guardian: GurdianStarter
                // sets it on the white-flash transition into an arena and the Finishers
                // clear it on the kill, and vanilla uses it to shut off the Holy Grail
                // warp and the Xelputter for the duration.
                bool dashAllowed = sys.checkSysFlag(SYSTEMFLAG.TITLENOW) == 0
                                && sys.checkSysFlag(SYSTEMFLAG.ONGURDIAN) == 0
                                && !InBossArena(sys)
                                && !sys.checkStatsBarUP();

                // Prevent buffered inputs from triggering the dash when starting a fight.
                if (_armed)
                    UpdateArmedWindow(sys, dashAllowed);

                if (!dashAllowed)
                {
                    ResetTaps();
                    return;
                }

                NewPlayer player = sys.getPlayer();
                if (player == null || Mode != QuickGaleMode.Trigger)
                {
                    ResetTaps();
                    return;
                }

                L2KEYS tapped = TappedDirection(sys);
                if (tapped == L2KEYS.non)
                    return;

                if (tapped == _tapKey && _lastTapFrame >= 0 && Time.frameCount - _lastTapFrame <= TapGapFrames)
                {
                    _tapCount++;
                }
                else
                {
                    // A different direction, or too slow — this tap starts a new run.
                    _tapKey = tapped;
                    _tapCount = 1;
                }
                _lastTapFrame = Time.frameCount;

                if (_tapCount < TapsToDash)
                    return;

                ResetTaps();
                TryStartDash(sys, player);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[DASHTAP] Tap-to-dash failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Which of Left / Right went down this frame, or <c>non</c>.  Both at once
        /// is discarded rather than picked between — it isn't a directional gesture.
        /// </summary>
        private static L2KEYS TappedDirection(L2System sys)
        {
            bool left = sys.getL2Keys(L2KEYS.left, KEYSTATE.DOWN);
            bool right = sys.getL2Keys(L2KEYS.right, KEYSTATE.DOWN);

            if (left == right)
                return L2KEYS.non;

            return left ? L2KEYS.left : L2KEYS.right;
        }

        /// <summary>
        /// True while the player is standing in a boss arena.
        ///
        /// <c>ONGURDIAN</c> alone covers most of it, but not all: it is cleared by the
        /// Finisher on the kill, while the player stays in the room for the reward and
        /// the walk out, and it is never set at all for the DLC boss, whose arena has
        /// no <c>GurdianStarter</c> to set it (the nine guardian arenas and the final
        /// boss all do). 
        ///
        /// <c>SceenNoToFieldID</c> is the game's own scene-number→field-id map, called
        /// exactly as <c>PauseMenu.setFieldName()</c> calls it. Only arenas answer
        /// "fieldBoss" (the nine guardians, and the DLC boss) or "fieldBossL" (the
        /// final boss) — no ordinary field shares those ids — so the pair identifies
        /// every boss room without hardcoding scene numbers.
        /// </summary>
        private static bool InBossArena(L2System sys)
        {
            L2SystemCore core = sys.getL2SystemCore();
            if (core == null)
                return false;

            int sceaneNo = core.SceaneNo;

            // The lookup is a switch returning literals, but this runs every frame and
            // the answer only changes when the field does.
            if (sceaneNo != _bossArenaSceaneNo)
            {
                string field = sys.SceenNoToFieldID(sceaneNo);
                _bossArenaSceaneNo = sceaneNo;
                _bossArenaCached = field == "fieldBoss" || field == "fieldBossL";
            }

            return _bossArenaCached;
        }

        private static void ResetTaps()
        {
            _tapKey = L2KEYS.non;
            _tapCount = 0;
            _lastTapFrame = -1;
        }

        private static void TryStartDash(L2System sys, NewPlayer player)
        {
            // Already wearing it: the player has chosen to dash permanently, so this
            // gesture has nothing to add and must not strip their equip.
            if (sys.isEquipItem(GBandId))
                return;

            if (sys.isHaveItem(GBandId) == 0)
                return;

            // Already dashing — a second gesture would equip and immediately unequip.
            if (player.isDash())
                return;

            // isItemUsable(GBand) == !dashSeal && !dogooOn: refuses under the Eternal
            // Prison equipment seal, and in the Claydoll Suit (which can't dash at all).
            ItemData band = L2SystemCore.getItemData(GBandId);
            if (band == null || !player.isItemUsable(band))
                return;

            sys.equipItem(GBandId, true);

            // dashOn is cached on the player and only refreshed here — the menu makes
            // the same call after an equip.
            player.checkEquipItem();

            _armed = true;
            _armedFrame = Time.frameCount;
        }

        /// <summary>
        /// Takes the Fibula back off once the dash it was equipped for has started,
        /// or once it is clear that no dash is coming.
        /// </summary>
        private static void UpdateArmedWindow(L2System sys, bool dashAllowed)
        {
            NewPlayer player = sys.getPlayer();

            bool dashing = player != null && player.isDash();
            bool expired = Time.frameCount - _armedFrame >= EquipWindowFrames;

            // Losing the gate ends the window too: a menu opening mid-window would
            // otherwise let the player equip the Fibula themselves and have us strip
            // it again on the way out, and a guardian fight starting must take it off
            // rather than wait out the remaining frames.
            if (!dashing && !expired && dashAllowed && player != null)
                return;

            sys.unEquipItem(GBandId);
            if (player != null)
                player.checkEquipItem();

            _armed = false;
        }
    }

    /// <summary>
    /// Quality-of-life, <see cref="QuickGaleMode.Toggle"/>: pressing Previous
    /// Sub-Weapon + Next Sub-Weapon together (<c>L2KEYS.lchange2</c> +
    /// <c>L2KEYS.rchange2</c>) equips or removes the Gale Fibula in place, and leaves
    /// it that way, without opening the equipment menu.
    ///
    /// Same shape as <see cref="QuickToggleClaydollSuitPatch"/>, one row of keys
    /// down: that chord owns the main-weapon change keys, this one owns the
    /// sub-weapon change keys, so both shortcuts can be live in the same run without
    /// colliding.
    ///
    /// Hook point: <c>L2System.slideSubWeapon()</c>.  <c>L2STATUS.Status.Farst()</c>
    /// is its only caller -
    ///
    ///   if (getL2Keys(lchange2, DOWN))      slideSubWeapon(0);
    ///   else if (getL2Keys(rchange2, DOWN)) slideSubWeapon(1);
    ///
    /// - so a prefix there inherits every gate vanilla already applies to the
    /// sub-weapon cycle keys (title screen, menu open, key block, player exists) and
    /// knows which of the two keys fired.  If the *other* key is held at that moment
    /// the press is a chord rather than a cycle: we toggle the Fibula and return false
    /// so the cycle never happens.
    ///
    /// The two keys are almost never pressed on the same frame, so the first half of
    /// the chord normally lands as an ordinary <c>slideSubWeapon</c> that has already
    /// changed the sub-weapon by the time the second half arrives.  Every non-chord
    /// call therefore records the sub-weapon it is about to move away from, and a
    /// chord that follows within <see cref="ComboGraceFrames"/> puts it back.  Outside
    /// that window the earlier press is taken to have been a deliberate change and is
    /// left alone - only the Fibula toggles.
    ///
    /// Eligibility is <c>ItemMenu.IsEquipChengeAbleNow()</c> for a non-fashion item,
    /// which reduces to <c>isItemUsable</c>: false under the Eternal Prison equipment
    /// seal (dashSeal) and inside the Claydoll Suit (dogooOn), which cannot dash - so
    /// the chord can never reach a state the menu could not.  Unlike
    /// <see cref="QuickToggleGaleFibulaPatch"/> there is no boss-arena gate: this is a
    /// deliberate, persistent equip that the menu would allow in the same room, not a
    /// gesture an ordinary movement input could trigger by accident.
    /// </summary>
    [HarmonyPatch(typeof(L2System), nameof(L2System.slideSubWeapon))]
    internal static class QuickToggleGaleFibulaChordPatch
    {
        /// <summary>Item id of the Gale Fibula, as the equip flags key it.</summary>
        private const string GBandId = "G Band";

        /// <summary>
        /// How long after a lone sub-weapon-cycle press the opposite key still counts
        /// as the other half of a chord (and so undoes that cycle).  Same window as
        /// the Claydoll chord, for the same reasons.
        /// </summary>
        private const int ComboGraceFrames = 20;

        /// <summary>
        /// Feedback on the flip, so the gesture is never silent.  Both directions lead
        /// with <c>SeDashStart</c> - the Fibula's own start-of-dash whoosh, which
        /// <c>NewPlayer</c> plays off <c>dashSeStart</c> - to name the thing being
        /// switched, and then say which way it went with the software
        /// activate / deactivate chirps (<c>SoftMenu</c> plays 120 on install and 121
        /// on uninstall).  A refusal gets the equipment menu's error buzz instead.
        /// </summary>
        private const int SeDashStart = 280;
        private const int SeSoftOn = 120;
        private const int SeSoftOff = 121;
        private const int SeRefuse = 91;

        /// <summary>
        /// Adjust the volume of the gesture (dash start sound and software sound).
        /// </summary>
        private const float DashStartVolume = 0.0f;
        private const float SoftToggleVolume = 0.8f;

        // setTurnSmoke() is `protected void` on NewPlayer with no accessor. It is the
        // puff vanilla kicks up alongside SE 280 at the start of a dash, so pairing the
        // two gives the toggle a visual tell as well as an audible one. Resolved once;
        // if it cannot be found the SE alone still carries the feedback.
        private static readonly Action<NewPlayer> TurnSmoke = ResolveTurnSmoke();

        // Sub-weapon the last non-chord press moved away from, and the frame it did so.
        // _pendingFrame < 0 means "nothing to undo".
        private static SUBWEAPON _subBeforePress = SUBWEAPON.NON;
        private static int _pendingFrame = -1;

        private static Action<NewPlayer> ResolveTurnSmoke()
        {
            try
            {
                return AccessTools.MethodDelegate<Action<NewPlayer>>(
                    AccessTools.Method(typeof(NewPlayer), "setTurnSmoke"));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[DASHTOGGLE] NewPlayer.setTurnSmoke unavailable, "
                                    + "toggle feedback is audio only: " + ex.Message);
                return null;
            }
        }

        static bool Prefix(L2System __instance, int slide)
        {
            if (QuickToggleGaleFibulaPatch.Mode != QuickGaleMode.Toggle || __instance == null)
                return true;

            try
            {
                // Status.Farst() calls slide 0 for lchange2 and 1 for rchange2, so the
                // key that produced this call is known without re-reading both.
                L2KEYS otherKey = (slide == 0) ? L2KEYS.rchange2 : L2KEYS.lchange2;

                if (!__instance.getL2Keys(otherKey, KEYSTATE.NORMAL))
                {
                    // Ordinary sub-weapon cycle. Remember what it is about to leave, in
                    // case the opposite key arrives a frame or two from now.
                    _subBeforePress = __instance.getSubWeapon();
                    _pendingFrame = Time.frameCount;
                    return true;
                }

                // Chord. Undo the cycle the first half of it caused, if that was recent.
                if (_pendingFrame >= 0 && Time.frameCount - _pendingFrame <= ComboGraceFrames)
                    __instance.setSubWeapon(_subBeforePress);
                _pendingFrame = -1;

                ToggleFibula(__instance);
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[DASHTOGGLE] Quick toggle failed: " + ex.Message);
                return true;
            }
        }

        private static void ToggleFibula(L2System sys)
        {
            NewPlayer player = sys.getPlayer();
            if (player == null)
                return;

            bool wearing = sys.isEquipItem(GBandId);

            // Nothing to put on if it has not been found yet. Taking it off is allowed
            // regardless, so the player can never be stranded permanently dashing.
            if (!wearing && sys.isHaveItem(GBandId) == 0)
                return;

            // ItemMenu.IsEquipChengeAbleNow() for a non-fashion item is exactly this:
            // isItemUsable(G Band) == !dashSeal && !dogooOn.
            ItemData band = L2SystemCore.getItemData(GBandId);
            if (band == null || !player.isItemUsable(band))
            {
                PlaySE(sys, SeRefuse);
                return;
            }

            if (wearing)
                sys.unEquipItem(GBandId);
            else
                sys.equipItem(GBandId, true);

            // dashOn is cached on the player and only refreshed here - the menu makes
            // the same call after an equip.
            player.checkEquipItem();

            PlaySE(sys, SeDashStart, DashStartVolume);
            PlaySE(sys, wearing ? SeSoftOff : SeSoftOn, SoftToggleVolume);

            // The dust puff only makes sense on the way in, where it is the same cue
            // vanilla pairs with SE 280 at the start of a real dash.
            if (!wearing && TurnSmoke != null)
            {
                try { TurnSmoke(player); } catch { }
            }
        }

        /// <summary>
        /// <paramref name="volume"/> is the per-play scale <c>SoundEffectPlayer</c>
        /// applies on top of the player's own SE volume setting, so ducking here stays
        /// relative to whatever they have the slider at.  Pitch is left at 1: the
        /// manager de-duplicates by (seNo, pitch), and changing it would let a second
        /// press stack a copy of a sound still playing.
        /// </summary>
        private static void PlaySE(L2System sys, int seNo, float volume = 1f)
        {
            try
            {
                L2SystemCore core = sys.getL2SystemCore();
                if (core == null || core.seManager == null)
                    return;

                int handle = core.seManager.playSE(null, seNo, volume, 1f);
                core.seManager.releaseGameObjectFromPlayer(handle);
            }
            catch { }
        }
    }

}
