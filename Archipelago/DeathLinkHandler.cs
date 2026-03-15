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

            // 1. Process incoming deaths if alive and not already waiting to land
            if (deathLinks.Count > 0 && !isWaitingForLanding && !isCurrentlyDead)
            {
                ProcessDeathLinkQueue();
            }

            // 2. Wait for the player to safely land before applying the kill
            if (isWaitingForLanding && !isCurrentlyDead)
            {
                CheckForLanding();
            }

            // 3. Monitor for natural deaths to send out
            SendDeathLink(isCurrentlyDead);
        }

        private void ProcessDeathLinkQueue()
        {
            try
            {
                var player = sys.getPlayer();
                if (player == null) return;

                // Flag the incoming death sequence
                isDyingFromDeathLink = true;
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

                // Execute the visual effect and kill them
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

                    // If we died because of Archipelago, suppress sending it back
                    if (isDyingFromDeathLink)
                    {
                        Plugin.Log.LogInfo("[DeathLink] Died from received DeathLink. Suppressing outgoing link.");
                        return;
                    }

                    Plugin.Log.LogMessage("sharing your death...");
                    service.SendDeathLink(new DeathLink(slotName));
                }
            }
            catch (Exception e) { Plugin.Log.LogError(e); }
        }
    }
}