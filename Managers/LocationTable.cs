using System;
using System.Collections.Generic;
using LaMulana2RandomizerShared;

namespace LaMulana2Archipelago.Managers
{
    /// <summary>
    /// Central mapping between La-Mulana 2 LocationID enums
    /// and Archipelago location IDs.
    ///
    /// LM2 LocationID (enum, int) <-> AP Location ID (long, 430xxx)
    ///
    /// This table is authoritative for:
    /// - Reporting location checks to AP
    /// - Resolving AP location IDs back into LM2 locations
    /// </summary>
    public static class LocationTable
    {
        /// <summary>
        /// Forward mapping:
        /// LM2 LocationID -> AP location ID
        /// </summary>
        private static readonly Dictionary<LocationID, long> _map =
            new Dictionary<LocationID, long>()
            {
                { LocationID.XelpudItem, 430000 },
                { LocationID.NeburItem, 430001 },
                { LocationID.AlsedanaItem, 430002 },
                { LocationID.GiltoriyoItem, 430003 },
                { LocationID.NeburShop1, 430004 },
                { LocationID.NeburShop2, 430005 },
                { LocationID.NeburShop3, 430006 },
                { LocationID.ModroShop1, 430007 },
                { LocationID.ModroShop2, 430008 },
                { LocationID.ModroShop3, 430009 },
                { LocationID.SidroShop1, 430010 },
                { LocationID.SidroShop2, 430011 },
                { LocationID.SidroShop3, 430012 },
                { LocationID.SacredOrbVoD, 430013 },
                { LocationID.ChildMantraMural, 430014 },
                { LocationID.ShellHornChest, 430015 },
                { LocationID.HolyGrailChest, 430016 },
                { LocationID.HinerShop1, 430017 },
                { LocationID.HinerShop2, 430018 },
                { LocationID.HinerShop3, 430019 },
                { LocationID.HinerShop4, 430020 },
                { LocationID.BronzeMirrorSpot, 430021 },
                { LocationID.DissonanceMoG, 430022 },
                { LocationID.FreyasItem, 430023 },
                { LocationID.FobosItem, 430024 },
                { LocationID.FobosItem2, 430025 },
                { LocationID.MapChestRoY, 430026 },
                { LocationID.AnkhChestRoY, 430027 },
                { LocationID.CrystalSkullChestRoY, 430028 },
                { LocationID.ShurikenPuzzleReward, 430029 },
                { LocationID.KnifePuzzleReward, 430030 },
                { LocationID.MantraMural, 430031 },
                { LocationID.Ratatoskr1, 430032 },
                { LocationID.Nidhogg, 430033 },
                { LocationID.Fafnir, 430034 },
                { LocationID.SacredOrbChestRoY, 430035 },
                { LocationID.PyramidCrystalChest, 430036 },
                { LocationID.KorobockShop1, 430037 },
                { LocationID.KorobockShop2, 430038 },
                { LocationID.KorobockShop3, 430039 },
                { LocationID.MapChestAnnwfn, 430040 },
                { LocationID.GloveChest, 430041 },
                { LocationID.CrystalSkullChestAnnwfn, 430042 },
                { LocationID.RollingShurikenPuzzleReward, 430043 },
                { LocationID.ResearchAnnwfn, 430044 },
                { LocationID.PymShop1, 430045 },
                { LocationID.PymShop2, 430046 },
                { LocationID.PymShop3, 430047 },
                { LocationID.EarthMantraMural, 430048 },
                { LocationID.Kaliya, 430049 },
                { LocationID.Heimdall, 430050 },
                { LocationID.SacredOrbChestAnnwfn, 430051 },
                { LocationID.SilverShieldPuzzleReward, 430052 },
                { LocationID.DjedPillarChest, 430053 },
                { LocationID.Ixtab, 430054 },
                { LocationID.Kujata, 430055 },
                { LocationID.AnnwfnRightShortcut, 430056 },
                { LocationID.PeibalusaShop1, 430057 },
                { LocationID.PeibalusaShop2, 430058 },
                { LocationID.PeibalusaShop3, 430059 },
                { LocationID.Cetus, 430060 },
                { LocationID.GaleFibulaChest, 430061 },
                { LocationID.EarthSpearPuzzleReward, 430062 },
                { LocationID.ResearchIBTopLeft, 430063 },
                { LocationID.MapChestIB, 430064 },
                { LocationID.IceCloakChest, 430065 },
                { LocationID.TotemPoleChest, 430066 },
                { LocationID.SacredOrbChestIB, 430067 },
                { LocationID.LampofTimeChest, 430068 },
                { LocationID.MjolnirChest, 430069 },
                { LocationID.CrystalSkullIB, 430070 },
                { LocationID.ResearchIBTopRight, 430071 },
                { LocationID.ResearchIBTent1, 430072 },
                { LocationID.ResearchIBTent2, 430073 },
                { LocationID.ResearchIBTent3, 430074 },
                { LocationID.MulbrukItem, 430075 },
                { LocationID.Ratatoskr2, 430076 },
                { LocationID.Jormangund, 430077 },
                { LocationID.ChakramPuzzleReward, 430078 },
                { LocationID.HiroRoderickShop1, 430079 },
                { LocationID.HiroRoderickShop2, 430080 },
                { LocationID.HiroRoderickShop3, 430081 },
                { LocationID.Svipdagr, 430082 },
                { LocationID.ChainWhipPuzzleReward, 430083 },
                { LocationID.ResearchIBPit, 430084 },
                { LocationID.IBLeftShortcut, 430085 },
                { LocationID.ResearchIBLeft, 430086 },
                { LocationID.BatteryChest, 430087 },
                { LocationID.DinosaurFigureChest, 430088 },
                { LocationID.MoonMantraMural, 430089 },
                { LocationID.SunMantraMural, 430090 },
                { LocationID.SecretTreasureofLifeItem, 430091 },
                { LocationID.ResearchIT, 430092 },
                { LocationID.MinoShop1, 430093 },
                { LocationID.MinoShop2, 430094 },
                { LocationID.MinoShop3, 430095 },
                { LocationID.GrappleClawChest, 430096 },
                { LocationID.FlameTorcChest, 430097 },
                { LocationID.Surtr, 430098 },
                { LocationID.MapChestIT, 430099 },
                { LocationID.AnkhChestIT, 430100 },
                { LocationID.LifeSigilChest, 430101 },
                { LocationID.BTKShop1, 430102 },
                { LocationID.BTKShop2, 430103 },
                { LocationID.BTKShop3, 430104 },
                { LocationID.Vedfolnir, 430105 },
                { LocationID.FairyGuildPassChest, 430106 },
                { LocationID.CrystalSkullChestIT, 430107 },
                { LocationID.SacredOrbChestIT, 430108 },
                { LocationID.Ratatoskr3, 430109 },
                { LocationID.Vidofnir, 430110 },
                { LocationID.MapChestDF, 430111 },
                { LocationID.OriginSealChest, 430112 },
                { LocationID.FreyShip, 430113 },
                { LocationID.HeavenMantraMural, 430114 },
                { LocationID.HuginandMunin, 430115 },
                { LocationID.AnkhChestDF, 430116 },
                { LocationID.SacredOrbChestDF, 430117 },
                { LocationID.ShuhokaShop1, 430118 },
                { LocationID.ShuhokaShop2, 430119 },
                { LocationID.ShuhokaShop3, 430120 },
                { LocationID.DeathVillageChest, 430121 },
                { LocationID.MapChestSotFG, 430122 },
                { LocationID.PochetteKeyChest, 430123 },
                { LocationID.WeaponFairy, 430124 },
                { LocationID.BadhbhCath, 430125 },
                { LocationID.Fenrir, 430126 },
                { LocationID.BirthSigilChest, 430127 },
                { LocationID.AnkhChestSotFG, 430128 },
                { LocationID.RapierPuzzleReward, 430129 },
                { LocationID.GauntletChest, 430130 },
                { LocationID.HydlitShop1, 430131 },
                { LocationID.HydlitShop2, 430132 },
                { LocationID.HydlitShop3, 430133 },
                { LocationID.Bergelmir, 430134 },
                { LocationID.SacredOrbChestSotFG, 430135 },
                { LocationID.SeaMantraMural, 430136 },
                { LocationID.ClaydollChest, 430137 },
                { LocationID.Balor, 430138 },
                { LocationID.Tezcatlipoca, 430139 },
                { LocationID.MapChestGotD, 430140 },
                { LocationID.YagooMapStreetChest, 430141 },
                { LocationID.VajraChest, 430142 },
                { LocationID.SacredOrbChestGotD, 430143 },
                { LocationID.AnchorChest, 430144 },
                { LocationID.CrystalSkullChestGotD, 430145 },
                { LocationID.KatanaPuzzleReward, 430146 },
                { LocationID.AytumShop1, 430147 },
                { LocationID.AytumShop2, 430148 },
                { LocationID.AytumShop3, 430149 },
                { LocationID.WhitePedestals, 430150 },
                { LocationID.Unicorn, 430151 },
                { LocationID.MapChestTS, 430152 },
                { LocationID.SacredOrbChestTS, 430153 },
                { LocationID.AshGeenShop1, 430154 },
                { LocationID.AshGeenShop2, 430155 },
                { LocationID.AshGeenShop3, 430156 },
                { LocationID.RaijinandFujin, 430157 },
                { LocationID.RingChest, 430158 },
                { LocationID.FlarePuzzleReward, 430159 },
                { LocationID.CrystalSkullChestTS, 430160 },
                { LocationID.EggofCreationChest, 430161 },
                { LocationID.Daji, 430162 },
                { LocationID.PowerBandChest, 430163 },
                { LocationID.Belial, 430164 },
                { LocationID.MapChestHL, 430165 },
                { LocationID.CrystalSkullChestHL, 430166 },
                { LocationID.MegarockShop1, 430167 },
                { LocationID.MegarockShop2, 430168 },
                { LocationID.MegarockShop3, 430169 },
                { LocationID.SacredOrbChestHL, 430170 },
                { LocationID.PerfumeChest, 430171 },
                { LocationID.AxePuzzleReward, 430172 },
                { LocationID.MobileSuperX3Item, 430173 },
                { LocationID.DissonanceHL, 430174 },
                { LocationID.Arachne, 430175 },
                { LocationID.Scylla, 430176 },
                { LocationID.GlasyaLabolas, 430177 },
                { LocationID.Griffin, 430178 },
                { LocationID.CogofAntiquityChest, 430179 },
                { LocationID.MapChestValhalla, 430180 },
                { LocationID.ScalesphereChest, 430181 },
                { LocationID.CrystalSkullChestValhalla, 430182 },
                { LocationID.CaltropPuzzleReward, 430183 },
                { LocationID.BargainDuckShop1, 430184 },
                { LocationID.BargainDuckShop2, 430185 },
                { LocationID.BargainDuckShop3, 430186 },
                { LocationID.VucubCaquiz, 430187 },
                { LocationID.Jalandhara, 430188 },
                { LocationID.Vritra, 430189 },
                { LocationID.DissonanceValhalla, 430190 },
                { LocationID.FireMantraMural, 430191 },
                { LocationID.FlailWhipPuzzleReward, 430192 },
                { LocationID.FeatherChest, 430193 },
                { LocationID.AnkhChestDLM, 430194 },
                { LocationID.MapChestDLM, 430195 },
                { LocationID.CrucifixChest, 430196 },
                { LocationID.MaatsFeatherChest, 430197 },
                { LocationID.ResearchDSLM, 430198 },
                { LocationID.KeroShop1, 430199 },
                { LocationID.KeroShop2, 430200 },
                { LocationID.KeroShop3, 430201 },
                { LocationID.MoneyFairy, 430202 },
                { LocationID.Sekhmet, 430203 },
                { LocationID.AtenRa, 430204 },
                { LocationID.DissonanceDSLM, 430205 },
                { LocationID.CrystalSkullChestDLM, 430206 },
                { LocationID.LaMulanaChest, 430207 },
                { LocationID.VesselChest, 430208 },
                { LocationID.AngraMainyu, 430209 },
                { LocationID.Ammit, 430210 },
                { LocationID.NightMantraMural, 430211 },
                { LocationID.DissonanceNibiru, 430212 },
                { LocationID.MapChestAC, 430213 },
                { LocationID.VenumShop1, 430214 },
                { LocationID.VenumShop2, 430215 },
                { LocationID.VenumShop3, 430216 },
                { LocationID.Kisikillillake, 430217 },
                { LocationID.WindMantraMural, 430218 },
                { LocationID.AnkhChestAC, 430219 },
                { LocationID.CrystalSkullChestAC, 430220 },
                { LocationID.DestinyTabletChest, 430221 },
                { LocationID.ScripturesChest, 430222 },
                { LocationID.Anzu, 430223 },
                { LocationID.Anu, 430224 },
                { LocationID.MapChestHoM, 430225 },
                { LocationID.HoMLadder, 430226 },
                { LocationID.FairylanShop1, 430227 },
                { LocationID.FairylanShop2, 430228 },
                { LocationID.FairylanShop3, 430229 },
                { LocationID.NemeanFurChest, 430230 },
                { LocationID.CrystalSkullChestHoM, 430231 },
                { LocationID.GiantsFluteChest, 430232 },
                { LocationID.MiracleWitchChest, 430233 },
                { LocationID.HoMLeftPath, 430234 },
                { LocationID.HoMMiddlePath, 430235 },
                { LocationID.HoMRightPath, 430236 },
                { LocationID.Echidna, 430237 },
                { LocationID.CrystalSkullChestEPD, 430238 },
                { LocationID.MapChestEPD, 430239 },
                { LocationID.LaMulana2Chest, 430240 },
                { LocationID.OsirisItem, 430241 },
                { LocationID.Hraesvelgr, 430242 },
                { LocationID.SpaulderChest, 430243 },
                { LocationID.Hel, 430244 },
                { LocationID.BookoftheDeadChest, 430245 },
                { LocationID.DissonanceEPG, 430246 },
                { LocationID.MapChestEPG, 430247 },
                { LocationID.BeoEglanaMural, 430248 },
                { LocationID.DeathSigilChest, 430249 },
                { LocationID.MotherMantraMural, 430250 },
                { LocationID.AnkhJewel, 430251 },
                { LocationID.KeyFairy, 430252 },
                { LocationID.GarmStatuePuzzle, 430253 },
                { LocationID.BombPuzzleReward, 430254 },
                { LocationID.SakitPuzzle, 430255 },
                { LocationID.Ratatoskr4, 430256 },
                { LocationID.NinthChild, 430257 },
            };

        /// <summary>
        /// Reverse mapping:
        /// AP location ID -> LM2 LocationID
        /// </summary>
        private static readonly Dictionary<long, LocationID> _reverseMap =
            new Dictionary<long, LocationID>();

        /// <summary>
        /// Static constructor builds reverse lookup and validates table.
        /// </summary>
        static LocationTable()
        {
            Plugin.Log.LogInfo("[LocationTable] Initializing");

            foreach (var kv in _map)
            {
                if (_reverseMap.ContainsKey(kv.Value))
                {
                    Plugin.Log.LogError(
                        $"[LocationTable] Duplicate AP ID detected: {kv.Value} " +
                        $"(existing={_reverseMap[kv.Value]}, new={kv.Key})"
                    );
                    continue;
                }

                _reverseMap.Add(kv.Value, kv.Key);
            }

            Plugin.Log.LogInfo(
                $"[LocationTable] Loaded {_map.Count} LM2 locations"
            );
        }

        /// <summary>
        /// Get the Archipelago location ID for a given LM2 LocationID.
        /// Returns -1 if unmapped.
        /// </summary>
        public static long GetApLocationId(LocationID location)
        {
            if (location == LocationID.None)
            {
                Plugin.Log.LogDebug(
                    "[LocationTable] Requested AP ID for LocationID.None"
                );
                return -1;
            }

            if (_map.TryGetValue(location, out long apId))
                return apId;

            Plugin.Log.LogWarning(
                $"[LocationTable] No AP ID for LM2 location: {location}"
            );
            return -1;
        }

        /// <summary>
        /// Resolve an AP location ID into an LM2 LocationID.
        /// Returns null if unmapped.
        /// </summary>
        public static LocationID? GetLM2LocationId(long apLocationId)
        {
            if (_reverseMap.TryGetValue(apLocationId, out var location))
                return location;

            Plugin.Log.LogWarning(
                $"[LocationTable] Unknown AP location ID: {apLocationId}"
            );
            return null;
        }

        /// <summary>
        /// Returns true if the given LM2 LocationID exists in the mapping.
        /// </summary>
        public static bool HasLocation(LocationID location)
        {
            return _map.ContainsKey(location);
        }

        /// <summary>
        /// Returns true if the given AP location ID exists in the mapping.
        /// </summary>
        public static bool HasApLocation(long apLocationId)
        {
            return _reverseMap.ContainsKey(apLocationId);
        }

        /// <summary>
        /// Enumerate all mapped LM2 locations.
        /// Useful for debugging and validation.
        /// </summary>
        public static IEnumerable<LocationID> AllLM2Locations()
        {
            return _map.Keys;
        }

        /// <summary>
        /// Enumerate all mapped AP location IDs.
        /// Useful for debugging and validation.
        /// </summary>
        public static IEnumerable<long> AllApLocations()
        {
            return _reverseMap.Keys;
        }
        public static bool TryGetApLocationId(
            LocationID location,
            out long apLocationId
        )
        {
            apLocationId = -1;

            if (location == LocationID.None)
                return false;

            if (_map.TryGetValue(location, out apLocationId))
                return true;

            return false;
        }
    }
}