using HarmonyLib;
using LaMulana2Archipelago.Archipelago;
using LaMulana2Archipelago.Managers;
using LaMulana2RandomizerShared;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Decoupled glossanity stops new enemy chip drops once collected.
    /// </summary>
    [HarmonyPatch(typeof(DropItemGeneratorScript), "dropNChip")]
    internal static class MonsterChipDropGatePatch
    {
        static bool Prefix(int id, ref bool __result)
        {
            if (!GlossaryManager.Enabled || id < 0)
                return true;

            if (!GlossaryManager.TryGetLocation(id, out LocationID loc))
                return true; // not a registered glossary entry → vanilla book-flag gate

            long apLoc = 430000L + (int)loc;
            bool collected = CheckManager.IsLocationReported(apLoc)
                || (ArchipelagoClient.ServerData != null
                    && ArchipelagoClient.ServerData.CheckedLocations != null
                    && ArchipelagoClient.ServerData.CheckedLocations.Contains(apLoc));

            if (collected)
            {
                __result = false; // already checked → don't spawn another chip
                return false;
            }
            return true; // not collected yet → let vanilla drop it
        }
    }
}
