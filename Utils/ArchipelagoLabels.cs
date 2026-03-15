using System.Collections.Generic;

namespace LaMulana2Archipelago.Utils
{
    /// <summary>
    /// Shared lookup table: sheet-31 flag index → AP item label string.
    ///
    /// Populated by ArchipelagoClient after location scouting (see below).
    /// Consumed by ItemDialogApItemPatch when the player picks up an AP
    /// placeholder chest/NPC in their own world.
    ///
    /// Label format matches the AP queue items: "Item Name (PlayerName)"
    /// e.g. "1 Puzzle Piece (CrownJigsaw)"
    ///
    /// ── How to populate from ArchipelagoClient ───────────────────────────
    ///
    ///   After a successful Connect() / login, scout all your world's locations:
    ///
    ///     var scoutResult = await session.Locations.ScoutLocationsAsync(
    ///         false,   // hint = false (don't create hints)
    ///         allLocationIds.ToArray());
    ///
    ///     foreach (var networkItem in scoutResult.Locations)
    ///     {
    ///         // Convert AP location ID back to your AP placeholder item ID.
    ///         // The exact mapping depends on your Python ids.py layout.
    ///         int apPlaceholderId = YourLocationIdToPlaceholderId(networkItem.LocationId);
    ///
    ///         if (ApItemIDs.IsApPlaceholder(apPlaceholderId))
    ///         {
    ///             int flagIndex = ApItemIDs.ToFlagIndex(apPlaceholderId);
    ///             string itemName  = session.Items.GetItemName(networkItem.Item)
    ///                                ?? $"Item {networkItem.Item}";
    ///             string playerName = session.Players.GetPlayerAlias(networkItem.Player)
    ///                                ?? $"Player {networkItem.Player}";
    ///             ApLabelStore.SetLabel(flagIndex, $"{itemName} ({playerName})");
    ///         }
    ///     }
    /// </summary>
    public static class ApLabelStore
    {
        // flagIndex (sheet 31) → display label, e.g. "1 Puzzle Piece (CrownJigsaw)"
        private static readonly Dictionary<int, string> _labels = new();

        public static void SetLabel(int flagIndex, string label)
            => _labels[flagIndex] = label;

        public static string GetLabel(int flagIndex, string fallback = "AP Item")
            => _labels.TryGetValue(flagIndex, out string lbl) ? lbl : fallback;

        public static void Clear() => _labels.Clear();
    }
}