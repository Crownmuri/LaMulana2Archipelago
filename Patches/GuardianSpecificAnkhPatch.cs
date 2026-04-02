using HarmonyLib;
using L2Base;
using L2Word;
using System;
using System.Text;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// When GuardianSpecificAnkhsEnabled is true, each AnchScript only activates
    /// from — and consumes — the Ankh Jewel that matches its specific guardian slot.
    ///
    /// guardian flagName → jewel name mapping (sheet "03world", sheet index 3):
    ///   guardian00 → Ankh Jewel1  (Fafnir)
    ///   guardian01 → Ankh Jewel2  (Vritra)
    ///   guardian02 → Ankh Jewel3  (Kujata)
    ///   guardian03 → Ankh Jewel4  (Aten-Ra)
    ///   guardian04 → Ankh Jewel5  (Jormungand)
    ///   guardian05 → Ankh Jewel6  (Anu)
    ///   guardian06 → Ankh Jewel7  (Surtr)
    ///   guardian07 → Ankh Jewel8  (Echidna)
    ///   guardian08 → Ankh Jewel9  (Hel)
    ///
    /// How it works:
    ///   1. A Prefix on AnchScript.ActionCharacterFarst / ActionCharacterBack sets
    ///      a thread-local _activeAnkh reference before the method body runs.
    ///   2. A Postfix on each clears it afterwards (even on exception via Finalizer).
    ///   3. Prefixes on L2System.getItemNum and L2System.decItem check _activeAnkh
    ///      and swap "Ankh Jewel" → "Ankh JewelN" when appropriate.
    ///   4. The decItem prefix fully handles numbered jewels itself and skips the
    ///      original (which has no handling for "Ankh Jewel1"–"9").
    /// </summary>
    [HarmonyPatch]
    public static class GuardianSpecificAnkhPatch
    {
        // ---------------------------------------------------------------
        // Public toggle — set this from your slot-data / settings loader.
        // ---------------------------------------------------------------
        
        private static bool _enabled = false;
        public static volatile bool SlotRefresh = false;

        public static bool GuardianSpecificAnkhsEnabled
        {
            get => _enabled;
            set
            {
                if (_enabled != value)
                {
                    _enabled = value;
                    SlotRefresh = true; // Signal the main thread to refresh scene objects
                }
            }
        }

        // ---------------------------------------------------------------
        // Thread-local tracker: which AnchScript is currently executing.
        // ThreadStatic keeps this safe if Unity ever calls Update on a
        // worker thread (unlikely, but costs nothing to be safe).
        // ---------------------------------------------------------------
        [ThreadStatic]
        private static AnchScript _activeAnkh;

        // ---------------------------------------------------------------
        // Helper: derive "Ankh JewelN" from an AnchScript's flagName field.
        // flagName is a public field set in the Unity Inspector per-object,
        // e.g. "guardian00", "guardian01", … "guardian08".
        // Returns "Ankh Jewel" (the vanilla generic name) as a safe fallback.
        // ---------------------------------------------------------------
        private static string GetBossJewelName(AnchScript ankh)
        {
            // flagName is a public field — no reflection needed.
            string fn = ankh?.flagName;

            if (fn != null
                && fn.StartsWith("guardian", StringComparison.Ordinal)
                && fn.Length > 8
                && int.TryParse(fn.Substring(8), out int idx)
                && idx >= 0 && idx <= 8)
            {
                return "Ankh Jewel" + (idx + 1);   // guardian00→"Ankh Jewel1", etc.
            }

            return "Ankh Jewel"; // fallback: vanilla behaviour
        }

        // ===============================================================
        // 1. Track the active AnchScript around ActionCharacterFarst
        //    (this is where the "do I have a jewel?" checks live: states
        //    2 and 10-completion both call getItemNum("Ankh Jewel"))
        // ===============================================================

        [HarmonyPrefix]
        [HarmonyPatch(typeof(AnchScript), nameof(AnchScript.ActionCharacterFarst))]
        private static void AnchFarst_Prefix(AnchScript __instance)
        {
            _activeAnkh = GuardianSpecificAnkhsEnabled ? __instance : null;
        }

        [HarmonyFinalizer]   // runs after Postfix AND on exception — guarantees cleanup
        [HarmonyPatch(typeof(AnchScript), nameof(AnchScript.ActionCharacterFarst))]
        private static void AnchFarst_Finalizer()
        {
            _activeAnkh = null;
        }

        // ===============================================================
        // 2. Track the active AnchScript around ActionCharacterBack
        //    (this is where decItem("Ankh Jewel", 1) is called when the
        //    player inserts the jewel into the ankh)
        // ===============================================================

        [HarmonyPrefix]
        [HarmonyPatch(typeof(AnchScript), nameof(AnchScript.ActionCharacterBack))]
        private static void AnchBack_Prefix(AnchScript __instance)
        {
            _activeAnkh = GuardianSpecificAnkhsEnabled ? __instance : null;
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(AnchScript), nameof(AnchScript.ActionCharacterBack))]
        private static void AnchBack_Finalizer()
        {
            _activeAnkh = null;
        }

        // ===============================================================
        // 3. Intercept getItemNum("Ankh Jewel") while inside an AnchScript.
        //    Redirects to the boss-specific jewel name so the ankh only
        //    lights up when the player holds *that* boss's jewel.
        // ===============================================================

        [HarmonyPrefix]
        [HarmonyPatch(typeof(L2System), nameof(L2System.getItemNum))]
        private static bool GetItemNum_Prefix(L2System __instance, ref string item_name, ref int __result)
        {
            if (_activeAnkh == null || item_name != "Ankh Jewel")
                return true;

            string jewel = GetBossJewelName(_activeAnkh);
            if (jewel == "Ankh Jewel")
                return true;

            // Read the specific jewel's flag directly.
            // 0 = never obtained, 1 = held, 2 = consumed.
            // Only value 1 means the player currently has the jewel.
            int sheet = __instance.SeetNametoNo("02Items");
            short val = 0;
            if (sheet >= 0)
                __instance.getFlag(sheet, jewel, ref val);
            __result = (val == 1) ? 1 : 0;
            return false;
        }

        // ===============================================================
        // 4. Intercept decItem("Ankh Jewel", 1) while inside AnchScript.
        //    L2System.decItem has no case for "Ankh Jewel1"–"9", so we
        //    handle the numbered jewel ourselves and skip the original.
        //
        //    What we do manually (mirrors addItem's inverse logic):
        //      a) Zero out the specific jewel's flag in sheet "02Items"
        //      b) Decrement the global A_Jewel counter in sheet "00system"
        //
        //    Returns false → Harmony skips the original method body.
        //    Returns true  → Harmony runs the original (vanilla fallback).
        // ===============================================================

        [HarmonyPrefix]
        [HarmonyPatch(typeof(L2System), nameof(L2System.decItem))]
        private static bool DecItem_Prefix(L2System __instance, ref string item_name, int num)
        {
            if (_activeAnkh == null || item_name != "Ankh Jewel")
                return true;

            string jewel = GetBossJewelName(_activeAnkh);
            if (jewel == "Ankh Jewel")
                return true;

            // Mark the specific jewel as consumed (2) rather than clearing to 0.
            // Value 0 = never obtained, 1 = held, 2 = consumed.
            // Keeping it non-zero prevents the item from respawning at its
            // pickup location (the game treats 0 as "not yet collected").
            // The original will handle A_Jewel decrement + HUD refresh.
            int itemsSheet = __instance.SeetNametoNo("02Items");
            if (itemsSheet >= 0)
                __instance.setFlagData(itemsSheet, jewel, 2);

            return true; // let original run
        }

        // ===============================================================
        // 5. Track the active AnchScript around resetActionCharacter.
        //    This is called on death, scene reload, and save/load —
        //    the "case 2" branch calls getItemNum("Ankh Jewel"), which
        //    must be redirected to the boss-specific jewel just like
        //    in ActionCharacterFarst and ActionCharacterBack.
        // ===============================================================

        [HarmonyPrefix]
        [HarmonyPatch(typeof(AnchScript), nameof(AnchScript.resetActionCharacter))]
        private static void AnchReset_Prefix(AnchScript __instance)
        {
            _activeAnkh = GuardianSpecificAnkhsEnabled ? __instance : null;
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(AnchScript), nameof(AnchScript.resetActionCharacter))]
        private static void AnchReset_Finalizer()
        {
            _activeAnkh = null;
        }
    }
    /// <summary>
    /// When guardian_specific_ankhs is enabled, replaces the generic Ankh Jewel
    /// item description in the inventory menu with a live list of which boss jewels
    /// the player currently holds.
    ///
    /// e.g.  "Guardian Ankh Jewels held: Fafnir, Anu, Echidna"
    ///       "No Guardian Ankh Jewels held."
    /// </summary>
    [HarmonyPatch]
    public static class AnkhJewelDescriptionPatch
    {
        private static readonly string[] BossNames =
        {
            "Fafnir", "Vritra", "Kujata", "Aten-Ra",
            "Jormungand", "Anu", "Surtr", "Echidna", "Hel"
        };

        // getMojiText(bool firstCall, string sheetName, string id, mojiScriptType type)
        [HarmonyPostfix]
        [HarmonyPatch(typeof(L2System), "getMojiText",
            new System.Type[] {
        typeof(bool), typeof(string), typeof(string), typeof(mojiScriptType)
            })]
        private static void GetMojiText_Postfix(
            L2System __instance,
            bool color_f,
            string sheet,
            string line,
            mojiScriptType mst,
            ref string __result)
        {
            if (line != "Ankh Jewel" || sheet != "weaponText") return;
            if (!GuardianSpecificAnkhPatch.GuardianSpecificAnkhsEnabled) return;

            int itemsSheet = __instance.SeetNametoNo("02Items");
            if (itemsSheet < 0) return;

            var held = new System.Collections.Generic.List<string>();
            for (int i = 0; i < BossNames.Length; i++)
            {
                string jewelName = "Ankh Jewel" + (i + 1);
                short val = 0;
                __instance.getFlag(itemsSheet, jewelName, ref val);
                if (val == 1)
                    held.Add(BossNames[i]);
            }

            if (held.Count == 0)
            {
                __result = "Guardian Specific Ankh Jewels in possession: n/a";
            }
            else
            {
                var sb = new StringBuilder("Guardian Specific Ankh Jewels in possession: ");
                for (int i = 0; i < held.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(held[i]);
                }
                sb.Append(".");
                __result = sb.ToString();
            }
        }
    }
}