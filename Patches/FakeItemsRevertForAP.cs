#if LEGACY
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using LaMulana2RandomizerShared;
using LM2RandomiserMod;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Makes L2Rando.ChangeEventItems() treat AP placeholder items as normal items
    /// (instead of creating FakeItem placeholders).
    /// Only applies when the original randomizer's patched DLL is present.
    /// </summary>
    [HarmonyPatch(typeof(L2Rando), "ChangeEventItems")]
    internal static class FakeItemsRevertForAP
    {
        // Helper that returns true for any item that should be a normal pickup.
        private static bool ShouldTreatAsNormalItem(ItemID id)
        {
            // AP placeholders count as normal items
            if (ApItemIDs.IsApPlaceholder((int)id))
                return true;

            // Original condition: id < ItemID.ChestWeight01
            return (int)id < (int)ItemID.ChestWeight01;
        }

        // Transpiler that replaces the original condition with a call to the helper.
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);

            FieldInfo chestField = typeof(ItemID).GetField("ChestWeight01");
            if (chestField == null)
                return codes;

            int chestValue = (int)chestField.GetRawConstantValue();

            for (int i = 0; i < codes.Count - 2; i++)
            {
                if (codes[i].opcode == OpCodes.Ldc_I4 && (int)codes[i].operand == chestValue)
                {
                    if (i > 0 && (codes[i - 1].opcode == OpCodes.Ldloc_S || codes[i - 1].opcode == OpCodes.Ldloc))
                    {
                        var branchOp = codes[i + 1].opcode;
                        if (branchOp == OpCodes.Bge || branchOp == OpCodes.Bge_S ||
                            branchOp == OpCodes.Blt || branchOp == OpCodes.Blt_S)
                        {
                            var targetLabel = codes[i + 1].operand;
                            bool branchOnTrue = (branchOp == OpCodes.Blt || branchOp == OpCodes.Blt_S);

                            var newInstructions = new List<CodeInstruction>
                            {
                                codes[i - 1],
                                new CodeInstruction(OpCodes.Call,
                                    AccessTools.Method(typeof(FakeItemsRevertForAP), nameof(ShouldTreatAsNormalItem)))
                            };

                            newInstructions.Add(branchOnTrue
                                ? new CodeInstruction(OpCodes.Brtrue, targetLabel)
                                : new CodeInstruction(OpCodes.Brfalse, targetLabel));

                            codes.RemoveRange(i - 1, 3);
                            codes.InsertRange(i - 1, newInstructions);
                            break;
                        }
                    }
                }
            }

            return codes;
        }
    }
}
#endif
