using HarmonyLib;
using L2Base;
using L2Hit;
using L2Word;
using L2STATUS;
using LaMulana2Archipelago.Managers;
using LaMulana2Archipelago.Archipelago;

namespace LaMulana2Archipelago.Patches
{
    // SaveMenu calls sys.dataLoad(SaveTab * 5 + slotIndex) / sys.dataSave(...)
    // NOT fileLoad/fileSave (those are dead code wrappers).

    [HarmonyPatch(typeof(L2System), nameof(L2System.dataLoad))]
    internal static class Shadow_DataLoad_Patch
    {
        static void Prefix(L2System __instance, int file_no)
        {
            ShadowSaveManager.SetCurrentSaveSlot(file_no);
            ArchipelagoClient.SetCurrentSaveSlot(file_no);
            ShadowSaveManager.OnFileLoad();

            // Wipe inherited Status state before memLoad rebuilds it.
            //
            // The boot-time Demos.cs reInitSystem(false) lands before AP connects,
            // so our StatusResetPatch is disabled and vanilla Status.resetPlayerStatus
            // runs — which hardcodes haveMainWeapon(LWHIP, true) + setMainWeapon(LWHIP).
            // memLoad's later setMainWeapon(saved) is gated on l2_main_have[saved];
            // for a save that started with no main weapon (saved == NON), that gate
            // is false, the assignment is skipped, and l2_eq_main stays LWHIP —
            // leaving the player with a phantom whip equipped that isn't in inventory.
            //
            // clearItemsNum here gives loadInitFlagToItem a clean slate to rebuild
            // l2_main_have / l2_sub_have / l2_use_have from the saved flags, after
            // which setMainWeapon(saved) finally takes.
            if (StatusResetPatch.Enabled)
            {
                var playerst = Traverse.Create(__instance).Field("playerst").GetValue<Status>();
                if (playerst != null)
                {
                    playerst.clearItemsNum();

                    // clearItemsNum wipes the Status data but not the StatusBar UI.
                    // The boot reInitSystem(false) already called statusbar.setMain(LWHIP, 0),
                    // so the whip icon is still drawn. If memLoad doesn't re-equip a main
                    // weapon (because saved l2_eq_main == NON), setMainWeapon is gated by
                    // l2_main_have and skips its statusbar.setMain call, and the whip icon
                    // sticks. Reset the UI here so memLoad starts from a clean slate —
                    // for non-NON saves, memLoad's setMainWeapon/setSubWeapon/setUseItem
                    // will repaint the correct icons.
                    var statusbar = Traverse.Create(playerst).Field("statusbar").GetValue<StatusBarIF>();
                    if (statusbar != null)
                    {
                        statusbar.setMain(MAINWEAPON.NON, 0);
                        statusbar.setSub(SUBWEAPON.NON, 0);
                        statusbar.setUse(USEITEM.NON, 0);
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(L2System), nameof(L2System.dataSave))]
    internal static class Shadow_DataSave_Patch
    {
        static void Prefix(int file_no)
        {
            ShadowSaveManager.SetCurrentSaveSlot(file_no);
            ArchipelagoClient.SetCurrentSaveSlot(file_no);
        }

        static void Postfix()
        {
            ShadowSaveManager.OnDataSave();
        }
    }

    [HarmonyPatch(typeof(L2System), nameof(L2System.memSave))]
    internal static class Shadow_MemSave_Patch
    {
        static void Postfix(bool __result)
        {
            if (__result)
                ShadowSaveManager.OnMemSave();
        }
    }

    [HarmonyPatch(typeof(GameOverTask), nameof(GameOverTask.gameOverStart))]
    internal static class Shadow_GameOver_Patch
    {
        static void Postfix()
        {
            ShadowSaveManager.OnDeath();
        }
    }

    // gameStat is the new-game entry point from the title's Start option.
    // The Continue / Load paths use memLoad / dataLoad and don't reach here,
    // so this hook fires only for a fresh run and is the only place where
    // staging needs to be wiped without a slot file backing it.
    [HarmonyPatch(typeof(L2System), nameof(L2System.gameStat))]
    internal static class Shadow_GameStat_Patch
    {
        static void Postfix()
        {
            ShadowSaveManager.OnNewGame();
        }
    }

    // memLoad reverts the in-memory state to the last memSave (autosave)
    // checkpoint. It fires from three call sites:
    //   - Title.cs Continue button
    //   - GameOverTask path 1 (memSave → reInitSystem → memLoad)
    //   - GameOverTask path 2 (reInitSystem → memLoad, no memSave)
    // The death paths already flag via gameOverStart (which lands before the
    // in-death memSave); this hook is what catches the title-Continue path,
    // and is a harmless no-op in the death paths.
    [HarmonyPatch(typeof(L2System), nameof(L2System.memLoad))]
    internal static class Shadow_MemLoad_Patch
    {
        static void Postfix()
        {
            ShadowSaveManager.OnMemLoad();
        }
    }
}
