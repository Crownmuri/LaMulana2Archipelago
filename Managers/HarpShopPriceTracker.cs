using L2Base;

namespace LaMulana2Archipelago.Managers
{
    /// <summary>
    /// Re-prices the "Include Expensive Item In Shop" slot once the Harp is in
    /// hand.
    ///
    /// SceneRandomizer.ChangeShopItems() bakes every price into
    /// shopDataBase.cellData once after connection, so picking the Harp up
    /// later would otherwise leave the slot at 1500 for the rest of the run.
    /// Hooking the flag write rather than the AP grant means it fires however
    /// the Harp arrives -- received from another world, or found in our own.
    ///
    /// Mirrors the vanilla Enga Musica shape: 1500 until the Harp, 50 after.
    /// </summary>
    public static class HarpShopPriceTracker
    {
        // Harp: ItemDB lists it as itemSheet 2, itemFlag 46.
        private const int HarpSheet = 2;
        private const int HarpFlag = 46;

        private static bool refreshed;

        /// <summary>
        /// Called from SetFlagDataPatch.Postfix and AddFlagPatch.Postfix.
        /// Filters internally to the Harp flag, and only acts on the first
        /// transition -- ChangeShopItems() rewrites 20 shop strings, so there
        /// is no reason to run it on every later write.
        /// </summary>
        public static void NotifyFlagSet(int sheet, int flag, short value)
        {
            if (refreshed || sheet != HarpSheet || flag != HarpFlag || value <= 0)
                return;

            var randomizer = SceneRandomizer.Instance;
            if (randomizer == null)
                return;

            refreshed = true;
            randomizer.RefreshShopPrices();
            Plugin.Log.LogInfo("[SHOP] Harp obtained -> expensive shop slot re-priced to 50.");
        }

        /// <summary>
        /// Catch a Harp that was already held before this hook could see the
        /// write (save loaded mid-run, or a grant that landed during connect).
        /// </summary>
        public static void NotifySceneLoaded()
        {
            if (refreshed)
                return;

            var sys = UnityEngine.Object.FindObjectOfType<L2System>();
            if (sys == null)
                return;

            short current = 0;
            try { sys.getFlag(HarpSheet, HarpFlag, ref current); }
            catch { return; }

            NotifyFlagSet(HarpSheet, HarpFlag, current);
        }

        public static void Reset()
        {
            refreshed = false;
        }
    }
}
