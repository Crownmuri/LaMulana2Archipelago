using System.Collections.Generic;
using HarmonyLib;
using L2Flag;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Stops the Key Fairy from reacting to selected KeyFairyTargetScripts.
    ///
    /// isTargetActive is the only gate KeyFairyScript checks when it picks a target, so returning
    /// false there keeps the fairy with the player. A target is recognised by the (sheet, flag) its
    /// effectFlags write. Most targets are "keymarks": the fairy only sets a keyXX flag that makes
    /// a key symbol appear on a breakable wall. The wall's WeaponHitArea never reads that flag, so
    /// with the target disabled the wall still breaks normally.
    ///
    /// To disable another target, add its effect flag to <see cref="Disabled"/>.
    ///
    /// field08 Takamagahara Shrine (level10, sheet 12)
    ///   (12,33) B3_key  [B-4] opens the Ziten Seal3 (Life Sigil) for the Ring Chest (ACH)
    ///   (12,67) keyB3   [B-4] wall B3_break3 (12,28) hiding the Ring Chest                   DISABLED
    ///   (12,61) keyA0   [A-1] wall A0_bomb (12,0) hiding the Cog of Antiquity spot
    ///   (12,62) keyC2   [C-3] wall C2_break1 (12,19) hiding Spirit Palace Hidden Coin Pot    DISABLED
    ///   (12,64) keyC3s  [C-4] wall C3_break1 (12,34) hiding the Ash Geen shop entrance       DISABLED
    ///   (12,66) keyC3h  [C-4] wall C2_hidden (12,65) passage to the Ring Chest room           DISABLED
    ///   (12,71) keyC6   [C-7] wall C6_gate (12,50) hiding the Amanoiwato gate door
    ///   (12,63) keyD3   [D-4] wall D3_break (12,37) hiding "Glossary in Bottom-Right Breakable Wall"
    ///   (12,68) keyC5   [D-6] wall D5_break1 (12,43), passage to Life Sigil for Skull chest
    ///   (12,69) keyD5   [D-6] wall D5_break2 (12,44) hiding Breakable Pillar Coin Pot
    ///   (12,70) keyE5   [E-6] wall E5_break (12,46), underpass before Daji
    ///
    /// field14 Eternal Prison Gloom (level16, sheet 18)
    ///   (18,10) C5_key  [C-5] breaks the Bomb weapon vault wall (ACH)
    ///   (18,56) keyC4   [C-4] wall C4_bomb (18,14) hiding Mother mantra mural with Bomb     DISABLED
    ///   (18,57) keyC6   [C-6] wall C6_bomb (18,18) hiding the Beo Eg-Lana mural
    ///
    /// </summary>
    [HarmonyPatch(typeof(KeyFairyTargetScript), nameof(KeyFairyTargetScript.isTargetActive))]
    internal static class KeyFairyTargetFilterPatch
    {
        private static readonly HashSet<int> Disabled = new HashSet<int>
        {
            Key(12, 67), // TS keyB3  — Ring Chest wall
            Key(12, 62), // TS keyC2  — Spirit Palace hidden coin pot wall
            Key(12, 64), // TS keyC3s — Ash Geen shop wall
            Key(12, 66), // TS keyC3h — Ring Chest room access wall
            Key(18, 56), // EPG keyC4 — Mother mantra mural wall
        };

        // net35 has no ValueTuple; flags per sheet stay well below 1000.
        private static int Key(int sheet, int flag) => sheet * 1000 + flag;

        static void Postfix(KeyFairyTargetScript __instance, ref bool __result)
        {
            if (!__result) return;

            L2FlagBoxEnd[] effects = __instance.effectFlags;
            if (effects == null) return;

            foreach (var e in effects)
            {
                if (e != null && Disabled.Contains(Key(e.seet_no1, e.flag_no1)))
                {
                    __result = false;
                    return;
                }
            }
        }
    }
}
