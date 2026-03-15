using HarmonyLib;
using L2Base;

namespace LaMulana2Archipelago.Patches
{
    [HarmonyPatch(typeof(L2System), "gameStat")]
    internal static class NewGamePatch
    {
        static void Postfix(L2System __instance)
        {
            SaveData.ResetIndex(__instance);
            // Pass true to indicate this is a new game (apply delay)
            Plugin.Instance?.NotifyGameplayStarted(true);
        }
    }

    [HarmonyPatch(typeof(L2System), "dataLoad")]
    internal static class DataLoadPatch
    {
        static void Postfix(L2System __instance)
        {
            int savedIndex = SaveData.LoadIndex(__instance);
            Archipelago.ArchipelagoClient.ServerData.Index = savedIndex;
            Plugin.Log.LogInfo($"[AP] Restored item index from save: {savedIndex}");

            int before = Archipelago.ArchipelagoClient.ItemQueue.Count;
            Archipelago.ArchipelagoClient.ItemQueue.Clear();
            int after = Archipelago.ArchipelagoClient.ItemQueue.Count;
            Plugin.Log.LogInfo($"[AP] ItemQueue cleared: {before} -> {after}");
        }
    }
}