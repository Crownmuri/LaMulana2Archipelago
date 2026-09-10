using System;
using HarmonyLib;
using L2Base;
using L2STATUS;
using L2Task;
using UnityEngine;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Quality-of-life: tapping Left or Right three times in quick succession
    /// starts a Gale Fibula dash, without the equipment-menu round trip.
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
        /// <summary>Master switch — <c>Plugin</c> binds this to a BepInEx config entry.</summary>
        internal static bool Enabled = true;

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
            if (!Enabled || SysRef == null || __instance == null)
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
                if (player == null)
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
}
