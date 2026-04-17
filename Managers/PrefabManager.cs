using L2Base;
using L2Word;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LaMulana2Archipelago.Managers
{
    public class PrefabHarvester : MonoBehaviour
    {
        public static Dictionary<string, GameObject> CachedPrefabs = new Dictionary<string, GameObject>();
        public static bool HasHarvested = false;

        private static bool IsHarvesting = false;

        private L2System _sys;

        public static void StartHarvest(L2System sys)
        {
            if (HasHarvested || IsHarvesting || sys == null)
                return;

            CachedPrefabs.Clear();

            GameObject go = new GameObject("AP_PrefabHarvester");
            DontDestroyOnLoad(go);

            PrefabHarvester harvester = go.AddComponent<PrefabHarvester>();
            harvester._sys = sys;
            harvester.StartCoroutine(harvester.HarvestCoroutine());
        }

        private IEnumerator HarvestCoroutine()
        {
            IsHarvesting = true;

            try
            {
                if (_sys != null)
                    _sys.setKeyBlock(true);

                // Let the current scene settle very briefly before touching anything.
                yield return null;
                yield return new WaitForEndOfFrame();

                // Match the original ordering as closely as possible.
                // field04: turquoise chest + curse + three/five soul gates + ladder
                yield return StartCoroutine(LoadHarvestScene("field04", HarvestField04));
                SafeReInitSystem();

                // field05: blue chest + one soul gate/soul
                yield return StartCoroutine(LoadHarvestScene("field05", HarvestField05));
                SafeReInitSystem();

                // field13: nine soul gate/soul
                yield return StartCoroutine(LoadHarvestScene("field13", HarvestField13));
                SafeReInitSystem();

                // fieldP00: red chest
                yield return StartCoroutine(LoadHarvestScene("fieldP00", HarvestFieldP00));
                SafeReInitSystem();

                // field07: pink chest + two soul gate/soul
                yield return StartCoroutine(LoadHarvestScene("field07", HarvestField07));
                SafeReInitSystem();

                // field12: yellow chest
                yield return StartCoroutine(LoadHarvestScene("field12", HarvestField12));
                SafeReInitSystem();

                // Return through the game's own title transition.
                ReturnToTitle();

                while (SceneManager.GetActiveScene().name != "title" &&
                       SceneManager.GetActiveScene().name != "Title")
                {
                    yield return null;
                }

                // Give title one frame to finish its visuals.
                yield return null;

                HasHarvested = true;
                Plugin.Log.LogInfo($"[AP] Prefab harvest complete. Cached: {string.Join(", ", new List<string>(CachedPrefabs.Keys).ToArray())}");
            }
            finally
            {
                if (_sys != null)
                    _sys.setKeyBlock(false);

                IsHarvesting = false;
                Destroy(gameObject);
            }
        }
        private void ReturnToTitle()
        {
            if (_sys == null)
                return;

            try
            {
                L2SystemCore core = _sys.getL2SystemCore();
                if (core == null)
                {
                    Plugin.Log.LogWarning("[AP] ReturnToTitle failed: L2SystemCore was null.");
                    return;
                }

                MethodInfo loadDemo = typeof(L2SystemCore).GetMethod(
                    "loadDemoSceane",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                );

                if (loadDemo != null)
                {
                    loadDemo.Invoke(core, new object[] { "Title" });
                    return;
                }

                Plugin.Log.LogWarning("[AP] ReturnToTitle failed: loadDemoSceane was not found.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[AP] ReturnToTitle failed: " + ex);
            }
        }
        private IEnumerator LoadHarvestScene(string sceneName, Action harvestAction)
        {
            AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            if (asyncLoad == null)
                yield break;

            while (!asyncLoad.isDone)
                yield return null;

            if (harvestAction != null)
                harvestAction();

            // Another tiny settle frame before the reset call.
            yield return null;
        }

        private void SafeReInitSystem()
        {
            if (_sys == null)
                return;

            try
            {
                // Prefer the original no-arg form if it exists in this build.
                MethodInfo noArg = typeof(L2System).GetMethod("reInitSystem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                if (noArg != null)
                {
                    noArg.Invoke(_sys, null);
                    return;
                }

                // Fall back to the bool overload if that is what this build has.
                MethodInfo boolArg = typeof(L2System).GetMethod("reInitSystem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { typeof(bool) }, null);
                if (boolArg != null)
                {
                    boolArg.Invoke(_sys, new object[] { false });
                    return;
                }

                Plugin.Log.LogWarning("[AP] Could not find a usable reInitSystem overload.");
            }
            catch (Exception ex)
            {
                // Keep harvesting even if one reset path misbehaves.
                Plugin.Log.LogWarning("[AP] reInitSystem failed during prefab harvest: " + ex);
            }
        }

        private void HarvestField04()
        {
            GetTreasureChest("turquiseChest", "Turquise Chest Prefab", true);
            CacheCursePrefab();
            CacheSpecialSoulGatesField04();
            CacheFirstNamedController("ladder", "Ladder Prefab", "E0Ladder");
        }

        private void HarvestField05()
        {
            GetTreasureChest("blueChest", "Blue Chest Prefab", false);
            CacheFirstNamedController("oneSoulGate", "One Soul Gate Prefab", "soul_gate");
            CacheFirstNamedController("oneSoul", "One Soul Prefab", "soul_cont");
        }

        private void HarvestField13()
        {
            CacheFirstNamedController("nineSoulGate", "Nine Soul Gate Prefab", "soul_gate");
            CacheFirstNamedController("nineSoul", "Nine Soul Prefab", "soul_cont");
        }

        private void HarvestFieldP00()
        {
            GetTreasureChest("redChest", "Red Chest Prefab", true);
        }

        private void HarvestField07()
        {
            GetTreasureChest("pinkChest", "Pink Chest Prefab", true);
            CacheFirstNamedController("twoSoulGate", "Two Soul Gate Prefab", "soul_gate");
            CacheFirstNamedController("twoSoul", "Two Soul Prefab", "soul_cont");
        }

        private void HarvestField12()
        {
            GetTreasureChest("yellowChest", "Yellow Chest Prefab", true);
        }

        private void CacheCursePrefab()
        {
            Animator[] animators = FindObjectsOfType<Animator>();
            if (animators == null)
                return;

            foreach (Animator animator in animators)
            {
                if (animator == null)
                    continue;

                if (animator.name == "Curse Tresure")
                {
                    CacheClone("curse", "Curse Prefab", animator.gameObject, false);
                    break;
                }
            }
        }

        private void CacheSpecialSoulGatesField04()
        {
            AnimatorController[] controllers = FindObjectsOfType<AnimatorController>();
            if (controllers == null)
                return;

            foreach (AnimatorController controller in controllers)
            {
                if (controller == null)
                    continue;

                if (controller.name == "soul_gate")
                {
                    if (HasFlagValue(controller, 5))
                        CacheClone("threeSoulGate", "Three Soul Gate Prefab", controller.gameObject, false);
                    else if (HasFlagValue(controller, 8))
                        CacheClone("fiveSoulGate", "Five Soul Gate Prefab", controller.gameObject, false);
                }
                else if (controller.name == "soul_cont")
                {
                    if (HasFlagValue(controller, 23))
                        CacheClone("threeSoul", "Three Soul Prefab", controller.gameObject, false);
                    else if (HasFlagValue(controller, 26))
                        CacheClone("fiveSoul", "Five Soul Prefab", controller.gameObject, false);
                }
            }
        }

        private bool HasFlagValue(AnimatorController controller, int flagNo1)
        {
            if (controller == null || controller.CheckFlags == null || controller.CheckFlags.Length == 0)
                return false;

            L2Flag.L2FlagBoxParent parent = controller.CheckFlags[0];
            if (parent == null || parent.BOX == null || parent.BOX.Length == 0)
                return false;

            L2Flag.L2FlagBox box = parent.BOX[0];
            return box != null && box.flag_no1 == flagNo1;
        }

        private void CacheFirstNamedController(string key, string prefabName, string controllerName)
        {
            AnimatorController[] controllers = FindObjectsOfType<AnimatorController>();
            if (controllers == null)
                return;

            foreach (AnimatorController controller in controllers)
            {
                if (controller == null)
                    continue;

                if (controller.name == controllerName)
                {
                    CacheClone(key, prefabName, controller.gameObject, false);
                    break;
                }
            }
        }

        private void GetTreasureChest(string key, string name, bool closet)
        {
            TreasureBoxScript[] boxes = FindObjectsOfType<TreasureBoxScript>();
            if (boxes == null)
                return;

            foreach (TreasureBoxScript box in boxes)
            {
                if (box == null)
                    continue;

                if (closet && !box.closetMode)
                    continue;

                CacheClone(key, name, box.gameObject, false);
                break;
            }
        }

        private void CacheClone(string key, string objectName, GameObject source, bool activate)
        {
            if (source == null)
                return;

            GameObject obj = Instantiate(source);
            obj.name = objectName;
            DontDestroyOnLoad(obj);
            obj.SetActive(activate);

            if (CachedPrefabs.ContainsKey(key))
                CachedPrefabs[key] = obj;
            else
                CachedPrefabs.Add(key, obj);
        }
    }
}