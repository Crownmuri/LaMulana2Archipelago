using System;
using System.Collections.Generic;
using LaMulana2RandomizerShared;

namespace LaMulana2Archipelago
{
    /// <summary>
    /// Central mapping between La-Mulana 2 ItemID enums
    /// and Archipelago item IDs.
    ///
    /// LM2 ItemID (enum, int) <-> AP Item ID (int)
    ///
    /// This table is authoritative for:
    /// - Converting AP-generated placements into LM2 seed data
    /// - Converting received AP items into LM2 items (if needed)
    /// </summary>
    public static class ItemTable
    {
        /// <summary>
        /// Forward mapping:
        /// LM2 ItemID -> AP item ID
        /// </summary>
        private static readonly Dictionary<ItemID, int> _map =
            new Dictionary<ItemID, int>
            {
                { ItemID.HandScanner, 420000 },
                { ItemID.DjedPillar, 420001 },
                { ItemID.Mjolnir, 420002 },
                { ItemID.Beherit, 420003 },
                { ItemID.AncientBattery, 420004 },
                { ItemID.LampofTime, 420005 },
                { ItemID.PochetteKey, 420006 },
                { ItemID.PyramidCrystal, 420007 },
                { ItemID.Vessel, 420008 },
                { ItemID.Pepper, 420009 },
                { ItemID.EggofCreation, 420010 },
                { ItemID.GiantsFlute, 420011 },
                { ItemID.CogofAntiquity, 420012 },
                { ItemID.MulanaTalisman, 420013 },
                { ItemID.MobileSuperx3P, 420014 },
                { ItemID.ShellHorn, 420015 },
                { ItemID.HolyGrail, 420016 },
                { ItemID.FairyPass, 420017 },
                { ItemID.Gloves, 420018 },
                { ItemID.DinosaurFigure, 420019 },
                { ItemID.GaleFibula, 420020 },
                { ItemID.FlameTorc, 420021 },
                { ItemID.Vajra, 420022 },
                { ItemID.PowerBand, 420023 },
                { ItemID.BronzeMirror, 420024 },
                { ItemID.Perfume, 420025 },
                { ItemID.IceCloak, 420026 },
                { ItemID.NemeanFur, 420027 },
                { ItemID.Gauntlet, 420028 },
                { ItemID.Anchor, 420029 },
                { ItemID.FreyasPendant, 420030 },
                { ItemID.TotemPole, 420031 },
                { ItemID.GrappleClaw, 420032 },
                { ItemID.Spaulder, 420033 },
                { ItemID.Scalesphere, 420034 },
                { ItemID.Crucifix, 420035 },
                { ItemID.GaneshaTalisman, 420036 },
                { ItemID.MaatsFeather, 420037 },
                { ItemID.Ring, 420038 },
                { ItemID.Bracelet, 420039 },
                { ItemID.Feather, 420040 },
                { ItemID.Scriptures, 420041 },
                { ItemID.FreysShip, 420042 },
                { ItemID.Codices, 420043 },
                { ItemID.SnowShoes, 420044 },
                { ItemID.Harp, 420045 },
                { ItemID.BookoftheDead, 420046 },
                { ItemID.LightScythe, 420047 },
                { ItemID.DestinyTablet, 420048 },
                { ItemID.SecretTreasureofLife, 420049 },
                { ItemID.OriginSigil, 420050 },
                { ItemID.BirthSigil, 420051 },
                { ItemID.LifeSigil, 420052 },
                { ItemID.DeathSigil, 420053 },
                { ItemID.ClaydollSuit, 420054 },
                { ItemID.Whip1, 420055 },
                { ItemID.Whip2, 420056 },
                { ItemID.Whip3, 420057 },
                { ItemID.Knife, 420058 },
                { ItemID.Rapier, 420059 },
                { ItemID.Axe, 420060 },
                { ItemID.Katana, 420061 },
                { ItemID.Shuriken, 420062 },
                { ItemID.RollingShuriken, 420063 },
                { ItemID.EarthSpear, 420064 },
                { ItemID.Flare, 420065 },
                { ItemID.Bomb, 420066 },
                { ItemID.Chakram, 420067 },
                { ItemID.Caltrops, 420068 },
                { ItemID.Pistol, 420069 },
                { ItemID.Shield1, 420070 },
                { ItemID.Shield2, 420071 },
                { ItemID.Shield3, 420072 },
                { ItemID.Xelputter, 420073 },
                { ItemID.YagooMapReader, 420074 },
                { ItemID.YagooMapStreet, 420075 },
                { ItemID.TextTrax, 420076 },
                { ItemID.RuinsEncylopedia, 420077 },
                { ItemID.Mantra, 420078 },
                { ItemID.Guild, 420079 },
                { ItemID.EngaMusica, 420080 },
                { ItemID.BeoEglana, 420081 },
                { ItemID.Alert, 420082 },
                { ItemID.Snapshot, 420083 },
                { ItemID.SkullReader, 420084 },
                { ItemID.RaceScanner, 420085 },
                { ItemID.DeathVillage, 420086 },
                { ItemID.RoseandCamelia, 420087 },
                { ItemID.SpaceCapstarII, 420088 },
                { ItemID.LonelyHouseMoving, 420089 },
                { ItemID.MekuriMaster, 420090 },
                { ItemID.BounceShot, 420091 },
                { ItemID.MiracleWitch, 420092 },
                { ItemID.FutureDevelopmentCompany, 420093 },
                { ItemID.LaMulana, 420094 },
                { ItemID.LaMulana2, 420095 },
                { ItemID.SacredOrb0, 420096 },
                { ItemID.SacredOrb1, 420097 },
                { ItemID.SacredOrb2, 420098 },
                { ItemID.SacredOrb3, 420099 },
                { ItemID.SacredOrb4, 420100 },
                { ItemID.SacredOrb5, 420101 },
                { ItemID.SacredOrb6, 420102 },
                { ItemID.SacredOrb7, 420103 },
                { ItemID.SacredOrb8, 420104 },
                { ItemID.SacredOrb9, 420105 },
                { ItemID.Map1, 420106 },
                { ItemID.Map2, 420107 },
                { ItemID.Map3, 420108 },
                { ItemID.Map4, 420109 },
                { ItemID.Map5, 420110 },
                { ItemID.Map6, 420111 },
                { ItemID.Map7, 420112 },
                { ItemID.Map8, 420113 },
                { ItemID.Map9, 420114 },
                { ItemID.Map10, 420115 },
                { ItemID.Map11, 420116 },
                { ItemID.Map12, 420117 },
                { ItemID.Map13, 420118 },
                { ItemID.Map14, 420119 },
                { ItemID.Map15, 420120 },
                { ItemID.Map16, 420121 },
                { ItemID.AnkhJewel1, 420122 },
                { ItemID.AnkhJewel2, 420123 },
                { ItemID.AnkhJewel3, 420124 },
                { ItemID.AnkhJewel4, 420125 },
                { ItemID.AnkhJewel5, 420126 },
                { ItemID.AnkhJewel6, 420127 },
                { ItemID.AnkhJewel7, 420128 },
                { ItemID.AnkhJewel8, 420129 },
                { ItemID.AnkhJewel9, 420130 },
                { ItemID.CrystalSkull1, 420131 },
                { ItemID.CrystalSkull2, 420132 },
                { ItemID.CrystalSkull3, 420133 },
                { ItemID.CrystalSkull4, 420134 },
                { ItemID.CrystalSkull5, 420135 },
                { ItemID.CrystalSkull6, 420136 },
                { ItemID.CrystalSkull7, 420137 },
                { ItemID.CrystalSkull8, 420138 },
                { ItemID.CrystalSkull9, 420139 },
                { ItemID.CrystalSkull10, 420140 },
                { ItemID.CrystalSkull11, 420141 },
                { ItemID.CrystalSkull12, 420142 },
                { ItemID.Heaven, 420143 },
                { ItemID.Earth, 420144 },
                { ItemID.Sun, 420145 },
                { ItemID.Moon, 420146 },
                { ItemID.Sea, 420147 },
                { ItemID.Fire, 420148 },
                { ItemID.Wind, 420149 },
                { ItemID.Mother, 420150 },
                { ItemID.Child, 420151 },
                { ItemID.Night, 420152 },
                { ItemID.Research1, 420153 },
                { ItemID.Research2, 420154 },
                { ItemID.Research3, 420155 },
                { ItemID.Research4, 420156 },
                { ItemID.Research5, 420157 },
                { ItemID.Research6, 420158 },
                { ItemID.Research7, 420159 },
                { ItemID.Research8, 420160 },
                { ItemID.Research9, 420161 },
                { ItemID.Research10, 420162 },
                { ItemID.ShurikenAmmo, 420163 },
                { ItemID.RollingShurikenAmmo, 420164 },
                { ItemID.EarthSpearAmmo, 420165 },
                { ItemID.FlareAmmo, 420166 },
                { ItemID.BombAmmo, 420167 },
                { ItemID.ChakramAmmo, 420168 },
                { ItemID.CaltropsAmmo, 420169 },
                { ItemID.PistolAmmo, 420170 },
                { ItemID.Weights, 420171 },
            };

        /// <summary>
        /// Reverse mapping:
        /// AP item ID -> LM2 ItemID
        /// </summary>
        private static readonly Dictionary<int, ItemID> _reverseMap =
            new Dictionary<int, ItemID>();

        /// <summary>
        /// Static constructor builds reverse lookup once.
        /// </summary>
        static ItemTable()
        {
            foreach (var kv in _map)
            {
                // Avoid accidental collisions
                if (_reverseMap.ContainsKey(kv.Value))
                    continue;

                _reverseMap.Add(kv.Value, kv.Key);
            }
        }

        /// <summary>
        /// Get the Archipelago item ID for a given LM2 ItemID.
        /// Returns -1 if unmapped.
        /// </summary>
        public static int GetApItemId(ItemID item)
        {
            if (item == ItemID.None)
                return -1;

            return _map.TryGetValue(item, out int apId)
                ? apId
                : -1;
        }

        /// <summary>
        /// Try to resolve an AP item ID into an LM2 ItemID.
        /// Returns null if unmapped.
        /// </summary>
        public static ItemID? GetLM2ItemId(int apItemId)
        {
            return _reverseMap.TryGetValue(apItemId, out var item)
                ? item
                : null;
        }

        /// <summary>
        /// Returns true if the given LM2 item exists in the mapping.
        /// </summary>
        public static bool HasItem(ItemID item)
        {
            return _map.ContainsKey(item);
        }

        /// <summary>
        /// Returns true if the given AP item ID maps to an LM2 item.
        /// </summary>
        public static bool HasApItem(int apItemId)
        {
            return _reverseMap.ContainsKey(apItemId);
        }

        /// <summary>
        /// Enumerate all mapped LM2 items.
        /// Useful for validation and debugging.
        /// </summary>
        public static IEnumerable<ItemID> AllLM2Items()
        {
            return _map.Keys;
        }

        /// <summary>
        /// Enumerate all mapped AP item IDs.
        /// Useful for validation and debugging.
        /// </summary>
        public static IEnumerable<int> AllApItems()
        {
            return _reverseMap.Keys;
        }
    }
}