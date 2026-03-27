using HarmonyLib;
using L2Base;
using L2Word;
using LaMulana2Archipelago.Archipelago;
using LaMulana2Archipelago.Managers;
using System;
using System.Reflection;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Patches the item acquisition dialog to display:
    ///   1. The AP item label instead of the internal game name.
    ///      e.g.  "Ankh Jewel" acquired.       →  "Ankh Jewel (Fafnir)" acquired.
    ///            "Map Software" acquired.      →  "Map (Village of Departure)" acquired.
    ///   2. The sending player's name appended to the suffix (AP grants only).
    ///      e.g.  "Ankh Jewel (Fafnir)" acquired from APJigsaw.
    ///
    /// How it works:
    ///   The dialog assembles three pieces in StartSwitch():
    ///     text1  =  getMojiText("system", "itemDialog1")   → opening colored quote
    ///     text2  =  getMojiText(itemSheet, itemKey)         → localized item name
    ///     text3  =  getMojiText("system", "itemDialog2")   → closing quote + " acquired."
    ///
    ///   MessString[0] must be left unchanged so icon loading and moji-text lookups
    ///   succeed.  Once StartSwitch() has run, this postfix overwrites DialogText.text
    ///   with the desired label + text3.
    ///
    ///   For AP grants:   PendingDisplayLabel / PendingSenderName are set by Plugin.Update.
    ///   For in-game ankh pickups (guardian_specific_ankhs):
    ///                    CheckManager.PendingAnkhJewelName is set by ReportLocation.
    /// </summary>
    [HarmonyPatch]
    public static class ItemDialogPatch
    {
        // ---------------------------------------------------------------
        // Set by Plugin.Update before each AP item grant.
        // ---------------------------------------------------------------
        public static string PendingDisplayLabel { get; set; }
        public static string PendingSenderName { get; set; }
        public static string PendingRecipientName { get; set; }

        /// <summary>
        /// Set to true when this patch has already overwritten DialogText.
        /// Checked by ItemDialogApItemPatch to avoid clobbering the result.
        /// </summary>
        public static bool DialogHandled { get; set; }

        // ---------------------------------------------------------------
        // Boss name lookup for numbered ankh jewels.
        // ---------------------------------------------------------------
        private static readonly string[] AnkhBossNames =
        {
            "Fafnir", "Vritra", "Kujata", "Aten-Ra",
            "Jormungand", "Anu", "Surtr", "Echidna", "Hel"
        };

        private static string GetAnkhDisplayLabel(string jewel)
        {
            // "Ankh Jewel1"–"Ankh Jewel9" → "Ankh Jewel (BossName)"
            if (jewel != null
                && jewel.StartsWith("Ankh Jewel", StringComparison.Ordinal)
                && jewel.Length == 11
                && int.TryParse(jewel.Substring(10), out int idx)
                && idx >= 1 && idx <= 9)
            {
                return "Ankh Jewel (" + AnkhBossNames[idx - 1] + ")";
            }
            return null;
        }

        // ---------------------------------------------------------------
        // cellData reference captured once at construction.
        // ---------------------------------------------------------------
        private static string[][][][] _talkCellData;

        private const string OriginalEnglishSuffix = " acquired.";
        private const int Row = 11;  // "itemDialog2" row
        private const int ColEnglish = 3;
        private const int TextIndex = 1;   // [0]=color open  [1]=text  [2]=color close

        // ===============================================================
        // A) Capture cellData from L2TalkSystemDataBase after construction.
        // ===============================================================

        [HarmonyPostfix]
        [HarmonyPatch(typeof(L2TalkSystemDataBase), MethodType.Constructor)]
        private static void TalkDB_Ctor_Postfix(L2TalkSystemDataBase __instance)
        {
            if (_talkCellData != null) return;

            FieldInfo fi =
                typeof(L2TalkSystemDataBase).BaseType?
                    .GetField("cellData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? typeof(L2TalkSystemDataBase)
                    .GetField("cellData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (fi != null)
            {
                _talkCellData = (string[][][][])fi.GetValue(__instance);
                Plugin.Log.LogInfo("[AP] ItemDialogPatch: captured cellData.");
            }
            else
            {
                Plugin.Log.LogWarning("[AP] ItemDialogPatch: cellData not found — sender suffix disabled.");
            }
        }

        // ===============================================================
        // B) Overwrite dialog text after StartSwitch completes.
        // ===============================================================

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ItemDialog), "StartSwitch")]
        private static void ItemDialog_StartSwitch_Postfix(ItemDialog __instance)
        {
            bool consumeLabel = false;
            try
            {
                // If first == true, Init() hasn't run yet and the dialog body was
                // skipped — preserve labels for the next call.
                FieldInfo firstField = FindFieldInHierarchy(typeof(ItemDialog), "first");
                if (firstField != null && (bool)firstField.GetValue(__instance))
                    return;

                consumeLabel = true;

                L2System sys = (L2System)FindFieldInHierarchy(typeof(ItemDialog), "sys")
                    ?.GetValue(__instance);

                ItemDialogController con = (ItemDialogController)AccessTools
                    .Field(typeof(ItemDialog), "con")
                    ?.GetValue(__instance);

                if (sys == null || con == null || con.DialogText == null)
                {
                    Plugin.Log.LogWarning("[AP] ItemDialogPatch: sys or con is null.");
                    return;
                }

                // Priority 1: AP grant label (set by Plugin.Update).
                string label = PendingDisplayLabel;

                // Priority 2: In-game ankh jewel pickup when guardian_specific_ankhs is on.
                if (string.IsNullOrEmpty(label)
                    && GuardianSpecificAnkhPatch.GuardianSpecificAnkhsEnabled
                    && !string.IsNullOrEmpty(CheckManager.PendingAnkhJewelName))
                {
                    label = GetAnkhDisplayLabel(CheckManager.PendingAnkhJewelName);
                }

                if (string.IsNullOrEmpty(label))
                    return;

                // Patch cellData with sender suffix before fetching text3, then restore.
                if (_talkCellData != null)
                {
                    if (!string.IsNullOrEmpty(PendingRecipientName))
                    {
                        _talkCellData[0][Row][ColEnglish][TextIndex] =
                            $"<line-height=50%>\nto <color=#4FFD84FF>{PendingRecipientName}</color>.<line-height=100%>";
                    }
                    else
                    {
                        _talkCellData[0][Row][ColEnglish][TextIndex] =
                            string.IsNullOrEmpty(PendingSenderName)
                                ? OriginalEnglishSuffix
                                : $"<color=#FFD700FF><line-height=50%></color>\nacquired from <color=#4FFD84FF>{PendingSenderName}</color>.<line-height=100%>";
                    }
                }

                string text3 = sys.getMojiText(true, "system", "itemDialog2", mojiScriptType.system)
                    .Replace("\"", "");

                if (_talkCellData != null)
                    _talkCellData[0][Row][ColEnglish][TextIndex] = OriginalEnglishSuffix;

                string displayPrefix = !string.IsNullOrEmpty(PendingRecipientName) ? "Sent <color=#FFD700FF>" : "</color>";
                con.DialogText.text = displayPrefix + label + text3;
                DialogHandled = true;

                // Show custom AP icon in the dialog (StartSwitch hid it for "Nothing").
                if (ItemDialogApItemPatch.WasApPlaceholder
                    && ApSpriteLoader.IsLoaded && con.Icon != null)
                {
                    con.Icon.sprite = ApSpriteLoader.MapSprite;
                    con.Icon.gameObject.SetActive(true);
                }

                Plugin.Log.LogInfo($"[AP] Dialog label substituted: \"{label}\"");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[AP] ItemDialogPatch.StartSwitch_Postfix: {ex}");
                if (_talkCellData != null)
                    _talkCellData[0][Row][ColEnglish][TextIndex] = OriginalEnglishSuffix;
            }
            finally
            {
                if (consumeLabel)
                {
                    PendingDisplayLabel = null;
                    PendingSenderName = null;
                    PendingRecipientName = null;
                    CheckManager.PendingAnkhJewelName = null;
                }
            }
        }

        // ===============================================================
        // C) Safety-net restore when the dialog closes.
        // ===============================================================

        [HarmonyPostfix]
        [HarmonyPatch(typeof(L2System), nameof(L2System.closeItemDialog))]
        private static void CloseItemDialog_Postfix()
        {
            if (_talkCellData != null)
                _talkCellData[0][Row][ColEnglish][TextIndex] = OriginalEnglishSuffix;

            PendingDisplayLabel = null;
            PendingSenderName = null;
            PendingRecipientName = null;
            DialogHandled = false;
            CheckManager.PendingAnkhJewelName = null;
        }

        // ---------------------------------------------------------------
        // Helper: walk the type hierarchy to find a field by name.
        // ---------------------------------------------------------------
        internal static FieldInfo FindFieldInHierarchy(Type type, string name)
        {
            for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                FieldInfo fi = t.GetField(name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fi != null) return fi;
            }
            return null;
        }
    }
}