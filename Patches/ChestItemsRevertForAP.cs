using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using LaMulana2RandomizerShared;
using LM2RandomiserMod;

namespace LaMulana2Archipelago.Patches
{
    internal static class ChestItemApHelper
    {
        // Returns true for items that should use normal chest colour and show a sprite.
        public static bool IsNormalItemForChest(ItemID id)
        {
            return (int)id < (int)ItemID.ChestWeight01 || ApItemIDs.IsApPlaceholder((int)id);
        }

        // Returns true for actual weight items (not AP placeholders).
        public static bool IsWeightItem(ItemID id)
        {
            return (int)id >= (int)ItemID.ChestWeight01 && !ApItemIDs.IsApPlaceholder((int)id);
        }
    }

    // ------------------------------------------------------------------------
    // Patch L2Rando.ChangeTreasureChests to use normal chest colour for AP items
    // ------------------------------------------------------------------------
    [HarmonyPatch(typeof(L2Rando), "ChangeTreasureChests")]
    internal static class ChangeTreasureChestsForAP
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            FieldInfo chestField = typeof(ItemID).GetField("ChestWeight01");
            if (chestField == null) return codes;
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
                            // Original: if (newItemID >= ChestWeight01) use weight chest
                            // New: if (!IsNormalItemForChest(newItemID)) use weight chest
                            var newInstructions = new List<CodeInstruction>
                            {
                                codes[i - 1], // ldloc newItemID
                                new CodeInstruction(OpCodes.Call,
                                    AccessTools.Method(typeof(ChestItemApHelper), nameof(ChestItemApHelper.IsNormalItemForChest))),
                                new CodeInstruction(OpCodes.Brfalse, targetLabel)
                            };
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

    // ------------------------------------------------------------------------
    // Patch L2Rando.ChangeChestItemFlags to:
    //   - Set sprite for AP items (first if)
    //   - Skip itemValue assignment for AP items (second if)
    // ------------------------------------------------------------------------
    [HarmonyPatch(typeof(L2Rando), "ChangeChestItemFlags")]
    internal static class ChangeChestItemFlagsForAP
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            FieldInfo chestField = typeof(ItemID).GetField("ChestWeight01");
            if (chestField == null) return codes;
            int chestValue = (int)chestField.GetRawConstantValue();

            // Find all occurrences of ldc.i4 chestValue
            List<int> targetIndices = new List<int>();
            for (int i = 0; i < codes.Count; i++)
                if (codes[i].opcode == OpCodes.Ldc_I4 && (int)codes[i].operand == chestValue)
                    targetIndices.Add(i);

            // Process in reverse to keep indices valid
            for (int idx = targetIndices.Count - 1; idx >= 0; idx--)
            {
                int i = targetIndices[idx];
                if (i > 0 && i < codes.Count - 1)
                {
                    CodeInstruction prev = codes[i - 1];
                    CodeInstruction next = codes[i + 1];

                    if (prev.opcode == OpCodes.Ldarg_1 &&
                        (next.opcode == OpCodes.Blt || next.opcode == OpCodes.Blt_S ||
                         next.opcode == OpCodes.Bge || next.opcode == OpCodes.Bge_S))
                    {
                        var targetLabel = next.operand;
                        bool isBlt = next.opcode == OpCodes.Blt || next.opcode == OpCodes.Blt_S;

                        List<CodeInstruction> newInstr;
                        if (isBlt)
                        {
                            // First condition: if (itemID < ChestWeight01) → set sprite
                            // Replace with if (IsNormalItemForChest(itemID))
                            newInstr = new List<CodeInstruction>
                            {
                                prev,
                                new CodeInstruction(OpCodes.Call,
                                    AccessTools.Method(typeof(ChestItemApHelper), nameof(ChestItemApHelper.IsNormalItemForChest))),
                                new CodeInstruction(OpCodes.Brtrue, targetLabel)
                            };
                        }
                        else
                        {
                            // Second condition: if (itemID >= ChestWeight01) → set itemValue (only for actual weights)
                            // Replace with if (IsWeightItem(itemID))
                            newInstr = new List<CodeInstruction>
                            {
                                prev,
                                new CodeInstruction(OpCodes.Call,
                                    AccessTools.Method(typeof(ChestItemApHelper), nameof(ChestItemApHelper.IsWeightItem))),
                                new CodeInstruction(OpCodes.Brtrue, targetLabel)
                            };
                        }

                        codes.RemoveRange(i - 1, 3);
                        codes.InsertRange(i - 1, newInstr);
                    }
                }
            }
            return codes;
        }
    }
}