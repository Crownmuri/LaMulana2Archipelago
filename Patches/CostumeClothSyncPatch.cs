using System;
using HarmonyLib;
using L2Base;
using LaMulana2Archipelago.Managers;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// <c>L2System.setSystemDataToClothFlag</c> regenerates the five sheet-2
    /// costume flags from <c>getClothBox</c> on every load, new game and reset.
    /// With <see cref="ClothBoxPatch"/> answering getClothBox from the AP-received
    /// set, that sync now writes 1 for costumes the player owns — and it writes
    /// them through the string <c>setFlagData</c> overload, which
    /// <see cref="SetFlagDataPatch"/> forwards to CheckManager. Wrap the whole
    /// method in the grant guard so a costume flag going 0→1 during a load can
    /// never be mistaken for the player collecting a location.
    ///
    /// This is a profile→flag sync, never a pickup, so the guard is unconditional
    /// rather than gated on costumesanity.
    /// </summary>
    [HarmonyPatch(typeof(L2System), "setSystemDataToClothFlag")]
    internal static class CostumeClothSyncPatch
    {
        static void Prefix(out IDisposable __state)
        {
            __state = ItemGrantRecursiveGuard.Begin();
        }

        // Finalizer, not Postfix: a Postfix is skipped when the method throws,
        // which would leave the guard open and silence every flag-based location
        // check for the rest of the session. A void finalizer rethrows as usual.
        static void Finalizer(IDisposable __state)
        {
            __state?.Dispose();
        }
    }
}
