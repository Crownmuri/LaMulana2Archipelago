using System;

namespace LaMulana2Archipelago.Trackers
{
    public static class DialogEvents
    {
        public static event Action DialogEnded;

        public static void RaiseDialogEnded()
        {
            DialogEnded?.Invoke();
        }
    }
}
