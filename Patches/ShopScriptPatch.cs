using System;
using HarmonyLib;
using L2Base;
using L2Word;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Replaces ShopScript.itemCallBack so that randomized items display
    /// correct icons, names, and prices in shops.
    /// Port of the original randomizer's MonoMod patch.
    /// </summary>
    [HarmonyPatch(typeof(ShopScript), nameof(ShopScript.itemCallBack))]
    internal static class ShopItemCallBackPatch
    {
        public static bool Enabled = false;

        private static readonly string[] Weapons =
        {
            "Knife", "Rapier", "Axe", "Katana", "Shuriken", "R-Shuriken",
            "E-Spear", "Flare Gun", "Bomb", "Chakram", "Caltrops", "Clay Doll",
            "Origin Seal", "Birth Seal", "Life Seal", "Death Seal"
        };

        static bool Prefix(ShopScript __instance, string tab, string name, int vale, int num, ref bool __result)
        {
            if (!Enabled) { __result = false; return true; }

            var trav = Traverse.Create(__instance);
            var icon = trav.Field("icon").GetValue<Sprite[]>();
            var item_id = trav.Field("item_id").GetValue<string[]>();
            var item_copunter = trav.Field("item_copunter").GetValue<int>();
            var item_num = trav.Field("item_num").GetValue<int[]>();
            var item_value = trav.Field("item_value").GetValue<int[]>();
            var shop_item = trav.Field("shop_item").GetValue<Image[]>();
            var item_valu = trav.Field("item_valu").GetValue<Text[]>();
            var item_name = trav.Field("item_name").GetValue<TextMeshProUGUI[]>();
            var true_name = trav.Field("true_name").GetValue<string[]>();
            var sys = trav.Field("sys").GetValue<L2System>();

            if (item_copunter > 2)
            {
                __result = false;
                return false;
            }

            true_name[item_copunter] = name;

            if (sys.isAnkJewel(name))
                name = "Ankh Jewel";
            else if (sys.isMap(name))
                name = "Map";
            else if (name.Contains("Research"))
                name = "Research";

            item_id[item_copunter] = name;

            if (name.Contains("Mantra") && !name.Equals("Mantra"))
            {
                icon[item_copunter] = L2SystemCore.getShopIconSprite(L2SystemCore.getItemData("Mantra"));
                shop_item[item_copunter].sprite = icon[item_copunter];

                string mojiName = name.Equals("Mantra10") ? "mantra1stM10" : "mantra1stM" + name.Substring(6, 1);
                item_name[item_copunter].text = sys.getMojiText(true,
                    sys.mojiSheetNameToNo(tab, sys.getMojiScript(mojiScriptType.system)),
                    sys.mojiIdToNo(tab, mojiName, sys.getMojiScript(mojiScriptType.system)),
                    sys.getNowLangage(), sys.getMojiScript(mojiScriptType.system));
            }
            else if (name.Contains("Beherit"))
            {
                name = "Beherit";
                icon[item_copunter] = L2SystemCore.getShopIconSprite(L2SystemCore.getItemData("Beherit"));
                shop_item[item_copunter].sprite = icon[item_copunter];

                short data = 0;
                sys.getFlag(2, 3, ref data);
                if (data > 0)
                {
                    item_name[item_copunter].text = "Dissonance " + data;
                }
                else
                {
                    item_name[item_copunter].text = sys.getMojiText(true,
                        sys.mojiSheetNameToNo(tab, sys.getMojiScript(mojiScriptType.item)),
                        sys.mojiIdToNo(tab, name, sys.getMojiScript(mojiScriptType.item)),
                        sys.getNowLangage(), sys.getMojiScript(mojiScriptType.item));
                }
            }
            else
            {
                if (name.Equals("Map"))
                {
                    icon[item_copunter] = L2SystemCore.getMapIconSprite(L2SystemCore.getItemData("Map"));
                }
                else if (name.Contains("Crystal S"))
                {
                    name = "Crystal S";
                    icon[item_copunter] = L2SystemCore.getShopIconSprite(L2SystemCore.getItemData(name));
                }
                else if (name.Contains("Sacred Orb"))
                {
                    name = "Sacred Orb";
                    icon[item_copunter] = L2SystemCore.getMenuIconSprite(L2SystemCore.getItemData(name));
                }
                else if (name.Contains("Whip"))
                {
                    short data = 0;
                    sys.getFlag(2, "Whip", ref data);
                    if (data == 0) name = "Whip";
                    else if (data == 1) { name = "Whip2"; vale *= 2; }
                    else { name = "Whip3"; vale *= 4; }
                    icon[item_copunter] = L2SystemCore.getMenuIconSprite(L2SystemCore.getItemData(name));
                }
                else if (name.Contains("Shield"))
                {
                    short data = 0;
                    sys.getFlag(2, 196, ref data);
                    if (data == 0) name = "Shield";
                    else if (data == 1) { name = "Shield2"; vale *= 2; }
                    else { name = "Shield3"; vale *= 4; }
                    icon[item_copunter] = Load("Textures/icons_shops", name);
                }
                else if (name.Equals("MSX"))
                {
                    icon[item_copunter] = L2SystemCore.getShopIconSprite(L2SystemCore.getItemData("MSX3p"));
                }
                else if (Array.IndexOf(Weapons, name) > -1)
                {
                    icon[item_copunter] = L2SystemCore.getMenuIconSprite(L2SystemCore.getItemData(name));
                }
                else
                {
                    icon[item_copunter] = Load("Textures/icons_shops", name);
                }

                shop_item[item_copunter].sprite = icon[item_copunter];
                item_name[item_copunter].text = sys.getMojiText(true,
                    sys.mojiSheetNameToNo(tab, sys.getMojiScript(mojiScriptType.item)),
                    sys.mojiIdToNo(tab, name, sys.getMojiScript(mojiScriptType.item)),
                    sys.getNowLangage(), sys.getMojiScript(mojiScriptType.item));
            }

            item_value[item_copunter] = vale;
            item_valu[item_copunter].text = vale > 999
                ? L2Math.numToText(vale, 4)
                : L2Math.numToText(vale, 3);
            item_num[item_copunter] = num;

            trav.Field("item_copunter").SetValue(item_copunter + 1);

            __result = true;
            return false;
        }

        private static Sprite Load(string atlas, string name)
        {
            var sprites = Resources.LoadAll<Sprite>(atlas);
            foreach (var s in sprites)
                if (s.name == name) return s;
            return null;
        }
    }

    /// <summary>
    /// Replaces ShopScript.setSouldOut to handle randomized items
    /// that have different sold-out conditions than vanilla.
    /// </summary>
    [HarmonyPatch(typeof(ShopScript), "setSouldOut")]
    internal static class ShopSetSoldOutPatch
    {
        public static bool Enabled = false;

        static bool Prefix(ShopScript __instance)
        {
            if (!Enabled) return true;

            var trav = Traverse.Create(__instance);
            var item_id = trav.Field("item_id").GetValue<string[]>();
            var true_name = trav.Field("true_name").GetValue<string[]>();
            var isSouldOut = trav.Field("isSouldOut").GetValue<bool[]>();
            var sys = trav.Field("sys").GetValue<L2System>();

            for (int i = 0; i < 3; i++)
            {
                string text = trav.Method("exchangeItemName", item_id[i]).GetValue<string>();
                if (text != item_id[i])
                {
                    short num = 0;
                    sys.getFlag(sys.SeetNametoNo("02Items"), text, ref num);
                    if (num != 0)
                    {
                        if (item_id[i] == "Pistol-b")
                        {
                            isSouldOut[i] = sys.getItemNum("pistolBox") >= sys.getItemMax("pistolBox");
                        }
                        else
                        {
                            isSouldOut[i] = sys.getItemNum(true_name[i]) >= sys.getItemMax(item_id[i]);
                        }
                    }
                    else
                    {
                        isSouldOut[i] = true;
                    }
                }
                else if (item_id[i] == "Pepper")
                {
                    short num2 = 0;
                    sys.getFlag(0, "Pepper-b", ref num2);
                    isSouldOut[i] = num2 != 0;
                }
                else
                {
                    isSouldOut[i] = sys.getItemNum(true_name[i]) >= sys.getItemMax(item_id[i]);
                }
            }
            isSouldOut[3] = false;

            // Call drawItems via reflection
            trav.Method("drawItems").GetValue();

            return false;
        }
    }
}
