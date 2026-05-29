using HarmonyLib;
using L2Base;
using LaMulana2Archipelago.Archipelago;

namespace LaMulana2Archipelago.Patches
{
    // Re-applies the AP Game Difficulty toggle on save-load and new-game.
    //   dataLoad / memLoad — Continue / mid-run reload.
    //   gameStat           — New Game; belt-and-suspenders alongside the
    //                        Prefix in GameFlagResetsPatch, in case anything
    //                        between gameFlagResets and the async scene
    //                        activation rewrites G_Difficulty.
    // All hooks no-op when no AP / offline session is active.

    internal static class GameDifficultyApplier
    {
        public static void Apply(L2System sys, string source)
        {
            if (sys == null) return;
            if (!ArchipelagoClient.Authenticated && !ArchipelagoClient.OfflineMode) return;

            var handler = ArchipelagoClient.GameDifficultyHandler;
            if (handler == null) return;

            Plugin.Log.LogInfo($"[AP][Difficulty] Re-apply via {source}");
            handler.ApplyTo(sys);
        }
    }

    [HarmonyPatch(typeof(L2System), nameof(L2System.dataLoad))]
    internal static class GameDifficulty_DataLoad_Patch
    {
        static void Postfix(L2System __instance) => GameDifficultyApplier.Apply(__instance, "dataLoad");
    }

    [HarmonyPatch(typeof(L2System), nameof(L2System.memLoad))]
    internal static class GameDifficulty_MemLoad_Patch
    {
        static void Postfix(L2System __instance) => GameDifficultyApplier.Apply(__instance, "memLoad");
    }

    [HarmonyPatch(typeof(L2System), nameof(L2System.gameStat))]
    internal static class GameDifficulty_GameStat_Patch
    {
        static void Postfix(L2System __instance) => GameDifficultyApplier.Apply(__instance, "gameStat");
    }
}
