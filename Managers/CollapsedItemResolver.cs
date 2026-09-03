using System.Collections.Generic;
using L2Base;
using LaMulana2RandomizerShared;
using LM2RandomiserMod;

namespace LaMulana2Archipelago.Managers
{
    /// <summary>
    /// Turns the apworld's collapsed item families back into a concrete
    /// per-instance ItemID.
    ///
    /// The apworld ships the ten area Sacred Orbs, the ten bonus orbs, the
    /// twelve Crystal Skulls and the nine Ankh Jewels under ONE AP id per
    /// family; the real game id rides on Item.lm2_game_id, which only ever
    /// reaches our own seed's placements. So an own-world find still arrives
    /// numbered (chest get-flags built from the seed), but a REMOTE player
    /// finding one of ours sends the collapsed code instead:
    ///
    ///   420823  Sacred Orb          420009  Crystal Skull
    ///   420824  Sacred Orb (Bonus)  420079  Ankh Jewel
    ///
    /// None of those can be granted as-is. 823/824 aren't game items at all
    /// (ItemGrantManager discarded them outright), and the generic Crystal
    /// Skull / Ankh Jewel entries carry the wrong (sheet, flag), so they skip
    /// the per-instance flag, the (0,32) skull counter, the (3,30) difficulty
    /// tally and auto-place-skull.
    ///
    /// Collapsing was a labelling change, not a feature change, so the fix is
    /// to hand the grant path a real member of the family and let every
    /// numbered branch run unchanged. Which member is arbitrary — the game
    /// treats them identically and AP logic only ever counts them — so take
    /// the first candidate that is neither:
    ///
    ///   • reserved by our own seed (SeedFlagMapBuilder.LocationToItem). Those
    ///     ids arrive through their own pickup, and their flag is registered in
    ///     LocationFlagMap's numeric map — stamping it here would report that
    ///     location as checked without the player ever opening it. This
    ///     exclusion is a correctness requirement, not a tidiness one.
    ///
    ///   • already stamped (its ItemDB flag is set). Reading live flag state is
    ///     what keeps the choice stable across a reload and what makes two orbs
    ///     received in one session land on different slots.
    ///
    /// The two sets line up exactly: each instance id is placed once across the
    /// whole multiworld, so the ids NOT reserved by our seed are precisely the
    /// ones that can still arrive collapsed.
    /// </summary>
    internal static class CollapsedItemResolver
    {
        /// <summary>Outcome of resolving one incoming AP id.</summary>
        internal enum Outcome
        {
            /// <summary>Not a collapsed family id — grant the id as it arrived.</summary>
            NotCollapsed,
            /// <summary>Use the returned instance in place of the collapsed id.</summary>
            Resolved,
            /// <summary>
            /// Same, but the instance was one the seed placed in OUR world: every
            /// copy that could arrive from elsewhere is already accounted for, so
            /// this grant consumes a world placement instead. Stamping its flag
            /// takes the chest/pickup out of the world (itemActiveFlag gates on
            /// that flag being 0), so the caller must also report the returned
            /// location's check — its item has just been handed over.
            /// </summary>
            ClaimedFromWorld,
            /// <summary>Collapsed, but the seed map isn't up yet — grant generically.</summary>
            Unavailable,
            /// <summary>
            /// This seed's options never collapse the family, so the id cannot have
            /// come from its pool. Discard it rather than pick a stand-in.
            /// </summary>
            NotInSeed,
            /// <summary>
            /// Every member of the family is now held, so this copy is one more than
            /// the game has items for. Discard it: the family sizes ARE the game's
            /// ceilings, and exceeding them corrupts state (a thirteenth Crystal
            /// Skull pushes the status bar's crystal icon past the end of its array
            /// and throws every time the item menu draws).
            /// </summary>
            Full,
        }

        private static readonly ItemID[] SacredOrbInstances =
        {
            ItemID.SacredOrb0, ItemID.SacredOrb1, ItemID.SacredOrb2, ItemID.SacredOrb3, ItemID.SacredOrb4,
            ItemID.SacredOrb5, ItemID.SacredOrb6, ItemID.SacredOrb7, ItemID.SacredOrb8, ItemID.SacredOrb9
        };

        private static readonly ItemID[] BonusOrbInstances =
        {
            ItemID.SacredOrb10, ItemID.SacredOrb11, ItemID.SacredOrb12, ItemID.SacredOrb13, ItemID.SacredOrb14,
            ItemID.SacredOrb15, ItemID.SacredOrb16, ItemID.SacredOrb17, ItemID.SacredOrb18, ItemID.SacredOrb19
        };

        private static readonly ItemID[] CrystalSkullInstances =
        {
            ItemID.CrystalSkull1, ItemID.CrystalSkull2, ItemID.CrystalSkull3, ItemID.CrystalSkull4,
            ItemID.CrystalSkull5, ItemID.CrystalSkull6, ItemID.CrystalSkull7, ItemID.CrystalSkull8,
            ItemID.CrystalSkull9, ItemID.CrystalSkull10, ItemID.CrystalSkull11, ItemID.CrystalSkull12
        };

        private static readonly ItemID[] AnkhJewelInstances =
        {
            ItemID.AnkhJewel1, ItemID.AnkhJewel2, ItemID.AnkhJewel3, ItemID.AnkhJewel4, ItemID.AnkhJewel5,
            ItemID.AnkhJewel6, ItemID.AnkhJewel7, ItemID.AnkhJewel8, ItemID.AnkhJewel9
        };

        /// <summary>
        /// The per-instance ids a collapsed game id stands for, or null when this
        /// id isn't a collapsed family (the overwhelmingly common case).
        /// </summary>
        private static ItemID[] FamilyFor(int gameId)
        {
            switch (gameId)
            {
                case (int)ItemID.SacredOrb:      return SacredOrbInstances;
                case (int)ItemID.SacredOrbBonus: return BonusOrbInstances;
                case (int)ItemID.CrystalSkull:   return CrystalSkullInstances;
                case (int)ItemID.AnkhJewel:      return AnkhJewelInstances;
                default:                         return null;
            }
        }

        /// <summary>
        /// Resolves a collapsed AP game id to the instance the grant should use.
        /// See <see cref="Outcome"/> for what the caller does with each result.
        /// </summary>
        public static Outcome Resolve(L2System sys, int gameId, out ItemID instance,
                                      out LocationID worldLocation)
        {
            instance = ItemID.None;
            worldLocation = LocationID.None;

            ItemID[] family = FamilyFor(gameId);
            if (family == null || sys == null)
                return Outcome.NotCollapsed;

            // guardian_specific_ankhs un-collapses the jewels: build_item_pool gives
            // each of the nine its own boss name and AP id, so 420079 is not in this
            // seed's pool and can only be a cheat send. Standing in for one would
            // hand over a specific jewel — the generic name delivering AnkhJewel3,
            // i.e. Kujata's — while the dialog still reads "Ankh Jewel", leaving the
            // player no way to know which guardian they can now reach. Refuse it and
            // let them send the named jewel they actually want.
            if (gameId == (int)ItemID.AnkhJewel
                && Patches.GuardianSpecificAnkhPatch.GuardianSpecificAnkhsEnabled)
            {
                Plugin.Log.LogWarning("[ITEM] Generic Ankh Jewel refused: this seed uses "
                    + "guardian_specific_ankhs, so jewels are only ever sent by boss name "
                    + "(\"Ankh Jewel (Vritra)\", ...).");
                return Outcome.NotInSeed;
            }

            // Flag map not built yet (a grant racing connect). Without the
            // reserved set we could stamp a flag the seed uses as a location
            // trigger and report a check the player never made, so degrade to a
            // generic grant — the effect and the count still land.
            if (SeedFlagMapBuilder.LocationToItem.Count == 0)
            {
                Plugin.Log.LogWarning("[ITEM] Collapsed gameId " + gameId
                    + " arrived before the seed placement map was built; granting generically.");
                return Outcome.Unavailable;
            }

            Dictionary<ItemID, LocationID> reserved = ReservedBySeed(family);

            // Pass 1 — instances the seed put in someone else's world. There are
            // exactly as many of these as copies that can legitimately arrive, so
            // every real delivery lands here and never disturbs our own placements.
            for (int i = 0; i < family.Length; i++)
            {
                ItemID candidate = family[i];
                if (reserved.ContainsKey(candidate) || IsClaimed(sys, candidate))
                    continue;

                instance = candidate;
                return Outcome.Resolved;
            }

            // Pass 2 — surplus (a cheat-console send, or a rescue when the seed
            // turns out unbeatable). Nothing is left in other worlds, so pay for it
            // out of our own: hand over a placement we haven't collected yet and let
            // the caller report its check. The item leaves the world rather than
            // being duplicated, which keeps the family at its true size.
            for (int i = 0; i < family.Length; i++)
            {
                ItemID candidate = family[i];
                LocationID location;
                if (!reserved.TryGetValue(candidate, out location) || IsClaimed(sys, candidate))
                    continue;

                instance = candidate;
                worldLocation = location;
                Plugin.Log.LogInfo("[ITEM] Collapsed family for gameId " + gameId
                    + " has nothing left from other worlds; consuming own placement "
                    + candidate + " at " + location + ".");
                return Outcome.ClaimedFromWorld;
            }

            // Both passes dry: all of the family's instances are stamped, so the
            // player holds (or has used) every one the game has.
            Plugin.Log.LogWarning("[ITEM] Collapsed family for gameId " + gameId
                + " is full (all " + family.Length
                + " instances accounted for); discarding this copy.");
            return Outcome.Full;
        }

        /// <summary>
        /// Members of this family that our own seed placed in our own world, mapped
        /// to the location holding each. A normal delivery must never take one —
        /// these flags double as location-check triggers, so stamping one would
        /// report a check the player never made. Only pass 2 spends them, and it
        /// reports that check deliberately.
        /// </summary>
        private static Dictionary<ItemID, LocationID> ReservedBySeed(ItemID[] family)
        {
            Dictionary<ItemID, LocationID> reserved = new Dictionary<ItemID, LocationID>();

            foreach (KeyValuePair<LocationID, ItemID> kvp in SeedFlagMapBuilder.LocationToItem)
            {
                for (int i = 0; i < family.Length; i++)
                {
                    if (kvp.Value == family[i])
                    {
                        reserved[kvp.Value] = kvp.Key;
                        break;
                    }
                }
            }

            return reserved;
        }

        /// <summary>
        /// True when this instance's unique flag is already set — either the
        /// player has it, or an earlier collapsed grant took the slot.
        /// </summary>
        private static bool IsClaimed(L2System sys, ItemID id)
        {
            ItemInfo info = ItemDB.GetItemInfo(id);
            if (info == null || info.ItemFlag < 0)
                return true; // unusable as a stand-in

            short value = 0;
            sys.getFlag(info.ItemSheet, info.ItemFlag, ref value);
            return value != 0;
        }
    }
}
