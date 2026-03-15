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

            // Find the IL pattern:
            //   ldloc.s   newItemID
            //   ldc.i4    <value of ItemID.ChestWeight01>
            //   bge.s     <else label>   (or blt.s <normal label>)
            // and replace it with:
            //   ldloc.s   newItemID
            //   call      bool ShouldTreatAsNormalItem
            //   brfalse.s <else label>   (or brtrue.s <normal label>)

            // Get the integer value of ItemID.ChestWeight01 (it's a constant)
            FieldInfo chestField = typeof(ItemID).GetField("ChestWeight01");
            if (chestField == null)
            {
                // Fallback – return original if something is wrong
                return codes;
            }
            int chestValue = (int)chestField.GetRawConstantValue();

            for (int i = 0; i < codes.Count - 2; i++)
            {
                // Look for ldc.i4 with the chest value
                if (codes[i].opcode == OpCodes.Ldc_I4 && (int)codes[i].operand == chestValue)
                {
                    // The previous instruction should be loading newItemID
                    if (i > 0 && (codes[i - 1].opcode == OpCodes.Ldloc_S || codes[i - 1].opcode == OpCodes.Ldloc))
                    {
                        // The next instruction should be a branch (bge, bge.s, blt, blt.s)
                        var branchOp = codes[i + 1].opcode;
                        if (branchOp == OpCodes.Bge || branchOp == OpCodes.Bge_S ||
                            branchOp == OpCodes.Blt || branchOp == OpCodes.Blt_S)
                        {
                            var targetLabel = codes[i + 1].operand;

                            // Determine whether the original branch jumped to the "normal" block
                            bool branchOnTrue = (branchOp == OpCodes.Blt || branchOp == OpCodes.Blt_S);

                            // Create new instructions
                            var newInstructions = new List<CodeInstruction>
                            {
                                codes[i - 1], // ldloc newItemID
                                new CodeInstruction(OpCodes.Call,
                                    AccessTools.Method(typeof(FakeItemsRevertForAP), nameof(ShouldTreatAsNormalItem)))
                            };

                            // Add the appropriate branch
                            newInstructions.Add(branchOnTrue
                                ? new CodeInstruction(OpCodes.Brtrue, targetLabel)
                                : new CodeInstruction(OpCodes.Brfalse, targetLabel));

                            // Remove the original three instructions and insert the new ones
                            codes.RemoveRange(i - 1, 3);
                            codes.InsertRange(i - 1, newInstructions);

                            break; // Only one such pattern exists
                        }
                    }
                }
            }

            return codes;
        }
    }
}