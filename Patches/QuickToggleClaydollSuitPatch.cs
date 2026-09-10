using System;
using HarmonyLib;
using L2Base;
using L2Hit;
using UnityEngine;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Quality-of-life: pressing Previous Weapon + Next Weapon together
    /// (<c>L2KEYS.lchange</c> + <c>L2KEYS.rchange</c>, LB+RB by default) equips or
    /// removes the Claydoll Suit in place, without opening the equipment menu.
    /// Taking it off restores whatever costume was being worn when it went on.
    ///
    /// Hook point: <c>L2System.slideMainWeapon()</c>.  <c>L2STATUS.Status.Farst()</c>
    /// is its only caller —
    ///
    ///   if (getL2Keys(lchange, DOWN))      slideMainWeapon(0);
    ///   else if (getL2Keys(rchange, DOWN)) slideMainWeapon(1);
    ///
    /// — so a prefix there inherits every gate vanilla already applies to the
    /// weapon-cycle keys (title screen, menu open, key block, <c>isNextMainAttack</c>)
    /// and knows exactly which of the two keys fired.  If the *other* key is held at
    /// that moment the press is a chord rather than a weapon cycle: we toggle the
    /// suit and return false so the cycle never happens.
    ///
    /// The two keys are almost never pressed on the same frame, so the first half of
    /// the chord normally lands as an ordinary <c>slideMainWeapon</c> that has already
    /// changed the weapon by the time the second half arrives.  Every non-chord call
    /// therefore records the weapon it is about to move away from, and a chord that
    /// follows within <see cref="ComboGraceFrames"/> puts it back.  Outside that
    /// window the earlier press is taken to have been a deliberate weapon change and
    /// is left alone — only the suit toggles.
    ///
    /// The eligibility test mirrors <c>ItemMenu.IsEquipChengeAbleNow()</c> for a
    /// fashion item, and the equip itself mirrors the menu's own sequence, so the
    /// chord can never reach a state the menu could not: sealed suit (dogoSeal),
    /// mid-attack, swimming, dashing, ladders and cutscenes all refuse it.
    /// </summary>
    [HarmonyPatch(typeof(L2System), nameof(L2System.slideMainWeapon))]
    internal static class QuickToggleClaydollSuitPatch
    {
        /// <summary>Master switch — <c>Plugin</c> binds this to a BepInEx config entry.</summary>
        internal static bool Enabled = true;

        /// <summary>
        /// How long after a lone weapon-cycle press the opposite key still counts as
        /// the other half of a chord (and so undoes that cycle).  ~1/3s at the game's
        /// 60fps step: comfortably longer than any human "both at once", far shorter
        /// than a deliberate cycle-then-toggle.
        /// </summary>
        private const int ComboGraceFrames = 20;

        /// <summary>
        /// Costume ids (<c>ItemMenu.FashionData</c> / <c>NewPlayer.changeCostume</c>)
        /// mapped to the item ids the equip flags are keyed on.  Index 0 is "no
        /// costume", which owns no item.
        /// </summary>
        private static readonly string[] FashionIdByClothesNo =
        {
            null,               // 0 — Lumisa's default outfit
            "Clay Doll",        // 1
            "Kimono Cowgirl",   // 2
            "Valkyria",         // 3
            "Little Devil",     // 4
            "Eastern Europe",   // 5
            "Fish Suit",        // 6 (DLC)
        };

        internal const int ClayDollClothesNo = 1;
        internal const int DefaultClothesNo = 0;

        /// <summary>SE the equipment menu plays on a successful equip / on a refusal.</summary>
        private const int SeEquip = 57;
        private const int SeRefuse = 91;

        /// <summary>
        /// Costume worn immediately before the Claydoll Suit went on, restored when
        /// the chord takes it off.  Maintained by <see cref="ClaydollPreviousCostumeTracker"/>
        /// rather than written here, so that it is also correct when the suit was put
        /// on through the equipment menu instead of the chord.  In memory only: a
        /// fresh launch (or a save load, which re-runs <c>changeCostume</c>) starts
        /// from the default outfit.
        /// </summary>
        internal static int ClothesBeforeSuit = DefaultClothesNo;

        // Weapon the last non-chord press moved away from, and the frame it did so.
        // _pendingFrame < 0 means "nothing to undo".
        private static MAINWEAPON _weaponBeforePress = MAINWEAPON.NON;
        private static int _pendingFrame = -1;

        static bool Prefix(L2System __instance, int slide)
        {
            if (!Enabled || __instance == null)
                return true;

            try
            {
                // Status.Farst() calls slide 0 for lchange and 1 for rchange, so the
                // key that produced this call is known without re-reading both.
                L2KEYS otherKey = (slide == 0) ? L2KEYS.rchange : L2KEYS.lchange;

                if (!__instance.getL2Keys(otherKey, KEYSTATE.NORMAL))
                {
                    // Ordinary weapon cycle. Remember what it is about to leave, in
                    // case the opposite key arrives a frame or two from now.
                    _weaponBeforePress = __instance.getMainWeapon();
                    _pendingFrame = Time.frameCount;
                    return true;
                }

                // Chord. Undo the cycle the first half of it caused, if that was recent.
                if (_pendingFrame >= 0 && Time.frameCount - _pendingFrame <= ComboGraceFrames)
                    __instance.setMainWeapon(_weaponBeforePress);
                _pendingFrame = -1;

                ToggleClaydoll(__instance);
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[CLAYDOLL] Quick toggle failed: " + ex.Message);
                return true;
            }
        }

        private static void ToggleClaydoll(L2System sys)
        {
            NewPlayer player = sys.getPlayer();
            if (player == null)
                return;

            bool wearing = sys.isEquipItem(FashionIdByClothesNo[ClayDollClothesNo]);

            // Nothing to put on if it hasn't been found yet. Taking it off is allowed
            // regardless, so the player can never be stranded in a suit they can't shed.
            if (!wearing && sys.isHaveItem(FashionIdByClothesNo[ClayDollClothesNo]) == 0)
                return;

            if (!CanChangeCostumeNow(sys, player))
            {
                PlaySE(sys, SeRefuse);
                return;
            }

            if (wearing)
            {
                int restore = ClothesBeforeSuit;

                // The suit can never be its own predecessor, and a value from an older
                // build of the mod (or a costume that no longer exists) must not strand
                // the player mid-change: fall back to the default outfit.
                if (restore == ClayDollClothesNo || restore < 0 || restore >= FashionIdByClothesNo.Length)
                    restore = DefaultClothesNo;

                WearCostume(sys, player, restore);
            }
            else
            {
                WearCostume(sys, player, ClayDollClothesNo);
            }

            PlaySE(sys, SeEquip);
        }

        /// <summary>
        /// Switches to <paramref name="clothesNo"/> the way <c>ItemMenu</c> does:
        /// clear every fashion equip flag, set the target's, write the clothes flag,
        /// then hand the id to <c>changeCostume</c>.
        ///
        /// The order matters.  <c>changeCostume</c> validates ownership itself — an
        /// unowned closet costume, or the Fish Suit without the DLC, is coerced back
        /// to 0 *and* has its equip flag and the clothes flag cleared on the way out —
        /// so driving it last means a restore to a costume the player can no longer
        /// wear degrades to the default outfit instead of desyncing the flags.
        /// </summary>
        private static void WearCostume(L2System sys, NewPlayer player, int clothesNo)
        {
            for (int i = 1; i < FashionIdByClothesNo.Length; i++)
                sys.unEquipItem(FashionIdByClothesNo[i]);

            string id = FashionIdByClothesNo[clothesNo];
            if (id != null)
                sys.equipItem(id, true);

            sys.setNowClothesNo(clothesNo);
            player.changeCostume((byte)clothesNo);
        }

        /// <summary>
        /// <c>ItemMenu.IsEquipChengeAbleNow()</c> for the Claydoll Suit, reproduced
        /// here because the original is private to the menu.
        /// </summary>
        private static bool CanChangeCostumeNow(L2System sys, NewPlayer player)
        {
            ItemData item = L2SystemCore.getItemData(FashionIdByClothesNo[ClayDollClothesNo]);
            if (item == null)
                return false;

            // isItemUsable(ClayDoll) == !dogoSeal — the Eternal Prison equipment seal.
            if (!player.isItemUsable(item))
                return false;

            if (sys.isActionEvent(PLAYERACTION.main) ||
                sys.isActionEvent(PLAYERACTION.sub) ||
                sys.isActionEvent(PLAYERACTION.swiming))
                return false;

            if (sys.isHaveItem("G Band") != 0 && player.isDash())
                return false;

            return player.canCostumeChange();
        }

        private static void PlaySE(L2System sys, int seNo)
        {
            try
            {
                L2SystemCore core = sys.getL2SystemCore();
                if (core == null || core.seManager == null)
                    return;

                int handle = core.seManager.playSE(null, seNo);
                core.seManager.releaseGameObjectFromPlayer(handle);
            }
            catch { }
        }
    }

    /// <summary>
    /// Keeps <see cref="QuickToggleClaydollSuitPatch.ClothesBeforeSuit"/> pointing at the
    /// costume the player was wearing when the Claydoll Suit went on.
    ///
    /// Recording this inside the chord itself would only cover suits put on by the
    /// chord: equip it from the equipment menu instead and the memory would still
    /// hold whatever the chord last saw, so chording it off would restore a costume
    /// the player had since taken off.  <c>NewPlayer.changeCostume()</c> is the one
    /// funnel every route goes through — chord, menu, save load, the forced strip in
    /// <c>NewPlayer</c> when the suit is sealed — so watching the transition into
    /// costume 1 there keeps the memory correct however the suit was equipped.
    ///
    /// It is watched as a real before/after transition rather than read off the
    /// argument, because <c>changeCostume</c> can bail (already wearing it, or the
    /// suit is sealed / unowned) and can coerce its own argument to 0.  Only a
    /// <c>nowCosId</c> that actually became 1 counts.
    /// </summary>
    [HarmonyPatch(typeof(NewPlayer), nameof(NewPlayer.changeCostume))]
    internal static class ClaydollPreviousCostumeTracker
    {
        // nowCosId is `protected byte` with no accessor. Resolved once into a
        // compiled delegate; changeCostume is rare enough that the cost is moot.
        private static readonly AccessTools.FieldRef<NewPlayer, byte> NowCosId = ResolveNowCosId();

        private static AccessTools.FieldRef<NewPlayer, byte> ResolveNowCosId()
        {
            try
            {
                return AccessTools.FieldRefAccess<NewPlayer, byte>("nowCosId");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[CLAYDOLL] nowCosId unavailable, costume memory disabled: " + ex.Message);
                return null;
            }
        }

        static void Prefix(NewPlayer __instance, out int __state)
        {
            __state = (NowCosId != null && __instance != null)
                ? NowCosId(__instance)
                : QuickToggleClaydollSuitPatch.ClayDollClothesNo; // never records
        }

        static void Postfix(NewPlayer __instance, int __state)
        {
            if (NowCosId == null || __instance == null)
                return;

            if (NowCosId(__instance) == QuickToggleClaydollSuitPatch.ClayDollClothesNo &&
                __state != QuickToggleClaydollSuitPatch.ClayDollClothesNo)
            {
                QuickToggleClaydollSuitPatch.ClothesBeforeSuit = __state;
            }
        }
    }
}
