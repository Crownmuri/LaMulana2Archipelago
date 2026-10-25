using LaMulana2Archipelago.Archipelago;

namespace LaMulana2Archipelago.Managers
{
    /// <summary>
    /// Master switches for the "unchecked location" indicators:
    ///   - Pot Hints      → PotMarkerPatch (red tint + outline on unchecked potsanity pots)
    ///   - Glossary Hints → GlossaryEnemyMarker + NpcDoorGlossaryMarker (AP logo over
    ///                      enemies / in doorways with an unchecked glossary entry)
    ///
    /// Defaults come from the player's title-screen buttons (BepInEx config
    /// Gameplay/PotHints, Gameplay/GlossaryHints). On connect the YAML options
    /// unchecked_pot_indicators / unchecked_glossary_indicators replace them for the
    /// session when the slot data carries them; the buttons can still flip them
    /// afterwards. Older apworlds (no key) and offline seeds keep the local choice.
    /// The finer per-effect config switches (TintUncheckedPots, MarkGlossaryEnemies,
    /// ...) still apply underneath.
    /// </summary>
    internal static class HintIndicators
    {
        public const string PotKey = "unchecked_pot_indicators";
        public const string GlossaryKey = "unchecked_glossary_indicators";

        public static bool PotHints = true;
        public static bool GlossaryHints = true;

        public const string InfiniteAmmoKey = "infinite_starting_ammo";

        /// <summary>
        /// Applies the client-side YAML options (the two indicators plus
        /// infinite_starting_ammo, which drives the "∞ Start Ammo" button the same
        /// way). Called for both AP connects and offline seed.lm2ap activation; a
        /// missing key keeps the player's current button choice.
        /// </summary>
        public static void ApplySlotData(ArchipelagoData data)
        {
            if (data == null)
                return;
            PotHints = data.GetSlotBool(PotKey, PotHints);
            GlossaryHints = data.GetSlotBool(GlossaryKey, GlossaryHints);
            Patches.InfiniteStartingAmmoPatch.Enabled =
                data.GetSlotBool(InfiniteAmmoKey, Patches.InfiniteStartingAmmoPatch.Enabled);
            Plugin.Log.LogInfo($"[AP]   {PotKey} = {PotHints}, {GlossaryKey} = {GlossaryHints}, " +
                $"{InfiniteAmmoKey} = {Patches.InfiniteStartingAmmoPatch.Enabled}");
        }
    }
}
