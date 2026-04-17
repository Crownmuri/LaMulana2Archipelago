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
            ShadowSaveManager.OnMemSave();
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
}
