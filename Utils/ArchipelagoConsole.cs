using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using L2Base;
using LaMulana2Archipelago.Archipelago;
using LaMulana2Archipelago.Managers;
using LM2RandomiserMod;
using UnityEngine;

namespace LaMulana2Archipelago.Utils
{
    // Source: oc2-modding https://github.com/toasterparty/oc2-modding/blob/main/OC2Modding/GameLog.cs
    public static class ArchipelagoConsole
    {
        public static bool Hidden = true;

        /// <summary>
        /// Master visibility toggle, controlled by F11.
        /// When false the entire console (log + buttons) is suppressed.
        /// </summary>
        public static bool Visible = true;

        private static List<string> logLines = new();
        private static Vector2 scrollView;
        private static Rect window;
        private static Rect scroll;
        private static Rect text;
        private static Rect hideShowButton;
        private static Rect warpStartButton;

        private static GUIStyle textStyle = new();
        private static string scrollText = "";
        private static float lastUpdateTime = Time.time;
        private const int MaxLogLines = 80;
        private const float HideTimeout = 15f;

        private static string CommandText = "";
        private static Rect CommandTextRect;
        private static Rect SendCommandButton;

        public static void Awake()
        {
            UpdateWindow();
        }

        public static void LogMessage(string message)
        {
            if (message.IsNullOrWhiteSpace()) return;

            if (logLines.Count == MaxLogLines)
            {
                logLines.RemoveAt(0);
            }
            logLines.Add(message);
            Plugin.Log.LogMessage(message);
            lastUpdateTime = Time.time;
            UpdateWindow();
        }

        public static void OnGUI()
        {
            // F11 master toggle (check in OnGUI so it works even when Update isn't called)
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.F11)
            {
                Visible = !Visible;
                Event.current.Use();
            }

            if (!Visible) return;
            if (logLines.Count == 0) return;

            if (!Hidden || Time.time - lastUpdateTime < HideTimeout)
            {
                scrollView = GUI.BeginScrollView(window, scrollView, scroll);
                GUI.Box(text, "");
                GUI.Box(text, scrollText, textStyle);
                GUI.EndScrollView();
            }

            if (GUI.Button(hideShowButton, Hidden ? "Show" : "Hide"))
            {
                Hidden = !Hidden;
                UpdateWindow();
            }

            if (GUI.Button(warpStartButton, "Warp to Start"))
            {
                WarpToStartingGrail();
            }

            // draw client/server commands entry
            if (Hidden || !ArchipelagoClient.Authenticated) return;

            CommandText = GUI.TextField(CommandTextRect, CommandText);

            bool enterPressed = Event.current.type == EventType.KeyDown
                && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);

            if (!CommandText.IsNullOrWhiteSpace()
                && (GUI.Button(SendCommandButton, "Send") || enterPressed))
            {
                Plugin.ArchipelagoClient.SendMessage(CommandText);
                ArchipelagoConsole.LogMessage($"[sent] {CommandText}");
                CommandText = "";
                if (enterPressed) Event.current.Use();
            }
        }

        public static void UpdateWindow()
        {
            scrollText = "";

            if (Hidden)
            {
                if (logLines.Count > 0)
                {
                    scrollText = logLines[logLines.Count - 1];
                }
            }
            else
            {
                for (var i = 0; i < logLines.Count; i++)
                {
                    scrollText += "> ";
                    scrollText += logLines.ElementAt(i);
                    if (i < logLines.Count - 1)
                    {
                        scrollText += "\n\n";
                    }
                }
            }

            var width = (int)(Screen.width * 0.4f);
            int height;
            int scrollDepth;
            if (Hidden)
            {
                height = (int)(Screen.height * 0.03f);
                scrollDepth = height;
            }
            else
            {
                height = (int)(Screen.height * 0.3f);
                scrollDepth = height * 10;
            }

            window = new Rect(Screen.width / 2 - width / 2, 0, width, height);
            scroll = new Rect(0, 0, width * 0.9f, scrollDepth);
            scrollView = new Vector2(0, scrollDepth);
            text = new Rect(0, 0, width, scrollDepth);

            textStyle.alignment = TextAnchor.LowerLeft;
            textStyle.fontSize = Hidden ? (int)(Screen.height * 0.018f) : (int)(Screen.height * 0.020f);
            textStyle.normal.textColor = Color.white;
            textStyle.wordWrap = !Hidden;

            var xPadding = (int)(Screen.width * 0.01f);
            var yPadding = (int)(Screen.height * 0.01f);

            textStyle.padding = Hidden
                ? new RectOffset(xPadding / 2, xPadding / 2, yPadding / 2, yPadding / 2)
                : new RectOffset(xPadding, xPadding, yPadding, yPadding);

            var buttonWidth = (int)(Screen.width * 0.12f);
            var buttonHeight = (int)(Screen.height * 0.03f);

            hideShowButton = new Rect(Screen.width / 2 + width / 2 + buttonWidth / 3, Screen.height * 0.004f, buttonWidth,
                buttonHeight);

            var buttonGap = (int)(Screen.width * 0.005f);
            warpStartButton = new Rect(hideShowButton.x + buttonWidth + buttonGap, hideShowButton.y, buttonWidth,
                buttonHeight);

            // draw server command text field and button
            width = (int)(Screen.width * 0.4f);
            var xPos = (int)(Screen.width / 2.0f - width / 2.0f);
            var yPos = (int)(Screen.height * 0.307f);
            height = 25;

            CommandTextRect = new Rect(xPos, yPos, width, height);

            var sendWidth = 100;
            yPos += height + 4;
            SendCommandButton = new Rect(xPos, yPos, sendWidth, height);
        }

        private static void WarpToStartingGrail()
        {
            try
            {
                var rando = SceneRandomizer.Instance;
                if (rando == null)
                {
                    LogMessage("[WarpToStart] SceneRandomizer not ready.");
                    return;
                }

                StartInfo startInfo = StartDB.GetStartInfo(rando.StartingArea);
                if (startInfo == null)
                {
                    LogMessage($"[WarpToStart] No StartInfo for area {rando.StartingArea}.");
                    return;
                }

                var sys = UnityEngine.Object.FindObjectOfType<L2System>();
                if (sys == null || sys.getPlayer() == null)
                {
                    LogMessage("[WarpToStart] Game not loaded.");
                    return;
                }

                L2SystemCore sysCore = sys.getL2SystemCore();
                if (sysCore == null)
                {
                    LogMessage("[WarpToStart] L2SystemCore unavailable.");
                    return;
                }

                sysCore.gameScreenFadeOut(10);
                sysCore.setFadeInFlag(true);
                sysCore.setJumpPosition(startInfo.FieldNo, startInfo.AnchorName, true, false);

                LogMessage($"[WarpToStart] Warped to {rando.StartingArea} ({startInfo.AnchorName}).");
            }
            catch (Exception ex)
            {
                LogMessage($"[WarpToStart] Error: {ex.Message}");
            }
        }
    }
}