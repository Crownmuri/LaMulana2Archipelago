using LaMulana2Archipelago.Archipelago;

namespace LaMulana2Archipelago
{
    /// <summary>
    /// Provides access to the active ArchipelagoClient instance.
    /// </summary>
    public static class ArchipelagoClientProvider
    {
        public static ArchipelagoClient Client { get; set; }
    }
}