using HarmonyLib;
using L2Base;
using L2Word;
using LaMulana2RandomizerShared;
using LM2RandomiserMod;
using System;
using System.Reflection;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LaMulana2Archipelago.Utils;

namespace LaMulana2Archipelago
{
    /// <summary>
    /// AP placeholder item IDs.
    ///
    /// Every LM2 location that holds an item from another player's world is
    /// assigned a unique placeholder ID in the seed file:
    ///
    ///   AP_ITEM_PLACEHOLDER + 1   (first foreign-item location, sorted by LocationID)
    ///   AP_ITEM_PLACEHOLDER + 2   (second)
    ///   …
    ///
    /// The C# plugin recognises any value in the half-open range
    /// [AP_ITEM_PLACEHOLDER, BASE_ITEM_ID) as an AP placeholder.  Using distinct
    /// IDs means each location can independently track its collection state via
    /// a unique flag in sheet 31, rather than every location sharing flag (31,110).
    /// </summary>
    public static class ApItemIDs
    {
        /// <summary>First value in the AP placeholder range (410000).</summary>
        public const int Placeholder = 410000;

        /// <summary>
        /// All values in [Placeholder, BaseItemId) are AP placeholders.
        /// BaseItemId = 420000 (Python BASE_ITEM_ID).
        /// </summary>
        public static bool IsApPlaceholder(int id) =>
            id >= Placeholder && id < 420000;

        /// <summary>
        /// Sheet-31 flag layout for real game items:
        ///   ChestWeight01-40  → flags  0-39
        ///   FakeItem01-40     → flags 40-79
        ///   NPCMoney01-10     → flags 80-89
        ///   FakeScan01-15     → flags 90-104
        ///
        /// To avoid collisions we start AP placeholder flags at 105 (FlagOffset).
        /// ID 410001 → flag 106, ID 410002 → flag 107, … ID 410053 → flag 158.
        /// All safely within the 0-254 flag-sheet range.
        /// </summary>
        private const int FlagOffset = 105;

        /// <summary>
        /// Converts a placeholder ID to the sheet-31 flag index used to track
        /// whether this location has been collected.
        /// ID 410001 → flag 106, ID 410002 → flag 107, etc.
        /// </summary>
        public static int ToFlagIndex(int id) =>
            System.Math.Max(0, System.Math.Min(254, id - Placeholder + FlagOffset));
    }
}

namespace LaMulana2Archipelago.Managers
{
    public static class FillerRewardMap
    {
        public static void GetReward(int itemId, out int coinAmount, out int weightAmount, out string boxName)
        {
            coinAmount = 0; weightAmount = 0; boxName = "Weight1";

            int idx = -1, type = -1;

            if (itemId >= 191 && itemId <= 230) { idx = itemId - 191; type = 0; } // ChestWeight
            else if (itemId >= 231 && itemId <= 270) { idx = itemId - 231; type = 0; } // FakeItem
            else if (itemId >= 271 && itemId <= 280) { idx = itemId - 271; type = 1; } // NPCMoney
            else if (itemId >= 281 && itemId <= 295) { idx = itemId - 281; type = 2; } // FakeScan
            else return;

            if (type == 0) // 40-item distribution
            {
                if (idx < 3) coinAmount = 1;
                else if (idx < 9) coinAmount = 10;
                else if (idx < 17) coinAmount = 30;
                else if (idx < 20) coinAmount = 50;
                else if (idx < 22) coinAmount = 80;
                else if (idx < 23) coinAmount = 100;
                else if (idx < 27) weightAmount = 1;
                else if (idx < 37) weightAmount = 5;
                else if (idx < 39) weightAmount = 10;
                else weightAmount = 20;
            }
            else if (type == 1) // 10-item distribution
            {
                switch (idx)
                {
                    case 0: coinAmount = 1; break;
                    case 1: coinAmount = 10; break;
                    case 2: coinAmount = 30; break;
                    case 3: coinAmount = 50; break;
                    case 4: coinAmount = 80; break;
                    case 5: coinAmount = 100; break;
                    case 6: weightAmount = 1; break;
                    case 7: weightAmount = 5; break;
                    case 8: weightAmount = 10; break;
                    case 9: weightAmount = 20; break;
                }
            }
            else if (type == 2) // 15-item distribution
            {
                switch (idx)
                {
                    case 0: coinAmount = 1; break;
                    case 1: case 2: coinAmount = 10; break;
                    case 3: case 4: case 5: coinAmount = 30; break;
                    case 6: coinAmount = 50; break;
                    case 7: coinAmount = 80; break;
                    case 8: coinAmount = 100; break;
                    case 9: weightAmount = 1; break;
                    case 10: case 11: weightAmount = 5; break;
                    case 12: case 13: weightAmount = 10; break;
                    case 14: weightAmount = 20; break;
                }
            }

            if (coinAmount > 0) boxName = "Coin" + coinAmount;
            else if (weightAmount > 0) boxName = "Weight" + weightAmount;
            else boxName = "Weight1";
        }
    }

    // =========================================================================
    // ItemDB — return a valid ItemInfo for every AP placeholder ID
    // =========================================================================

    /// <summary>
    /// Makes ItemDB.GetItemInfo() return a valid ItemInfo for any AP placeholder
    /// instead of null.  Each unique ID maps to a unique flag in sheet 31 so that
    /// collection tracking is independent per location.
    /// </summary>
    [HarmonyPatch(typeof(ItemDB), nameof(ItemDB.GetItemInfo))]
    internal static class ItemDBApPatch
    {
        static bool TryRemapFillerName(ItemID id, out string newBoxName)
        {
            int raw = (int)id;
            if ((raw >= 191 && raw <= 295))
            {
                FillerRewardMap.GetReward(raw, out _, out _, out newBoxName);
                return true;
            }
            newBoxName = null;
            return false;
        }

        static void ForceSetItemInfoNames(ItemInfo info, string boxName)
        {
            // ItemInfo layout varies; Traverse is safest
            var t = Traverse.Create(info);

            if (t.Field("BoxName").FieldExists()) t.Field("BoxName").SetValue(boxName);
            else if (t.Property("BoxName").PropertyExists()) t.Property("BoxName").SetValue(boxName);

            if (t.Field("ShopName").FieldExists()) t.Field("ShopName").SetValue(boxName);
            else if (t.Property("ShopName").PropertyExists()) t.Property("ShopName").SetValue(boxName);

            // safe default
            if (t.Field("ShopType").FieldExists()) t.Field("ShopType").SetValue("item");
            else if (t.Property("ShopType").PropertyExists()) t.Property("ShopType").SetValue("item");
        }

        static void Postfix(ItemID id, ref ItemInfo __result)
        {
            int raw = (int)id;

            // 1) If DB returned null AND this is an AP placeholder, provide a stub.
            if (__result == null && ApItemIDs.IsApPlaceholder(raw))
            {
                int flagIndex = ApItemIDs.ToFlagIndex(raw);

                __result = new ItemInfo("AP Item", "AP Item", "item", 31, flagIndex, 10, 1, -1);
                Plugin.Log.LogDebug($"[AP] ItemDB stub for placeholder {raw} → sheet=31 flag={flagIndex}");
                return; // IMPORTANT: don't remap placeholders
            }

            // 2) If DB returned a real item, remap filler variants into CoinX/WeightX.
            if (__result != null && TryRemapFillerName(id, out var renamed))
            {
                ForceSetItemInfoNames(__result, renamed);
                Plugin.Log.LogDebug($"[AP] Remapped filler ItemInfo: {id} -> BoxName='{renamed}'");
            }
        }
    }

    // =========================================================================
    // setItem — suppress the game grant for AP placeholders
    // =========================================================================

    /// <summary>
    /// Prevents sys.setItem("AP Item", ...) from trying to grant a real in-game
    /// item.  The actual reward is handled by the AP server via CheckManager.
    /// </summary>
    [HarmonyPatch(typeof(L2System), nameof(L2System.setItem))]
    internal static class SetItemApPatch
    {
        // Coin pickup sound (CoinScript.itemGetAction): SE 109 (from your CoinScript comment)
        private const int CoinPickupSe = 109;

        // Generic pickup sound used by DropItemScript (your DropItemScript tooltip notes 23 as basic SE)
        private const int WeightPickupSe = 23;

        static bool Prefix(L2System __instance, ref string item_name, int num, bool direct = false, bool loadcall = false, bool sub_add = true)
        {
            // Suppress AP placeholder (matches "AP Item", "AP Item 0", "AP Item 123", etc.)
            if (item_name == "AP Item" || item_name.StartsWith("AP Item "))
            {
                Plugin.Log.LogInfo($"[AP] Suppressed setItem for AP placeholder: {item_name}");
                return false;
            }

            // Intercept our filler names
            if (TryGrantFiller(__instance, item_name))
                return false;

            // Redirect generic "Ankh Jewel" to the specific numbered jewel so that:
            //   a) the per-jewel flag in 02Items is correctly written (ankh activation)
            //   b) SetItem_Postfix in ItemDialogPatch sees the specific name (dialog label)
            if (item_name == "Ankh Jewel"
                && Patches.GuardianSpecificAnkhPatch.GuardianSpecificAnkhsEnabled
                && !string.IsNullOrEmpty(Managers.CheckManager.PendingAnkhJewelName))
            {
                item_name = Managers.CheckManager.PendingAnkhJewelName;
                Plugin.Log.LogInfo($"[AP] Redirected setItem 'Ankh Jewel' -> '{item_name}'");
            }

            return true;
        }

        private static bool TryGrantFiller(L2System sys, string itemName)
        {
            int coinAmount = 0;
            int weightAmount = 0;
            int flagIndex = -1;

            SetItemApPatch.ChestFillerDialog = false;

            // Parse optional flag suffix (e.g., "Coin30 0")
            string baseName = itemName;
            if (itemName.Contains(" "))
            {
                var parts = itemName.Split(' ');
                if (parts.Length == 2 && int.TryParse(parts[1], out int parsedFlag))
                {
                    flagIndex = parsedFlag;
                    baseName = parts[0];
                }
            }

            // Parse the base name
            if (baseName.StartsWith("Coin") && int.TryParse(baseName.Substring(4), out int parsedCoin))
                coinAmount = parsedCoin;
            else if (baseName.StartsWith("Weight") && int.TryParse(baseName.Substring(6), out int parsedWeight))
                weightAmount = parsedWeight;
            else
                return false; // not ours

            try
            {
                // ---- playerst (Status) ----
                // private Status playerst;
                object st = Traverse.Create(sys).Field("playerst").GetValue();
                if (st == null)
                {
                    Plugin.Log.LogWarning($"[AP] Could not grant {itemName}: playerst is null");
                    return true; // consume to avoid crashes/retries
                }

                // ---- player ----
                // Player pl = sys.getPlayer();
                object pl = Traverse.Create(sys).Method("getPlayer").GetValue();
                if (pl == null)
                {
                    Plugin.Log.LogWarning($"[AP] Could not grant {itemName}: getPlayer() returned null");
                    return true;
                }

                // ---- core + seManager ----
                // L2SystemCore core = sys.getL2SystemCore();
                // core.seManager.playSE(null, seNo)
                // core.seManager.releaseGameObjectFromPlayer(handle)
                object core = Traverse.Create(sys).Method("getL2SystemCore").GetValue();
                object seManager = (core != null) ? Traverse.Create(core).Field("seManager").GetValue() : null;

                // ---- apply reward ----
                if (coinAmount > 0)
                {
                    if (ChestFillerDrop(sys, "Coin", coinAmount))
                    {
                        Plugin.Log.LogInfo($"[AP] Dropped {coinAmount} coin(s) from chest via {itemName}");
                        return true;
                    }

                    // The NPC's KataribeScript already opened its own item dialog before
                    // calling setItem(). ItemDialogApItemPatch rewrites that popup's label
                    // to "1 Coin" / "10 Coins" etc. Do NOT open a second dialog here --
                    // just play the coin SFX and grant silently.
                    PlaySe(seManager, CoinPickupSe);

                    using (ItemGrantRecursiveGuard.Begin())
                    {
                        sys.setItem("Gold", coinAmount, direct: false, loadcall: false, sub_add: true);
                    }

                    Plugin.Log.LogInfo($"[AP] Granted {coinAmount} coin(s) via {itemName}");
                    return true;
                }

                if (weightAmount > 0)
                {
                    if (ChestFillerDrop(sys, "Weight", weightAmount))
                    {
                        Plugin.Log.LogInfo($"[AP] Dropped {weightAmount} weight(s) from chest via {itemName}");
                        return true;
                    }

                    // Same as coins: NPC popup already shown by KataribeScript, grant silently.
                    PlaySe(seManager, WeightPickupSe);

                    using (ItemGrantRecursiveGuard.Begin())
                    {
                        sys.setItem("Weight", weightAmount, direct: false, loadcall: false, sub_add: true);
                    }

                    Plugin.Log.LogInfo($"[AP] Granted {weightAmount} weight(s) via {itemName}");
                    return true;
                }

                // Mark the shop purchase flag
                if (flagIndex >= 0)
                {
                    sys.setFlagData(31, flagIndex, 1);
                    Plugin.Log.LogInfo($"[AP] Set shop flag 31,{flagIndex}=1 for {itemName}");
                }
                return true;
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"[AP] TryGrantFiller failed for {itemName}: {ex}");
                return true; // consume to avoid hard crash loops
            }
        }

        public static bool ChestFillerDialog = false;

        private static bool ChestFillerDrop(L2System sys, string kind, int amount)
        {
            if (!ChestFillerSetup.Armed) return false;

            if (Time.time - ChestFillerSetup.ArmedAt > 8.0f)
            {
                ChestFillerSetup.Armed = false;
                return false;
            }

            ChestFillerSetup.Armed = false;
            SetItemApPatch.ChestFillerDialog = true;

            try
            {
                object core = Traverse.Create(sys).Method("getL2SystemCore").GetValue();
                if (core == null) return false;

                object dropGen =
                    Traverse.Create(core).Property("dropItemGenerator").GetValue()
                    ?? Traverse.Create(core).Field("dropItemGen").GetValue();

                if (dropGen == null)
                {
                    Plugin.Log.LogWarning("[AP] ChestFillerDrop: dropGen null");
                    return false;
                }

                Vector3 pos = ChestFillerSetup.DropPos;

                // LM2 uses ref Vector3 overloads
                Type genType = dropGen.GetType();
                Type refVec3 = typeof(Vector3).MakeByRefType();

                string methodName = (kind == "Coin") ? "dropCoins" :
                                    (kind == "Weight") ? "dropWeight" : null;

                if (methodName == null) return false;

                MethodInfo mi =
                    AccessTools.Method(genType, methodName, new Type[] { refVec3, typeof(int) })
                    ?? AccessTools.Method(genType, methodName, new Type[] { typeof(Vector3), typeof(int) }); // fallback

                if (mi == null)
                {
                    Plugin.Log.LogWarning($"[AP] ChestFillerDrop: could not find {methodName} overload");
                    return false;
                }

                object[] args = new object[] { pos, amount };
                mi.Invoke(dropGen, args);

                // If we used ref Vector3, args[0] may be updated
                if (args[0] is Vector3 newPos)
                    ChestFillerSetup.DropPos = newPos;

                Plugin.Log.LogDebug($"[AP] ChestFillerDrop OK: {amount} {kind} at {ChestFillerSetup.DropPos}");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[AP] ChestFillerDrop failed: {ex}");
                return false;
            }
        }
        private static void PlaySe(object seManager, int seNo)
        {
            if (seManager == null) return;

            try
            {
                // int handle = seManager.playSE(GameObject, int)
                object handleObj = Traverse.Create(seManager).Method("playSE", (GameObject)null, seNo).GetValue();

                // Some builds return int, others might return object; handle both.
                if (handleObj != null)
                {
                    // seManager.releaseGameObjectFromPlayer(int)
                    Traverse.Create(seManager).Method("releaseGameObjectFromPlayer", handleObj).GetValue();
                }
            }
            catch
            {
                // Don't hard fail if SE signature differs
            }
        }

        private static void SetPopup(object playerObj, string popupTypeName, int amount)
        {
            if (playerObj == null) return;

            try
            {
                // We avoid referencing ItemPopUpController.PopUpType at compile time.
                // We find the nested enum type by name, then parse the value.

                var playerType = playerObj.GetType();
                var method = AccessTools.Method(playerType, "setPopUp");
                if (method == null) return;

                var parms = method.GetParameters();
                if (parms.Length != 2) return;

                var enumType = parms[0].ParameterType;
                if (!enumType.IsEnum) return;

                // popupTypeName must match enum member exactly ("Coin" / "Weight")
                object enumValue = System.Enum.Parse(enumType, popupTypeName);

                method.Invoke(playerObj, new object[] { enumValue, amount });
            }
            catch
            {
                // Popup is "nice to have"; don't crash if signatures differ
            }
        }
    }

#if LEGACY
    // =========================================================================
    // L2Rando.CreateSetItemString — safe shop display for AP placeholders
    // (Legacy mode only — requires original randomizer's L2Rando)
    // =========================================================================
    [HarmonyPatch(typeof(L2Rando), "CreateSetItemString")]
    internal static class CreateSetItemStringApPatch
    {
        static bool Prefix(object __instance, LocationID locationID, ref string __result)
        {
            var shopMap = Traverse.Create(__instance)
                .Field("shopToItemMap")
                .GetValue<Dictionary<LocationID, ShopItem>>();

            if (shopMap == null) return true;

            ShopItem shopItem;
            if (!shopMap.TryGetValue(locationID, out shopItem)) return true;

            int itemId = (int)shopItem.ID;
            bool isApPlaceholder = ApItemIDs.IsApPlaceholder(itemId);
            bool isFiller = (itemId >= 191 && itemId <= 295); // all filler ranges

            if (!isApPlaceholder && !isFiller) return true;

            int flagIndex;
            string itemName;
            int mult = shopItem.Multiplier;

            if (isApPlaceholder)
            {
                flagIndex = ApItemIDs.ToFlagIndex(itemId);
                itemName = "AP Item";
                mult = shopItem.Multiplier < 5 ? 10 : shopItem.Multiplier;
            }
            else // filler item
            {
                // Map filler ID to its sheet‑31 flag (see ApItemIDs comment)
                if (itemId >= 191 && itemId <= 230)          // ChestWeight
                    flagIndex = itemId - 191;
                else if (itemId >= 231 && itemId <= 270)     // FakeItem
                    flagIndex = itemId - 231 + 40;
                else if (itemId >= 271 && itemId <= 280)     // NPCMoney
                    flagIndex = itemId - 271 + 80;
                else /* 281–295 */                           // FakeScan
                    flagIndex = itemId - 281 + 90;

                // Get display name (e.g., "Coin30")
                FillerRewardMap.GetReward(itemId, out _, out _, out string boxName);
                itemName = boxName;
                mult = shopItem.Multiplier = 0;
            }

            __result = $"[@sitm,item,{itemName} {flagIndex},{mult * 10},1]";

            Plugin.Log.LogDebug($"[AP] Shop {locationID}: {itemName} {flagIndex} -> sitm '{itemName} {flagIndex}'");
            return false;
        }
    }
#endif

    // =========================================================================
    // ItemDialog.StartSwitch — safe handling for all special item names
    // =========================================================================

    /// <summary>
    /// Patches ItemDialog.StartSwitch to safely handle every special item name
    /// that would otherwise crash in vanilla getMojiText (unknown item names).
    ///
    ///   "AP Item"    — skip vanilla, set up dialog manually with AP icon
    ///   "CoinX"      — skip vanilla, set up dialog manually (filler)
    ///   "WeightX"    — skip vanilla, set up dialog manually (filler)
    ///   "Research7"  — rename to "Research" and let vanilla handle it
    ///   "Beherit3"   — rename to "Beherit" and let vanilla handle it
    /// </summary>
    [HarmonyPatch(typeof(ItemDialog), "StartSwitch")]
    public class ItemDialogApItemPatch
    {
        /// <summary>
        /// True when the current dialog was originally for an "AP Item" placeholder
        /// (i.e. an item belonging to another player's world, with no native icon).
        /// Consumed by <see cref="Patches.ItemDialogPatch"/> to decide whether to
        /// show the custom AP icon.  NOT set for Coin/Weight filler or real game items.
        /// </summary>
        public static bool WasApPlaceholder { get; set; }

        /// <summary>
        /// Prefix: for AP/filler items, skip the vanilla StartSwitch entirely
        /// and set up the dialog manually (vanilla crashes on unknown item names
        /// in getMojiText). For progressive items, just rename and let vanilla run.
        /// </summary>
        static bool Prefix(ItemDialog __instance)
        {
            WasApPlaceholder = false;

            string[] messString = Traverse.Create(__instance)
                .Field("MessString")
                .GetValue<string[]>();
            if (messString == null || messString[0] == null) return true;

            string displayLabel = null;
            bool isAp = false;

            if (messString[0] == "AP Item" || messString[0].StartsWith("AP Item "))
            {
                displayLabel = "AP Item";
                isAp = true;
                WasApPlaceholder = true;
            }
            else if (messString[0] == "Nothing" || messString[0] == "Fake" || messString[0] == "Money")
            {
                // Not in the vanilla text DB — handle safely
                displayLabel = "";
            }
            else if (messString[0] == "Weight" || messString[0] == "Gold")
            {
                // Bare "Weight"/"Gold" from filler chest/mural — not in vanilla text DB
                displayLabel = messString[0] == "Weight" ? "1 Weight" : "Coins";
            }
            else if (messString[0].StartsWith("Coin") && TryParseFiller(messString[0], "Coin", out int coins))
            {
                displayLabel = coins == 1 ? "1 Coin" : $"{coins} Coins";
            }
            else if (messString[0].StartsWith("Weight") && TryParseFiller(messString[0], "Weight", out int weights))
            {
                displayLabel = weights == 1 ? "1 Weight" : $"{weights} Weights";
            }
            else
            {
                // Progressive item variants: the game only knows the base name.
                // "Research7" → "Research", "Beherit3" → "Beherit", etc.
                string baseName = GetProgressiveBaseName(messString[0]);
                if (baseName != null)
                    messString[0] = baseName;
                return true; // let vanilla StartSwitch handle it
            }

            // --- Skip vanilla StartSwitch: manually set up the dialog ---
            // Vanilla StartSwitch crashes on unknown names (getMojiText → getMojiNameToNo)
            // so we replicate its essential setup here.
            SetupDialogManually(__instance, messString, displayLabel, isAp);
            return false; // skip original
        }

        private static void SetupDialogManually(ItemDialog __instance, string[] messString, string label, bool isAp)
        {
            var t = Traverse.Create(__instance);
            var sys = t.Field("sys").GetValue<L2System>();
            var con = t.Field("con").GetValue<ItemDialogController>();
            if (sys == null || con == null) return;

            // Camera + position (same as vanilla)
            var cam = GameObject.Find("BGScrollSystemBase")?.GetComponent<BGScrollSystem>()?.bgCamera?.BaseCamera;
            t.Field("cam").SetValue(cam);

            float posF, posD;
            if (messString[1] == "kataribe")
            {
                posF = 100f;
                posD = 120f;
            }
            else
            {
                Vector3 position = sys.getPlayer().gameObject.transform.position;
                if (cam != null && cam.WorldToScreenPoint(position).y > 224f)
                {
                    posF = -100f;
                    posD = -120f;
                }
                else
                {
                    posF = 100f;
                    posD = 120f;
                }
                sys.setSysFlag(L2Base.SYSTEMFLAG.MENUOPEN);
            }
            t.Field("PositionF").SetValue(posF);
            t.Field("PositionD").SetValue(posD);

            // Canvas group
            var cgroup = con.DialogBase.GetComponent<CanvasGroup>();
            t.Field("cgroup").SetValue(cgroup);
            cgroup.alpha = 0f;

            // Icon
            if (isAp && ApSpriteLoader.IsLoaded && con.Icon != null)
            {
                con.Icon.sprite = ApSpriteLoader.MapSprite;
                con.Icon.gameObject.SetActive(true);
            }
            else if (isAp)
            {
                // Fallback: use Holy Grail icon for AP items
                var grailSprite = L2Math.Load("Textures/icons_itemmenu", "Holy Grail");
                if (grailSprite != null)
                {
                    con.Icon.sprite = grailSprite;
                    con.Icon.gameObject.SetActive(true);
                }
                else
                {
                    con.Icon.gameObject.SetActive(false);
                }
            }
            else
            {
                // Filler — hide icon
                con.Icon.gameObject.SetActive(false);
            }

            // Build dialog text — use the system dialog prefix/suffix directly
            // ("You got" + label + "!")
            string text;
            try
            {
                string pre = sys.getMojiText(true, "system", "itemDialog1", mojiScriptType.system).Replace("\"", "");
                string post = sys.getMojiText(true, "system", "itemDialog2", mojiScriptType.system).Replace("\"", "");

                // Check if ItemDialogPatch has a better label (e.g. "1 Puzzle Piece (CrownJigsaw)")
                string finalLabel = label;
                if (isAp && Patches.ItemDialogPatch.PendingDisplayLabel != null)
                    finalLabel = Patches.ItemDialogPatch.PendingDisplayLabel;

                text = pre + finalLabel + post;
            }
            catch
            {
                text = label;
            }
            con.DialogText.text = text;

            // Set state and activate — same as vanilla
            sys.setSysFlag(L2Base.SYSTEMFLAG.ITDLRBLOCK);
            t.Field("sta").SetValue(1);
            con.DialogBase.transform.localPosition = new Vector3(0f, posD, 600f);
            t.Field("move_count").SetValue(0f);
            t.Field("m_obj").GetValue<GameObject>().SetActive(true);

            // Let ItemDialogPatch.Postfix do its sender-name overlay if needed
            Patches.ItemDialogPatch.DialogHandled = true;
        }

        /// <summary>
        /// Parses filler names like "Coin30", "Weight5", or "Coin30 15" (with flag suffix).
        /// Returns the numeric amount, stripping any trailing flag index after a space.
        /// </summary>
        private static bool TryParseFiller(string name, string prefix, out int amount)
        {
            amount = 0;
            string remainder = name.Substring(prefix.Length);
            // Strip flag suffix: "30 15" → "30"
            int spaceIdx = remainder.IndexOf(' ');
            if (spaceIdx >= 0)
                remainder = remainder.Substring(0, spaceIdx);
            return int.TryParse(remainder, out amount) && amount > 0;
        }

        /// <summary>
        /// Returns the base game item name for numbered progressive items,
        /// or null if the name doesn't need remapping.
        /// </summary>
        private static string GetProgressiveBaseName(string name)
        {
            if (name == null || name.Length < 2) return null;
            char last = name[name.Length - 1];
            if (last < '0' || last > '9') return null;

            if (name.StartsWith("Research") && name.Length > 8) return "Research";
            if (name.StartsWith("Beherit") && name.Length > 7) return "Beherit";
            if (name.StartsWith("Whip") && name.Length > 4) return null; // handled by setItem rename
            if (name.StartsWith("Shield") && name.Length > 6) return null; // handled by setItem rename
            if (name.StartsWith("Mantra") && name.Length > 6) return "Mantra";
            if (name.StartsWith("Ankh Jewel") && name.Length > 10) return "Ankh Jewel";

            return null;
        }

        // =========================================================================
        // L2SystemCore.getItemData — return Holy Grail data for "AP Item" names
        // =========================================================================

        /// <summary>
        /// Makes L2SystemCore.getItemData(string) return Holy Grail's ItemData for
        /// any name that starts with "AP Item".  This is the display database lookup
        /// used by:
        ///   • EventItemScript.itemGetAction — chest/ground pickup animation
        ///     (NewPlayer.setGetItemIcon → L2SystemCore.getMapIconSprite NRE fix)
        ///   • Any other system doing a visual lookup for the AP Item name
        ///
        /// Sets <see cref="LastWasApRedirect"/> so downstream patches (e.g.
        /// <see cref="SetGetItemIconApPatch"/>) can distinguish a real Holy Grail
        /// from an AP placeholder redirect.
        /// </summary>
        [HarmonyPatch(typeof(L2SystemCore), "getItemData", new[] { typeof(string) })]
        internal static class GetItemDataApItemPatch
        {
            /// <summary>
            /// True when the most recent getItemData call was an AP Item redirect.
            /// Consumed (cleared) by <see cref="SetGetItemIconApPatch"/>.
            /// </summary>
            internal static bool LastWasApRedirect;

            static void Postfix(string itemId, ref ItemData __result)
            {
                if (__result == null && itemId != null && itemId.StartsWith("AP Item"))
                {
                    __result = L2SystemCore.getItemData("Holy Grail");
                    LastWasApRedirect = true;
                }
                else
                {
                    LastWasApRedirect = false;
                }
            }
        }

        // =========================================================================
        // NewPlayer.setGetItemIcon — swap pickup animation icon for AP items
        // =========================================================================

        /// <summary>
        /// After setGetItemIcon has set the Holy Grail sprite (due to the
        /// <see cref="GetItemDataApItemPatch"/> redirect), overwrite it with the
        /// custom AP icon if available.
        /// </summary>
        [HarmonyPatch(typeof(NewPlayer), "setGetItemIcon")]
        internal static class SetGetItemIconApPatch
        {
            static void Postfix(NewPlayer __instance)
            {
                try
                {
                    if (!GetItemDataApItemPatch.LastWasApRedirect) return;
                    GetItemDataApItemPatch.LastWasApRedirect = false;

                    if (!ApSpriteLoader.IsLoaded) return;

                    var renderer = Traverse.Create(__instance)
                        .Field("itemRenderer")
                        .GetValue<SpriteRenderer>();

                    if (renderer != null)
                        renderer.sprite = ApSpriteLoader.MapSprite;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning("[AP] SetGetItemIconApPatch error: " + ex);
                }
            }
        }

        // =========================================================================
        // ShopScript.itemCallBack — show Holy Grail icon for AP Item shop slots
        // =========================================================================

        /// <summary>
        /// After itemCallBack has registered an "AP Item {flagIndex}" shop slot,
        /// overwrite its icon and display name so the shop UI shows a recognisable
        /// Holy Grail icon with the text "AP Item" instead of a blank slot.
        /// </summary>
        [HarmonyPatch(typeof(ShopScript), "itemCallBack")]
        internal static class ShopItemCallBackApPatch
        {
            static void Postfix(ShopScript __instance, string name)
            {
                if (name == null) return;

                // item_copunter was incremented at the end of the original method
                int idx = Traverse.Create(__instance).Field("item_copunter").GetValue<int>() - 1;
                if (idx < 0) return;

                var icons = Traverse.Create(__instance).Field("icon").GetValue<Sprite[]>();
                var shopItems = Traverse.Create(__instance).Field("shop_item").GetValue<Image[]>();
                var itemNames = Traverse.Create(__instance).Field("item_name").GetValue<TextMeshProUGUI[]>();

                if (icons == null || shopItems == null || itemNames == null) return;
                if (idx >= icons.Length) return;

                // Safely isolate the base name from the appended flag index (e.g., "Coin30 120" -> "Coin30")
                // We use LastIndexOf to avoid breaking names that naturally have spaces like "AP Item 120"
                string baseName = name;
                int lastSpaceIdx = name.LastIndexOf(' ');

                if (lastSpaceIdx > 0)
                {
                    // Ensure the part after the space is actually a number (the flag index)
                    // This prevents us from truncating valid items like "Fairy Guild Pass"
                    if (int.TryParse(name.Substring(lastSpaceIdx + 1), out _))
                    {
                        baseName = name.Substring(0, lastSpaceIdx);
                    }
                }

                Sprite chosenSprite = null;

                try
                {
                    if (baseName.StartsWith("AP Item"))
                    {
                        if (ApSpriteLoader.IsLoaded)
                            chosenSprite = ApSpriteLoader.ShopSprite;
                        else
                        {
                            var data = L2SystemCore.getItemData("Holy Grail");
                            if (data != null) chosenSprite = L2SystemCore.getShopIconSprite(data);
                        }
                    }
                    else if (baseName.StartsWith("Coin"))
                    {
                        var data = L2SystemCore.getItemData("Pandora Box"); // "Pandora Box" is a nice alternative for Coins
                        if (data != null) chosenSprite = L2SystemCore.getShopIconSprite(data);
                    }
                    else if (baseName.StartsWith("Weight") && baseName != "Weight")
                    {
                        var data = L2SystemCore.getItemData("Pandora Box"); // The regular Weight icon has the fixed x5...
                        if (data != null) chosenSprite = L2SystemCore.getShopIconSprite(data);
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[AP] ShopItemCallBackApPatch: failed to pick sprite for '{name}': {ex}");
                }

                if (chosenSprite != null)
                {
                    icons[idx] = chosenSprite;
                    if (shopItems[idx] != null)
                        shopItems[idx].sprite = chosenSprite;
                }
                else
                {
                    Plugin.Log.LogDebug($"[AP] Shop slot {idx}: no custom icon chosen for '{name}'");
                }
            }
        }

        // =========================================================================
        // ShopScript.setSouldOut — sold-out state via sheet 31 for AP Item slots
        // =========================================================================

        /// <summary>
        /// After setSouldOut has run its default inventory-count logic, override the
        /// sold-out state for every "AP Item {flagIndex}" slot by reading the sheet 31
        /// flag that was set by the "thanks" script when the player bought the item.
        /// Also re-calls drawItems() so the price display ("---") is updated.
        /// </summary>
        [HarmonyPatch(typeof(ShopScript), "setSouldOut")]
        internal static class ShopSoldOutApPatch
        {
            static void Prefix(ShopScript __instance)
            {
                var sys = Traverse.Create(__instance).Field("sys").GetValue<L2System>();
                sys?.runToMojiScFlagQ();   // apply any pending flag changes
            }
            static void Postfix(ShopScript __instance)
            {
                var itemId = Traverse.Create(__instance).Field("item_id").GetValue<string[]>();
                var soldOut = Traverse.Create(__instance).Field("isSouldOut").GetValue<bool[]>();
                var sys = Traverse.Create(__instance).Field("sys").GetValue<L2System>();

                if (itemId == null || soldOut == null || sys == null) return;

                bool anyChanged = false;

                for (int i = 0; i < 3 && i < itemId.Length; i++)
                {
                    if (itemId[i] == null) continue;

                    // Format: "ItemName FlagIndex"
                    var parts = itemId[i].Split(' ');
                    if (parts.Length < 2) continue;

                    if (!int.TryParse(parts[parts.Length - 1], out int flagIndex))
                        continue;

                    short flagVal = 0;
                    sys.getFlag(31, flagIndex, ref flagVal);
                    bool nowSoldOut = (flagVal != 0);

                    if (soldOut[i] != nowSoldOut)
                    {
                        soldOut[i] = nowSoldOut;
                        anyChanged = true;
                    }

                    Plugin.Log.LogDebug(
                        $"[AP] Shop setSouldOut slot {i}: flag31[{flagIndex}]={flagVal} -> soldOut={nowSoldOut}");
                }

                // Re-draw price column so "---" reflects updated state
                if (anyChanged)
                    Traverse.Create(__instance).Method("drawItems").GetValue();
            }
        }
    }
}