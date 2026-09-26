using System;
using System.Collections.Generic;
using HarmonyLib;
using L2Base;
using L2Flag;
using L2Hit;
using L2STATUS;
using LaMulana2Archipelago.Managers;
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
        // Scroll system of the scene we warped out of — a cross-scene warp is
        // finished once ScrollSystem reports a different instance.
        private BGScrollSystem warpFromBGSys;

        // Looked up live: each scene load replaces the BGScrollSystem, so a
        // cached one goes stale on any door/save load that isn't a DevUI warp.
        private BGScrollSystem CurrentBGSys
        {
            get
            {
                var core = sys != null ? sys.getL2SystemCore() : null;
                BGScrollSystem bg = core != null ? core.ScrollSystem : null;
                return bg != null ? bg : null; // collapse destroyed Unity objects to null
            }
        }

        // Set flag fields
        private string sheetString;
        private string flagString;
        private string valueString;

        // Get flag fields
        private string getSheetString;
        private string getFlagString;
        private string getValueString;

        // Flag picker — named flags of the sheet typed into the Set Flag sheet box
        private bool showFlagList;
        private string flagFilter = "";
        private Vector2 flagListScroll;
        private int flagListSheet = -1;
        private readonly List<KeyValuePair<int, string>> flagListEntries = new List<KeyValuePair<int, string>>();

        // Resource fields (gold / weights / ammo) — edited on the right of the F10 panel
        private static readonly string[] ResourceLabels =
        {
            "Gold", "Weights", "Shuriken", "Rolling Shuriken", "Earth Spear", "Flare",
            "Caltrops", "Chakram", "Bomb", "Pistol Clips", "Pistol Bullets",
        };
        private static readonly SUBWEAPON[] AmmoSlots =
        {
            SUBWEAPON.SUB_SYURIKEN_B, SUBWEAPON.SUB_KURUMA_B, SUBWEAPON.SUB_DAICHI_B,
            SUBWEAPON.SUB_HATUDAN_B, SUBWEAPON.SUB_MAKIBI_B, SUBWEAPON.SUB_CHAKURA_B,
            SUBWEAPON.SUB_BOM_B, SUBWEAPON.SUB_REGUN, SUBWEAPON.SUB_GUN_B,
        };
        // Weapon each ammo row belongs to — clicking the row label grants it.
        private static readonly SUBWEAPON[] AmmoWeapons =
        {
            SUBWEAPON.SUB_SYURIKEN, SUBWEAPON.SUB_KURUMA, SUBWEAPON.SUB_DAICHI,
            SUBWEAPON.SUB_HATUDAN, SUBWEAPON.SUB_MAKIBI, SUBWEAPON.SUB_CHAKURA,
            SUBWEAPON.SUB_BOM, SUBWEAPON.SUB_GUN, SUBWEAPON.SUB_GUN,
        };
        private const int GoldRow = 0;
        private const int WeightRow = 1;
        private const int FirstAmmoRow = 2;
        private readonly string[] resourceStrings = new string[ResourceLabels.Length];

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
            {
                showUI = !showUI;
                if (showUI)
                    RefreshResources();
            }

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
            if (!sceneJump || sys == null || sys.getPlayer() == null) return;

            BGScrollSystem bg = CurrentBGSys;
            if (bg != null && bg != warpFromBGSys)
            {
                sceneJump = false;
                warpFromBGSys = null;
                UpdatePositionInfo();
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

                if (GUI.Button(new Rect(100, 100, 100, 25), showFlagList ? "Flag List ▲" : "Flag List ▼"))
                    showFlagList = !showFlagList;

                if (showFlagList)
                    DrawFlagList(100, 125);

                getSheetString = GUI.TextArea(new Rect(200, 0, 100, 25), getSheetString);
                getFlagString = GUI.TextArea(new Rect(200, 25, 100, 25), getFlagString);
                getValueString = GUI.TextArea(new Rect(200, 50, 100, 25), getValueString);

                if (GUI.Button(new Rect(200, 75, 100, 25), "Get Flag"))
                    GetFlag();

                sys.setPandaModeHP(GUI.Toggle(new Rect(300, 0, 120, 25), sys.getPandaModeHP(), "Panda Mode"));
                sys.setPandaModeHit(GUI.Toggle(new Rect(300, 25, 120, 25), sys.getPandaModeHit(), "Panda Hit Mode"));

                const float resourcePanelWidth = 240f;
                float resourcePanelHeight = (ResourceLabels.Length + 1) * 25f;
                DrawResourcePanel(Screen.width - resourcePanelWidth, Screen.height - resourcePanelHeight);
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
        // Flag picker
        // ================================================================

        private void DrawFlagList(float x, float y)
        {
            const float width = 300f;
            const float height = 300f;

            int sheet;
            if (!int.TryParse(sheetString, out sheet))
            {
                GUI.Box(new Rect(x, y, width, 25), "Enter a sheet number first");
                return;
            }
            if (sheet != flagListSheet)
                BuildFlagList(sheet);

            GUI.Box(new Rect(x, y, width, height + 25), GUIContent.none);
            GUI.Label(new Rect(x + 4, y, 40, 25), "Filter");
            flagFilter = GUI.TextField(new Rect(x + 44, y, width - 44, 25), flagFilter ?? "");

            var visible = new List<KeyValuePair<int, string>>();
            foreach (var entry in flagListEntries)
            {
                if (flagFilter.Length == 0
                    || entry.Value.IndexOf(flagFilter, StringComparison.OrdinalIgnoreCase) >= 0
                    || entry.Key.ToString() == flagFilter)
                    visible.Add(entry);
            }

            const float rowHeight = 22f;
            Rect view = new Rect(0, 0, width - 20, Mathf.Max(height, visible.Count * rowHeight));
            flagListScroll = GUI.BeginScrollView(new Rect(x, y + 25, width, height), flagListScroll, view);
            for (int i = 0; i < visible.Count; i++)
            {
                var entry = visible[i];
                short value = 0;
                sys.getFlag(sheet, entry.Key, ref value);
                if (GUI.Button(new Rect(0, i * rowHeight, width - 20, rowHeight), entry.Key + " - " + entry.Value + "  (" + value + ")"))
                {
                    // Fill both the Set and Get columns so the entry can be read or written.
                    flagString = entry.Key.ToString();
                    getSheetString = sheet.ToString();
                    getFlagString = entry.Key.ToString();
                    getValueString = value.ToString();
                    showFlagList = false;
                }
            }
            GUI.EndScrollView();
        }

        private void BuildFlagList(int sheet)
        {
            flagListSheet = sheet;
            flagListEntries.Clear();
            flagListScroll = Vector2.zero;

            // Bound by the FlagGuard range check: past the sheet's last row the
            // guarded getFlagBaseObject hands back a stub (and logs) instead of
            // throwing, and sheet 31 would start minting virtual flags.
            L2FlagSystem flagSys = sys.getFlagSys();
            for (int i = 0; Patches.GetFlagSystemPatch.IsFlagIndexValid(flagSys, sheet, i); i++)
            {
                L2FlagBase fb;
                try
                {
                    if (!flagSys.getFlagBaseObject(sheet, i, out fb)) break;
                }
                catch (Exception)
                {
                    break;
                }
                if (fb != null && !string.IsNullOrEmpty(fb.flagName))
                    flagListEntries.Add(new KeyValuePair<int, string>(i, fb.flagName));
            }
        }

        // ================================================================
        // Resources — gold / weights / ammo
        // ================================================================

        private void DrawResourcePanel(float x, float y)
        {
            GUI.Box(new Rect(x, y, 240, (ResourceLabels.Length + 1) * 25), GUIContent.none);

            for (int i = 0; i < ResourceLabels.Length; i++)
            {
                float rowY = y + i * 25;
                if (i >= FirstAmmoRow)
                {
                    SUBWEAPON weapon = AmmoWeapons[i - FirstAmmoRow];
                    bool owned = sys.isSubWeapon(weapon);
                    GUI.enabled = !owned;
                    if (GUI.Button(new Rect(x, rowY, 110, 25), ResourceLabels[i]))
                        GrantSubWeapon(weapon);
                    GUI.enabled = true;
                }
                else
                {
                    GUI.Label(new Rect(x, rowY, 110, 25), ResourceLabels[i]);
                }
                resourceStrings[i] = GUI.TextField(new Rect(x + 110, rowY, 50, 25), resourceStrings[i] ?? "");

                if (GUI.Button(new Rect(x + 160, rowY, 40, 25), "Set"))
                {
                    int value;
                    if (int.TryParse(resourceStrings[i], out value))
                        SetResource(i, value);
                }
                if (GUI.Button(new Rect(x + 200, rowY, 40, 25), "Max"))
                    SetResource(i, int.MaxValue);
            }

            if (GUI.Button(new Rect(x, y + ResourceLabels.Length * 25, 240, 25), "Refresh Resources"))
                RefreshResources();
        }

        /// <summary>
        /// Grants a subweapon exactly like a pickup (sheet-2 flag, have-state,
        /// starting ammo). Not wrapped in ItemGrantRecursiveGuard, so on AP it
        /// fires whatever location check the item flag maps to — same as Set Flag.
        /// </summary>
        private void GrantSubWeapon(SUBWEAPON weapon)
        {
            try
            {
                using (ItemGrantRecursiveGuard.Begin())
                    sys.setItem(sys.exchengeSubWeaponEnumToName(weapon), 1, direct: false, loadcall: false, sub_add: true);
                RefreshResources();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[DevUI] GrantSubWeapon: " + ex.Message);
            }
        }

        private Status GetStatus()
        {
            return sys == null ? null : Traverse.Create(sys).Field("playerst").GetValue<Status>();
        }

        private int GetResourceMax(Status st, int row)
        {
            if (row == GoldRow) return st.getMaxCoint();
            if (row == WeightRow) return 999;
            return sys.getSubWeaponMax(AmmoSlots[row - FirstAmmoRow]);
        }

        private int GetResource(Status st, int row)
        {
            if (row == GoldRow) return st.getCoin();
            if (row == WeightRow) return st.getWait();
            return st.getSubWeaponNum(AmmoSlots[row - FirstAmmoRow]);
        }

        /// <summary>
        /// Writes straight to Status (the real store — the 00system flags are only
        /// mirrors). Bypasses setItem, which drops value==0 and runs AP grant logic.
        /// </summary>
        private void SetResource(int row, int value)
        {
            try
            {
                Status st = GetStatus();
                if (st == null) return;

                value = Mathf.Clamp(value, 0, GetResourceMax(st, row));
                using (ItemGrantRecursiveGuard.Begin())
                {
                    if (row == GoldRow) st.setCoin(value);
                    else if (row == WeightRow) st.setWait(value);
                    else st.setSubWeaponNum(AmmoSlots[row - FirstAmmoRow], value);
                }

                resourceStrings[row] = GetResource(st, row).ToString();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[DevUI] SetResource: " + ex.Message);
            }
        }

        private void RefreshResources()
        {
            try
            {
                Status st = GetStatus();
                if (st == null) return;
                for (int i = 0; i < ResourceLabels.Length; i++)
                    resourceStrings[i] = GetResource(st, i).ToString();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[DevUI] RefreshResources: " + ex.Message);
            }
        }

        // ================================================================
        // Position info
        // ================================================================

        private void UpdatePositionInfo()
        {
            BGScrollSystem currentBGSys = CurrentBGSys;
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
                // Grant guard: DevUI edits never fire AP location checks.
                using (ItemGrantRecursiveGuard.Begin())
                {
                    sys.setFlagData(sheet, flag, value);

                    // A raw flag write only updates the save data; Status (weapon/item
                    // have-state, equip lists) is rebuilt from sheet 2 on load. Replay the
                    // same setItem the loader runs (ItemNameConnection.setFlagToItem) so
                    // the item is usable immediately instead of after a reload, or strip
                    // the have-state when the flag is cleared.
                    if (sheet == sys.SeetNametoNo("02Items"))
                    {
                        string itemName = GetFlagName(sheet, flag);
                        if (!string.IsNullOrEmpty(itemName) && itemName != flag.ToString())
                        {
                            if (value > 0)
                                sys.setItem(itemName, value, direct: true, loadcall: false, sub_add: true);
                            else
                                RemoveItem(itemName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[DevUI] SetFlag: " + ex.Message);
            }
        }

        /// <summary>
        /// Inverse of the load-time setItem replay: drops the in-memory have-state
        /// for a sheet-2 item whose flag was just cleared, re-equipping something
        /// else if it was the equipped one.
        /// </summary>
        private void RemoveItem(string itemName)
        {
            Status st = GetStatus();
            if (st == null) return;

            // Whip / Shield flags hold the tier, so clearing them removes every tier.
            if (itemName == "Whip")
            {
                RemoveMainWeapon(st, MAINWEAPON.LWHIP);
                RemoveMainWeapon(st, MAINWEAPON.MWIHP);
                RemoveMainWeapon(st, MAINWEAPON.HWHIP);
                return;
            }
            if (itemName == "Shield")
            {
                st.haveSubWeapon(SUBWEAPON.SUB_SHIELD1, false);
                st.haveSubWeapon(SUBWEAPON.SUB_SHIELD2, false);
                st.haveSubWeapon(SUBWEAPON.SUB_SHIELD3, false);
                return;
            }

            MAINWEAPON main = sys.exchengeMainWeaponNameToEnum(itemName);
            if (main != MAINWEAPON.NON)
            {
                RemoveMainWeapon(st, main);
                return;
            }

            SUBWEAPON sub = sys.exchengeSubWeaponNameToEnum(itemName);
            if (sub != SUBWEAPON.NON && sub <= SUBWEAPON.SUB_ANKJEWEL)
            {
                // Status.haveSubWeapon(false) already swaps off an equipped subweapon.
                st.haveSubWeapon(sub, false);
                return;
            }

            USEITEM use = sys.exchengeUseItemNameToEnum(itemName);
            if (use != USEITEM.NON)
            {
                st.haveUsesItem(use, false);
                st.setUseItemNum(use, 0);
                if (st.getUseItem() == use)
                {
                    USEITEM next = st.changeUseItem(1); // bounded search, NON when nothing is left
                    if (next != USEITEM.NON)
                    {
                        st.setUseItem(next);
                    }
                    else
                    {
                        sys.unEquipItem(itemName);
                        Traverse.Create(st).Field("l2_eq_use").SetValue(USEITEM.NON);
                    }
                }
                return;
            }

            // Passive equipment / software: take it out of the active equip list.
            if (sys.isEquipItem(itemName))
                sys.unEquipItem(itemName);
        }

        private void RemoveMainWeapon(Status st, MAINWEAPON weapon)
        {
            st.haveMainWeapon(weapon, false);
            st.setMainWeaponNum(weapon, 0);
            if (st.getMainWeapon() != weapon) return;

            // Status.changeMainWeapon loops forever when nothing is owned, so
            // search for a replacement ourselves.
            foreach (MAINWEAPON candidate in Enum.GetValues(typeof(MAINWEAPON)))
            {
                if (candidate != MAINWEAPON.NON && st.isMainWeapon(candidate))
                {
                    st.setMainWeapon(candidate);
                    return;
                }
            }
            sys.unEquipItem(sys.exchengeMainWeaponEnumToName(weapon));
            Traverse.Create(st).Field("l2_eq_main").SetValue(MAINWEAPON.NON);
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
                    warpFromBGSys = CurrentBGSys;
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
            BGScrollSystem currentBGSys = CurrentBGSys;
            if (currentBGSys == null) return;
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
