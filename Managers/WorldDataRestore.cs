using System.Collections.Generic;
using HarmonyLib;
using L2Base;
using L2Flag;
using L2Word;
using UnityEngine;

namespace LaMulana2Archipelago.Managers
{
    /// <summary>
    /// Undo layer for the two kinds of *process-lifetime* world data the mod
    /// rewrites in place. Both survive a scene unload and a return to the title,
    /// so without an explicit restore a disconnect leaves the previous seed's
    /// placements live:
    ///
    ///   - Moji script databases (L2ShopDataBase / L2TalkDataBase). These are
    ///     plain objects built once as L2System field initialisers, so every
    ///     cellData write by SceneRandomizer.TryInitShopDialogue and
    ///     ShopDialogPatch.Apply is permanent: shops keep selling the old
    ///     seed's items and NPCs keep handing out the old seed's items.
    ///
    ///   - Chest item prefabs. TreasureBoxScript.itemObj is the *prefab* the
    ///     chest instantiates, not a scene child, so ChangeChestItemFlags'
    ///     writes to itemLabel / itemGetFlags / itemActiveFlag / sprite outlive
    ///     the scene. After a disconnect those chests still drop the old seed's
    ///     item - an "AP Item" with no name once the scout cache has been dropped.
    ///
    /// The pristine baseline is captured lazily, on the first write of a
    /// session, and kept armed afterwards so repeated connect/disconnect cycles
    /// all restore to vanilla rather than to the previous seed.
    /// </summary>
    internal static class WorldDataRestore
    {
        // ── Moji script databases ────────────────────────────────────────────

        private static string[][][][] _shopBaseline;
        private static string[][][][] _talkBaseline;

        /// <summary>
        /// Snapshot shop cellData before the first write of the process. Safe to
        /// call repeatedly; only the first call (which sees pristine data) sticks.
        /// </summary>
        public static void EnsureShopBaseline(L2ShopDataBase db)
        {
            if (_shopBaseline != null || db == null) return;
            _shopBaseline = Clone(db.cellData);
            Plugin.Log.LogInfo("[Restore] Captured vanilla shop script baseline.");
        }

        /// <summary>Snapshot talk cellData before the first write of the process.</summary>
        public static void EnsureTalkBaseline(L2TalkDataBase db)
        {
            if (_talkBaseline != null || db == null) return;
            _talkBaseline = Clone(db.cellData);
            Plugin.Log.LogInfo("[Restore] Captured vanilla talk script baseline.");
        }

        private static void RestoreMojiScripts(L2ShopDataBase shopDb, L2TalkDataBase talkDb)
        {
            if (shopDb != null && _shopBaseline != null)
                CopyInto(_shopBaseline, shopDb.cellData);
            if (talkDb != null && _talkBaseline != null)
                CopyInto(_talkBaseline, talkDb.cellData);
        }

        // Clone the array skeleton; the strings themselves are immutable and shared.
        private static string[][][][] Clone(string[][][][] src)
        {
            if (src == null) return null;

            var dst = new string[src.Length][][][];
            for (int a = 0; a < src.Length; a++)
            {
                if (src[a] == null) continue;
                dst[a] = new string[src[a].Length][][];
                for (int b = 0; b < src[a].Length; b++)
                {
                    if (src[a][b] == null) continue;
                    dst[a][b] = new string[src[a][b].Length][];
                    for (int c = 0; c < src[a][b].Length; c++)
                    {
                        if (src[a][b][c] == null) continue;
                        dst[a][b][c] = (string[])src[a][b][c].Clone();
                    }
                }
            }
            return dst;
        }

        // Write the baseline back element-wise. The live arrays are only ever
        // written cell-by-cell (never re-shaped), so the two always line up; the
        // bounds checks are belt and braces against a future re-shape.
        private static void CopyInto(string[][][][] src, string[][][][] dst)
        {
            if (src == null || dst == null) return;

            int aMax = Mathf.Min(src.Length, dst.Length);
            for (int a = 0; a < aMax; a++)
            {
                if (src[a] == null || dst[a] == null) continue;
                int bMax = Mathf.Min(src[a].Length, dst[a].Length);
                for (int b = 0; b < bMax; b++)
                {
                    if (src[a][b] == null || dst[a][b] == null) continue;
                    int cMax = Mathf.Min(src[a][b].Length, dst[a][b].Length);
                    for (int c = 0; c < cMax; c++)
                    {
                        if (src[a][b][c] == null || dst[a][b][c] == null) continue;
                        int dMax = Mathf.Min(src[a][b][c].Length, dst[a][b][c].Length);
                        for (int d = 0; d < dMax; d++)
                            dst[a][b][c][d] = src[a][b][c][d];
                    }
                }
            }
        }

        // ── Chest item prefabs ───────────────────────────────────────────────

        private class PrefabSnapshot
        {
            public EventItemScript Item;
            public string Label;
            public int Value;
            public L2FlagBoxEnd[] GetFlags;
            public L2FlagBoxParent[] ActiveFlags;
            public SpriteRenderer Renderer;
            public Sprite Sprite;
        }

        private static readonly Dictionary<int, PrefabSnapshot> _prefabSnapshots =
            new Dictionary<int, PrefabSnapshot>();

        /// <summary>
        /// Record a chest item prefab's vanilla state before it is rewritten.
        /// Keyed by instance id, so the first capture (the pristine one) wins for
        /// the whole process even across connect/disconnect cycles.
        /// </summary>
        public static void CaptureItemPrefab(EventItemScript item)
        {
            if (item == null) return;

            int key = item.GetInstanceID();
            if (_prefabSnapshots.ContainsKey(key)) return;

            var renderer = item.GetComponent<SpriteRenderer>();
            _prefabSnapshots[key] = new PrefabSnapshot
            {
                Item = item,
                Label = item.itemLabel,
                Value = item.itemValue,
                GetFlags = item.itemGetFlags,
                ActiveFlags = item.itemActiveFlag,
                Renderer = renderer,
                Sprite = renderer != null ? renderer.sprite : null,
            };
        }

        private static void RestoreItemPrefabs()
        {
            int restored = 0;
            foreach (var snap in _prefabSnapshots.Values)
            {
                // Destroyed (a scene instance that happened to be captured, or an
                // asset Unity unloaded) - nothing left to put back.
                if (snap.Item == null) continue;

                snap.Item.itemLabel = snap.Label;
                snap.Item.itemValue = snap.Value;
                snap.Item.itemGetFlags = snap.GetFlags;
                snap.Item.itemActiveFlag = snap.ActiveFlags;
                if (snap.Renderer != null)
                    snap.Renderer.sprite = snap.Sprite;
                restored++;
            }

            _prefabSnapshots.Clear();
            Plugin.Log.LogInfo($"[Restore] Reverted {restored} chest item prefabs to vanilla.");
        }

        // ── Entry point ──────────────────────────────────────────────────────

        /// <summary>
        /// Put every process-lifetime rewrite back. Called from the shared
        /// standalone teardown (manual disconnect / offline deactivate), which
        /// only runs from the title screen - no talk session can be holding a
        /// mid-script cellData reference at that point.
        /// </summary>
        public static void RestoreAll()
        {
            // Prefer the references the SceneRandomizer already resolved; fall
            // back to L2System for the offline path (or a teardown that runs
            // after the component is gone).
            SceneRandomizer rando = SceneRandomizer.Instance;
            bool randoAlive = rando != null; // Unity null check: also false once destroyed

            L2ShopDataBase shopDb = randoAlive ? rando.ShopDataBase : null;
            L2TalkDataBase talkDb = randoAlive ? rando.TalkDataBase : null;

            if (shopDb == null || talkDb == null)
            {
                var sys = Object.FindObjectOfType<L2System>();
                if (sys != null)
                {
                    shopDb ??= Traverse.Create(sys).Field("l2sdb").GetValue<L2ShopDataBase>();
                    talkDb ??= Traverse.Create(sys).Field("l2tdb").GetValue<L2TalkDataBase>();
                }
                else
                {
                    Plugin.Log.LogWarning("[Restore] No L2System - moji scripts left as-is.");
                }
            }

            RestoreAll(shopDb, talkDb);
        }

        /// <inheritdoc cref="RestoreAll()"/>
        public static void RestoreAll(L2ShopDataBase shopDb, L2TalkDataBase talkDb)
        {
            try
            {
                RestoreMojiScripts(shopDb, talkDb);
                RestoreItemPrefabs();
                Plugin.Log.LogInfo("[Restore] World data reverted to vanilla.");
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("[Restore] World data revert failed: " + ex);
            }
        }
    }
}
