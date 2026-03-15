using LaMulana2RandomizerShared;
using L2Base;

namespace LaMulana2Archipelago.Utils
{
    /// <summary>
    /// Resolves MojiScript item calls into ItemID values.
    ///
    /// IMPORTANT:
    /// - MojiScript item names are engine-native strings
    /// - ItemTable does NOT expose TryGet or enumeration
    /// - L2SystemCore.getItemData() is the authoritative source
    /// </summary>
    public static class ItemNameResolver
    {
        public static bool TryResolve(string tab, string name, out ItemID item)
        {
            item = ItemID.None;

            if (string.IsNullOrEmpty(name))
                return false;

            // Query engine item database
            var itemData = L2SystemCore.getItemData(name);
            if (itemData == null)
                return false;

            // Convert engine enum to ItemID
            var engineItemName = itemData.getItemName();

            // ItemID enum matches engine item enum numerically
            item = (ItemID)engineItemName;

            return item != ItemID.None;
        }
    }
}