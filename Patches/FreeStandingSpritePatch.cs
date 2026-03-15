using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using L2Base;
using UnityEngine;

namespace LaMulana2Archipelago.Patches
{
    [HarmonyPatch]
    internal static class FreeStandingSpritePatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var t = AccessTools.TypeByName("LM2RandomiserMod.FakeItem");
            if (t == null) yield break;

            var m = AccessTools.Method(t, "Update");
            if (m != null) yield return m;
        }

        static void Postfix(object __instance)
        {
            try
            {
                var mb = __instance as MonoBehaviour;
                if (mb == null) return;

                // Expand range to match SeedFlagMapBuilder: Flags 40..79 are FakeItems
                int flagNo = 0;
                try { flagNo = Traverse.Create(__instance).Field("flagNo").GetValue<int>(); } catch { }
                if (flagNo < 40 || flagNo > 79) return;
                
                var sr = mb.GetComponent<SpriteRenderer>();
                if (sr == null) return;

                // If it isn't visible yet, don't bother
                if (!sr.enabled) return;

                // Same technique as TreasureBoxSpritePatch
                var shellData = L2SystemCore.getItemData("Shell Horn") ?? L2SystemCore.getItemData("ShellHorn");
                if (shellData == null) return;

                var shellSprite = L2SystemCore.getMapIconSprite(shellData);
                if (shellSprite == null) return;

                // Force it every frame while active (beats any "random sprite" component)
                sr.sprite = shellSprite;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[SPRITE] FreeStandingSpritePatch failed: " + ex);
            }
        }
    }
}