using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using L2Base;
using L2Flag;
using L2Hit;
using L2MobTask;
using L2Word;
using LaMulana2RandomizerShared;
using LM2RandomiserMod;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LaMulana2Archipelago.Managers
{
    /// <summary>
    /// Handles all per-scene randomization that the original L2Rando did at scene load.
    /// This replaces the MonoMod-patched L2Rando MonoBehaviour with pure Harmony + Unity calls.
    ///
    /// Activation: after AP connection provides slot_data with item_placements.
    /// </summary>
    public class SceneRandomizer : MonoBehaviour
    {
        public static SceneRandomizer Instance { get; private set; }

        // Placement data (loaded from slot_data)
        private Dictionary<LocationID, ItemID> locationToItemMap = new();
        private Dictionary<LocationID, ShopItem> shopToItemMap = new();
        private List<LocationID> cursedChests = new();
        private Dictionary<ExitID, ExitID> exitToExitMap = new();
        private Dictionary<ExitID, int> soulGateValueMap = new();

        // Settings
        private bool randomSoulGates;
        private bool randomDissonance;
        private bool autoPlaceSkull;
        private int echidna;
        private int requiredGuardians;
        private int itemChestColour;
        private int weightChestColour;
        private int apChestColour;
        private ItemID startingWeapon;
        private AreaID startingArea;
        private string startFieldName;
        public bool StartingGame;
        public bool IsRandomising { get; private set; }

        // Prefab cache
        private Dictionary<string, GameObject> objects = new();

        // Game references
        private L2System sys;
        private L2ShopDataBase shopDataBase;
        private L2TalkDataBase talkDataBase;

        // One-time initialization flag for shop/dialogue rewrites
        private bool shopDialogueInitialized;

        // Snapshot of original thank-script cells (sheet,row) → original string.
        // Captured on first ChangeThanksStrings call so we can restore + reapply
        // on reconnect (those cells use += which would otherwise double-stamp).
        // Key: "sheet:row"
        private readonly Dictionary<string, string> _shopThanksOriginals = new();

        public static void Create()
        {
            if (Instance != null) return;
            var go = new GameObject("AP_SceneRandomizer");
            Instance = go.AddComponent<SceneRandomizer>();
            DontDestroyOnLoad(go);
        }

        /// <summary>
        /// Load all placement/settings data from AP slot_data.
        /// Call after successful AP connection.
        /// </summary>
        public void LoadFromSlotData(Dictionary<string, object> slotData, Archipelago.ArchipelagoData serverData)
        {
            locationToItemMap.Clear();
            shopToItemMap.Clear();
            cursedChests.Clear();
            exitToExitMap.Clear();
            soulGateValueMap.Clear();

            // Item placements
            if (slotData.TryGetValue("item_placements", out object rawPl) && rawPl is JArray placements)
            {
                foreach (var entry in placements)
                {
                    var loc = (LocationID)(int)entry["location"];
                    var item = (ItemID)(int)entry["item"];
                    locationToItemMap[loc] = item;
                }
            }

            // Shop placements
            if (slotData.TryGetValue("shop_placements", out object rawSh) && rawSh is JArray shops)
            {
                foreach (var entry in shops)
                {
                    var loc = (LocationID)(int)entry["location"];
                    var item = (ItemID)(int)entry["item"];
                    int price = (int)entry["price"];
                    shopToItemMap[loc] = new ShopItem(item, price);
                }
            }

            // Cursed locations
            if (slotData.TryGetValue("cursed_locations", out object rawCursed) && rawCursed is JArray cursedArr)
            {
                foreach (var token in cursedArr)
                    cursedChests.Add((LocationID)(int)token);
            }

            // Entrance pairs
            if (slotData.TryGetValue("entrance_pairs", out object rawEntr) && rawEntr is JArray entrArr)
            {
                foreach (var pair in entrArr)
                {
                    var arr = pair as JArray;
                    if (arr != null && arr.Count >= 2)
                    {
                        ExitID e1 = (ExitID)(int)arr[0];
                        ExitID e2 = (ExitID)(int)arr[1];
                        exitToExitMap[e1] = e2;
                        exitToExitMap[e2] = e1;
                    }
                }
            }

            // Soul gate pairs
            if (slotData.TryGetValue("soul_gate_pairs", out object rawSG) && rawSG is JArray sgArr)
            {
                randomSoulGates = sgArr.Count > 0;
                foreach (var pair in sgArr)
                {
                    var arr = pair as JArray;
                    if (arr != null && arr.Count >= 3)
                    {
                        ExitID g1 = (ExitID)(int)arr[0];
                        ExitID g2 = (ExitID)(int)arr[1];
                        int soulVal = (int)arr[2];
                        exitToExitMap[g1] = g2;
                        exitToExitMap[g2] = g1;
                        soulGateValueMap[g1] = soulVal;
                        soulGateValueMap[g2] = soulVal;
                    }
                }
            }

            // Settings
            startingWeapon = (ItemID)serverData.GetSlotInt("starting_weapon", 0);
            startingArea = (AreaID)serverData.GetSlotInt("starting_area", 0);
            randomDissonance = serverData.GetSlotBool("random_dissonance", true);
            requiredGuardians = serverData.GetSlotInt("required_guardians", 5);
            echidna = serverData.GetSlotInt("echidna", 4);
            autoPlaceSkull = serverData.GetSlotBool("auto_place_skull", true);
            itemChestColour = serverData.GetSlotInt("item_chest_color", 0);
            weightChestColour = serverData.GetSlotInt("filler_chest_color", 4);
            apChestColour = serverData.GetSlotInt("ap_chest_color", 1);

            SetStartFieldName();

            Plugin.Log.LogInfo($"[SceneRando] Loaded: {locationToItemMap.Count} items, {shopToItemMap.Count} shops, " +
                $"{cursedChests.Count} cursed, {exitToExitMap.Count / 2} entrance pairs, " +
                $"{soulGateValueMap.Count / 2} soul gates, randomDissonance={randomDissonance}");

            IsRandomising = true;

            // Force shop/dialogue cellData to be rewritten with new seed data.
            // ChangeShopThanks restores from _shopThanksOriginals before re-appending,
            // so rerunning is safe.
            shopDialogueInitialized = false;
        }

        private void SetStartFieldName()
        {
            startFieldName = startingArea switch
            {
                AreaID.RoY => "field00",
                AreaID.VoD => "field01",
                AreaID.AnnwfnMain => "field02",
                AreaID.IBMain => "field03",
                AreaID.ITLeft => "field04",
                AreaID.DFMain => "field05",
                AreaID.SotFGGrail => "field06",
                AreaID.TSLeft => "field08",
                AreaID.ValhallaMain => "field10",
                AreaID.DSLMMain => "field11",
                AreaID.ACTablet => "field12",
                AreaID.HoMTop => "field13",
                _ => string.Empty,
            };
        }

        /// <summary>
        /// Called from Plugin.OnSceneLoaded when in standalone mode.
        /// Applies all per-scene randomization.
        /// </summary>
        public void OnSceneLoaded(Scene scene)
        {
            if (!IsRandomising) return;
            if (scene.name == "fieldLast" || scene.name == "title") return;

            // Cache sys reference
            if (sys == null)
                sys = FindObjectOfType<L2System>();
            if (sys == null) return;

            try
            {
                // One-time shop/dialogue database rewrites (global, not per-scene)
                if (!shopDialogueInitialized)
                    TryInitShopDialogue();

                List<GameObject> objectsToDeactivate = new List<GameObject>();

                CreateStartingFieldObjects(scene.name);
                AddAnchorPoints(scene.name);
                objectsToDeactivate.AddRange(ChangeEntrances(scene.name));
                objectsToDeactivate.AddRange(ChangeTreasureChests());
                ChangeEventItems();
                Plugin.Log.LogDebug($"[SceneRando] About to call DissonanceChests for {scene.name}");
                DissonanceChests(scene.name);
                ChangeFlagWatchers(scene.name);
                FieldSpecificChanges(scene.name);
                ObjectChanges();
                StartCoroutine(DeactivateObjects(objectsToDeactivate));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SceneRando] Error in {scene.name}: {ex}");
            }
        }

        private IEnumerator DeactivateObjects(List<GameObject> objs)
        {
            yield return new WaitForEndOfFrame();
            foreach (GameObject obj in objs)
                obj.SetActive(false);
        }

        // ================================================================
        // Item/Location helpers
        // ================================================================

        public ItemID GetItemIDForLocation(LocationID locationID)
        {
            locationToItemMap.TryGetValue(locationID, out ItemID id);
            return id;
        }

        private LocationID GetLocationID(string objName)
        {
            if (objName.Contains("ItemSym "))
            {
                string name = objName.Substring(8);
                if (name.Contains("SacredOrb"))
                    name = name.Insert(6, " ");
                else if (name == "MSX3p")
                    name = "MSX";
                else if (name == "B Mirror2")
                    return LocationID.None;

                return (LocationID)L2SystemCore.getItemData(name).getItemName();
            }
            return LocationID.None;
        }

        public LocationID GetLocationIDForMural(SnapShotTargetScript snapTarget)
        {
            if (snapTarget.itemName == ItemDatabaseSystem.ItemNames.BeoEgLana)
                return LocationID.BeoEglanaMural;
            else if (snapTarget.itemName == ItemDatabaseSystem.ItemNames.Mantra)
            {
                return snapTarget.cellName switch
                {
                    "" => LocationID.MantraMural,
                    "mantra1" => LocationID.HeavenMantraMural,
                    "mantra2" => LocationID.EarthMantraMural,
                    "mantra3" => LocationID.SunMantraMural,
                    "mantra4" => LocationID.MoonMantraMural,
                    "mantra5" => LocationID.SeaMantraMural,
                    "mantra6" => LocationID.FireMantraMural,
                    "mantra7" => LocationID.WindMantraMural,
                    "mantra8" => LocationID.MotherMantraMural,
                    "mantra9" => LocationID.ChildMantraMural,
                    "mantra10" => LocationID.NightMantraMural,
                    _ => LocationID.None,
                };
            }
            return LocationID.None;
        }

        private LocationID GetLocationIDForResearch(L2FlagBoxParent[] flags)
        {
            foreach (L2FlagBoxParent flagBoxParent in flags)
            {
                foreach (L2FlagBox flagBox in flagBoxParent.BOX)
                {
                    if (flagBox.seet_no1 == 6 && flagBox.flag_no1 == 43) return LocationID.ResearchAnnwfn;
                    else if (flagBox.seet_no1 == 8 && flagBox.flag_no1 == 45) return LocationID.ResearchIT;
                    else if (flagBox.seet_no1 == 7 && flagBox.flag_no1 == 78) return LocationID.ResearchIBTopLeft;
                    else if (flagBox.seet_no1 == 7 && flagBox.flag_no1 == 79) return LocationID.ResearchIBTopRight;
                    else if (flagBox.seet_no1 == 7 && flagBox.flag_no1 == 80) return LocationID.ResearchIBTent1;
                    else if (flagBox.seet_no1 == 7 && flagBox.flag_no1 == 81) return LocationID.ResearchIBPit;
                    else if (flagBox.seet_no1 == 7 && flagBox.flag_no1 == 83) return LocationID.ResearchIBLeft;
                    else if (flagBox.seet_no1 == 7 && flagBox.flag_no1 == 85) return LocationID.ResearchIBTent2;
                    else if (flagBox.seet_no1 == 7 && flagBox.flag_no1 == 86) return LocationID.ResearchIBTent3;
                    else if (flagBox.seet_no1 == 15 && flagBox.flag_no1 == 44) return LocationID.ResearchDSLM;
                }
            }
            return LocationID.None;
        }

        private bool IsLocationCursed(LocationID locationID)
        {
            return cursedChests.Contains(locationID);
        }

        /// <summary>
        /// Creates a chest from cached prefabs. Matches the original L2Rando.CreateChest
        /// exactly: bare Instantiate, no field resets, no animation pre-play.
        /// The cached prefab carries taskinit_flg=true from the harvest source,
        /// which means Start() does nothing — the chest renders purely from
        /// its inherited prefab visual state, as the original intended.
        /// </summary>
        private TreasureBoxScript CreateChest(int colour, Vector3 position, Quaternion rotation)
        {
            string key = colour switch
            {
                0 => "blueChest",
                1 => "turquiseChest",
                2 => "redChest",
                3 => "pinkChest",
                4 => "yellowChest",
                _ => "blueChest"
            };

            if (!PrefabHarvester.CachedPrefabs.TryGetValue(key, out GameObject prefab))
                return null;

            return Instantiate(prefab, position, rotation).GetComponent<TreasureBoxScript>();
        }

        // ================================================================
        // CreateGetFlags — same as L2Rando.CreateGetFlags
        // ================================================================

        public L2FlagBoxEnd[] CreateGetFlags(ItemID itemID, ItemInfo itemInfo)
        {
            ItemID[] storyItems = {ItemID.DjedPillar, ItemID.Mjolnir, ItemID.AncientBattery, ItemID.LampofTime, ItemID.PochetteKey,
                ItemID.PyramidCrystal, ItemID.Vessel, ItemID.EggofCreation, ItemID.GiantsFlute, ItemID.CogofAntiquity, ItemID.MulanaTalisman,
                ItemID.HolyGrail, ItemID.Gloves, ItemID.DinosaurFigure, ItemID.GaleFibula, ItemID.FlameTorc, ItemID.PowerBand, ItemID.GrappleClaw,
                ItemID.GaneshaTalisman, ItemID.MaatsFeather, ItemID.Feather, ItemID.FreysShip, ItemID.Harp, ItemID.DestinyTablet, ItemID.SecretTreasureofLife,
                ItemID.OriginSigil, ItemID.BirthSigil, ItemID.LifeSigil, ItemID.DeathSigil, ItemID.ClaydollSuit};
            List<L2FlagBoxEnd> getFlags = new List<L2FlagBoxEnd>();

            short data;
            if (itemID >= ItemID.SacredOrb0 && itemID <= ItemID.SacredOrb9)
            {
                getFlags.Add(new L2FlagBoxEnd { calcu = CALCU.ADD, seet_no1 = 0, flag_no1 = 2, data = 1 });
                getFlags.Add(new L2FlagBoxEnd { calcu = CALCU.EQR, seet_no1 = itemInfo.ItemSheet, flag_no1 = itemInfo.ItemFlag, data = 1 });
            }
            else if (itemID >= ItemID.CrystalSkull1 && itemID <= ItemID.CrystalSkull12)
            {
                getFlags.Add(new L2FlagBoxEnd { calcu = CALCU.ADD, seet_no1 = 0, flag_no1 = 32, data = 1 });
                getFlags.Add(new L2FlagBoxEnd { calcu = CALCU.ADD, seet_no1 = 3, flag_no1 = 30, data = 4 });
                if (autoPlaceSkull)
                {
                    getFlags.Add(new L2FlagBoxEnd { calcu = CALCU.ADD, seet_no1 = 5, flag_no1 = (int)itemID - 108, data = 1 });
                    getFlags.Add(new L2FlagBoxEnd { calcu = CALCU.ADD, seet_no1 = 5, flag_no1 = 47, data = 1 });
                }
                getFlags.Add(new L2FlagBoxEnd { calcu = CALCU.EQR, seet_no1 = itemInfo.ItemSheet, flag_no1 = itemInfo.ItemFlag, data = 1 });
            }
            else if ((itemID >= ItemID.AnkhJewel1 && itemID <= ItemID.AnkhJewel9) || Array.IndexOf(storyItems, itemID) > -1)
            {
                data = 4;
                if (itemID == ItemID.GrappleClaw || itemID == ItemID.HolyGrail)
                    data = 2;
                getFlags.Add(new L2FlagBoxEnd { calcu = CALCU.ADD, seet_no1 = 3, flag_no1 = 30, data = data });

                data = 1;
                if (itemID == ItemID.LampofTime)
                    data = 2;
                getFlags.Add(new L2FlagBoxEnd { calcu = CALCU.EQR, seet_no1 = itemInfo.ItemSheet, flag_no1 = itemInfo.ItemFlag, data = data });
            }
            else if (itemID >= ItemID.ProgressiveBeherit1 && itemID <= ItemID.ProgressiveBeherit7)
            {
                getFlags.Add(new L2FlagBoxEnd { calcu = CALCU.EQR, seet_no1 = itemInfo.ItemSheet, flag_no1 = itemInfo.ItemFlag, data = 1 });
            }
            else
            {
                data = 1;
                if (itemID == ItemID.MobileSuperx3P)
                    data = 2;
                getFlags.Add(new L2FlagBoxEnd { calcu = CALCU.EQR, seet_no1 = itemInfo.ItemSheet, flag_no1 = itemInfo.ItemFlag, data = data });
            }

            return getFlags.ToArray();
        }

        // ================================================================
        // Sprite helpers
        // ================================================================

        private Sprite GetItemSprite(string itemName, ItemID itemID)
        {
            if (itemID == ItemID.Whip1 || itemID == ItemID.Whip2 || itemID == ItemID.Whip3)
            {
                short data = 0;
                string name;
                sys.getFlag(2, "Whip", ref data);
                if (data == 0) name = "Whip";
                else if (data == 1) name = "Whip2";
                else name = "Whip3";
                return L2SystemCore.getMapIconSprite(L2SystemCore.getItemData(name));
            }
            else if (itemID == ItemID.Shield1 || itemID == ItemID.Shield2 || itemID == ItemID.Shield3)
            {
                short data = 0;
                string name;
                sys.getFlag(2, 196, ref data);
                if (data == 0) name = "Shield";
                else if (data == 1) name = "Shield2";
                else name = "Shield3";
                return L2SystemCore.getMapIconSprite(L2SystemCore.getItemData(name));
            }
            else if (itemID >= ItemID.Research1 && itemID <= ItemID.Research10)
                return L2SystemCore.getMapIconSprite(L2SystemCore.getItemData("Research"));
            else if (itemID >= ItemID.ProgressiveBeherit1 && itemID <= ItemID.ProgressiveBeherit7)
                return L2SystemCore.getMapIconSprite(L2SystemCore.getItemData("Beherit"));
            else if (itemID >= ItemID.Heaven && itemID <= ItemID.Night)
                return L2SystemCore.getMapIconSprite(L2SystemCore.getItemData("Mantra"));
            else
                return L2SystemCore.getMapIconSprite(L2SystemCore.getItemData(itemName));
        }

        private Sprite GetRandomSprite()
        {
            ItemID itemID = (ItemID)UnityEngine.Random.Range(1, (int)ItemID.Research10);
            ItemInfo itemInfo = ItemDB.GetItemInfo(itemID);
            return GetItemSprite(itemInfo.BoxName, itemID);
        }

        // ================================================================
        // ChangeTreasureChests
        // ================================================================

        private List<GameObject> ChangeTreasureChests()
        {
            List<GameObject> objectsToDeactivate = new List<GameObject>();
            foreach (TreasureBoxScript oldChest in FindObjectsOfType<TreasureBoxScript>())
            {
                if (oldChest.closetMode) continue;
                if (oldChest.itemObj == null) continue;
                LocationID locationID = GetLocationID(oldChest.itemObj.name);
                if (locationID == LocationID.None) continue;
                if (!locationToItemMap.TryGetValue(locationID, out ItemID newItemID)) continue;

                // Determine correct color based on item type (AP items use AP color)
                int colorToUse = itemChestColour;
                if ((int)newItemID >= 410000) colorToUse = apChestColour;
                else if (newItemID >= ItemID.ChestWeight01) colorToUse = weightChestColour;

                // Swap the chest prefab
                TreasureBoxScript newChest = CreateChest(colorToUse, oldChest.transform.position, oldChest.transform.rotation);
                if (newChest == null) continue;

                if (IsLocationCursed(locationID) && PrefabHarvester.CachedPrefabs.TryGetValue("curse", out GameObject cursePrefab))
                {
                    GameObject curse = Instantiate(cursePrefab, oldChest.transform.position, oldChest.transform.rotation);
                    curse.SetActive(true);
                    curse.transform.SetParent(newChest.transform);
                    newChest.curseAnime = curse.GetComponent<Animator>();
                    newChest.curseParticle = curse.GetComponent<ParticleSystem>();
                    newChest.curseMode = true;
                }
                else
                {
                    newChest.curseMode = false;
                }

                newChest.closetMode = false;
                newChest.forceOpenFlags = oldChest.forceOpenFlags;
                newChest.itemFlags = oldChest.itemFlags;
                newChest.openActionFlags = oldChest.openActionFlags;
                newChest.openFlags = oldChest.openFlags;
                newChest.unlockFlags = oldChest.unlockFlags;
                newChest.itemObj = oldChest.itemObj;
                newChest.transform.SetParent(oldChest.transform.parent);
                newChest.gameObject.SetActive(true);

                ChangeChestItemFlags(newChest, newItemID);

                // Even if SetActive(false) loses a race
                // (dynamic re-spawns, parent re-enables, late-init chests like
                // Maat's Feather), sta=7 + null itemObj keeps it non-interactive.
                Traverse.Create(oldChest).Field("sta").SetValue(7);
                oldChest.itemObj = null;
                oldChest.curseMode = false;
                objectsToDeactivate.Add(oldChest.gameObject);
            }
            return objectsToDeactivate;
        }

        private void ChangeChestItemFlags(TreasureBoxScript chest, ItemID itemID)
        {
            ItemInfo itemInfo = ItemDB.GetItemInfo(itemID);
            if (itemInfo == null) return;

            // Update open flags
            foreach (L2FlagBoxParent flagBoxParent in chest.openFlags)
            {
                foreach (L2FlagBox flagBox in flagBoxParent.BOX)
                {
                    if (flagBox.seet_no1 == 2)
                    {
                        flagBox.seet_no1 = itemInfo.ItemSheet;
                        flagBox.flag_no1 = itemInfo.ItemFlag;
                        flagBox.flag_no2 = 1;
                        if (itemID == ItemID.MobileSuperx3P)
                            flagBox.flag_no2 = 2;
                    }
                }
            }

            EventItemScript item = chest.itemObj.GetComponent<EventItemScript>();
            if (item == null) return;

            // Update active flags
            item.itemActiveFlag = new L2FlagBoxParent[]
            {
                new L2FlagBoxParent
                {
                    BOX = new L2FlagBox[] {
                        new L2FlagBox()
                        {
                            seet_no1 = itemInfo.ItemSheet,
                            flag_no1 = itemInfo.ItemFlag,
                            seet_no2 = -1,
                            flag_no2 = itemID == ItemID.MobileSuperx3P ? 1 : 0,
                            comp = itemID == ItemID.MobileSuperx3P ? COMPARISON.LessEq : COMPARISON.Equal,
                            logic = LOGIC.NON,
                        }
                    }
                }
            };

            // Update get flags
            item.itemGetFlags = CreateGetFlags(itemID, itemInfo);
            item.itemLabel = itemInfo.BoxName;

            if (itemID >= ItemID.ChestWeight01)
                item.itemValue = itemInfo.ItemFlag;

            if (itemID < ItemID.ChestWeight01)
                item.gameObject.GetComponent<SpriteRenderer>().sprite = GetItemSprite(itemInfo.BoxName, itemID);
        }

        // ================================================================
        // ChangeEventItems
        // ================================================================

        private void ChangeEventItems()
        {
            foreach (EventItemScript item in FindObjectsOfType<EventItemScript>())
            {
                LocationID locationID;
                if (item.name.Contains("Research"))
                    locationID = GetLocationIDForResearch(item.itemActiveFlag);
                else
                {
                    locationID = GetLocationID(item.name);
                    if (locationID == LocationID.None) continue;
                }

                if (!locationToItemMap.TryGetValue(locationID, out ItemID newItemID)) continue;

                ItemInfo newItemInfo = ItemDB.GetItemInfo(newItemID);
                if (newItemInfo == null) continue;

                if (locationID >= LocationID.ResearchAnnwfn && locationID <= LocationID.ResearchDSLM)
                {
                    List<L2FlagBox> flags = new List<L2FlagBox>();
                    L2FlagBox flagBox = new L2FlagBox()
                    {
                        seet_no1 = newItemInfo.ItemSheet,
                        flag_no1 = newItemInfo.ItemFlag,
                        seet_no2 = -1,
                        flag_no2 = 0,
                        comp = COMPARISON.Equal,
                        logic = LOGIC.AND
                    };

                    if (newItemID == ItemID.MobileSuperx3P)
                    {
                        flagBox.flag_no2 = 1;
                        flagBox.comp = COMPARISON.LessEq;
                    }
                    flags.Add(flagBox);

                    if (locationID == LocationID.ResearchIBTent2)
                    {
                        flags.Add(new L2FlagBox()
                        {
                            seet_no1 = 3, flag_no1 = 0, seet_no2 = -1, flag_no2 = 7,
                            comp = COMPARISON.GreaterEq, logic = LOGIC.AND
                        });
                    }
                    else if (locationID == LocationID.ResearchIBTent3)
                    {
                        flags.Add(new L2FlagBox()
                        {
                            seet_no1 = 3, flag_no1 = 86, seet_no2 = -1, flag_no2 = 1,
                            comp = COMPARISON.Equal, logic = LOGIC.AND
                        });
                    }
                    else if (locationID == LocationID.ResearchIBPit)
                    {
                        item.gameObject.transform.position += new Vector3(0, 70, 0);
                    }

                    item.itemActiveFlag[0].BOX = flags.ToArray();
                }
                else
                {
                    foreach (L2FlagBoxParent flagBoxParent in item.itemActiveFlag)
                    {
                        foreach (L2FlagBox flagBox in flagBoxParent.BOX)
                        {
                            if (flagBox.seet_no1 == 2)
                            {
                                flagBox.seet_no1 = newItemInfo.ItemSheet;
                                flagBox.flag_no1 = newItemInfo.ItemFlag;
                                flagBox.comp = COMPARISON.Equal;
                                flagBox.flag_no2 = 0;
                                if (newItemID == ItemID.MobileSuperx3P)
                                {
                                    flagBox.flag_no2 = 1;
                                    flagBox.comp = COMPARISON.LessEq;
                                }
                            }
                        }
                    }
                }

                bool isApItem = (int)newItemID >= 410000;

                if (newItemID < ItemID.ChestWeight01 || isApItem)
                {
                    item.itemGetFlags = CreateGetFlags(newItemID, newItemInfo);
                    item.itemLabel = newItemInfo.BoxName;

                    // Assign correct sprite
                    if (isApItem)
                    {
                        item.gameObject.GetComponent<SpriteRenderer>().sprite = ApSpriteLoader.IsLoaded
                            ? ApSpriteLoader.MapSprite
                            : L2SystemCore.getMapIconSprite(L2SystemCore.getItemData("Holy Grail"));
                    }
                    else
                    {
                        item.gameObject.GetComponent<SpriteRenderer>().sprite = GetItemSprite(newItemInfo.BoxName, newItemID);
                    }
                }
                else
                {
                    // Create fake weight item
                    GameObject obj = new GameObject(newItemID.ToString());
                    obj.transform.position = item.transform.position;
                    obj.transform.SetParent(item.transform.parent);

                    FakeItem fakeItem = obj.AddComponent<FakeItem>();
                    fakeItem.Init(sys, item.itemActiveFlag, newItemInfo.ItemFlag);

                    SpriteRenderer renderer = obj.AddComponent<SpriteRenderer>();
                    renderer.sprite = GetRandomSprite();
                    renderer.enabled = false;

                    item.gameObject.SetActive(false);
                }
            }
        }

        // ================================================================
        // DissonanceChests
        // ================================================================

        private void DissonanceChests(string field)
        {
            Plugin.Log.LogInfo($"[SceneRando] DissonanceChests entered: field={field} randomDissonance={randomDissonance}");

            if (!randomDissonance || field == "lastBoss")
                return;

            // Destroy dissonance use-item targets
            GameObject toDestroy = null;
            foreach (var useItem in FindObjectsOfType<UsingItemTargetScript>())
            {
                if (useItem == null)
                    continue;

                foreach (var target in useItem.itemTargets)
                {
                    if (target.targetItem != L2Hit.USEITEM.USE_BEHERIT)
                        continue;

                    foreach (var flags in target.effectFlags)
                    {
                        if (flags.seet_no1 == 2 && flags.flag_no1 == 3)
                        {
                            toDestroy = useItem.gameObject;
                            break;
                        }
                    }

                    if (toDestroy != null)
                        break;
                }

                if (toDestroy != null)
                    break;
            }

            if (toDestroy != null)
                Destroy(toDestroy);

            // Turn off dissonance smoke effects
            foreach (var animator in FindObjectsOfType<Animator>())
            {
                if (animator != null && animator.name == "MinusSmoke")
                {
                    animator.gameObject.SetActive(false);
                    break;
                }
            }

            // Determine chest location based on field
            LocationID locationID = LocationID.None;
            Vector3 position = Vector3.zero;
            int sheet = 0, flag = 0;

            switch (field)
            {
                case "fieldL02":
                    locationID = LocationID.DissonanceMoG;
                    position = new Vector3(926, -166, 8);
                    sheet = 5;
                    flag = 59;
                    break;

                case "field09":
                    locationID = LocationID.DissonanceHL;
                    position = new Vector3(-55, -270, 8);
                    sheet = 13;
                    flag = 39;
                    break;

                case "field10":
                    locationID = LocationID.DissonanceValhalla;
                    position = new Vector3(1000, -6, 8);
                    sheet = 14;
                    flag = 42;
                    break;

                case "field11":
                    locationID = LocationID.DissonanceDSLM;
                    position = new Vector3(175, -870, 8);
                    sheet = 15;
                    flag = 45;
                    break;

                case "field15":
                    locationID = LocationID.DissonanceEPG;
                    position = new Vector3(-990, 138, 8);
                    sheet = 18;
                    flag = 55;
                    break;

                case "fieldSpace":
                    locationID = LocationID.DissonanceNibiru;
                    position = new Vector3(50, 138, 8);
                    sheet = 5;
                    flag = 60;
                    break;
            }

            Plugin.Log.LogInfo($"[SceneRando] DissonanceChests: field={field} → locationID={locationID} (int={(int)locationID})");

            if (locationID == LocationID.None)
                return;

            if (!locationToItemMap.TryGetValue(locationID, out ItemID itemID))
            {
                Plugin.Log.LogWarning($"[SceneRando] DissonanceChests: {locationID} (int={(int)locationID}) not in locationToItemMap (field={field}, map has {locationToItemMap.Count} entries)");
                return;
            }

            Plugin.Log.LogInfo($"[SceneRando] DissonanceChests: creating chest for {locationID} → item {itemID} (int={(int)itemID})");

            // Choose chest color by item type — AP check must come first since
            // AP item IDs (410000+) are numerically above ChestWeight01.
            int colorToUse;
            if ((int)itemID >= 410000)
                colorToUse = apChestColour;
            else if (itemID >= ItemID.ChestWeight01)
                colorToUse = weightChestColour;
            else
                colorToUse = itemChestColour;

            // Original L2Rando uses the blue chest prefab's rotation for dissonance chests.
            Quaternion rot = PrefabHarvester.CachedPrefabs.TryGetValue("blueChest", out GameObject blueRef)
                ? blueRef.transform.rotation : Quaternion.identity;
            TreasureBoxScript chest = CreateChest(colorToUse, position, rot);
            if (chest == null)
            {
                Plugin.Log.LogWarning($"[SceneRando] DissonanceChests: CreateChest returned null for color={colorToUse}");
                return;
            }

            if (IsLocationCursed(locationID) && PrefabHarvester.CachedPrefabs.TryGetValue("curse", out GameObject cursePrefab))
            {
                GameObject curse = Instantiate(cursePrefab, chest.transform.position, chest.transform.rotation);
                curse.SetActive(true);
                curse.transform.SetParent(chest.transform);
                chest.curseAnime = curse.GetComponent<Animator>();
                chest.curseParticle = curse.GetComponent<ParticleSystem>();
                chest.curseMode = true;
            }
            else
            {
                chest.curseMode = false;
            }

            chest.closetMode = false;
            chest.unlockFlags = new L2FlagBoxParent[]
            {
        new L2FlagBoxParent()
        {
            logoc = LOGIC.AND,
            BOX = new L2FlagBox[]
            {
                new L2FlagBox()
                {
                    seet_no1 = sheet,
                    flag_no1 = flag,
                    seet_no2 = -1,
                    flag_no2 = 1,
                    logic = LOGIC.AND,
                    comp = COMPARISON.GreaterEq
                },
                new L2FlagBox()
                {
                    seet_no1 = 2,
                    flag_no1 = 3,
                    seet_no2 = -1,
                    flag_no2 = 0,
                    logic = LOGIC.AND,
                    comp = COMPARISON.Greater
                }
            }
        }
            };

            chest.openFlags = new L2FlagBoxParent[]
            {
        new L2FlagBoxParent()
        {
            BOX = new L2FlagBox[]
            {
                new L2FlagBox()
                {
                    seet_no1 = 2,
                    flag_no1 = -1,
                    seet_no2 = -1,
                    flag_no2 = 1,
                    logic = LOGIC.NON,
                    comp = COMPARISON.GreaterEq
                }
            }
        }
            };

            ChangeChestItemFlags(chest, itemID);
            chest.gameObject.SetActive(true);

            Plugin.Log.LogInfo($"[SceneRando] DissonanceChests: chest spawned — " +
                $"active={chest.gameObject.activeInHierarchy} " +
                $"pos={chest.transform.position} " +
                $"parent={chest.transform.parent?.name ?? "ROOT"}");
        }

        // ================================================================
        // ChangeFlagWatchers
        // ================================================================

        private void ChangeFlagWatchers(string fieldName)
        {
            foreach (FlagWatcherScript flagWatcher in FindObjectsOfType<FlagWatcherScript>())
            {
                if (flagWatcher.name == "hardWatcher3")
                {
                    flagWatcher.gameObject.SetActive(false);
                }
                else if (flagWatcher.name == "sougiOn")
                {
                    foreach (L2FlagBoxParent flagBoxParent in flagWatcher.CheckFlags)
                    {
                        foreach (L2FlagBox flagBox in flagBoxParent.BOX)
                        {
                            if (flagBox.seet_no1 == 3 && flagBox.flag_no1 == 30)
                            {
                                flagBox.flag_no1 = 0;
                                flagBox.flag_no2 = 6;
                                flagBox.comp = COMPARISON.GreaterEq;
                            }
                        }
                    }
                    flagWatcher.actionWaitFrames = 60;
                }
                else if (flagWatcher.name == "ragnarok")
                {
                    foreach (L2FlagBoxParent flagBoxParent in flagWatcher.CheckFlags)
                    {
                        L2FlagBox[] flagBoxes = new L2FlagBox[3];
                        flagBoxes[0] = flagBoxParent.BOX[0];
                        flagBoxes[1] = flagBoxParent.BOX[1];
                        flagBoxes[2] = new L2FlagBox()
                        {
                            seet_no1 = 3, flag_no1 = 14, seet_no2 = -1, flag_no2 = 2,
                            comp = COMPARISON.GreaterEq, logic = LOGIC.AND
                        };
                        flagBoxParent.BOX = flagBoxes;
                    }
                }

                if (fieldName == "fieldL00")
                {
                    foreach (L2FlagBoxParent flagBoxParent in flagWatcher.CheckFlags)
                    {
                        foreach (L2FlagBox flagBox in flagBoxParent.BOX)
                        {
                            if (flagBox.seet_no1 == 5 && flagBox.flag_no1 == 3 && flagBox.flag_no2 == 2)
                            {
                                flagBox.flag_no2 = 1;
                                flagBox.comp = COMPARISON.GreaterEq;
                            }
                            else if (flagBox.seet_no1 == 2 && flagBox.flag_no1 == 131)
                            {
                                if (shopToItemMap.TryGetValue(LocationID.HinerShop3, out ShopItem item))
                                {
                                    ItemInfo info = ItemDB.GetItemInfo(item.ID);
                                    flagBox.seet_no1 = info.ItemSheet;
                                    flagBox.flag_no1 = info.ItemFlag;
                                    flagBox.flag_no2 = 1;
                                    if (item.ID == ItemID.MobileSuperx3P)
                                        flagBox.flag_no2 = 2;
                                }
                            }
                        }
                    }
                }
                else if (fieldName == "field02")
                {
                    foreach (L2FlagBoxParent flagBoxParent in flagWatcher.CheckFlags)
                    {
                        foreach (L2FlagBox flagBox in flagBoxParent.BOX)
                        {
                            if (flagBox.seet_no1 == 3 && flagBox.flag_no1 == 30 && flagBox.flag_no2 == 80)
                                flagBox.flag_no2 = 255;
                        }
                    }
                }
                else if (fieldName == "fieldL08")
                {
                    if (flagWatcher.name == "FlagWatcher (8)")
                        flagWatcher.gameObject.SetActive(false);
                }
                else if (fieldName == "field13")
                {
                    if (echidna != 5 && (flagWatcher.name == "FlagWatcherTime2" || flagWatcher.name == "FlagWatcherTime3"))
                        flagWatcher.gameObject.SetActive(false);

                    if (flagWatcher.name == "FlagWatcherTime1")
                    {
                        switch (echidna)
                        {
                            case 0:
                                flagWatcher.gameObject.SetActive(false);
                                break;
                            case 1: case 2: case 3:
                                foreach (L2FlagBoxParent flagBoxParent in flagWatcher.CheckFlags)
                                    foreach (L2FlagBox flagBox in flagBoxParent.BOX)
                                        if (flagBox.seet_no1 == 0 && flagBox.flag_no1 == 35)
                                            flagBox.flag_no2 = 0;
                                foreach (L2FlagBoxEnd flagBoxEnd in flagWatcher.ActionFlags)
                                    flagBoxEnd.data = (short)echidna;
                                break;
                        }
                    }

                    RemoveCorridorSealers(flagWatcher);
                }
                else if (fieldName == "field06-2" || fieldName == "field10" || fieldName == "field11" ||
                         fieldName == "field12" || fieldName == "field15")
                {
                    RemoveCorridorSealers(flagWatcher);
                }
            }
        }

        private void RemoveCorridorSealers(FlagWatcherScript flagWatcher)
        {
            bool isCorridorSealer = false;
            foreach (L2FlagBoxParent flagBoxParent in flagWatcher.CheckFlags)
            {
                foreach (L2FlagBox flagBox in flagBoxParent.BOX)
                {
                    if (flagBox.seet_no1 == 5 && flagBox.flag_no1 == 48 && flagBox.flag_no2 == 1)
                    {
                        isCorridorSealer = true;
                        break;
                    }
                }
            }
            if (isCorridorSealer)
                Destroy(flagWatcher.gameObject);
        }

        // ================================================================
        // FieldSpecificChanges
        // ================================================================

        private void FieldSpecificChanges(string fieldName)
        {
            if (fieldName == "field01-2")
            {
                foreach (ItemPotScript pot in FindObjectsOfType<ItemPotScript>())
                    pot.transform.position = new Vector3(pot.transform.position.x - 100, pot.transform.position.y, pot.transform.position.z);
            }
            else if (fieldName == "field00")
            {
                GameObject obj = GameObject.Find("endPiller");
                if (obj != null) obj.SetActive(false);
            }
            else if (fieldName == "field04")
            {
                foreach (ShopGateScript shopGate in FindObjectsOfType<ShopGateScript>())
                {
                    if (shopGate.name == "ShopGate (1)")
                        shopGate.shdowtask = null;
                    else if (shopGate.name == "ShopGate (2)")
                        shopGate.gameObject.SetActive(false);
                }
            }
            else if (fieldName == "field10")
            {
                CorridorSealerFlagWatcher(new Vector3(48, 208, 0));
            }
            else if (fieldName == "field11")
            {
                CorridorSealerFlagWatcher(new Vector3(28, 504, 0));

                foreach (var stepController in FindObjectsOfType<StepAnimationController>())
                {
                    if (stepController.name == "Pyramid")
                    {
                        foreach (var flagBoxParent in stepController.animeSteps[2].nextFlag)
                            foreach (var flagBox in flagBoxParent.BOX)
                                if (flagBox.seet_no1 == 3 && flagBox.flag_no1 == 95)
                                {
                                    flagBox.flag_no2 = 100;
                                    flagBox.comp = COMPARISON.Greater;
                                }
                        foreach (var flagBoxParent in stepController.animeSteps[3].stateFlag)
                            foreach (var flagBox in flagBoxParent.BOX)
                                if (flagBox.seet_no1 == 3 && flagBox.flag_no1 == 95)
                                {
                                    flagBox.flag_no2 = 100;
                                    flagBox.comp = COMPARISON.Greater;
                                }
                    }
                }
            }
            else if (fieldName == "field12")
                CorridorSealerFlagWatcher(new Vector3(210, 168, 0));
            else if (fieldName == "field13")
                CorridorSealerFlagWatcher(new Vector3(824, -544, 0));
            else if (fieldName == "field14")
                CorridorSealerFlagWatcher(new Vector3(-8, 48, 0));
            else if (fieldName == "field06-2")
                CorridorSealerFlagWatcher(new Vector3(572, -16, 0));
            else if (fieldName == "fieldSpace")
            {
                foreach (HolyGrailCancellerScript grailCanceller in FindObjectsOfType<HolyGrailCancellerScript>())
                    grailCanceller.gameObject.SetActive(false);
            }
            else if (fieldName == "fieldL08")
            {
                foreach (ShopGateScript talkGate in FindObjectsOfType<ShopGateScript>())
                {
                    if (talkGate.shdowtask != null)
                    {
                        foreach (L2FlagBoxParent flagBoxParent in talkGate.shdowtask.startflag)
                        {
                            foreach (L2FlagBox flagBox in flagBoxParent.BOX)
                            {
                                ItemID itemID = GetItemIDForLocation(LocationID.FreyasItem);
                                ItemInfo itemInfo = ItemDB.GetItemInfo(itemID);
                                if (itemInfo != null)
                                {
                                    flagBox.comp = COMPARISON.Less;
                                    flagBox.seet_no1 = itemInfo.ItemSheet;
                                    flagBox.flag_no1 = itemInfo.ItemFlag;
                                    flagBox.flag_no2 = 1;
                                    if (itemID == ItemID.MobileSuperx3P)
                                        flagBox.flag_no2 = 2;
                                }
                            }
                        }
                    }
                }
            }
            else if (fieldName == "lastBoss")
            {
                foreach (var useItem in FindObjectsOfType<UsingItemTargetScript>())
                {
                    foreach (var target in useItem.itemTargets)
                    {
                        if (target.targetItem == L2Hit.USEITEM.USE_BEHERIT)
                        {
                            foreach (var flags in target.effectFlags)
                            {
                                if (flags.seet_no1 == 2 && flags.flag_no1 == 3)
                                    flags.data = 2;
                            }
                        }
                    }
                }
            }
        }

        private void CorridorSealerFlagWatcher(Vector3 position)
        {
            GameObject obj = new GameObject("CorridorSealerFlagWatcher");
            obj.transform.position = position;
            FlagWatcherScript flagWatcher = obj.AddComponent<FlagWatcherScript>();
            flagWatcher.setTaskSystemName(L2Base.TASKSYSNAME.SCENE);
            flagWatcher.actionWaitFrames = 90;
            flagWatcher.autoFinish = false;
            flagWatcher.characterEfxType = MoveCharacterBase.CharacterEffectType.NONE;
            flagWatcher.startAreaMode = MoveCharacterBase.ActionstartAreaMode.VIEW;
            flagWatcher.taskLayerNo = 2;
            flagWatcher.AnimeData = new GameObject[0];
            flagWatcher.ResetFlags = new L2FlagBoxEnd[0];

            List<L2FlagBox> flagBoxes = new List<L2FlagBox>
            {
                new L2FlagBox() { seet_no1 = 3, flag_no1 = 93, seet_no2 = -1, flag_no2 = 0, logic = LOGIC.AND, comp = COMPARISON.Equal },
                new L2FlagBox() { seet_no1 = 2, flag_no1 = 3, seet_no2 = -1, flag_no2 = 7, logic = LOGIC.AND, comp = COMPARISON.Equal }
            };

            if (randomDissonance)
            {
                flagBoxes.Add(new L2FlagBox()
                {
                    seet_no1 = 3, flag_no1 = 0, seet_no2 = -1, flag_no2 = requiredGuardians,
                    logic = LOGIC.AND, comp = COMPARISON.GreaterEq
                });
            }
            else
            {
                flagBoxes.Add(new L2FlagBox()
                {
                    seet_no1 = 3, flag_no1 = 15, seet_no2 = -1, flag_no2 = 4,
                    logic = LOGIC.AND, comp = COMPARISON.GreaterEq
                });
            }

            flagWatcher.CheckFlags = new L2FlagBoxParent[] { new L2FlagBoxParent { BOX = flagBoxes.ToArray() } };
            flagWatcher.ActionFlags = new L2FlagBoxEnd[]
            {
                new L2FlagBoxEnd() { seet_no1 = 3, flag_no1 = 93, data = 2, calcu = CALCU.EQR },
            };
            flagWatcher.finishFlags = new L2FlagBoxParent[]
            {
                new L2FlagBoxParent
                {
                    BOX = new L2FlagBox[]
                    {
                        new L2FlagBox() { seet_no1 = 3, flag_no1 = 93, seet_no2 = -1, flag_no2 = 2, logic = LOGIC.NON, comp = COMPARISON.Equal }
                    }
                }
            };
            obj.SetActive(true);
        }

        // ================================================================
        // ObjectChanges
        // ================================================================

        private void ObjectChanges()
        {
            foreach (FlagDialogueScript flagDialogue in FindObjectsOfType<FlagDialogueScript>())
            {
                if (flagDialogue.cellName.Contains("mantraDialog"))
                    flagDialogue.gameObject.SetActive(false);
            }

            foreach (SnapShotTargetScript snapTarget in FindObjectsOfType<SnapShotTargetScript>())
            {
                LocationID locationID = GetLocationIDForMural(snapTarget);
                if (locationID != LocationID.None)
                    snapTarget.mode = SnapShotTargetScript.SnapShotMode.SOFTWARE;
            }

            foreach (AnchorGateZ anchorGate in FindObjectsOfType<AnchorGateZ>())
            {
                if (anchorGate.shdowtask != null)
                {
                    foreach (L2FlagBoxParent flagBoxParent in anchorGate.shdowtask.startflag)
                    {
                        foreach (L2FlagBox flagBox in flagBoxParent.BOX)
                        {
                            if (flagBox.seet_no1 == 3 && flagBox.flag_no1 == 93 && flagBox.flag_no2 == 0)
                                flagBox.comp = COMPARISON.GreaterEq;
                        }
                    }
                }
            }

            foreach (AnimatorController animatorController in FindObjectsOfType<AnimatorController>())
            {
                if (animatorController.name == "WrongCall")
                {
                    foreach (L2FlagBoxParent flagBoxParent in animatorController.CheckFlags)
                    {
                        foreach (L2FlagBox flagBox in flagBoxParent.BOX)
                        {
                            if (flagBox.seet_no1 == 2 && flagBox.flag_no1 == 16 && flagBox.flag_no2 == 1)
                            {
                                flagBox.flag_no2 = 0;
                                flagBox.comp = COMPARISON.GreaterEq;
                            }
                        }
                    }
                }
            }

            foreach (Animator animator in FindObjectsOfType<Animator>())
            {
                if (animator.name == "6starDemo")
                    animator.gameObject.SetActive(false);
            }
        }

        // ================================================================
        // ChangeEntrances — full parity with L2Rando including
        // YugGate doors, soul gate prefab instantiation, and fLGate watcher
        // ================================================================

        private List<GameObject> ChangeEntrances(string field)
        {
            List<GameObject> objectsToDeactivate = new List<GameObject>();

            if (StartingGame && field == "field01-2")
            {
                var gate = FindObjectOfType<AnchorGateZ>();
                var startInfo = StartDB.GetStartInfo(startingArea);
                if (gate != null && startInfo != null)
                {
                    gate.AnchorName = startInfo.AnchorName;
                    gate.FieldNo = startInfo.FieldNo;
                    gate.AnchorID = -1;
                }
                StartingGame = false;
                return objectsToDeactivate;
            }

            // When soul gates are randomized, remove all existing soul gate visuals
            // so we can replace them with the correct soul-value prefabs.
            if (randomSoulGates)
            {
                foreach (AnimatorController controller in FindObjectsOfType<AnimatorController>())
                {
                    if (controller.name == "soul_gate" || controller.name == "soul_cont")
                        objectsToDeactivate.Add(controller.gameObject);
                }
            }

            // Collect YugGateDoor animators for regular gate visual updates
            List<AnimatorController> yugGates = new List<AnimatorController>();
            foreach (AnimatorController animator in FindObjectsOfType<AnimatorController>())
            {
                if (animator.name == "YugGateDoor")
                    yugGates.Add(animator);
            }

            foreach (AnchorGateZ gate in FindObjectsOfType<AnchorGateZ>())
            {
                ExitID exitID = GetExitIDFromAnchorName(gate.AnchorName, field);
                if (exitID == ExitID.None) continue;

                if (exitToExitMap.TryGetValue(exitID, out ExitID destinationID))
                {
                    ExitInfo destinationInfo = ExitDB.GetExitInfo(destinationID);
                    if (destinationInfo == null) continue;

                    gate.AnchorName = destinationInfo.AnchorName;
                    gate.FieldNo = destinationInfo.FieldNo;
                    gate.AnchorID = -1;

                    ExitInfo exitInfo = ExitDB.GetExitInfo(exitID);

                    gate.bgmFadeOut = false;

                    // Regular (Yug) gates — update door visuals and shadow task flags
                    if (exitID >= ExitID.f00GateY0 && exitID <= ExitID.fL11GateN)
                    {
                        L2FlagBoxParent[] boxParents = new L2FlagBoxParent[] { new L2FlagBoxParent() };
                        List<L2FlagBox> flagBoxes = new List<L2FlagBox>();

                        // Find the nearby YugGateDoor animator for this gate
                        AnimatorController gateDoor = null;
                        foreach (var door in yugGates)
                        {
                            Vector3 position = gate.transform.position;
                            if (position.x - 30 < door.transform.position.x && position.x + 30 > door.transform.position.x &&
                                position.y - 30 < door.transform.position.y && position.y + 30 > door.transform.position.y)
                            {
                                gateDoor = door;
                                break;
                            }
                        }

                        flagBoxes.Add(new L2FlagBox()
                        {
                            seet_no1 = exitInfo.SheetNo,
                            flag_no1 = exitInfo.FlagNo,
                            seet_no2 = -1,
                            flag_no2 = exitInfo.SheetNo == 0 ? -1 : 0,
                            logic = LOGIC.AND,
                            comp = COMPARISON.Greater
                        });

                        if (exitID == ExitID.f07GateP0)
                        {
                            flagBoxes.Add(new L2FlagBox()
                            {
                                seet_no1 = 11, flag_no1 = 24, seet_no2 = -1, flag_no2 = 0,
                                logic = LOGIC.AND, comp = COMPARISON.Equal
                            });
                        }

                        boxParents[0].BOX = flagBoxes.ToArray();
                        if (gate.shdowtask != null)
                            gate.shdowtask.startflag = boxParents;

                        if (gateDoor != null)
                            gateDoor.CheckFlags = boxParents;
                    }
                    // Soul gates — instantiate the correct soul gate/soul prefabs
                    else if (exitID >= ExitID.f00GateN1 && exitID <= ExitID.f14GateN6)
                    {
                        if (soulGateValueMap.TryGetValue(exitID, out int soulValue))
                        {
                            L2FlagBoxParent[] boxParents = new L2FlagBoxParent[] { new L2FlagBoxParent() };
                            List<L2FlagBox> flagBoxes = new List<L2FlagBox>();

                            AnimatorController soulGateDoor = null;
                            AnimatorController soul = null;

                            string gateKey = null, soulKey = null;
                            switch (soulValue)
                            {
                                case 1: gateKey = "oneSoulGate"; soulKey = "oneSoul"; break;
                                case 2: gateKey = "twoSoulGate"; soulKey = "twoSoul"; break;
                                case 3: gateKey = "threeSoulGate"; soulKey = "threeSoul"; break;
                                case 5: gateKey = "fiveSoulGate"; soulKey = "fiveSoul"; break;
                                case 9: gateKey = "nineSoulGate"; soulKey = "nineSoul"; break;
                            }

                            if (gateKey != null && PrefabHarvester.CachedPrefabs.TryGetValue(gateKey, out GameObject gatePrefab))
                                soulGateDoor = Instantiate(gatePrefab, gate.transform.position, Quaternion.identity).GetComponent<AnimatorController>();

                            if (soulKey != null && PrefabHarvester.CachedPrefabs.TryGetValue(soulKey, out GameObject soulPrefab))
                                soul = Instantiate(soulPrefab, gate.transform.position, Quaternion.identity).GetComponent<AnimatorController>();

                            if (soulGateDoor != null)
                                soulGateDoor.transform.SetParent(gate.transform.parent);
                            if (soul != null)
                                soul.transform.SetParent(gate.transform.parent);

                            flagBoxes.Add(new L2FlagBox()
                            {
                                seet_no1 = 3,
                                flag_no1 = 0,
                                seet_no2 = -1,
                                flag_no2 = soulValue,
                                logic = LOGIC.AND,
                                comp = COMPARISON.GreaterEq
                            });

                            boxParents[0].BOX = flagBoxes.ToArray();
                            if (gate.shdowtask != null)
                                gate.shdowtask.startflag = boxParents;

                            if (soulGateDoor != null)
                            {
                                soulGateDoor.gameObject.SetActive(true);
                                soulGateDoor.CheckFlags = boxParents;
                            }

                            if (soul != null)
                            {
                                soul.gameObject.SetActive(true);
                                soul.CheckFlags = boxParents;
                            }
                        }
                    }

                    // Gate flags for specific destinations
                    List<L2FlagBoxEnd> gateFlags = new List<L2FlagBoxEnd>();
                    if (destinationID == ExitID.fL02Left || destinationID == ExitID.fL02Up)
                    {
                        gateFlags.Add(new L2FlagBoxEnd() { seet_no1 = 5, flag_no1 = 73, data = 2, calcu = CALCU.EQR });
                        gateFlags.Add(new L2FlagBoxEnd() { seet_no1 = 5, flag_no1 = 22, data = 1, calcu = CALCU.EQR });
                    }
                    else if (destinationID == ExitID.f14GateN6)
                    {
                        gateFlags.Add(new L2FlagBoxEnd() { seet_no1 = 18, flag_no1 = 0, data = 1, calcu = CALCU.EQR });
                    }

                    if (gateFlags.Count > 0)
                    {
                        if (gate.gateFlags != null)
                            gateFlags.AddRange(gate.gateFlags.ToList());
                        gate.gateFlags = gateFlags.ToArray();
                    }

                    // fLGate destination — create corridor sealer FlagWatcher
                    if (destinationID == ExitID.fLGate)
                    {
                        GameObject obj = new GameObject();
                        obj.transform.position = gate.transform.position;
                        FlagWatcherScript flagWatcher = obj.AddComponent<FlagWatcherScript>();
                        flagWatcher.actionWaitFrames = 60;
                        flagWatcher.autoFinish = false;
                        flagWatcher.characterEfxType = MoveCharacterBase.CharacterEffectType.NONE;
                        flagWatcher.startAreaMode = MoveCharacterBase.ActionstartAreaMode.VIEW;
                        flagWatcher.taskLayerNo = 2;
                        flagWatcher.AnimeData = new GameObject[0];
                        flagWatcher.ResetFlags = new L2FlagBoxEnd[0];
                        flagWatcher.CheckFlags = new L2FlagBoxParent[]
                        {
                            new L2FlagBoxParent
                            {
                                BOX = new L2FlagBox[]
                                {
                                    new L2FlagBox()
                                    {
                                        seet_no1 = 5, flag_no1 = 73, seet_no2 = -1, flag_no2 = 2,
                                        logic = LOGIC.NON, comp = COMPARISON.Equal
                                    }
                                }
                            }
                        };
                        flagWatcher.ActionFlags = new L2FlagBoxEnd[]
                        {
                            new L2FlagBoxEnd() { seet_no1 = 5, flag_no1 = 73, data = 1, calcu = CALCU.EQR }
                        };
                        flagWatcher.finishFlags = new L2FlagBoxParent[]
                        {
                            new L2FlagBoxParent
                            {
                                BOX = new L2FlagBox[]
                                {
                                    new L2FlagBox()
                                    {
                                        seet_no1 = 5, flag_no1 = 73, seet_no2 = -1, flag_no2 = 1,
                                        logic = LOGIC.NON, comp = COMPARISON.Equal
                                    }
                                }
                            }
                        };
                    }
                }
                else if (exitID == ExitID.fLDown)
                {
                    List<L2FlagBoxEnd> gateFlags = new List<L2FlagBoxEnd>
                    {
                        new L2FlagBoxEnd() { seet_no1 = 5, flag_no1 = 73, data = 2, calcu = CALCU.EQR },
                        new L2FlagBoxEnd() { seet_no1 = 5, flag_no1 = 22, data = 1, calcu = CALCU.EQR }
                    };
                    if (gate.gateFlags != null)
                        gateFlags.AddRange(gate.gateFlags.ToList());
                    gate.gateFlags = gateFlags.ToArray();
                }
            }

            return objectsToDeactivate;
        }

        private ExitID GetExitIDFromAnchorName(string anchorName, string field)
        {
            if (anchorName == "PlayerStart")
            {
                if (field == "fieldL02") return ExitID.fL02Left;
                else if (field == "field03") return ExitID.f03Right;
                else if (field == "fieldP00") return ExitID.fP00Right;
                else if (field == "field01") return ExitID.f01Down;
                else if (field == "field11") return ExitID.f11Pyramid;
            }
            else if (anchorName == "PlayerStart0")
                return ExitID.f03GateP0;
            else if (anchorName == "PlayerStart1")
                return ExitID.f03GateP1;
            else if (field == "field01-2" && anchorName == "PlayerStart f01Right")
                return ExitID.fStart;
            else if (field == "field01" && anchorName == "PlayerStart f01Right")
                return ExitID.f01Start;

            return ExitDB.AnchorNameToExitID(anchorName);
        }

        // ================================================================
        // AddAnchorPoints
        // ================================================================

        private void AddAnchorPoints(string fieldName)
        {
            BGScrollSystem bgScroll = FindObjectOfType<BGScrollSystem>();
            if (bgScroll == null) return;

            string anchorName = null;
            Vector3 anchorPos = Vector3.zero;

            if (fieldName == "field02") { anchorName = "PlayerStart f02Bifrost"; anchorPos = new Vector3(-480, -460, 0); }
            else if (fieldName == "field03") { anchorName = "PlayerStart f03Up"; anchorPos = new Vector3(488, 756, 0); }
            else if (fieldName == "field04") { anchorName = "PlayerStart f04Up3"; anchorPos = new Vector3(840, 640, 0); }
            else if (fieldName == "field08") { anchorName = "PlayerStart f08Neck"; anchorPos = new Vector3(910, 170, 0); }

            if (anchorName != null)
            {
                GameObject obj = new GameObject(anchorName);
                obj.transform.SetParent(bgScroll.transform);
                PlayerAnchor2 playerAnchor = obj.AddComponent<PlayerAnchor2>();
                playerAnchor.transform.position = anchorPos;
                bgScroll.WarpAnchors.Add(playerAnchor);
            }
        }

        // ================================================================
        // CreateStartingFieldObjects
        // ================================================================

        private void CreateStartingFieldObjects(string field)
        {
            if (!field.Equals(startFieldName))
                return;

            Vector3 tabletPosition = Vector3.zero;
            foreach (HolyTabretScript holyTablet in FindObjectsOfType<HolyTabretScript>())
            {
                if (holyTablet.name == "TabletH" || (holyTablet.name == "TabletHB" && startingArea >= AreaID.ValhallaMain))
                {
                    tabletPosition = holyTablet.transform.position;
                    var hotSpring = holyTablet.gameObject.AddComponent<HotSpring>();
                    hotSpring.Init(sys);
                }
            }

            // VoD already has its native shops nearby — skip the "Start Shop" placement.
            if (startingArea == AreaID.VoD)
                return;

            StartInfo startInfo = StartDB.GetStartInfo(startingArea);
            if (startInfo == null) return;

            foreach (ShopGateScript shopGate in FindObjectsOfType<ShopGateScript>())
            {
                if (shopGate.name == "ShopGate")
                {
                    Vector3 shopPosition = tabletPosition + startInfo.ShopOffset;
                    shopPosition.z = 0;
                    GameObject shop = Instantiate(shopGate.gameObject, shopPosition, Quaternion.identity, shopGate.transform.parent);
                    shop.name = "Start Shop";
                    shop.SetActive(true);
                    ShopGateScript gateScript = shop.GetComponent<ShopGateScript>();
                    gateScript.sheetName = "f04-1e";
                    gateScript.shdowtask = null;

                    GameObject shopVisual = new GameObject("Start Shop Entrance");
                    shopVisual.transform.position = new Vector3(shopPosition.x - 10, shopPosition.y, 1);
                    shopVisual.transform.localScale = new Vector3(5, 7, 1);
                    shopVisual.SetActive(true);
                    SpriteRenderer spriteRenderer = shopVisual.AddComponent<SpriteRenderer>();
                    spriteRenderer.sprite = Sprite.Create(Texture2D.whiteTexture,
                        new Rect(0, 0, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
                        new Vector2(0, 0), 1);
                    spriteRenderer.color = Color.white;
                    break;
                }
            }
        }
        // ================================================================
        // Shop & Dialogue Database Rewrites (one-time after connection)
        // ================================================================

        private void TryInitShopDialogue()
        {
            if (shopDataBase == null || talkDataBase == null)
            {
                var sys = FindObjectOfType<L2System>();
                if (sys != null)
                {
                    if (shopDataBase == null)
                        shopDataBase = Traverse.Create(sys).Field("l2sdb").GetValue<L2ShopDataBase>();
                    if (talkDataBase == null)
                        talkDataBase = Traverse.Create(sys).Field("l2tdb").GetValue<L2TalkDataBase>();
                }
            }

            if (shopDataBase == null || talkDataBase == null)
                return;

            ChangeShopItems();
            ChangeShopThanks();
            ChangeDialogueItems();
            MojiScriptFixes();

            shopDialogueInitialized = true;
            Plugin.Log.LogInfo("[SceneRando] Shop/dialogue databases rewritten.");
        }

        // ================================================================
        // ChangeShopItems — rewrite shop item listings
        // ================================================================

        private void ChangeShopItems()
        {
            shopDataBase.cellData[0][25][1][0] = CreateShopItemsString(LocationID.SidroShop1, LocationID.SidroShop2, LocationID.SidroShop3);
            shopDataBase.cellData[1][26][1][0] = CreateShopItemsString(LocationID.ModroShop1, LocationID.ModroShop2, LocationID.ModroShop3);
            shopDataBase.cellData[2][24][1][0] = CreateShopItemsString(LocationID.NeburShop1, LocationID.NeburShop2, LocationID.NeburShop3);
            shopDataBase.cellData[3][25][1][0] = CreateShopItemsString(LocationID.HinerShop1, LocationID.HinerShop2, LocationID.HinerShop3);
            shopDataBase.cellData[4][24][1][0] = CreateShopItemsString(LocationID.HinerShop1, LocationID.HinerShop2, LocationID.HinerShop4);
            shopDataBase.cellData[5][24][1][0] = CreateShopItemsString(LocationID.KorobockShop1, LocationID.KorobockShop2, LocationID.KorobockShop3);
            shopDataBase.cellData[6][24][1][0] = CreateShopItemsString(LocationID.PymShop1, LocationID.PymShop2, LocationID.PymShop3);
            shopDataBase.cellData[7][24][1][0] = CreateShopItemsString(LocationID.PeibalusaShop1, LocationID.PeibalusaShop2, LocationID.PeibalusaShop3);
            shopDataBase.cellData[8][24][1][0] = CreateShopItemsString(LocationID.HiroRoderickShop1, LocationID.HiroRoderickShop2, LocationID.HiroRoderickShop3);
            shopDataBase.cellData[9][24][1][0] = CreateShopItemsString(LocationID.BTKShop1, LocationID.BTKShop2, LocationID.BTKShop3);
            shopDataBase.cellData[10][24][1][0] = CreateShopItemsString(LocationID.StartingShop1, LocationID.StartingShop2, LocationID.StartingShop3);
            shopDataBase.cellData[11][24][1][0] = CreateShopItemsString(LocationID.MinoShop1, LocationID.MinoShop2, LocationID.MinoShop3);
            shopDataBase.cellData[12][24][1][0] = CreateShopItemsString(LocationID.ShuhokaShop1, LocationID.ShuhokaShop2, LocationID.ShuhokaShop3);
            shopDataBase.cellData[13][24][1][0] = CreateShopItemsString(LocationID.HydlitShop1, LocationID.HydlitShop2, LocationID.HydlitShop3);
            shopDataBase.cellData[14][24][1][0] = CreateShopItemsString(LocationID.AytumShop1, LocationID.AytumShop2, LocationID.AytumShop3);
            shopDataBase.cellData[15][24][1][0] = CreateShopItemsString(LocationID.AshGeenShop1, LocationID.AshGeenShop2, LocationID.AshGeenShop3);
            shopDataBase.cellData[16][24][1][0] = CreateShopItemsString(LocationID.MegarockShop1, LocationID.MegarockShop2, LocationID.MegarockShop3);
            shopDataBase.cellData[17][24][1][0] = CreateShopItemsString(LocationID.BargainDuckShop1, LocationID.BargainDuckShop2, LocationID.BargainDuckShop3);
            shopDataBase.cellData[18][24][1][0] = CreateShopItemsString(LocationID.KeroShop1, LocationID.KeroShop2, LocationID.KeroShop3);
            shopDataBase.cellData[19][24][1][0] = CreateShopItemsString(LocationID.VenumShop1, LocationID.VenumShop2, LocationID.VenumShop3);
            shopDataBase.cellData[20][24][1][0] = CreateShopItemsString(LocationID.FairylanShop1, LocationID.FairylanShop2, LocationID.FairylanShop3);
        }

        private string CreateShopItemsString(LocationID first, LocationID second, LocationID third)
        {
            return $"{CreateSetItemString(first)}\n{CreateSetItemString(second)}\n{CreateSetItemString(third)}";
        }

        private string CreateSetItemString(LocationID locationID)
        {
            if (shopToItemMap.TryGetValue(locationID, out ShopItem shopItem))
            {
                ItemInfo info = ItemDB.GetItemInfo(shopItem.ID);
                int itemIdRaw = (int)shopItem.ID;
                bool isApPlaceholder = itemIdRaw >= 410000;
                bool isFiller = itemIdRaw >= 191 && itemIdRaw <= 295;

                // AP placeholders have no ItemDB entry — use defaults
                string shopType = info?.ShopType ?? "item";
                string itemName = info?.ShopName ?? "AP Item";
                int shopPrice = info?.ShopPrice ?? 10;
                int shopAmount = info?.ShopAmount ?? 1;
                int maxShopAmount = info?.MaxShopAmount ?? 1;
                int flagIndex = -1;

                // Append the tracking flag index directly into the item string for the Shop UI
                if (isApPlaceholder)
                {
                    flagIndex = ApItemIDs.ToFlagIndex(itemIdRaw);
                    itemName = $"AP Item {flagIndex}";
                }
                else if (isFiller)
                {
                    if (itemIdRaw >= 191 && itemIdRaw <= 230) flagIndex = itemIdRaw - 191;
                    else if (itemIdRaw >= 231 && itemIdRaw <= 270) flagIndex = itemIdRaw - 231 + 40;
                    else if (itemIdRaw >= 271 && itemIdRaw <= 280) flagIndex = itemIdRaw - 271 + 80;
                    else flagIndex = itemIdRaw - 281 + 90;

                    itemName = $"{info?.BoxName ?? "Weight"} {flagIndex}";
                }

                bool freeAmmo = IsStartWeaponAmmo(shopItem.ID);
                bool isWeight = info != null && info.BoxName == "Weight";
                bool isAmmo = info != null && info.ShopName != null && info.ShopName.EndsWith("-b");

                // Pricing:
                //   Regular items & AP items: ShopPrice * Multiplier
                //   Filler (Coin/Weight in shops): free
                //   Weight shop item: static 10
                //   Ammo: ShopPrice * Multiplier (vanilla)
                //   Starting weapon ammo: free
                int price;
                if (freeAmmo)
                    price = 0;
                else if (isFiller)
                    price = 0;
                else if (isWeight)
                    price = 10;
                else
                    price = shopPrice * shopItem.Multiplier;

                int amount = freeAmmo ? maxShopAmount : shopAmount;

                return $"[@sitm,{shopType},{itemName},{price},{amount}]";
            }
            return string.Empty;
        }

        private bool IsStartWeaponAmmo(ItemID ammoID)
        {
            return ammoID switch
            {
                ItemID.ShurikenAmmo => startingWeapon == ItemID.Shuriken,
                ItemID.RollingShurikenAmmo => startingWeapon == ItemID.RollingShuriken,
                ItemID.EarthSpearAmmo => startingWeapon == ItemID.EarthSpear,
                ItemID.FlareAmmo => startingWeapon == ItemID.Flare,
                ItemID.CaltropsAmmo => startingWeapon == ItemID.Caltrops,
                ItemID.ChakramAmmo => startingWeapon == ItemID.Chakram,
                ItemID.BombAmmo => startingWeapon == ItemID.Bomb,
                ItemID.PistolAmmo => startingWeapon == ItemID.Pistol,
                _ => false,
            };
        }

        // ================================================================
        // ChangeShopThanks — append get-flag strings to thank scripts
        // ================================================================

        private void ChangeShopThanks()
        {
            // Fix specific thank strings that do unwanted things
            shopDataBase.cellData[1][9][1][0] = "[@anim,thank,1]\n[@animp,buyF0121,1]";   // Modro thank1 (remove shield check)
            shopDataBase.cellData[3][11][1][0] = "[@anim,thank,1]\n[@animp,buyF0142,1]";  // Hiner thank3
            shopDataBase.cellData[4][10][1][0] = "[@anim,thank,1]\n[@animp,buyF0142,1]";  // Hiner thank4
            shopDataBase.cellData[8][10][1][0] = "[@anim,thank,1]\n[@animp,buyF032,1]";   // Hiro Roderick thank3
            shopDataBase.cellData[13][10][1][0] = "[@anim,wait,1]\n[@animp,buyF06,1]";    // Hydlit thank3

            ChangeThanksStrings(LocationID.SidroShop1, LocationID.SidroShop2, LocationID.SidroShop3, 0, 9);
            ChangeThanksStrings(LocationID.ModroShop1, LocationID.ModroShop2, LocationID.ModroShop3, 1, 9, 2, 3);
            ChangeThanksStrings(LocationID.NeburShop1, LocationID.NeburShop2, LocationID.NeburShop3, 2, 8);
            ChangeThanksStrings(LocationID.HinerShop1, LocationID.HinerShop2, LocationID.HinerShop3, 3, 9);
            ChangeThanksStrings(LocationID.HinerShop1, LocationID.HinerShop2, LocationID.HinerShop4, 4, 8);
            ChangeThanksStrings(LocationID.KorobockShop1, LocationID.KorobockShop2, LocationID.KorobockShop3, 5, 8);
            ChangeThanksStrings(LocationID.PymShop1, LocationID.PymShop2, LocationID.PymShop3, 6, 8);
            ChangeThanksStrings(LocationID.PeibalusaShop1, LocationID.PeibalusaShop2, LocationID.PeibalusaShop3, 7, 8);
            ChangeThanksStrings(LocationID.HiroRoderickShop1, LocationID.HiroRoderickShop2, LocationID.HiroRoderickShop3, 8, 8);
            ChangeThanksStrings(LocationID.BTKShop1, LocationID.BTKShop2, LocationID.BTKShop3, 9, 8);
            ChangeThanksStrings(LocationID.StartingShop1, LocationID.StartingShop2, LocationID.StartingShop3, 10, 8);
            ChangeThanksStrings(LocationID.MinoShop1, LocationID.MinoShop2, LocationID.MinoShop3, 11, 8);
            ChangeThanksStrings(LocationID.ShuhokaShop1, LocationID.ShuhokaShop2, LocationID.ShuhokaShop3, 12, 8);
            ChangeThanksStrings(LocationID.HydlitShop1, LocationID.HydlitShop2, LocationID.HydlitShop3, 13, 8);
            ChangeThanksStrings(LocationID.AytumShop1, LocationID.AytumShop2, LocationID.AytumShop3, 14, 8);
            ChangeThanksStrings(LocationID.AshGeenShop1, LocationID.AshGeenShop2, LocationID.AshGeenShop3, 15, 8);
            ChangeThanksStrings(LocationID.MegarockShop1, LocationID.MegarockShop2, LocationID.MegarockShop3, 16, 8);
            ChangeThanksStrings(LocationID.BargainDuckShop1, LocationID.BargainDuckShop2, LocationID.BargainDuckShop3, 17, 8);
            ChangeThanksStrings(LocationID.KeroShop1, LocationID.KeroShop2, LocationID.KeroShop3, 18, 8);
            ChangeThanksStrings(LocationID.VenumShop1, LocationID.VenumShop2, LocationID.VenumShop3, 19, 8);
            ChangeThanksStrings(LocationID.FairylanShop1, LocationID.FairylanShop2, LocationID.FairylanShop3, 20, 8);
        }

        private void ChangeThanksStrings(LocationID first, LocationID second, LocationID third, int sheet, int firstRow, int secondOffset = 1, int thirdOffset = 2)
        {
            AppendThank(sheet, firstRow, first);
            AppendThank(sheet, firstRow + secondOffset, second);
            AppendThank(sheet, firstRow + thirdOffset, third);
        }

        private void AppendThank(int sheet, int row, LocationID loc)
        {
            string key = sheet + ":" + row;
            if (!_shopThanksOriginals.TryGetValue(key, out string original))
            {
                original = shopDataBase.cellData[sheet][row][1][0];
                _shopThanksOriginals[key] = original;
            }
            shopDataBase.cellData[sheet][row][1][0] = original + CreateGetFlagString(loc);
        }

        private string CreateGetFlagString(LocationID locationID)
        {
            string flagString = string.Empty;

            if (shopToItemMap.TryGetValue(locationID, out ShopItem shopItem))
            {
                ItemInfo info = ItemDB.GetItemInfo(shopItem.ID);

                if (info.BoxName.Equals("Crystal S"))
                    flagString = "\n[@take,Crystal S,02item,1]";

                foreach (L2FlagBoxEnd flag in CreateGetFlags(shopItem.ID, info))
                {
                    if (flag.calcu == CALCU.ADD)
                        flagString += $"\n[@setf,{flag.seet_no1},{flag.flag_no1},+,{flag.data}]";
                    else if (flag.calcu == CALCU.EQR)
                        flagString += $"\n[@setf,{flag.seet_no1},{flag.flag_no1},=,{flag.data}]";
                }
            }
            return flagString;
        }

        // ================================================================
        // ChangeDialogueItems — rewrite NPC dialogue scripts
        // ================================================================

        private void ChangeDialogueItems()
        {
            // Xelpud's item
            talkDataBase.cellData[1][10][1][0] = ChangeTalkString(LocationID.XelpudItem,
                "{0}[@setf,3,31,=,1]\n[@setf,5,2,=,1]\n[@setf,5,20,=,2]\n[@p,lastC]");

            // Nebur's item
            talkDataBase.cellData[0][11][1][0] = ChangeTalkString(LocationID.NeburItem,
                "[@anim,thanks,1]\n{0}[@setf,2,127,=,1]\n[@setf,2,128,=,1]\n[@setf,2,129,=,1]\n[@setf,2,130,=,1]\n[@setf,5,3,=,1]\n[@out]");

            // Nebur's map -> Xelpud gives it instead
            talkDataBase.cellData[1][70][1][0] = ChangeTalkString(LocationID.NeburItem,
                "{0}[@setf,2,127,=,1]\n[@setf,5,4,=,2]\n[@anim,talk,1]");

            // Alsedana's item
            talkDataBase.cellData[2][13][1][0] = ChangeTalkString(LocationID.AlsedanaItem,
                "{0}[@anim,talk,1]\n[@setf,1,54,=,1]\n[@p,2nd-6]");

            // Giltoriyo's item
            talkDataBase.cellData[3][5][1][0] = ChangeTalkString(LocationID.GiltoriyoItem,
                "{0}[@setf,1,54,=,1]\n[@anim,talk,1]\n[@p,1st-3]");

            // Check if you can get Giltoriyo's item
            talkDataBase.cellData[3][4][1][0] = ChangeTalkFlagCheck(LocationID.GiltoriyoItem, COMPARISON.Greater,
                "[@iff,{0},{1},&gt;,{2},giltoriyo,1st-3]\n[@anim,talk,1]\n[@p,1st-2]");

            // Alsedana's item from Giltoriyo
            talkDataBase.cellData[3][7][1][0] = ChangeTalkStringAndFlagCheck(LocationID.AlsedanaItem,
                "[@iff,{0},{1},&gt;,{2},giltoriyo,2nd]\n[@exit]\n{3}[@anim,talk,1]\n[@p,1st-5]");

            // Fobos' 1st item
            talkDataBase.cellData[6][9][1][0] = ChangeTalkString(LocationID.FobosItem,
                "[@setf,5,16,=,5]\n[@anim,talk,1]\n{0}[@p,3rd-2]");

            // Fobos' 1st item check
            talkDataBase.cellData[5][3][1][0] = ChangeTalkFlagCheck(LocationID.FobosItem, COMPARISON.Less,
                "[@iff,5,16,=,0,fobos,1st]\n[@iff,{0},{1},&lt;,{2},fobos,2nd]\n[@p,gS1]");

            // Fobos' 1st item (alternate)
            talkDataBase.cellData[5][16][1][0] = ChangeTalkString(LocationID.FobosItem,
                "[@exit]\n[@anim,talk,1]\n[@setf,5,17,=,1]\n{0}[@p,lastC]");

            // Fobos' 2nd item check
            talkDataBase.cellData[5][22][1][0] = ChangeTalkFlagCheck(LocationID.FobosItem2, COMPARISON.Less,
                "[@setf,5,17,=,1]\n[@iff,{0},{1},&lt;,{2},fobos,gS2]\n[@anim,stalk2,1]\n[@setf,23,15,=,2]\n[@anifla,mnext,swait]\n[@out]");

            // Fobos' 2nd item
            talkDataBase.cellData[5][24][1][0] = ChangeTalkString(LocationID.FobosItem2,
                "[@exit]\n[@anim,talk,1]\n[@setf,23,15,=,4]\n{0}[@p,lastC]");

            // Freya's item
            talkDataBase.cellData[7][7][1][0] = ChangeTalkString(LocationID.FreyasItem,
                "[@anim,talk,1]\n{0}[@setf,5,67,=,1]\n[@p,lastC]");

            // Freya starting mojiscript check
            talkDataBase.cellData[7][3][1][0] = ChangeTalkFlagCheck(LocationID.FreyasItem, COMPARISON.Less,
                "[@anifla,mfanim,wait2]\n[@iff,{0},{1},&lt;,{2},freyja,1st-1]\n[@iff,3,95,&gt;,0,freyja,escape]\n" +
                "[@anifla,mfanim,wait]\n[@iff,3,35,&gt;,7,freyja,8th]\n[@iff,3,35,=,6,freyja,7th3]\n[@iff,3,35,&gt;,3,freyja,7th2]\n[@iff,3,35,=,3,freyja,ragna]\n[@iff,3,35,=,2,freyja,4th]\n" +
                "[@iff,3,35,=,1,freyja,3rd]\n[@iff,5,67,=,1,freyja,2nd]\n[@exit]\n[@anim,talk,1]\n[@p,2nd]");

            // Mulbruk's item
            talkDataBase.cellData[10][48][1][0] = ChangeTalkString(LocationID.MulbrukItem,
                "{0}[@setf,5,101,=,2]\n[@anim,talk,1]\n[@p,3rd-2]");

            // Mulbruk check
            talkDataBase.cellData[10][3][1][0] = ChangeTalkFlagCheck(LocationID.MulbrukItem, COMPARISON.Less,
                "[@iff,{0},{1},&lt;,{2},mulbruk2,3rd]\n[@iff,5,61,=,1,mulbruk2,mirror]\n" +
                "[@iff,5,86,=,1,mulbruk2,hint2]\n[@iff,5,87,=,1,mulbruk2,hint3]\n[@iff,5,88,=,1,mulbruk2,hint4]\n[@iff,5,89,=,1,mulbruk2,hint5]\n[@iff,5,90,=,1,mulbruk2,hint6]\n" +
                "[@iff,5,91,=,1,mulbruk2,hint7]\n[@iff,5,92,=,1,mulbruk2,hint8]\n[@iff,5,93,=,1,mulbruk2,hint9]\n[@iff,5,94,=,1,mulbruk2,hint10]\n[@iff,5,95,=,1,mulbruk2,hint11]\n" +
                "[@iff,3,33,&gt;,10,mulbruk2,5th]\n[@iff,5,0,=,2,mulbruk2,4th]\n[@anifla,mfanim,wait]\n[@iff,5,78,=,5,mulbruk2,hint1]\n[@anifla,mfanim,wait4]\n" +
                "[@iff,3,33,=,10,mulbruk2,3rdRnd]\n[@anifla,mfanim,wait]\n[@iff,5,78,=,5,mulbruk2,hint1]\n[@anifla,mfanim,wait3]\n[@iff,3,33,=,6,mulbruk2,rTalk2]\n" +
                "[@anifla,mfanim,wait2]\n[@iff,3,33,=,5,mulbruk2,rTalk1]\n[@anifla,mfanim,nochar]\n[@iff,3,33,=,4,mulbruk2,1st-8]\n[@iff,3,33,=,8,mulbruk2,1st-8]\n" +
                "[@anifla,mfanim,wait]\n[@iff,3,33,&lt;,7,mulbruk2,1st]\n[@iff,3,33,=,7,mulbruk2,2nd]\n");

            // Osiris' item
            talkDataBase.cellData[78][7][1][0] = ChangeTalkStringAndFlagCheck(LocationID.OsirisItem,
                "[@iff,{0},{1},&gt;,{2},f15-3,2nd]\n{3}[@anim,talk,1]\n[@p,lastC]");
        }

        // ================================================================
        // MojiScriptFixes — miscellaneous dialogue script corrections
        // ================================================================

        private void MojiScriptFixes()
        {
            // Nebur stays until you take her item or leave the surface
            shopDataBase.cellData[2][4][1][0] = "[@anim,smile,1]\n[@setf,5,27,+,1]";
            talkDataBase.cellData[0][10][1][0] = "[@anim,nejiru,1]\n[@out]";

            // Fobos's 2nd item check if you have a skull
            talkDataBase.cellData[5][23][1][0] = "[@iff,0,32,&gt;,0,fobos,gS3]\n[@anim,stalk,1]\n[@anifla,mnext,swait]";

            // Fobos dialogue
            talkDataBase.cellData[5][3][3][1] = "Hmmm.";
            talkDataBase.cellData[5][16][3][1] = "Here take this.";

            // Fairy King opens endless even with pendant
            talkDataBase.cellData[8][10][1][0] = "[@exit]\n[@anim,talk,1]\n[@setf,3,34,=,2]\n[@setf,5,12,=,1]\n[@p,2nd-2]";

            // Fairy King check on Freya's Pendant
            talkDataBase.cellData[8][3][1][0] = "[@iff,3,34,&gt;,3,freyr,5th]\n[@iff,3,34,=,3,freyr,4th]\n[@iff,3,34,=,2,freyr,3rd]\n[@iff,2,31,&gt;,0,freyr,2nd]\n" +
                "[@iff,3,34,=,1,freyr,1stEnd]\n[@iff,3,34,=,0,freyr,1st]";

            // Freya opens left door in Mausoleum of Giants
            talkDataBase.cellData[7][14][1][0] = "[@anim,talk,1]\n[@setf,5,12,=,1]\n[@setf,3,29,=,1]\n[@setf,3,39,=,1]\n[@setf,9,38,=,1]";

            // Mulbruk 4-guardian check for item
            talkDataBase.cellData[10][47][1][0] = "[@exit]\n[@anim,talk,1]\n[@setf,3,33,=,10]\n[@iff,3,0,&gt;,3,mulbruk2,3rd-1]\n[@p,lastC]";

            // Remove Giltoriyo check on his item
            talkDataBase.cellData[3][3][1][0] = "[@iff,5,62,=,7,giltoriyo,9th]\n[@iff,5,62,=,6,giltoriyo,7th]\n[@iff,5,62,=,5,giltoriyo,6th]\n" +
                "[@iff,5,62,=,4,giltoriyo,5th]\n[@iff,5,62,=,3,giltoriyo,4th]\n[@iff,5,62,=,2,giltoriyo,2nd]\n[@exit]\n[@anim,talk,1]\n[@p,1st]";

            // Fix Giltoriyo early dialogue exit
            talkDataBase.cellData[3][6][1][0] = "[@setf,5,62,=,2]\n[@setf,1,7,=,0]\n[@anim,talk,1]\n[@p,1st-4]";

            // Charon will always accept all your money
            talkDataBase.cellData[73][6][1][0] = "[@setf,0,1,=,0]\n[@setf,1,9,=,1]\n[@anim,talk,1]\n[@nobu]";
        }

        // ================================================================
        // Dialogue helper methods
        // ================================================================

        private string ChangeTalkString(LocationID locationID, string original)
        {
            if (locationToItemMap.TryGetValue(locationID, out ItemID newItemID))
            {
                ItemInfo info = ItemDB.GetItemInfo(newItemID);

                string itemString;
                if (info.BoxName.Equals("Crystal S") || info.BoxName.Equals("Sacred Orb") || info.BoxName.Equals("MSX3p"))
                    itemString = $"[@take,{info.BoxName},02item,1]\n";
                else if (info.BoxName.Equals("Money"))
                    itemString = "[@setfd,0,1,+,30]\n";
                else
                    itemString = $"[@take,{info.ShopName},02item,1]\n";

                foreach (L2FlagBoxEnd flag in CreateGetFlags(newItemID, info))
                {
                    if (flag.calcu == CALCU.ADD)
                        itemString += $"[@setf,{flag.seet_no1},{flag.flag_no1},+,{flag.data}]\n";
                    else if (flag.calcu == CALCU.EQR)
                        itemString += $"[@setf,{flag.seet_no1},{flag.flag_no1},=,{flag.data}]\n";
                }

                return string.Format(original, itemString);
            }
            return string.Empty;
        }

        private string ChangeTalkFlagCheck(LocationID locationID, COMPARISON comp, string original)
        {
            if (locationToItemMap.TryGetValue(locationID, out ItemID newItemID))
            {
                ItemInfo info = ItemDB.GetItemInfo(newItemID);

                int flagValue = comp == COMPARISON.Less ? 1 : 0;
                if (newItemID == ItemID.MobileSuperx3P)
                    flagValue++;

                return string.Format(original, info.ItemSheet, info.ItemFlag, flagValue);
            }
            return string.Empty;
        }

        private string ChangeTalkStringAndFlagCheck(LocationID locationID, string original)
        {
            if (locationToItemMap.TryGetValue(locationID, out ItemID newItemID))
            {
                ItemInfo info = ItemDB.GetItemInfo(newItemID);

                int flagValue = newItemID == ItemID.MobileSuperx3P ? 1 : 0;

                string itemString;
                if (info.BoxName.Equals("Crystal S") || info.BoxName.Equals("Sacred Orb") || info.BoxName.Equals("MSX3p"))
                    itemString = $"[@take,{info.BoxName},02item,1]\n";
                else if (info.BoxName.Equals("Money"))
                    itemString = "[@setfd,0,1,+,30]\n";
                else
                    itemString = $"[@take,{info.ShopName},02item,1]\n";

                foreach (L2FlagBoxEnd flag in CreateGetFlags(newItemID, info))
                {
                    if (flag.calcu == CALCU.ADD)
                        itemString += $"[@setf,{flag.seet_no1},{flag.flag_no1},+,{flag.data}]\n";
                    else if (flag.calcu == CALCU.EQR)
                        itemString += $"[@setf,{flag.seet_no1},{flag.flag_no1},=,{flag.data}]\n";
                }

                return string.Format(original, info.ItemSheet, info.ItemFlag, flagValue, itemString);
            }
            return string.Empty;
        }
    }
}
