using HarmonyLib;
using L2Base;
using LaMulana2Archipelago.Managers;
using LaMulana2Archipelago.Archipelago;

namespace LaMulana2Archipelago.Patches
{
    // SaveMenu calls sys.dataLoad(SaveTab * 5 + slotIndex) / sys.dataSave(...)
    // NOT fileLoad/fileSave (those are dead code wrappers).

    [HarmonyPatch(typeof(L2System), nameof(L2System.dataLoad))]
    internal static class Shadow_DataLoad_Patch
    {
        static void Prefix(int file_no)
        {
            ShadowSaveManager.SetCurrentSaveSlot(file_no);
            ArchipelagoClient.SetCurrentSaveSlot(file_no);
            ShadowSaveManager.OnFileLoad();
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
