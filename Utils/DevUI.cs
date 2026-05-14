using System;
using System.Collections.Generic;
using L2Base;
using L2Flag;
using UnityEngine;

namespace LaMulana2Archipelago.Utils
{
    /// <summary>
    /// In-game developer overlay — ported from the original randomizer's DevUI.
    /// Toggle with F10 (main panel), F9 (flag watch), F8 (pot labels), F7 (hitbox display).
    /// </summary>
    public class DevUI : MonoBehaviour
    {
        private L2System sys;
        private Font currentFont;

        private bool showUI;
        private bool showFlagWatch;
        private bool showPotLabels;

        // Warp fields
        private string areaString;
        private string screenXString;
        private string screenYString;
        private string posXString;
        private string posYString;
        private bool sceneJump = true;
        private BGScrollSystem currentBGSys;

        // Set flag fields
        private string sheetString;
        private string flagString;
        private string valueString;

        // Get flag fields
        private string getSheetString;
        private string getFlagString;
        private string getValueString;

        // Flag watch log (populated by our Harmony patches)
        private static readonly Queue<string> _flagWatch = new Queue<string>();
        private const int MaxFlagWatchEntries = 16;

        public void Initialise(L2System l2System)
        {
            sys = l2System;
            Cursor.visible = true;
        }

        /// <summary>
        /// Called by SetFlagDataPatch for numeric flag changes.
        /// Resolves sheet/flag names from the flag system for richer display.
        /// </summary>
        public static void RecordFlagChange(int sheet, int flag, short oldValue, short newValue)
        {
            if (oldValue == newValue) return;

            string flagName = GetFlagName(sheet, flag);
            if (ShouldFilterFlag(flagName)) return;

            short diff = (short)(newValue - oldValue);
            string sheetName = GetSheetName(sheet);
            _flagWatch.Enqueue($"[{sheet},{flag}]{sheetName}.{flagName} = {newValue} (diff:{diff})");

            while (_flagWatch.Count > MaxFlagWatchEntries)
                _flagWatch.Dequeue();
        }

        /// <summary>
        /// Called by SetFlagDataPatch for string-keyed flag changes.
        /// </summary>
        public static void RecordFlagChangeByName(int sheet, string name, short newValue)
        {
            if (ShouldFilterFlag(name)) return;

            string sheetName = GetSheetName(sheet);
            _flagWatch.Enqueue($"[{sheet}]{sheetName}.{name} = {newValue}");

            while (_flagWatch.Count > MaxFlagWatchEntries)
                _flagWatch.Dequeue();
        }

        private static bool ShouldFilterFlag(string name)
        {
            if (name == null) return false;
            if (name.StartsWith("playtime")) return true;
            if (name.Contains("pDoor")) return true;
            if (name == "Gold" || name == "weight" || name == "Playtime") return true;
            return false;
        }

        private static string GetSheetName(int sheet)
        {
            try
            {
                var sys = UnityEngine.Object.FindObjectOfType<L2System>();
                if (sys != null)
                {
                    string name = sys.getFlagSys().SeetNotoName(sheet);
                    if (name != null) return name;
                }
            }
            catch { }
            return sheet.ToString();
        }

        private static string GetFlagName(int sheet, int flag)
        {
            try
            {
                var sys = UnityEngine.Object.FindObjectOfType<L2System>();
                if (sys != null)
                {
                    L2FlagBase fb;
                    if (sys.getFlagSys().getFlagBaseObject(sheet, flag, out fb) && fb != null)
                        return fb.flagName ?? flag.ToString();
                }
            }
            catch { }
            return flag.ToString();
        }

        public void Update()
        {
            if (Input.GetKeyDown(KeyCode.F10))
                showUI = !showUI;

            if (Input.GetKeyDown(KeyCode.F9))
                showFlagWatch = !showFlagWatch;

            if (Input.GetKeyDown(KeyCode.F8))
                showPotLabels = !showPotLabels;

            if (Input.GetKeyDown(KeyCode.F7) && sys != null)
                sys.drawHitBox(!sys.drawHitBoxFlag);

            UpdateBGSys();
        }

        private void UpdateBGSys()
        {
            if (sceneJump && sys != null)
            {
                var core = sys.getL2SystemCore();
                if (core != null)
                {
                    currentBGSys = core.ScrollSystem;
                    if (currentBGSys != null)
                    {
                        sceneJump = false;
                        UpdatePositionInfo();
                    }
                }
            }
        }

        // ================================================================
        // OnGUI — matches original DevUI layout exactly
        // ================================================================

        public void OnGUI()
        {
            // F10 main panel — matches original DevUI.OnGUI layout
            if (showUI && sys != null && sys.getPlayer() != null)
            {
                areaString = GUI.TextArea(new Rect(0, 0, 100, 25), areaString);
                screenXString = GUI.TextArea(new Rect(0, 25, 50, 25), screenXString);
                screenYString = GUI.TextArea(new Rect(50, 25, 50, 25), screenYString);
                posXString = GUI.TextArea(new Rect(0, 50, 50, 25), posXString);
                posYString = GUI.TextArea(new Rect(50, 50, 50, 25), posYString);

                if (GUI.Button(new Rect(0, 75, 100, 25), "Warp"))
                    DoDebugWarp();

                if (GUI.Button(new Rect(0, 100, 100, 25), "Refresh Pos"))
                    UpdatePositionInfo();

                sheetString = GUI.TextArea(new Rect(100, 0, 100, 25), sheetString);
                flagString = GUI.TextArea(new Rect(100, 25, 100, 25), flagString);
                valueString = GUI.TextArea(new Rect(100, 50, 100, 25), valueString);

                if (GUI.Button(new Rect(100, 75, 100, 25), "Set Flag"))
                    SetFlag();

                getSheetString = GUI.TextArea(new Rect(200, 0, 100, 25), getSheetString);
                getFlagString = GUI.TextArea(new Rect(200, 25, 100, 25), getFlagString);
                getValueString = GUI.TextArea(new Rect(200, 50, 100, 25), getValueString);

                if (GUI.Button(new Rect(200, 75, 100, 25), "Get Flag"))
                    GetFlag();

                sys.setPandaModeHP(GUI.Toggle(new Rect(300, 0, 120, 25), sys.getPandaModeHP(), "Panda Mode"));
                sys.setPandaModeHit(GUI.Toggle(new Rect(300, 25, 120, 25), sys.getPandaModeHit(), "Panda Hit Mode"));
            }

            // F9 flag watch overlay
            if (showFlagWatch)
            {
                if (currentFont == null)
                    currentFont = Font.CreateDynamicFontFromOSFont("Consolas", 14);

                GUIStyle guistyle = new GUIStyle(GUI.skin.label);
                guistyle.normal.textColor = Color.white;
                guistyle.fontStyle = FontStyle.Bold;
                guistyle.font = currentFont;
                guistyle.fontSize = 14;

                // Switch to smaller font for the list, matching original
                guistyle.fontSize = 10;

                string flags = "";
                foreach (var entry in _flagWatch)
                    flags += "\n" + entry;

                if (flags.Length > 0)
                {
                    GUIContent content = new GUIContent(flags);
                    Vector2 size = guistyle.CalcSize(content);
                    GUI.Label(new Rect(0, Screen.height - size.y, size.x, size.y), content, guistyle);
                }
            }

            // F8 pot item drop labels
            if (showPotLabels)
                DrawPotLabels();
        }

        // ================================================================
        // F8 — Pot item drop labels (ported from patched_ItemPotScript)
        // ================================================================

        private void DrawPotLabels()
        {
            if (currentFont == null)
                currentFont = Font.CreateDynamicFontFromOSFont("Consolas", 14);

            // Find ExtCamera — same lookup as original
            Camera camera = null;
            var cams = FindObjectsOfType<Camera>();
            foreach (var cam in cams)
            {
                if (cam.gameObject.name == "ExtCamera")
                    camera = cam;
            }
            if (camera == null) return;

            GUIStyle guistyle = new GUIStyle(GUI.skin.label);
            guistyle.fontStyle = FontStyle.Bold;
            guistyle.normal.textColor = Color.white;
            guistyle.font = currentFont;

            var centerY = Screen.height / 2;

            var pots = FindObjectsOfType<ItemPotScript>();
            foreach (var pot in pots)
            {
                if (pot == null || !pot.isActiveAndEnabled || pot.exItemPrefab == null) continue;

                AbstractItemBase component = pot.exItemPrefab.GetComponent<AbstractItemBase>();
                if (component == null) continue;

                Vector3 worldPos = camera.WorldToScreenPoint(pot.transform.position);

                // Y-axis flip — exact same math as original patched_ItemPotScript
                if (worldPos.y <= centerY)
                {
                    var distToCenter = centerY - worldPos.y;
                    worldPos.Set(worldPos.x, distToCenter + centerY, worldPos.z);
                }
                else
                {
                    var distToCenter = worldPos.y - centerY;
                    worldPos.Set(worldPos.x, centerY - distToCenter, worldPos.z);
                }

                GUI.Label(new Rect(worldPos, new Vector3(100f, 100f)),
                    $"{component.itemLabel ?? "unknown"} ({component.itemValue})",
                    guistyle);
            }
        }

        // ================================================================
        // Position info
        // ================================================================

        private void UpdatePositionInfo()
        {
            if (sys == null || sys.getPlayer() == null || currentBGSys == null) return;

            try
            {
                L2SystemCore sysCore = sys.getL2SystemCore();
                GameObject playerObj = sys.getPlayer().gameObject;
                Vector3 position = playerObj.transform.position;
                ViewProperty currentView = currentBGSys.roomSetter.getCurrentView(position.x, position.y);
                int currentScene = sysCore.SceaneNo;
                areaString = currentScene.ToString();

                float num;
                float num2;
                if (currentView == null)
                {
                    screenXString = "-1";
                    screenYString = "-1";
                    num = 0f;
                    num2 = 0f;
                }
                else
                {
                    screenXString = currentView.ViewX.ToString();
                    screenYString = currentView.ViewY.ToString();
                    num = position.x - currentView.ViewLeft;
                    num2 = position.y - currentView.ViewBottom;
                }

                int num3 = (int)Mathf.Round(num * (float)BGAbstractScrollController.NumberCls);
                int num4 = (int)Mathf.Round(num2 * (float)BGAbstractScrollController.NumberCls);
                num3 /= 80;
                num4 /= 80;
                posXString = num3.ToString();
                posYString = num4.ToString();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[DevUI] UpdatePositionInfo: " + ex.Message);
            }
        }

        // ================================================================
        // Flag get/set
        // ================================================================

        private void SetFlag()
        {
            try
            {
                int sheet = int.Parse(sheetString);
                int flag = int.Parse(flagString);
                short value = short.Parse(valueString);
                sys.setFlagData(sheet, flag, value);
            }
            catch (Exception) { }
        }

        private void GetFlag()
        {
            try
            {
                int sheet = int.Parse(getSheetString);
                int flag = int.Parse(getFlagString);
                sys.getFlagSys().getFlagBaseObject(sheet, flag, out L2FlagBase l2Flag);
                getValueString = l2Flag.flagValue.ToString();
            }
            catch (Exception) { }
        }

        // ================================================================
        // Warp
        // ================================================================

        private void DoDebugWarp()
        {
            try
            {
                int area = int.Parse(areaString);
                int screenX = int.Parse(screenXString);
                int screenY = int.Parse(screenYString);
                int posX = int.Parse(posXString);
                int posY = int.Parse(posYString);

                L2SystemCore sysCore = sys.getL2SystemCore();

                sysCore.setJumpPosition(screenX, screenY, posX, posY, 0f);
                if (sysCore.SceaneNo != area)
                {
                    sysCore.gameScreenFadeOut(10);
                    sysCore.setFadeInFlag(true);
                    sysCore.changeFieldSceane(area, true, false);
                    sceneJump = true;
                }
                else
                {
                    JumpPosition();
                    UpdatePositionInfo();
                }
            }
            catch (Exception) { }
        }

        private void JumpPosition()
        {
            if (sceneJump) return;

            L2SystemCore sysCore = sys.getL2SystemCore();
            if (sysCore.getJumpPosition(out Vector3 vector))
            {
                sysCore.L2Sys.movePlayer(vector);
                currentBGSys.setPlayerPosition(vector, false);
                sysCore.resetFairy();
                currentBGSys.forceResetCameraPosition();
            }
        }
    }
}
