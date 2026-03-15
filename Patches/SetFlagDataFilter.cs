using HarmonyLib;
using L2Base;
using LaMulana2Archipelago.Managers;
using System.Collections.Generic;

[HarmonyPatch(
    typeof(L2System),
    nameof(L2System.setFlagData),
    new System.Type[] { typeof(int), typeof(string), typeof(short) }
)]
internal static class SetFlagDataFilter
{
    private static readonly HashSet<string> Ignored =
        new()
        {
            "Playtime", "playtimeS", "playtimeM", "playtimeH",
            "Gold", "weight"
        };

    static bool Prefix(int seet_no, string name, short data)
    {
        // If name is null, the game/randomizer flag code can crash in getFlagNo().
        // Ignoring null-name flag writes is safe.
        return name != null;
    }

    static void Postfix(int seet_no, string name, short data)
    {
        if (data <= 0 || string.IsNullOrEmpty(name) || Ignored.Contains(name)) return;

        CheckManager.NotifyStringFlag(seet_no, name, data);
        
    }
}