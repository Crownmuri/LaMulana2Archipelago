using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Fixes a hard main-thread freeze rooted in a latent SpriteStudio engine bug
    /// (byte-identical in vanilla and randomizer decompiles — NOT mod-introduced).
    ///
    /// The Claydoll Suit's player animation rig has cyclic/self-referential sub-parts.
    /// Buying at a shop whose shopkeeper is also a glossary NPC (Peibalusa/Venum/Fairylan)
    /// draws that rig during the give, and SpriteStudio's draw path spins forever on the
    /// cycle: one core pegged, no game-accessor calls, no logging (which is why it evaded
    /// ~9 diagnostic passes). The deepest breadcrumbed frame in the stall is
    /// Script_SpriteStudio_DrawManagerView.LateUpdate, whose callees do the actual spin.
    ///
    /// There are TWO independent cycle sites in that draw path, each guarded below:
    ///
    ///  1. LateUpdate's `while (k &lt; num2)` sub-parts-expansion loop grows the mesh list via
    ///     TableListMesh.Insert on a cyclic ScriptPartsRootSub. Guarded in
    ///     <see cref="LateUpdateInsertGuard"/>.
    ///
    ///  2. The ListMeshDraw `ChainNext` linked lists are spliced by ListMerge
    ///     (`MeshDataLast.ChainNext = ListNext.MeshDataTop`, by reference). Shared/cyclic
    ///     Claydoll sub-parts make a chain point back into itself, so the two ChainNext
    ///     walks reachable from LateUpdate — ListMeshDraw.getActiveMeshCount() and
    ///     ArrayListMeshDraw.MeshSetCombine()'s inner loop — never terminate. This is the
    ///     confirmed culprit here: the Insert guard (site 1) was installed yet never fired,
    ///     and the stall shows no Insert and no accessors — a pure chain walk. Guarded in
    ///     <see cref="MeshChainWalkGuard"/>.
    ///
    /// A callee-side guard on the standalone one-liner DrawListClear() failed earlier because
    /// Mono JIT-inlines it into LateUpdate, so patching the standalone method never intercepts
    /// the inlined call. We therefore TRANSPILE the enclosing methods (whose IL is recompiled
    /// fresh, defeating inlining) and inject stack-neutral guard calls at the growth/advance
    /// points. Each guard counts events per frame and throws past a large cap; the exception
    /// unwinds out of the frozen loop, Unity logs it, and the frame proceeds (that frame's
    /// SpriteStudio draw is skipped) instead of hard-freezing. Legitimate frames stay far below
    /// the cap, so healthy rendering is untouched. See [[shop-glossary-exit-freeze]].
    /// </summary>
    internal static class SpriteStudioGuardShared
    {
        // Far above any legitimate per-frame count; only a runaway cycle reaches it.
        internal const int PerFrameCap = 500000;

        // Advances a per-frame counter (reset when the frame changes) and returns the new
        // count. Callers log + throw once it hits the cap.
        internal static int Tick(ref int frameField, ref int countField)
        {
            int frame = Time.frameCount;
            if (frame != frameField)
            {
                frameField = frame;
                countField = 0;
            }
            return ++countField;
        }
    }

    /// <summary>
    /// Site 2 (confirmed culprit): guards the two ListMeshDraw.ChainNext linked-list walks
    /// reachable from LateUpdate against a cyclic chain. Injects a guard call before every
    /// `ldfld ChainNext` in ListMeshDraw.getActiveMeshCount and ArrayListMeshDraw.MeshSetCombine
    /// (whichever the JIT inlines is covered because both enclosing methods are transpiled).
    /// </summary>
    [HarmonyPatch]
    internal static class MeshChainWalkGuard
    {
        private static int _frame = -1;
        private static int _count;

        /// <summary>Injected before each ChainNext field load. Bounds a non-terminating
        /// cyclic-chain walk by throwing once ChainNext advances/frame explode.</summary>
        public static void ChainTick()
        {
            int n = SpriteStudioGuardShared.Tick(ref _frame, ref _count);
            if (n == SpriteStudioGuardShared.PerFrameCap)
                Plugin.Log.LogError(
                    "[SpriteStudioGuard] Cyclic mesh ChainNext detected (" +
                    SpriteStudioGuardShared.PerFrameCap + " chain steps in one frame) — aborting " +
                    "this frame's SpriteStudio draw to prevent a freeze. (Likely the Claydoll Suit " +
                    "rig during a shop/glossary give.)");
            if (n >= SpriteStudioGuardShared.PerFrameCap)
                throw new InvalidOperationException("[SpriteStudioGuard] cyclic mesh-chain (ChainNext) guard");
        }

        static IEnumerable<MethodBase> TargetMethods()
        {
            var m1 = ResolveMethod("Library_SpriteStudio+DrawManager+ListMeshDraw",
                                   "Library_SpriteStudio.DrawManager.ListMeshDraw", "getActiveMeshCount");
            if (m1 != null) yield return m1;

            var m2 = ResolveMethod("Library_SpriteStudio+DrawManager+ArrayListMeshDraw",
                                   "Library_SpriteStudio.DrawManager.ArrayListMeshDraw", "MeshSetCombine");
            if (m2 != null) yield return m2;
        }

        private static MethodBase ResolveMethod(string nestedName, string dottedName, string methodName)
        {
            Type t = AccessTools.TypeByName(nestedName) ?? AccessTools.TypeByName(dottedName);
            if (t == null)
            {
                Plugin.Log.LogError("[SpriteStudioGuard] type not found: " + nestedName + " — ChainNext guard NOT installed on " + methodName);
                return null;
            }
            MethodInfo m = AccessTools.Method(t, methodName);
            if (m == null)
                Plugin.Log.LogError("[SpriteStudioGuard] method not found: " + nestedName + "." + methodName + " — ChainNext guard NOT installed");
            return m;
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var result = new List<CodeInstruction>();
            MethodInfo tick = AccessTools.Method(typeof(MeshChainWalkGuard), nameof(ChainTick));
            int injected = 0;

            foreach (CodeInstruction code in instructions)
            {
                // Guard-tick right before advancing/reading ChainNext. The call is void/no-arg,
                // so it is stack-neutral (the object ref stays on the stack for the ldfld).
                if (code.opcode == OpCodes.Ldfld && code.operand is FieldInfo fi && fi.Name == "ChainNext")
                {
                    result.Add(new CodeInstruction(OpCodes.Call, tick));
                    injected++;
                }
                result.Add(code);
            }

            if (injected == 0)
                Plugin.Log.LogError("[SpriteStudioGuard] transpiler found NO ChainNext anchor in " +
                    __originalMethod.DeclaringType.Name + "." + __originalMethod.Name + " — guard NOT installed!");
            else
                Plugin.Log.LogInfo("[SpriteStudioGuard] transpiler injected " + injected +
                    " ChainNext guard tick(s) into " + __originalMethod.DeclaringType.Name + "." + __originalMethod.Name);

            return result;
        }
    }

    /// <summary>
    /// Site 1 (secondary net): guards LateUpdate's sub-parts-expansion loop, which grows the
    /// draw list via the method's only TableListMesh.Insert on a cyclic ScriptPartsRootSub.
    /// Kept as defense-in-depth for cyclic rigs whose cycle manifests as runaway inserts.
    /// </summary>
    [HarmonyPatch(typeof(Script_SpriteStudio_DrawManagerView), "LateUpdate")]
    internal static class LateUpdateInsertGuard
    {
        private static int _frame = -1;
        private static int _count;

        /// <summary>Injected after each TableListMesh.Insert in LateUpdate. Bounds the
        /// non-terminating cyclic sub-parts-expansion loop by throwing once inserts/frame explode.</summary>
        public static void InsertTick()
        {
            int n = SpriteStudioGuardShared.Tick(ref _frame, ref _count);
            if (n == SpriteStudioGuardShared.PerFrameCap)
                Plugin.Log.LogError(
                    "[SpriteStudioGuard] Cyclic sub-parts-root INSERT detected (" +
                    SpriteStudioGuardShared.PerFrameCap + " mesh inserts in one frame) — aborting " +
                    "this frame's SpriteStudio draw to prevent a freeze.");
            if (n >= SpriteStudioGuardShared.PerFrameCap)
                throw new InvalidOperationException("[SpriteStudioGuard] cyclic sub-parts INSERT guard");
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var result = new List<CodeInstruction>();
            MethodInfo tick = AccessTools.Method(typeof(LateUpdateInsertGuard), nameof(InsertTick));
            int injected = 0;

            foreach (CodeInstruction code in instructions)
            {
                result.Add(code);
                // The only List<>.Insert in LateUpdate is TableListMesh.Insert — the growth
                // point of the sub-parts expansion loop. Guard-tick right after it.
                if ((code.opcode == OpCodes.Callvirt || code.opcode == OpCodes.Call)
                    && code.operand is MethodInfo mi && mi.Name == "Insert")
                {
                    result.Add(new CodeInstruction(OpCodes.Call, tick));
                    injected++;
                }
            }

            if (injected == 0)
                Plugin.Log.LogError("[SpriteStudioGuard] transpiler found NO Insert anchor in LateUpdate — INSERT guard NOT installed!");
            else
                Plugin.Log.LogInfo("[SpriteStudioGuard] transpiler injected " + injected + " Insert guard tick(s) into LateUpdate");

            return result;
        }
    }
}
