using System.Collections.Generic;
using HarmonyLib;
using L2Base;
using L2Flag;
using L2Hit;
using LaMulana2Archipelago.Archipelago;
using LaMulana2Archipelago.Managers;
using LaMulana2RandomizerShared;
using LM2RandomiserMod;
using UnityEngine;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Glossanity replacement. Freestanding glossary chips (MonsterChipScript,
    /// e.g. the R Chip / Fafnir chip) hardcode their pickup; we replace it so the
    /// chip delivers the AP-placed item, FreeStanding-style.
    ///
    /// Delivery (AbstractItemBase pickup task): itemGetAction() → sys.setItem(itemLabel)
    /// → sys.setEffectFlag(itemGetFlags). The grant is driven by itemLabel +
    /// itemGetFlags even when this Prefix returns false, so we configure those.
    ///
    /// Three cases, matching ItemPotPatch:
    ///   - Filler (own coins/weights/ammo): physically dropped via
    ///     ItemPotPatch.DropFillerAt — NOT via CreateGetFlags (a filler ItemID has
    ///     no valid flag backing; feeding it to setEffectFlag→addFlag crashes with
    ///     IndexOutOfRange). Book flag set directly; itemGetFlags left empty.
    ///   - Own LM item (Life Sigil, …): itemLabel = BoxName, itemGetFlags =
    ///     CreateGetFlags(item) + book flag → granted natively with the real sprite.
    ///   - AP / foreign: itemLabel = "AP Item" + book flag → server echo delivers.
    /// The book flag (sheet 20) fires the AP check and unlocks the encyclopedia.
    ///
    /// Scope: chips registered in glossary_flag_map; dropItem (dynamic enemy drops)
    /// left to vanilla. Follow-up: in-world (floor) sprite swap; Hand-Scanner-only
    /// entries (scanItem).
    /// </summary>
    [HarmonyPatch(typeof(MonsterChipScript), "itemGetAction")]
    internal static class MonsterChipGlossaryPatch
    {
        private const int BookSheet = 20;

        static bool Prefix(MonsterChipScript __instance)
        {
            if (!GlossaryManager.Enabled) return true;

            // Defensive: leave dynamically-dropped chips (enemy kills) to vanilla.
            bool dropItem = Traverse.Create(__instance).Field("dropItem").GetValue<bool>();
            if (dropItem) return true;

            int chipId = Traverse.Create(__instance).Field("chipId").GetValue<int>();
            int bookFlag = chipId > -1
                ? chipId
                : Traverse.Create(__instance).Field("itemValue").GetValue<int>();

            if (!GlossaryManager.TryGetLocation(bookFlag, out LocationID locId))
                return true; // not a registered glossary chip → vanilla behaviour

            var sys = Traverse.Create(__instance).Field("sys").GetValue<L2System>();
            var pl = Traverse.Create(__instance).Field("pl").GetValue<NewPlayer>();
            var core = Traverse.Create(__instance).Method("getL2Core").GetValue<L2SystemCore>();

            // Already collected? Defer to vanilla (its `num == 0` guard no-ops).
            short cur = 0;
            sys.getFlag(BookSheet, bookFlag, ref cur);
            if (cur != 0) return true;

            // Scout the AP location for the placed item.
            long apLoc = 430000L + (int)locId;
            var scouted = ArchipelagoClientProvider.Client?.GetItemAtLocation(apLoc);

            // === FILLER PATH: own coins/weights/ammo dropped physically ===
            if (scouted != null && scouted.IsOwnItem && scouted.ItemName != null
                && ItemPotPatch.TryParseReward(scouted.ItemName, out _, out _))
            {
                // Book flag directly (the only check trigger here) — filler has no
                // itemGetFlags, so it can't carry the location flag.
                sys.setFlagData(BookSheet, bookFlag, 1);

                Vector3 pos = Traverse.Create(__instance).Field("actionPosition").GetValue<Vector3>();
                pos.y += 1.5f;
                ItemPotPatch.DropFillerAt(sys, pos, scouted.ItemName);

                int seFiller = core.seManager.playSE(__instance.gameObject, 23);
                core.seManager.releaseGameObjectFromPlayer(seFiller);

                // Neutralise the base grant: setItem("Nothing") no-ops, no flags.
                __instance.itemLabel = "Nothing";
                __instance.itemGetFlags = new L2FlagBoxEnd[0];
                return false;
            }

            // === ITEM PATH: own LM item or AP/foreign placeholder ===
            var sr = SceneRandomizer.Instance;
            ItemID placed = sr != null ? sr.GetItemIDForLocation(locId) : ItemID.None;

            bool isOwn = placed != ItemID.None && (int)placed < 1000;
            ItemInfo info = isOwn ? ItemDB.GetItemInfo(placed) : null;
            if (info == null) isOwn = false;

            var getFlags = new List<L2FlagBoxEnd>();
            string label = "AP Item";

            if (isOwn && sr != null)
            {
                var itemFlags = sr.CreateGetFlags(placed, info);
                // Guard: a non-flag-backed item (stray filler, etc.) yields an
                // out-of-range (sheet,flag) that crashes setEffectFlag→addFlag.
                // If any entry is invalid, fall back to the AP-echo path.
                if (AllFlagsValid(sys, itemFlags))
                {
                    getFlags.AddRange(itemFlags);
                    label = !string.IsNullOrEmpty(info.BoxName) ? info.BoxName : "AP Item";
                }
                else
                {
                    isOwn = false;
                    Plugin.Log.LogWarning($"[GLOSSARY] {locId}: CreateGetFlags for {placed} produced an invalid flag — using AP Item path");
                }
            }

            // Book flag last → fires the AP check + unlocks the encyclopedia entry.
            getFlags.Add(new L2FlagBoxEnd { seet_no1 = BookSheet, flag_no1 = bookFlag, calcu = CALCU.EQR, data = 1 });

            __instance.itemLabel = label;
            __instance.itemValue = 1;
            __instance.itemGetFlags = getFlags.ToArray();

            int slot = core.seManager.playSE(__instance.gameObject, 39);
            core.seManager.releaseGameObjectFromPlayer(slot);
            pl.setActionOder(PLAYERACTIONODER.getitem);
            pl.setGetItem(ref label);
            var iconData = L2SystemCore.getItemData(isOwn ? label : "AP Item");
            if (iconData != null)
                pl.setGetItemIcon(iconData);

            return false; // skip vanilla; base delivers the placed item
        }

        private static bool AllFlagsValid(L2System sys, L2FlagBoxEnd[] flags)
        {
            if (flags == null) return false;
            var fsys = sys.getFlagSys();
            foreach (var f in flags)
                if (!GetFlagSystemPatch.IsFlagIndexValid(fsys, f.seet_no1, f.flag_no1))
                    return false;
            return true;
        }
    }
}
