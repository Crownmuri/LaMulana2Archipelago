using L2Base;
using L2Task;
using UnityEngine;

namespace LaMulana2Archipelago.Trackers
{
    public static class DialogStateTracker
    {
        public static bool IsInDialog { get; private set; }

        public static void ForceDialogStart()
        {
            IsInDialog = true;
        }

        public static void ForceDialogEnd()
        {
            IsInDialog = false;
            DialogEvents.RaiseDialogEnded();
        }
    }
}