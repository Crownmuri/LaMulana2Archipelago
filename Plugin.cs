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
        public const string PluginVersion = "0.9.0";

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

        // Internal field number of fieldP02 (the ending-approach field whose own
        // trigger fires Congratulations → Ending1). From L2System's SceaneNo→name
        // map: fieldP00=24, fieldP01=24, fieldP02=25, fieldBlood=26.
        private const int FieldP02Number = 25;

        // Ending trigger location: room (3,8) of field 28 with (3,95) set.
        private const int IntermediateFieldNumber = 28;
        private const int Field28ViewX = 2;
        private const int Field28ViewY = 8;
        private const int Field28PlayerChipX = 25; 
        private const int Field28PlayerChipY = 2; 

        // Safety timeout (seconds) for the FieldLast prime warp: normally we advance
        // as soon as FieldLast's ScrollSystem finishes loading (frame-accurate, PC-
        // speed independent — same signal L2DebugWarpMenu waits on), but if that
        // never happens we force the next warp after this long so we can't hang.
        // changeFieldSceane nulls ScrollSystem synchronously, so a non-null value on
        // a later frame reliably means the new field (FieldLast) has come up.
        private const float FieldLastLoadTimeout = 8.0f;

        // Glossary-hunt credits transition. Calling loadDemoSceane straight from a
        // live field needs two things the game normally does for us: (1) fade the
        // screen to opaque black first (like Title→Opening in Title.Farst) so the
        // demo isn't revealed before its texture has drawn, and (2) quiesce the
        // reparented player so its task doesn't NRE in the demo scene. We drive
        // this as a two-phase state machine: phase 1 = fading, phase 2 = loaded.
        private const int FadeToCreditsFrames = 60;      // gameScreenFadeOut duration (~1s @ 60fps)
        private const float FadeToCreditsSeconds = 1.2f; // real-time wait before loading (fade + margin)
        private int _creditsPhase = 0;
        private float _creditsFadeCompleteTime = float.MaxValue;
        private float _secondWarpTime = float.MaxValue;

        // Frames spent in FieldLast after it finishes loading, before warping on to
        // fieldP02 — gives its escape trigger time to actually arm (ScrollSystem
        // becoming non-null is only the moment the field goes live). Sim runs at a
        // fixed step so a frame count is PC-speed independent.
        private int _framesInFieldLast = 0;
        private const int PrimeArmFrames = 90;

        // True while an Ending (credits) scene is loaded. Suppresses our
        // gameplay-active bookkeeping and item grants — the reparented player
        // is present there but the scene is not a playable field.
        private bool _inEndingScene = false;

        private bool _bootstrapStarted = false;

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

            // DIAGNOSTIC: background hang watchdog (own thread, so a frozen main
            // thread can't silence it). Reports where the intermittent shop /
            // NPC-glossary freeze stalls. Remove with HangWatchdog once fixed.
            Managers.HangWatchdog.Start();

            // Flag map is populated on demand — from AP slot_data on successful
            // Connect, or from seed.lm2r when the player clicks "Load seed.lm2r
            // (offline)". AP-only players never need to touch seed.lm2r at all.

            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;

            Managers.HangWatchdog.Stop();

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
            bool nowActive = sys != null && sys.getPlayer() != null && !IsTitleContext(sys) && !_inEndingScene;

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
            // DIAGNOSTIC: main-thread liveness tick for the hang watchdog. Must be
            // first so it beats every frame regardless of the early-returns below.
            Managers.HangWatchdog.Heartbeat();

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

            Managers.CostumeManager.TryApplyPending(sys);

            // Glossary-hunt credits, phase 1: the screen has been fading to black
            // since we consumed the credits request (below). Once the fade has
            // finished, set the escape state and warp — SCREEN KEPT BLACK — to the
            // ending trigger room (3,8) in FieldLast (field 28), which primes the
            // ending. Uses the same view-coordinate warp the mod's DevUI uses
            // (DoDebugWarp): setJumpPosition(viewX,viewY,posX,posY,z) then
            // changeFieldSceane(field,true,false). Handled here, before the
            // gameplay-active gate, so nothing stalls the sequence once committed.
            if (_creditsPhase == 1)
            {
                if (Time.realtimeSinceStartup >= _creditsFadeCompleteTime)
                {
                    _creditsPhase = 3; // → prime-in-FieldLast, then warp to fieldP02
                    Log.LogInfo($"[GlossaryGoal] Fade complete — setting escape state, warping (black) to FieldLast room ({Field28ViewX},{Field28ViewY}).");
                    try
                    {
                        sys.setFlagData(3, 95, 1); // "Escape" — 脱出状態 escape state 

                        var core = sys.getL2SystemCore();
                        core.setJumpPosition(Field28ViewX, Field28ViewY, Field28PlayerChipX, Field28PlayerChipY, 0f);
                        core.setFadeInFlag(false); // keep it black — this is only to prime the trigger

                        _framesInFieldLast = 0; // reset the arm-frame counter

                        core.changeFieldSceane(IntermediateFieldNumber, true, false);

                        // Advance when FieldLast's ScrollSystem comes up (see phase 3);
                        // this is only a safety cap in case that never happens.
                        _secondWarpTime = Time.realtimeSinceStartup + FieldLastLoadTimeout;
                    }
                    catch (System.Exception ex)
                    {
                        Log.LogError($"[GlossaryGoal] Failed warp to FieldLast room ({Field28ViewX},{Field28ViewY}): {ex}");
                        try { sys.getL2SystemCore().loadDemoSceane(GoalSceneFallback); } catch { }
                        _creditsPhase = 2;
                        gameplayActive = false;
                        gameplayActivationTime = float.MaxValue;
                    }
                }
                return;
            }

            // Phase 3: primed in FieldLast (still black) — now warp on to fieldP02
            // with the normal transition fade-in, where the ending should play.
            // Wait until FieldLast has actually finished loading (its ScrollSystem is
            // up) rather than a wall-clock delay, so it's robust to PC speed — the
            // same "loaded" signal L2DebugWarpMenu keys off. _secondWarpTime is only
            // a safety cap so we can't hang if the load never completes.
            if (_creditsPhase == 3)
            {
                bool loadTimedOut = Time.realtimeSinceStartup >= _secondWarpTime;
                if (!loadTimedOut)
                {
                    // Wait for FieldLast to finish loading (ScrollSystem up)...
                    if (sys.getL2SystemCore()?.ScrollSystem == null)
                        return;

                    // ...then let it run a fixed number of frames so its escape
                    // trigger actually arms before we warp away.
                    _framesInFieldLast++;
                    if (_framesInFieldLast < PrimeArmFrames)
                        return;
                }

                {
                    _creditsPhase = 2; // done
                    Log.LogInfo($"[GlossaryGoal] Warping to fieldP02 (field {FieldP02Number}) for the ending (after {_framesInFieldLast} frames in FieldLast).");
                    try
                    {
                        var core = sys.getL2SystemCore();
                        // Normal transition fade-in onto fieldP02, where the ending
                        // trigger fires. (FieldLast pops briefly during the fieldP02
                        // load — accepted; trying to hold black through it broke the
                        // ending trigger.)
                        core.setFadeInFlag(true);
                        core.setJumpPosition(FieldP02Number, "PlayerStart", true, false);

                        // Release the phase-1 input block.
                        sys.setKeyBlock(false);
                    }
                    catch (System.Exception ex)
                    {
                        Log.LogError($"[GlossaryGoal] Failed warp to fieldP02: {ex}");
                        try { sys.getL2SystemCore().loadDemoSceane(GoalSceneFallback); } catch { }
                    }
                    gameplayActive = false;
                    gameplayActivationTime = float.MaxValue;
                }
                return;
            }

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

            // Glossary-hunt goal: once the target entry count is reached, roll the
            // credits from a safe (non-dialog/non-transition) point. The goal
            // packet was already sent in GlossaryGoalTracker; Ending1's own
            // OnSceneLoaded handler re-reports it idempotently.
            if (Managers.GlossaryGoalTracker.TryConsumeCreditsRequest())
            {
                Log.LogInfo("[GlossaryGoal] Target reached — fading out before the ending.");
                try
                {
                    var core = sys.getL2SystemCore();
                    // Phase 1: fade the game screen to opaque black and fade the
                    // music down, then block input. We DON'T load the demo yet —
                    // the phase-2 handler above loads it once the fade finishes.
                    // This mirrors the Title→Opening demo entry exactly and is
                    // what stops Ending1 from rendering as a black screen.
                    core.gameScreenFadeOut(FadeToCreditsFrames);
                    core.musicManager.masterMusicVolumeFade(0f, 100);
                    sys.setKeyBlock(true);
                    _creditsFadeCompleteTime = Time.realtimeSinceStartup + FadeToCreditsSeconds;
                    _creditsPhase = 1;
                }
                catch (System.Exception ex)
                {
                    Log.LogError($"[GlossaryGoal] Failed to start credits fade: {ex}");
                    // Fall back to an immediate load so the run still ends.
                    try { sys.getL2SystemCore().loadDemoSceane(GoalSceneName); } catch { }
                    gameplayActive = false;
                    gameplayActivationTime = float.MaxValue;
                }
                return;
            }

            // Force the post-kill memSave once the kill is confirmed and the player is
            // back, safe, AND alive. The HP check is what IsSafe misses: it blocks on
            // player STATE == DEAD, but a suppressed death can leave HP at 0 while the
            // state still reads non-DEAD — saving then would loop on Continue. While
            // HP<=0 we hold the request and let game-over fire (refight, not a loop;
            // NotifyGameOver clears it). Runs before DeathLinkHandler so the save
            // commits before any queued kill.
            if (Managers.BossKillTracker.IsMemSavePending)
            {
                if (sys.getPlayerHP() > 0)
                {
                    Managers.BossKillTracker.TryConsumeMemSaveRequest();
                    var savePos = pl.getPlayerPositon();
                    if (sys.memSave(savePos.x, savePos.y, -1))
                        Log.LogInfo("[BossKillTracker] Forced post-kill memSave committed.");
                    else
                        Log.LogWarning("[BossKillTracker] Forced post-kill memSave returned false.");
                }
                else
                {
                    Log.LogWarning("[BossKillTracker] Post-kill save deferred: player HP<=0 (death beat the save); falling back to pre-fight checkpoint.");
                }
            }

            // DeathLink — kill player if a death is queued.
            ArchipelagoClient.DeathLinkHandler?.Update();

            // After IsSafe check, before queue processing — restore takes priority:
            if (ShadowSaveManager.TryRestore())
                return; // let queue settle for one frame after re-injection

            // Persistent Inventory: re-grant own-world items the save state lost.
            // Runs ahead of the AP queue so the player's own finds are back
            // before any foreign item opens its dialog, and one item per frame
            // so a large replay doesn't stall the frame.
            if (Managers.PersistentInventoryManager.Update(sys, pl))
                return;

            if (ArchipelagoClient.ItemQueue.Count <= 0)
                return;

            // One item at a time; dequeue only if successful
            var q = ArchipelagoClient.ItemQueue.Peek();

            // Prime the dialog patch with AP display info BEFORE granting.
            // ItemDialogPatch.setItemDialogOption Prefix will read these and
            // substitute the item name / sender suffix in the acquisition dialog.
            Patches.ItemDialogPatch.PendingDisplayLabel = q.ItemName;
            Patches.ItemDialogPatch.PendingSenderName = q.SenderName;

            bool granted = ItemGrantManager.TryGrantItem(sys, pl, q.Index, q.ItemId);

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
                    Patches.ItemDialogPatch.PendingRecipientColorHex = null;
                    Patches.ItemDialogPatch.PendingRecipientIconClass = null;
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
                Patches.ItemDialogPatch.PendingRecipientColorHex = null;
                Patches.ItemDialogPatch.PendingRecipientIconClass = null;
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
                Rect disconnectRect = new Rect(16, 510, 125, 20);

                if (GUI.Button(disconnectRect, "Disconnect"))
                {
                    Log.LogInfo("[AP] Manual disconnect requested");
                    ArchipelagoClient.DisconnectAndRevert();
                }

                // ===== DeathLink toggle button =====
                if (ArchipelagoClient.DeathLinkHandler != null)
                {
                    Rect deathLinkRect = new Rect(disconnectRect.x, disconnectRect.yMin - 20, 125, 20);

                    bool enabled = ArchipelagoClient.DeathLinkHandler.IsEnabled;

                    Color oldColor = GUI.color;
                    GUI.color = enabled ? Color.red : Color.green;

                    string label = enabled ? "DeathLink ON" : "DeathLink OFF";

                    if (GUI.Button(deathLinkRect, label))
                    {
                        ArchipelagoClient.DeathLinkHandler.ToggleDeathLink();
                    }

                    GUI.color = oldColor;
                }

                // Difficulty toggle button: Normal ↔ Hard.
                // Initial state from the seed's game_difficulty slot value.
                var diffHandler = Archipelago.ArchipelagoClient.GameDifficultyHandler;
                if (diffHandler != null)
                {
                    Rect hardModeRect = new Rect(disconnectRect.x, disconnectRect.yMin - 40, 125, 20);
                    var diffState = diffHandler.State;

                    Color oldColor = GUI.color;
                    GUI.color = diffState switch
                    {
                        Archipelago.GameDifficultyHandler.DifficultyState.Hard => Color.red,
                        _                                                      => Color.green,
                    };

                    if (GUI.Button(hardModeRect, $"Difficulty: {diffState}"))
                        diffHandler.ToggleHardMode();

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
                GUI.Label(new Rect(16, 450, 150, 20), "Host:", guiStyle);
                GUI.Label(new Rect(16, 470, 150, 20), "Player Name:", guiStyle);
                GUI.Label(new Rect(16, 490, 150, 20), "Password:", guiStyle);

                ArchipelagoClient.ServerData.Uri =
                    GUI.TextField(new Rect(150, 450, 150, 20), ArchipelagoClient.ServerData.Uri);

                ArchipelagoClient.ServerData.SlotName =
                    GUI.TextField(new Rect(150, 470, 150, 20), ArchipelagoClient.ServerData.SlotName);

                ArchipelagoClient.ServerData.Password =
                    GUI.TextField(new Rect(150, 490, 150, 20), ArchipelagoClient.ServerData.Password);

                Rect connectRect = new Rect(16, 510, 125, 20);

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

                // Live connection-process indicator, right of the Connect button.
                // Shows each stage (Connecting / Loading data package /
                // Authenticating / Scouting) and the reason on failure.
                {
                    var phase = ArchipelagoClient.Phase;
                    string statusText = phase == ArchipelagoClient.ConnectionPhase.Idle
                        ? "Status: Disconnected"
                        : "Status: " + ArchipelagoClient.PhaseText();

                    Color oldStatusColor = GUI.color;
                    GUI.color = phase switch
                    {
                        ArchipelagoClient.ConnectionPhase.Failed    => Color.red,
                        ArchipelagoClient.ConnectionPhase.Connected => Color.green,
                        ArchipelagoClient.ConnectionPhase.Idle      => Color.white,
                        _                                           => Color.yellow,
                    };
                    GUI.Label(new Rect(150, 522, 640, 20), statusText, guiStyle);
                    GUI.color = oldStatusColor;
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

            // Difficulty diagnostic — confirms what mobs in this scene will
            // observe when they run resetParameter. If this disagrees with
            // the title-screen toggle, something is rewriting G_Difficulty
            // between the GameFlagResetsPatch / GameStat hooks and scene
            // activation. Cheap log; remove later if it ever gets noisy.
            try
            {
                var sysProbe = _cachedSys ?? UnityEngine.Object.FindObjectOfType<L2System>();
                if (sysProbe != null)
                {
                    int gl = sysProbe.getGameLevel();
                    Log.LogInfo($"[Difficulty] scene='{scene.name}' getGameLevel={gl}");
                }
            }
            catch (System.Exception ex)
            {
                Log.LogWarning($"[Difficulty] probe failed on scene '{scene.name}': {ex.Message}");
            }

            // Drives the guardian-kill state machine; safe to call always.
            Managers.BossKillTracker.NotifySceneLoaded(scene.name);

            // Mirror current flag[2,3] (natural dissonance count) to AP
            // datastorage so PopTracker picks up the value after save loads
            // and reconnects, where setFlagData isn't replayed by the engine.
            Managers.DissonanceTracker.NotifySceneLoaded();

            // DLC-boss goal recovery: if the boss was beaten in a prior session,
            // the flag is restored on load without a setFlagData call, so re-check
            // it here. No-op unless the seed's goal is the DLC boss.
            Managers.DlcBossGoalTracker.NotifySceneLoaded();

            // Glossary-hunt goal recovery: recount shuffled entries unlocked in a
            // prior session (flags restored on load without setFlagData). No-op
            // unless the seed's goal is glossary_hunt.
            Managers.GlossaryGoalTracker.NotifySceneLoaded();

            if (ArchipelagoClient == null) return;

            // Clear DeathLink edge state on the freshly loaded field. DeathLinkHandler.Update()
            // is gated off during death/transitions, so it can't reliably reset its own flags
            // after a received-DeathLink kill; doing it here stops a stale suppression flag from
            // swallowing the player's next genuine death.
            ArchipelagoClient.DeathLinkHandler?.NotifySceneLoaded();

            bool isEnding1 = scene.name == GoalSceneName || scene.buildIndex == GoalSceneBuildIndex;
            bool isEnding2 = scene.name == GoalSceneFallback || scene.buildIndex == GoalSceneFallbackBuildIndex;

            // Track ending scenes so gameplay bookkeeping / grants stay quiet
            // there (the reparented player lingers but this is not a field).
            _inEndingScene = isEnding1 || isEnding2;

            // Goal scene handling runs even if the socket has dropped — we
            // need to record intent (GoalPending) and possibly kick a reconnect
            // so the deferred CLIENT_GOAL can land.
            // When the seed's goal is the DLC boss, reaching the normal credits
            // does NOT complete the AP goal — DlcBossGoalTracker owns it instead.
            if ((isEnding1 || isEnding2) && !Managers.DlcBossGoalTracker.IsDlcBossGoal)
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