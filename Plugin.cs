using BepInEx;
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
        public const string PluginVersion = "0.7.0.0";

        public const string ModDisplayInfo = $"{PluginName} v{PluginVersion}";
        private const string APDisplayInfo = $"Archipelago v{ArchipelagoClient.APVersion}";

        private Font guiFont;
        private GUIStyle guiStyle;
        private bool onTitle;

        internal static ManualLogSource Log;
        internal static ArchipelagoClient ArchipelagoClient;

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

            ArchipelagoClient.Connect();
            _bootstrapFinished = true;
        }

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo($"{ModDisplayInfo} initializing");

            ArchipelagoClient = new ArchipelagoClient();
            ArchipelagoClientProvider.Client = ArchipelagoClient;

            ArchipelagoConsole.Awake();
            ArchipelagoConsole.LogMessage($"{ModDisplayInfo} loaded");

            var wsType = System.Type.GetType("WebSocketSharp.WebSocket, websocket-sharp");
            Plugin.Log.LogInfo($"[AP] websocket-sharp loaded from: {wsType?.Assembly.Location ?? "NOT FOUND"}");

            ApSpriteLoader.Load(System.IO.Path.GetDirectoryName(Info.Location));

            _harmony = new Harmony(PluginGUID);
            _harmony.PatchAll();
            Log.LogInfo("Harmony patches applied");

            LocationFlagMap.InitializeFromSeed();

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
            if (!ArchipelagoClient.Authenticated)
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

            // Re-scan only if cache is stale (scene transitions null it out).
            if (_cachedSys == null)
                _cachedSys = UnityEngine.Object.FindObjectOfType<L2System>();
            if (_cachedSys == null)
                return;

            // Create DevUI once we have L2System
            if (_devUI == null)
            {
                _devUI = gameObject.AddComponent<DevUI>();
                _devUI.Initialise(_cachedSys);
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
                    Log.LogInfo("[AP] Manual connect requested");
                    ArchipelagoClient.Connect();
                }
            }

            GUI.matrix = prevMatrix;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {

            onTitle = scene.name.Equals("title");

            // Keep tracing (useful forever, cheap)
            Log.LogInfo($"[Scene] Loaded '{scene.name}' (buildIndex={scene.buildIndex}) mode={mode}");

            if (ArchipelagoClient == null || !ArchipelagoClient.Authenticated)
                return;

            // Exact trigger: credits roll
            if (scene.name == GoalSceneName || scene.buildIndex == GoalSceneBuildIndex)
            {
                Log.LogInfo($"[AP] Credits scene reached ('{scene.name}'), reporting goal.");
                ArchipelagoClient.ReportGoalOnce();

                // Stop processing items once the run is over.
                gameplayActive = false;
                gameplayActivationTime = float.MaxValue;
            }

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