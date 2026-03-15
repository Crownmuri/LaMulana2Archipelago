using HarmonyLib;
using UnityEngine;
using LaMulana2Archipelago;

namespace LaMulana2Archipelago.Patches
{
    [HarmonyPatch]
    internal static class ItemObjectDumper
    {
        // We patch the base "Awake" on the standard Unity MonoBehaviour.
        // Then we check if the object's components match the game's scripts.
        [HarmonyPatch(typeof(MonoBehaviour), "Awake")]
        [HarmonyPostfix]
        static void DumpOnAwake(MonoBehaviour __instance)
        {
            if (__instance == null) return;

            // We use 'is' checks to see if this specific MonoBehaviour is one of the item scripts
            // This bypasses the namespace issue while still catching the right objects
            if (__instance is EventItemScript eventItem)
            {
                Plugin.Log.LogWarning($"[VANILLA DUMP] EventItem Pickup: '{eventItem.gameObject.name}'");
            }
            else if (__instance is TreasureBoxScript chest)
            {
                // For chests, we want the name of the visual item object that appears when opened
                string innerName = chest.itemObj != null ? chest.itemObj.name : "NULL (Generic)";
                Plugin.Log.LogWarning($"[VANILLA DUMP] Chest found: '{chest.gameObject.name}' contains item: '{innerName}'");
            }
        }
    }
}