using LaMulana2RandomizerShared;
using L2Base;

namespace LaMulana2Archipelago.Utils
{
    public static class LocationResolver
    {
        public static LocationID FromObjName(string objName)
        {
            try
            {
                if (string.IsNullOrEmpty(objName)) return LocationID.None;

                const string prefix = "ItemSym ";
                int idx = objName.IndexOf(prefix);
                if (idx < 0) return LocationID.None;

                string name = objName.Substring(idx + prefix.Length);
                if (string.IsNullOrEmpty(name)) return LocationID.None;

                // First try raw (most stable)
                var data = L2SystemCore.getItemData(name);
                if (data != null)
                    return (LocationID)data.getItemName();

                // Fallbacks (only if raw fails)
                if (name.Contains("SacredOrb"))
                {
                    string spaced = name.Replace("SacredOrb", "Sacred Orb");
                    data = L2SystemCore.getItemData(spaced);
                    if (data != null)
                        return (LocationID)data.getItemName();
                }

                if (name == "MSX3p")
                {
                    data = L2SystemCore.getItemData("MSX");
                    if (data != null)
                        return (LocationID)data.getItemName();
                }

                if (name == "B Mirror2")
                    return LocationID.None;

                // Unknown / not in DB
                return LocationID.None;
            }
            catch (System.Exception e)
            {
                // NEVER allow resolver exceptions to break pickups
                Plugin.Log.LogError($"[AP] LocationResolver.FromObjName failed for '{objName}' (ignored): {e}");
                return LocationID.None;
            }
        }
    }
}
