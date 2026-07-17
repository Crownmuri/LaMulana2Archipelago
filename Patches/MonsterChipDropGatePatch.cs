using HarmonyLib;
using LaMulana2Archipelago.Archipelago;
using LaMulana2Archipelago.Managers;
using LaMulana2RandomizerShared;
using UnityEngine;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Decoupled glossanity drop gate for enemy chips (DropItemGeneratorScript.dropNChip).
    ///
    /// The vanilla method refuses to drop a chip once the enemy's sheet-20 book flag is
    /// set (getFlag(20, id) != 0 → return false). In the decoupled model that flag is the
    /// encyclopedia-unlock marker, NOT the location-collected marker: receiving this
    /// enemy's glossary ROM from AP (DeliverGlossaryRom) sets the book flag yet leaves the
    /// location unchecked. Deferring to vanilla there would make the location unreachable.
    ///
    /// So for a registered glossary id we drive the drop ourselves, gated purely on whether
    /// the AP location has been checked:
    ///   - collected  → suppress the drop (no duplicate chips after the check fires).
    ///   - otherwise  → FORCE the drop, bypassing the vanilla book-flag check, so the chip
    ///                  is always available until its location is actually checked.
    /// </summary>
    [HarmonyPatch(typeof(DropItemGeneratorScript), "dropNChip")]
    internal static class MonsterChipDropGatePatch
    {
        static bool Prefix(DropItemGeneratorScript __instance, ref Vector3 pos, int id, ref bool __result)
        {
            if (!GlossaryManager.Enabled || id < 0)
                return true;

            if (!GlossaryManager.TryGetLocation(id, out LocationID loc))
                return true; // not a registered glossary entry → vanilla book-flag gate

            if (GlossaryManager.IsLocationCollected(loc))
            {
                __result = false; // already checked → don't spawn another chip
                return false;
            }

            // Not collected: replicate the vanilla drop WITHOUT the sheet-20 book-flag guard,
            // so a received-ROM book flag can't lock the player out of the location.
            var go = Object.Instantiate(__instance.nchipPrefab, pos, Quaternion.identity);
            var chip = go.GetComponent<MonsterChipScript>();
            Traverse.Create(chip).Field("chipId").SetValue(id); // chipId is non-public
            chip.itemValue = id;
            chip.initTask();
            __result = true;
            return false;
        }
    }
}
