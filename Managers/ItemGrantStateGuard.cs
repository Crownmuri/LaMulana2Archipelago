using L2Base;

namespace LaMulana2Archipelago.Managers
{
    /// <summary>
    /// Rules are derived from the game's own pickup gate in
    /// AbstractItemBase.groundBack() plus the system-flag checks in
    /// NewPlayer.groundFirst().
    /// </summary>
    public static class ItemGrantStateGuard
    {
        /// <summary>
        /// True from the moment an NPC talk script fires [@exit] (dialog window
        /// closes but the script keeps running) until [@out] ends the session.
        /// Set/cleared by TalkSessionPatch. Must be reset on scene transitions.
        /// </summary>
        public static bool IsPostExitTalkActive = false;

        public static bool IsSafe(L2System sys, NewPlayer pl)
        {
            // --- System flags ---

            // MENUOPEN is set by ItemDialog (item popup), the pause menu, the
            // shop, and any other full-screen menu. This is the primary guard
            // that was missing before.
            if (sys.getSysFlag(SYSTEMFLAG.MENUOPEN) != 0)
            {
            //    Plugin.Log.LogDebug("[GRANT] Blocked: MENUOPEN");
                return false;
            }

            // ITDLRBLOCK is the narrower flag set for the duration of the
            // item-get dialog box itself. Belt-and-suspenders alongside MENUOPEN.
            if (sys.getSysFlag(SYSTEMFLAG.ITDLRBLOCK) != 0)
            {
            //    Plugin.Log.LogDebug("[GRANT] Blocked: ITDLRBLOCK");
                return false;
            }

            // ONSCROLL: room-transition scroll is in progress. NewPlayer bails
            // out of its own update early when this is set.
            if (sys.getSysFlag(SYSTEMFLAG.ONSCROLL) != 0)
            {
            //    Plugin.Log.LogDebug("[GRANT] Blocked: ONSCROLL");
                return false;
            }

            // DRAMATEJI: dramatic/cinematic text sequence. Player is frozen.
            if (sys.getSysFlag(SYSTEMFLAG.DRAMATEJI) != 0)
            {
            //    Plugin.Log.LogDebug("[GRANT] Blocked: DRAMATEJI");
                return false;
            }

            // (SYSTEMFLAG)4128: Other cutscenes
            if (sys.getSysFlag((SYSTEMFLAG)4128) != 0)
            {
            //    Plugin.Log.LogDebug("[GRANT] Blocked: SysFlag 4128");
                return false;
            }

            // Post-[@exit] NPC talk session: the dialog window closed but the NPC
            // script is still running. Player state is WALK and no system flag is set,
            // but the script may be suspended at [@anifla,mnext,wait2] waiting for a
            // button press. Granting here would deadlock (both the NPC wait and the
            // item dialog compete for the same input).
            if (IsPostExitTalkActive)
            {
            //    Plugin.Log.LogDebug("[GRANT] Blocked: IsPostExitTalkActive");
                return false;
            }

            // --- Player state ---
            var sta = pl.getSta();
            switch (sta)
            {
                // Already running a get-item sequence — do not stack another one.
                case NewPlayer.PLAYER_MST.GETITEM:
                case NewPlayer.PLAYER_MST.GETITEM_J:
                //    Plugin.Log.LogDebug("[GRANT] Blocked: player GETITEM/GETITEM_J");
                    return false;

                // Warp / gate transitions — state machine is mid-transition.
                case NewPlayer.PLAYER_MST.WARP_ACTION:
                case NewPlayer.PLAYER_MST.WARP_ACTION_FADE:
                case NewPlayer.PLAYER_MST.GRAILWARP:
                case NewPlayer.PLAYER_MST.GATE:
                //    Plugin.Log.LogDebug($"[GRANT] Blocked: player {sta}");
                    return false;

                // Cutscene / scripted animation modes — player is not in free control.
                case NewPlayer.PLAYER_MST.ANIME_MODE:
                case NewPlayer.PLAYER_MST.UNITY_MODE:
                case NewPlayer.PLAYER_MST.EVENTWAIT:
                //    Plugin.Log.LogDebug($"[GRANT] Blocked: player {sta}");
                    return false;

                // Screen scrolling between rooms (complements ONSCROLL flag).
                case NewPlayer.PLAYER_MST.SCROLL:
                 //   Plugin.Log.LogDebug("[GRANT] Blocked: player SCROLL");
                    return false;

                // Death states — no point granting while dying/dead.
                case NewPlayer.PLAYER_MST.DEAD:
                case NewPlayer.PLAYER_MST.BARRIER_DEAD:
                //    Plugin.Log.LogDebug("[GRANT] Blocked: player DEAD");
                    return false;

                // HANGMAN: hanging from a ceiling hook. Injecting a get-item
                // animation here would break the hang state.
                case NewPlayer.PLAYER_MST.HANGMAN:
                //    Plugin.Log.LogDebug("[GRANT] Blocked: player HANGMAN");
                    return false;
            }

            return true;
        }
    }
}