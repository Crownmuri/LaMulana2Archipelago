using System;
using System.Collections.Generic;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using BepInEx;
using L2Base;
using UnityEngine;

namespace LaMulana2Archipelago.Archipelago
{
    public class DeathLinkHandler
    {
        private static bool deathLinkEnabled;
        private string slotName;
        private readonly DeathLinkService service;
        private readonly Queue<DeathLink> deathLinks = new Queue<DeathLink>();
        private L2System sys;

        // Use static variables to prevent 6x logs and ping-pong loops globally
        private static bool wasDeadLastFrame = false;
        private static bool isDyingFromDeathLink = false;
        private bool isWaitingForLanding = false;

        public DeathLinkHandler(DeathLinkService deathLinkService, string name, bool enableDeathLink = false)
        {
            service = deathLinkService;
            service.OnDeathLinkReceived += DeathLinkReceived;
            slotName = name;
            deathLinkEnabled = enableDeathLink;

            if (deathLinkEnabled) service.EnableDeathLink();
        }

        public void ToggleDeathLink()
        {
            deathLinkEnabled = !deathLinkEnabled;
            if (deathLinkEnabled) service.EnableDeathLink();
            else service.DisableDeathLink();
        }

        public bool IsEnabled => deathLinkEnabled;

        /// <summary>
        /// Reliable reset point for the death-edge state, called on every scene load.
        /// A death always reloads the field, but DeathLinkHandler.Update() is gated
        /// behind ItemGrantStateGuard.IsSafe() and does NOT run during the death /
        /// transition, so its resurrection-based reset can never observe the dead
        /// frame. That left isDyingFromDeathLink armed after a received-DeathLink kill
        /// and made it swallow the player's next genuine death. Clearing here, when the
        /// player has respawned on the freshly loaded field, bounds the suppression
        /// flag to exactly the kill it was armed for.
        /// </summary>
        public void NotifySceneLoaded()
        {
            isDyingFromDeathLink = false;
            wasDeadLastFrame = false;
            isWaitingForLanding = false;
        }

        private void DeathLinkReceived(DeathLink deathLink)
        {
            deathLinks.Enqueue(deathLink);
            Plugin.Log.LogDebug(string.IsNullOrEmpty(deathLink.Cause) ? "Received Death Link from: " + deathLink.Source : deathLink.Cause);
        }

        public void Update()
        {
            if (sys == null) sys = UnityEngine.Object.FindObjectOfType<L2System>();
            if (sys == null) return;

            bool isCurrentlyDead = sys.getPlayerHP() <= 0;

            // Reset global locks when the player resurrects
            if (!isCurrentlyDead && wasDeadLastFrame)
            {
                wasDeadLastFrame = false;
                isDyingFromDeathLink = false;
            }

            // Hold incoming deaths across the guardian-finish window (boss-death →
            // fanfare → auto-return → post-kill save) but NOT during the live fight.
            // Applying a death in this window loops (saved at 0 HP), reverts to the
            // pre-fight checkpoint, or bounces a recursive DeathLink across the
            // auto-return load. DUMMYINPUT covers the fanfare; BossKillTracker the rest.
            bool guardianKillInProgress =
                sys.getSysFlag(SYSTEMFLAG.DUMMYINPUT) != 0 ||
                LaMulana2Archipelago.Managers.BossKillTracker.IsGuardianFinishInProgress;

            // 1. Process incoming deaths if alive and not already waiting to land
            if (deathLinks.Count > 0 && !isWaitingForLanding && !isCurrentlyDead && !guardianKillInProgress)
            {
                ProcessDeathLinkQueue();
            }

            // 2. Wait for the player to safely land before applying the kill
            if (isWaitingForLanding && !isCurrentlyDead && !guardianKillInProgress)
            {
                CheckForLanding();
            }

            // 3. Monitor for natural deaths to send out. Suppressed during the window:
            // a death detected there is the held kill or a transient HP read across the
            // auto-return load, never a fresh natural death — sending bounces a loop.
            SendDeathLink(isCurrentlyDead && !guardianKillInProgress);
        }

        private void ProcessDeathLinkQueue()
        {
            try
            {
                var player = sys.getPlayer();
                if (player == null) return;

                // Begin the incoming death sequence. The suppression flag is armed
                // later, at the moment we actually apply the kill (see CheckForLanding),
                // so it stays tightly scoped to the death we cause and can't be consumed
                // by an unrelated natural death while we wait to land.
                isWaitingForLanding = true;

                var sta = player.getPlayerSta();

                // If the player is on a ladder, stairs, or grappling, detach them.
                if (sta == NewPlayer.PLAYER_MST.LADDER ||
                    sta == NewPlayer.PLAYER_MST.LADDERTOP ||
                    sta == NewPlayer.PLAYER_MST.LADDER_TOP_WAIT ||
                    sta == NewPlayer.PLAYER_MST.STAIRS ||
                    sta == NewPlayer.PLAYER_MST.GLAP ||
                    sta == NewPlayer.PLAYER_MST.GLAPJUMP ||
                    sta == NewPlayer.PLAYER_MST.GLAPSLID ||
                    sta == NewPlayer.PLAYER_MST.GLAPWAIT)
                {
                    // Forces the player into a falling state, unhooking them from the wall/ladder
                    player.setSta(NewPlayer.PLAYER_MST.DOWN);
                    Plugin.Log.LogInfo("[DeathLink] Detaching player from wall/ladder, waiting for landing...");
                }
                else
                {
                    Plugin.Log.LogInfo("[DeathLink] Waiting for valid grounded state to apply death...");
                }
            }
            catch (Exception e) { Plugin.Log.LogError(e); }
        }

        private void CheckForLanding()
        {
            var player = sys.getPlayer();
            if (player == null) return;

            var sta = player.getPlayerSta();

            // Check if the player is in a grounded state where it's safe to die
            bool isGrounded = (sta == NewPlayer.PLAYER_MST.WALK ||
                               sta == NewPlayer.PLAYER_MST.SLIDE ||
                               sta == NewPlayer.PLAYER_MST.SLIP ||
                               sta == NewPlayer.PLAYER_MST.DROPDOWN);

            // If they have hit the ground safely
            if (isGrounded)
            {
                isWaitingForLanding = false;

                // Pop the message off the queue and announce it
                var deathLink = deathLinks.Dequeue();
                Plugin.Log.LogMessage(string.IsNullOrEmpty(deathLink.Cause) ? GetDeathLinkCause(deathLink) : deathLink.Cause);

                // Arm suppression for exactly this kill, then execute the visual
                // effect and kill them. SendDeathLink consumes the flag on the
                // resulting death edge so it never leaks into a later death.
                isDyingFromDeathLink = true;

                player.setFullMantraEfx();
                sys.setPLayerHP(-65535);

                Plugin.Log.LogInfo("[DeathLink] Player landed. Executing DeathLink kill.");
            }
        }

        private string GetDeathLinkCause(DeathLink deathLink) => "Received death from " + deathLink.Source;

        public void SendDeathLink(bool isCurrentlyDead)
        {
            try
            {
                if (!deathLinkEnabled) return;

                // Only send if health just dropped to 0 this exact frame
                if (isCurrentlyDead && !wasDeadLastFrame)
                {
                    wasDeadLastFrame = true;

                    // If we died because of Archipelago, suppress sending it back.
                    // Consume the flag immediately so it can only ever suppress the
                    // single death it was armed for. Relying on the resurrection
                    // reset alone is fragile (LM2's death/respawn/reload can skip the
                    // dead->alive HP edge), which would leave this flag lingering and
                    // wrongly swallow the player's next genuine death.
                    if (isDyingFromDeathLink)
                    {
                        isDyingFromDeathLink = false;
                        Plugin.Log.LogInfo("[DeathLink] Died from received DeathLink. Suppressing outgoing link.");
                        return;
                    }

                    // AP convention: Cause is a full sentence including the player's
                    // name, shown verbatim by other clients. Without one they fall
                    // back to their generic "Died a generic (unknown) death" text.
                    string cause = slotName + " suffered divine punishment.";
                    Plugin.Log.LogMessage("sharing your death...");
                    service.SendDeathLink(new DeathLink(slotName, cause));
                }
            }
            catch (Exception e) { Plugin.Log.LogError(e); }
        }
    }
}