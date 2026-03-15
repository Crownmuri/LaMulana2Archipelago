using HarmonyLib;
using LaMulana2Archipelago;

[HarmonyPatch(typeof(EventItemScript), "itemGetAction")]
internal static class EventItemPickupPatch
{
    static void Postfix(EventItemScript __instance)
    {
        try
        {
            if (__instance?.gameObject == null) return;

            // itemLabel is the RANDOMIZED item's BoxName (set by ChangeChestItemFlags/ChangeEventItems)
            // gameObject.name is the ORIGINAL item's name — useless for rando resolution
            // Checks are now handled via AddFlagPatch (addFlag intercept).
            // This patch is kept only for logging/debugging.
            Plugin.Log.LogDebug(
                $"[EVENT] itemGetAction: objName='{__instance.gameObject.name}' " +
                $"itemLabel='{__instance.itemLabel}'"
            );
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogError($"[AP] EventItemPatch logging failed: {e}");
        }
    }
}