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

            // Handle BOTH freestanding chips AND dynamic enemy drops (dropItem) the same
            // way — gated purely on whether the chip's book flag is a registered glossary
            // location. Non-glossary / unregistered chips fall through to vanilla. The
            // "already reported" guard below is the duplicate-reward protection: an enemy
            // can drop several chips (or be re-killed), but only the first pickup delivers.
            int chipId = Traverse.Create(__instance).Field("chipId").GetValue<int>();
            int bookFlag = chipId > -1
                ? chipId
                : Traverse.Create(__instance).Field("itemValue").GetValue<int>();

            if (!GlossaryManager.TryGetLocation(bookFlag, out LocationID locId))
                return true; // not a registered glossary chip → vanilla behaviour

            var sys = Traverse.Create(__instance).Field("sys").GetValue<L2System>();
            var pl = Traverse.Create(__instance).Field("pl").GetValue<NewPlayer>();
            var core = Traverse.Create(__instance).Method("getL2Core").GetValue<L2SystemCore>();

            long apLoc = 430000L + (int)locId;

            // DECOUPLED MODEL: scanning a chip fires its location CHECK and delivers the
            // item placed THERE. It must NOT set the chip's own sheet-20 book flag — that
            // would unlock the chip's own vanilla entry (e.g. scanning Herja unlocking
            // Herja). Encyclopedia entries unlock ONLY from received ROMs (DeliverGlossaryRom).
            // The check is reported directly via CheckManager.NotifyLocation instead.

            // Already checked (this session or on the server)? Stay inert — no re-deliver,
            // no flags. (We can't use the book flag as the collected-marker any more.)
            bool reported = CheckManager.IsLocationReported(apLoc)
                || (ArchipelagoClient.ServerData != null
                    && ArchipelagoClient.ServerData.CheckedLocations != null
                    && ArchipelagoClient.ServerData.CheckedLocations.Contains(apLoc));
            if (reported)
            {
                __instance.itemLabel = "Nothing";
                __instance.itemGetFlags = new L2FlagBoxEnd[0];
                return false;
            }

            var scouted = ArchipelagoClientProvider.Client?.GetItemAtLocation(apLoc);

            // === FILLER PATH: own coins/weights/ammo dropped physically ===
            if (scouted != null && scouted.IsOwnItem && scouted.ItemName != null
                && ItemPotPatch.TryParseReward(scouted.ItemName, out _, out _))
            {
                Vector3 pos = Traverse.Create(__instance).Field("actionPosition").GetValue<Vector3>();
                pos.y += 1.5f;
                ItemPotPatch.DropFillerAt(sys, pos, scouted.ItemName);

                int seFiller = core.seManager.playSE(__instance.gameObject, 23);
                core.seManager.releaseGameObjectFromPlayer(seFiller);

                // Filler shows only the coin/weight pop-up — suppress the dialog prime
                // (reuse the pot-filler skip path in CheckManager.ReportLocation).
                ItemPotPatch.PotFillerDialog = true;
                CheckManager.NotifyLocation(locId);
                __instance.itemLabel = "Nothing";
                __instance.itemGetFlags = new L2FlagBoxEnd[0];
                return false;
            }

            // === OWN GLOSSARY ROM: deliver the entry directly (unlock + floating popup) ===
            // Detect via the SCOUT's id window (own item + registered glossary game_id), not the
            // display name. Same flow as an enemy-dropped chip — no AP icon, no get-item dialog —
            // and it unlocks the SHUFFLED entry (the ROM placed here), never the chip's own entry.
            if (GlossaryManager.IsOwnGlossaryRom(scouted))
            {
                int romGameId = GlossaryManager.RomGameId(scouted.ItemId);
                ItemGrantManager.DeliverGlossaryRom(sys, romGameId);
                ItemPotPatch.PotFillerDialog = true;   // popup-only: skip the dialog prime
                CheckManager.NotifyLocation(locId);
                __instance.itemLabel = "Nothing";
                __instance.itemGetFlags = new L2FlagBoxEnd[0];
                Plugin.Log.LogInfo($"[GLOSSARY] {locId}: own ROM {scouted.ItemName} (gameId {romGameId}) → floating delivery");
                return false;
            }

            // === ITEM PATH: own non-ROM LM item, or AP/foreign placeholder ===
            var sr = SceneRandomizer.Instance;
            ItemID placed = sr != null ? sr.GetItemIDForLocation(locId) : ItemID.None;
            int placedRaw = (int)placed;
            bool isOwn = placedRaw != (int)ItemID.None && placedRaw < 1000;
            ItemInfo info = isOwn ? ItemDB.GetItemInfo(placed) : null;
            if (info == null) isOwn = false;

            var getFlags = new List<L2FlagBoxEnd>();
            string label = "AP Item";

            if (isOwn && sr != null)
            {
                var itemFlags = sr.CreateGetFlags(placed, info);
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

            // NO book flag appended — decoupled. The check is reported directly.
            __instance.itemLabel = label;
            __instance.itemValue = 1;
            __instance.itemGetFlags = getFlags.ToArray();

            CheckManager.NotifyLocation(locId);

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
