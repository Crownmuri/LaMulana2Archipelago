using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using LaMulana2Archipelago.Archipelago;
using LaMulana2Archipelago.Managers;
using LaMulana2Archipelago.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;
using L2Base;
using System.Collections;

namespace LaMulana2Archipelago
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.Crownmuri.Archipelago.LaMulana2";
        public const string PluginName = "LaMulana2Archipelago";
        public const string PluginVersion = "0.7.4";

        public const string ModDisplayInfo = $"{PluginName} v{PluginVersion}";
        private const string APDisplayInfo = $"Archipelago v{ArchipelagoClient.APVersion}";

        private Font guiFont;
        private GUIStyle guiStyle;
        private bool onTitle;

        internal static ManualLogSource Log;
        internal static ArchipelagoClient ArchipelagoClient;

        // Persisted connection fields — written on successful Connect click,
        // reloaded into the GUI on the next launch. Password is intentionally
        // not persisted (kept in memory only).
        private static ConfigEntry<string> _cfgHost;
        private static ConfigEntry<string> _cfgSlotName;

        private Harmony _harmony;
        private L2System _cachedSys;
        private DevUI _devUI;

        // ============================================================
        // Receive/grant gating (computed, not scene-name based)
        // ============================================================

        /// <summary>
        /// True while we are in an actual loaded run (player exists, not Title).
        /// This will turn off again if we return to Title / lose player context.
        /// </summary>
        private bool gameplayActive = false;

        /// <summary>
        /// Realtime timestamp recorded when gameplay first becomes active.
        /// Item grants are gated behind a short startup delay so that all
        /// game objects have fully initialised before we attempt any grants.
        /// </summary>
        private float gameplayActivationTime = float.MaxValue;

        /// <summary>
        /// Seconds to wait after gameplay becomes active before granting items.
        /// Gives the game time to finish its initialisation sequence.
        /// </summary>
        private const float GameplayStartupDelay = 2.5f;

        private const string GoalSceneName = "Ending1";
        private const int GoalSceneBuildIndex = 48;

        // Backup trigger: if the CLIENT_GOAL send at Ending1 failed (e.g.
        // socket silently closed), retry on Ending2 — kicking a reconnect
        // if we're no longer authenticated.
        private const string GoalSceneFallback = "Ending2";
        private const int GoalSceneFallbackBuildIndex = 49;

        private bool _bootstrapStarted = false;
        private bool _bootstrapFinished = false;

        private void Start()
        {
            if (_bootstrapStarted)
                return;

            _bootstrapStarted = true;
            StartCoroutine(BootstrapRoutine());
        }

        private IEnumerator BootstrapRoutine()
        {
            // Wait until the real first playable bootstrap scene appears.
            while (SceneManager.GetActiveScene().name != "Opening")
                yield return null;

            // Give Opening one extra frame to finish settling.
            yield return null;

            var sys = UnityEngine.Object.FindObjectOfType<L2Base.L2System>();
            if (sys != null)
            {
                Managers.PrefabHarvester.StartHarvest(sys);

                // Wait until the harvester finishes before connecting.
                while (!Managers.PrefabHarvester.HasHarvested)
                    yield return null;
            }

            // Connection is player-initiated from the title screen — no auto-connect
            // on startup. This avoids a spurious localhost:38281 attempt (and its
            // noisy retry logs) for players who are launching into offline play or
            // who host their server somewhere other than the default.

            _bootstrapFinished = true;
        }

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo($"{ModDisplayInfo} initializing");

            // net35's default SecurityProtocol is Ssl3|Tls (TLS 1.0). Modern wss://
            // servers (Let's Encrypt, Cloudflare, nginx defaults) require TLS 1.2+.
            // SecurityProtocolType.Tls12 doesn't exist as a named member in net35,
            // so the numeric value 3072 is used directly.
            try { System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072; }
            catch (System.Exception e) { Log.LogWarning($"[AP] Could not enable TLS 1.2: {e.Message}"); }

            _cfgHost = Config.Bind("Connection", "Host", "localhost:38281",
                "Archipelago server host and port. Remembered between sessions.");
            _cfgSlotName = Config.Bind("Connection", "SlotName", "Lumisa",
                "Slot (player) name used when connecting. Remembered between sessions.");

            ArchipelagoClient = new ArchipelagoClient();
            ArchipelagoClient.ServerData.Uri = _cfgHost.Value;
            ArchipelagoClient.ServerData.SlotName = _cfgSlotName.Value;
            ArchipelagoClientProvider.Client = ArchipelagoClient;

            ArchipelagoConsole.Awake();
            ArchipelagoConsole.LogMessage($"{ModDisplayInfo} loaded");

            var wsType = System.Type.GetType("WebSocketSharp.WebSocket, websocket-sharp");
            Plugin.Log.LogInfo($"[AP] websocket-sharp loaded from: {wsType?.Assembly.Location ?? "NOT FOUND"}");

            ApSpriteLoader.Load(System.IO.Path.GetDirectoryName(Info.Location));

            _harmony = new Harmony(PluginGUID);
            _harmony.PatchAll();
            Log.LogInfo("Harmony patches applied");

            // Flag map is populated on demand — from AP slot_data on successful
            // Connect, or from seed.lm2r when the player clicks "Load seed.lm2r
            // (offline)". AP-only players never need to touch seed.lm2r at all.

            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;

            Log.LogInfo("Unpatching Harmony");
            _harmony?.UnpatchSelf();
        }

        private static bool IsTitleContext(L2System sys)
        {
            if (sys == null) return true;

            var core = sys.getL2SystemCore();
            if (core == null) return true;

            // L2System.getNowSceneName
            return core.SceaneNo == 44;
        }

        private bool UpdateGameplayActive(L2System sys)
        {
            bool nowActive = sys != null && sys.getPlayer() != null && !IsTitleContext(sys);

            if (nowActive && !gameplayActive)
            {
                gameplayActive = true;
                gameplayActivationTime = Time.realtimeSinceStartup;
                CheckManager.MarkGameplayReady();
                Log.LogInfo($"[AP] Gameplay activated (player present, not Title). Item grants begin in {GameplayStartupDelay}s.");
            }
            else if (!nowActive && gameplayActive)
            {
                gameplayActive = false;
                gameplayActivationTime = float.MaxValue;
                Log.LogInfo("[AP] Gameplay deactivated (Title/menu context).");
            }

            return gameplayActive;
        }

        private void Update()
        {
            // Re-scan only if cache is stale (scene transitions null it out).
            if (_cachedSys == null)
                _cachedSys = UnityEngine.Object.FindObjectOfType<L2System>();
            if (_cachedSys == null)
                return;

            // Create DevUI once we have L2System — independent of AP auth so it
            // works in offline (seed.lm2r) play and pure vanilla too.
            if (_devUI == null)
            {
                _devUI = gameObject.AddComponent<DevUI>();
                _devUI.Initialise(_cachedSys);
            }

            if (!ArchipelagoClient.Authenticated && !ArchipelagoClient.OfflineMode)
                return;

            if (Patches.GuardianSpecificAnkhPatch.SlotRefresh)
            {
                Patches.GuardianSpecificAnkhPatch.SlotRefresh = false;

                var ankhScripts = UnityEngine.Object.FindObjectsOfType<AnchScript>();
                if (ankhScripts != null)
                {
                    foreach (var ankh in ankhScripts)
                    {
                        // Pass true to force a full state reset and inventory re-check
                        try { ankh.resetActionCharacter(true); }
                        catch { /* Ignore if a specific script fails to reset */ }
                    }
                }
                Log.LogInfo("[AP] Guardian Specific Ankhs setting received. Refreshed active Ankh scripts.");
            }

            // Must have system before we can compute gameplayActive safely
            var sys = _cachedSys;

            // Compute/transition gameplayActive based on actual run state
            if (!UpdateGameplayActive(sys))
                return;

            // Wait for the startup grace period to elapse before granting anything.
            if (Time.realtimeSinceStartup < gameplayActivationTime + GameplayStartupDelay)
                return;

            var pl = sys.getPlayer();
            if (pl == null)
                return;

            // Comprehensive guard: dialog, menus, pause, transitions, death, etc.
            if (!ItemGrantStateGuard.IsSafe(sys, pl))
                return;

            // DeathLink — kill player if a death is queued.
            ArchipelagoClient.DeathLinkHandler?.Update();

            // After IsSafe check, before queue processing — restore takes priority:
            if (ShadowSaveManager.TryRestore())
                return; // let queue settle for one frame after re-injection

            if (ArchipelagoClient.ItemQueue.Count <= 0)
                return;

            // One item at a time; dequeue only if successful
            var q = ArchipelagoClient.ItemQueue.Peek();

            // Prime the dialog patch with AP display info BEFORE granting.
            // ItemDialogPatch.setItemDialogOption Prefix will read these and
            // substitute the item name / sender suffix in the acquisition dialog.
            Patches.ItemDialogPatch.PendingDisplayLabel = q.ItemName;
            Patches.ItemDialogPatch.PendingSenderName = q.SenderName;

            bool granted = ItemGrantManager.TryGrantItem(sys, pl, q.ItemId);

            if (granted)
            {
                // Popup-only grants (coins, weights, ammo, pot filler) never open
                // the item dialog, so the prime we just set won't be consumed by
                // ItemDialogPatch.StartSwitch. Clear it now or it will overwrite
                // the next location-check's dialog label.
                if (ItemGrantManager.LastGrantUsedPopupOnly)
                {
                    Patches.ItemDialogPatch.PendingDisplayLabel = null;
                    Patches.ItemDialogPatch.PendingSenderName = null;
                    Patches.ItemDialogPatch.PendingRecipientName = null;
                }

                ArchipelagoClient.ItemQueue.Dequeue();
                ArchipelagoClient.MarkItemProcessed(q.Index);

                if (ShadowSaveManager.IsRestoringItem)
                    ShadowSaveManager.OnRestoreItemGranted();
                else
                    ShadowSaveManager.RecordGranted(q);
            }
            else
            {
                // Grant didn't happen this frame (guard blocked it, etc.).
                // Clear pending so a vanilla dialog can't accidentally pick them up.
                Patches.ItemDialogPatch.PendingDisplayLabel = null;
                Patches.ItemDialogPatch.PendingSenderName = null;
                Patches.ItemDialogPatch.PendingRecipientName = null;
            }
        }

        private void OnGUI()
        {
            if (guiFont == null)
            {
                guiFont = Font.CreateDynamicFontFromOSFont("Consolas", 14);
            }

            if (guiStyle == null)
            {
                guiStyle = new GUIStyle(GUI.skin.label);
                guiStyle.normal.textColor = Color.white;
                guiStyle.font = guiFont;
                guiStyle.fontStyle = FontStyle.Bold;
            }

            if (!onTitle)
            {
                // During gameplay, only draw the console (toggle with F11)
                ArchipelagoConsole.OnGUI();
                return;
            }

            // Make cursor visible on the title screen so the user can click text fields
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            ArchipelagoConsole.OnGUI();

            // Scale the AP title UI (designed for 960x540) to current resolution.
            Matrix4x4 prevMatrix = GUI.matrix;
            float sx = Screen.width / 960f;
            float sy = Screen.height / 540f;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(sx, sy, 1f));

            GUI.Label(new Rect(150, 510, 300, 20), ModDisplayInfo, guiStyle);

            if (ArchipelagoClient.Authenticated)
            {
                GUI.Label(new Rect(150, 522, 400, 20), "Status: Connected", guiStyle); // APDisplayInfo + " Status: Connected"
                Rect disconnectRect = new Rect(16, 510, 100, 20);

                if (GUI.Button(disconnectRect, "Disconnect"))
                {
                    Log.LogInfo("[AP] Manual disconnect requested");
                    ArchipelagoClient.Disconnect();
                }

                // ===== DeathLink toggle button =====
                if (ArchipelagoClient.DeathLinkHandler != null)
                {
                    Rect deathLinkRect = new Rect(disconnectRect.x, disconnectRect.yMin - 20, 100, 20);

                    bool enabled = ArchipelagoClient.DeathLinkHandler.IsEnabled;

                    Color oldColor = GUI.color;
                    GUI.color = enabled ? Color.green : Color.red;

                    string label = enabled ? "DeathLink ON" : "DeathLink OFF";

                    if (GUI.Button(deathLinkRect, label))
                    {
                        ArchipelagoClient.DeathLinkHandler.ToggleDeathLink();
                    }

                    GUI.color = oldColor;
                }
            }
            else if (ArchipelagoClient.OfflineMode)
            {
                GUI.Label(new Rect(150, 522, 400, 20), "Status: Offline Mode (seed.lm2r)", guiStyle);

                DrawOfflineToggles();

                Rect offlineRect = new Rect(960 - 16 - 160, 510, 160, 20);
                Color oldColor = GUI.color;
                GUI.color = Color.green;
                if (GUI.Button(offlineRect, "Loaded seed.lm2r (offline)"))
                {
                    Log.LogInfo("[AP] Offline mode toggle OFF requested");
                    ArchipelagoClient.DeactivateOffline();
                }
                GUI.color = oldColor;
            }
            else
            {
                GUI.Label(new Rect(150, 522, 400, 20), "Status: Disconnected", guiStyle); // APDisplayInfo + " Status: Disconnected"

                GUI.Label(new Rect(16, 450, 150, 20), "Host:", guiStyle);
                GUI.Label(new Rect(16, 470, 150, 20), "Player Name:", guiStyle);
                GUI.Label(new Rect(16, 490, 150, 20), "Password:", guiStyle);

                ArchipelagoClient.ServerData.Uri =
                    GUI.TextField(new Rect(150, 450, 150, 20), ArchipelagoClient.ServerData.Uri);

                ArchipelagoClient.ServerData.SlotName =
                    GUI.TextField(new Rect(150, 470, 150, 20), ArchipelagoClient.ServerData.SlotName);

                ArchipelagoClient.ServerData.Password =
                    GUI.TextField(new Rect(150, 490, 150, 20), ArchipelagoClient.ServerData.Password);

                Rect connectRect = new Rect(16, 510, 100, 20);

                if (GUI.Button(connectRect, "Connect") &&
                    !string.IsNullOrEmpty(ArchipelagoClient.ServerData.SlotName))
                {
                    // Persist host + slot name so the next launch pre-fills them.
                    // (Password is deliberately not persisted.)
                    _cfgHost.Value = ArchipelagoClient.ServerData.Uri ?? "";
                    _cfgSlotName.Value = ArchipelagoClient.ServerData.SlotName ?? "";

                    Log.LogInfo("[AP] Manual connect requested");
                    ArchipelagoClient.Connect();
                }

                // Offline mode toggle — activates immediately from seed.lm2r so
                // the user gets fast failure feedback if the file is missing.
                Rect offlineRect = new Rect(960 - 16 - 160, 510, 160, 20);
                Color oldColor = GUI.color;
                GUI.color = Color.red;
                if (GUI.Button(offlineRect, "Load seed.lm2r (offline)"))
                {
                    Log.LogInfo("[AP] Offline mode requested");
                    ArchipelagoClient.ActivateOffline();
                }
                GUI.color = oldColor;
            }

            GUI.matrix = prevMatrix;
        }

        /// <summary>
        /// Two offline-mode preference toggles rendered above the Load seed.lm2r
        /// button on the right side of the title screen. Visible both pre- and
        /// post-activation; post-activation flips sync live via
        /// <see cref="Patches.GuardianSpecificAnkhPatch.GuardianSpecificAnkhsEnabled"/>
        /// (which triggers its own scene refresh) and via <see cref="ArchipelagoClient.ApFillerActive"/>.
        /// </summary>
        private static void DrawOfflineToggles()
        {
            // Only visible once the player has clicked "Load seed.lm2r".
            if (!ArchipelagoClient.OfflineMode)
                return;

            Color oldColor = GUI.color;

            Rect apFillerRect = new Rect(960 - 16 - 160, 470, 160, 20);
            bool apFillerOn = ArchipelagoClient.OfflineApFillerEnabled;
            GUI.color = apFillerOn ? Color.green : Color.red;
            string apFillerLabel = apFillerOn ? "AP Filler: ON" : "AP Filler: OFF";
            if (GUI.Button(apFillerRect, apFillerLabel))
            {
                ArchipelagoClient.OfflineApFillerEnabled = !apFillerOn;
                Log.LogInfo($"[AP] OfflineApFillerEnabled -> {ArchipelagoClient.OfflineApFillerEnabled}");
            }

            Rect ankhRect = new Rect(960 - 16 - 160, 490, 160, 20);
            bool ankhOn = ArchipelagoClient.OfflineGuardianAnkhsEnabled;
            GUI.color = ankhOn ? Color.green : Color.red;
            string ankhLabel = ankhOn ? "Guardian Ankhs: ON" : "Guardian Ankhs: OFF";
            if (GUI.Button(ankhRect, ankhLabel))
            {
                ArchipelagoClient.OfflineGuardianAnkhsEnabled = !ankhOn;
                Log.LogInfo($"[AP] OfflineGuardianAnkhsEnabled -> {ArchipelagoClient.OfflineGuardianAnkhsEnabled}");

                // If offline mode is already live, mirror the preference into
                // the patch flag so scene Ankhs refresh immediately.
                if (ArchipelagoClient.OfflineMode)
                    Patches.GuardianSpecificAnkhPatch.GuardianSpecificAnkhsEnabled =
                        ArchipelagoClient.OfflineGuardianAnkhsEnabled;
            }

            GUI.color = oldColor;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {

            onTitle = scene.name.Equals("title");

            // Keep tracing (useful forever, cheap)
            Log.LogInfo($"[Scene] Loaded '{scene.name}' (buildIndex={scene.buildIndex}) mode={mode}");

            // Drives the guardian-kill state machine; safe to call always.
            Managers.BossKillTracker.NotifySceneLoaded(scene.name);

            // Mirror current flag[2,3] (natural dissonance count) to AP
            // datastorage so PopTracker picks up the value after save loads
            // and reconnects, where setFlagData isn't replayed by the engine.
            Managers.DissonanceTracker.NotifySceneLoaded();

            if (ArchipelagoClient == null) return;

            bool isEnding1 = scene.name == GoalSceneName || scene.buildIndex == GoalSceneBuildIndex;
            bool isEnding2 = scene.name == GoalSceneFallback || scene.buildIndex == GoalSceneFallbackBuildIndex;

            // Goal scene handling runs even if the socket has dropped — we
            // need to record intent (GoalPending) and possibly kick a reconnect
            // so the deferred CLIENT_GOAL can land.
            if (isEnding1 || isEnding2)
            {
                Log.LogInfo($"[AP] {(isEnding1 ? "Credits" : "Post-credits")} scene reached ('{scene.name}'), reporting goal.");
                ArchipelagoClient.ReportGoalOnce();

                // If we still owe the server a goal packet and we're not
                // currently connected, kick a reconnect. HandleConnectResult
                // will retry ReportGoalOnce on success.
                if (ArchipelagoClient.GoalPending && !ArchipelagoClient.Authenticated && !ArchipelagoClient.OfflineMode)
                {
                    Log.LogInfo("[AP] Goal still pending and not authenticated — kicking reconnect.");
                    ArchipelagoClient.Connect();
                }

                // Stop processing items once the run is over.
                gameplayActive = false;
                gameplayActivationTime = float.MaxValue;
            }

            if (!ArchipelagoClient.Authenticated && !ArchipelagoClient.OfflineMode)
                return;

            // Standalone scene randomization (chests, entrances, NPCs, etc.)
            if (Managers.SceneRandomizer.Instance != null)
                Managers.SceneRandomizer.Instance.OnSceneLoaded(scene);
        }
        private IEnumerator BeginHarvest()
        {
            yield return null; // let Opening finish settling
            var sys = UnityEngine.Object.FindObjectOfType<L2Base.L2System>();
            if (sys != null)
                Managers.PrefabHarvester.StartHarvest(sys);
        }

        [HarmonyPatch(typeof(NewPlayer), "hitCallBack")]
        internal static class DeathLinkSendPatch
        {
            static void Postfix(NewPlayer __instance)
            {
                // We don't want to trigger the logic here anymore because hitCallBack 
                // fires too frequently. Instead, we let the DeathLinkHandler.Update() 
                // monitor the HP state naturally.

                // However, if you want to ensure it feels instant, we just notify the handler.
                // The handler's static 'wasDeadLastFrame' will prevent the 6x spam.
                var sys = UnityEngine.Object.FindObjectOfType<L2Base.L2System>();
                if (sys == null) return;

                bool isDead = sys.getPlayerHP() <= 0;
                ArchipelagoClient?.DeathLinkHandler?.SendDeathLink(isDead);
            }
        }
    }
}