using System.Collections;
using HarmonyLib;

namespace LaMulana2Archipelago.Patches
{
    /// <summary>
    /// Field BGM stays silent after leaving a room where it was faded out (e.g. leaving the
    /// Ratatoskr arena in Mausoleum without beating him, then entering Gate of Guidance).
    ///
    /// A BaseTrackChangeScript fades the base volume to 0, which pauses the base tracks.
    /// Normally its shadowHide fades them back in, but a scene change unloads it first.
    /// When the next field's BaseBGMObject uses the same track (Mausoleum and GoG share
    /// music 6), vanilla setBaseBGM marks it "sameFlag", which means "already playing,
    /// don't restart". It also clears baseBgmPaused, so the resume path in
    /// changeAllBaseTrackVolume never runs either. The track stays paused for good.
    ///
    /// Fix: after setBaseBGM, any reused track whose player is paused is marked as not-same,
    /// so MusicManager.Update starts it with the new field's tracks. play() on a paused
    /// player resumes from its saved position.
    /// </summary>
    [HarmonyPatch(typeof(MusicManager), nameof(MusicManager.setBaseBGM))]
    internal static class BaseBgmPausedCarryOverPatch
    {
        private static readonly AccessTools.FieldRef<MusicManager, MusicPlayer[]> MusicList =
            AccessTools.FieldRefAccess<MusicManager, MusicPlayer[]>("MusicList");

        static void Postfix(MusicManager __instance)
        {
            try
            {
                var tracks = Traverse.Create(__instance).Field("baseTracks").GetValue() as IList;
                var players = MusicList(__instance);
                if (tracks == null || players == null) return;

                foreach (var c in tracks)
                {
                    var t = Traverse.Create(c);
                    if (!t.Field("sameFlag").GetValue<bool>()) continue;
                    int no = t.Field("musicNo").GetValue<int>();
                    if (no < 0 || no >= players.Length || players[no] == null) continue;
                    if (!players[no].isPaused()) continue;

                    t.Field("sameFlag").SetValue(false);
                    Plugin.Log.LogInfo($"[BGM] Reused base track {no} was left paused; resuming it on field change");
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[BGM] BaseBgmPausedCarryOverPatch failed: {e.Message}");
            }
        }
    }
}
