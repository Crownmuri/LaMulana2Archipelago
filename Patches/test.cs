using HarmonyLib;
using L2Base;
using System;
using TMPro;
using UnityEngine;

namespace LaMulana2Archipelago.DebugTools
{
    /// <summary>
    /// Debug helper to inspect shop icons and item IDs.
    /// Safe temporary patch — remove after investigation.
    /// </summary>
    public static class DebugShopIcons
    {
        private static bool dumpedIcons = false;

        private static void DumpIcons(ShopScript shop)
        {
            if (dumpedIcons)
                return;

            dumpedIcons = true;

            try
            {
                var sys = Traverse.Create(shop).Field("sys").GetValue<L2System>();
                if (sys == null) return;

                var core = sys.getL2SystemCore();
                if (core == null) return;

                var db = Traverse.Create(core).Field("itemDatabase").GetValue<ItemDatabaseSystem>();
                if (db == null) return;

                var icons = Traverse.Create(db).Field("shopIcons").GetValue<Sprite[]>();
                if (icons == null) return;

                Plugin.Log.LogInfo("===== SHOP ICON SPRITES =====");

                for (int i = 0; i < icons.Length; i++)
                {
                    var s = icons[i];
                    if (s != null)
                        Plugin.Log.LogInfo($"[{i}] {s.name}");
                }

                Plugin.Log.LogInfo("=============================");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[AP DEBUG] DumpIcons failed: {ex}");
            }
        }

        [HarmonyPatch(typeof(ShopScript), "itemCallBack")]
        internal static class ShopItemLogger
        {
            static void Postfix(ShopScript __instance, string name)
            {
                try
                {
                    DumpIcons(__instance);

                    int idx = Traverse.Create(__instance)
                        .Field("item_copunter")
                        .GetValue<int>() - 1;

                    if (idx < 0)
                        return;

                    var itemIds = Traverse.Create(__instance)
                        .Field("item_id")
                        .GetValue<string[]>();

                    var itemNames = Traverse.Create(__instance)
                        .Field("item_name")
                        .GetValue<TextMeshProUGUI[]>();

                    string id = "<null>";
                    string display = "<null>";

                    if (itemIds != null && idx < itemIds.Length)
                        id = itemIds[idx];

                    if (itemNames != null && idx < itemNames.Length && itemNames[idx] != null)
                        display = itemNames[idx].text;

                    Plugin.Log.LogInfo(
                        $"[AP DEBUG] Shop slot {idx}: id='{id}' display='{display}' raw='{name}'");
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[AP DEBUG] ShopItemLogger error: {ex}");
                }
            }
        }
    }
}