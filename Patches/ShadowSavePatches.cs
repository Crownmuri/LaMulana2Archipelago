using HarmonyLib;
using L2Base;
using LaMulana2Archipelago.Managers;
using LaMulana2Archipelago.Archipelago;

namespace LaMulana2Archipelago.Patches
{
    [HarmonyPatch(typeof(L2System), nameof(L2System.fileLoad))]
    internal static class Shadow_FileLoad_Patch
    {
        static void Prefix(int no)
        {
            ShadowSaveManager.SetCurrentSaveSlot(no);
            ArchipelagoClient.SetCurrentSaveSlot(no);
            ShadowSaveManager.OnFileLoad();
        }
    }

    [HarmonyPatch(typeof(L2System), nameof(L2System.fileSave))]
    internal static class Shadow_FileSave_Patch
    {
        static void Prefix(int no)
        {
            ShadowSaveManager.SetCurrentSaveSlot(no);
            ArchipelagoClient.SetCurrentSaveSlot(no);
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