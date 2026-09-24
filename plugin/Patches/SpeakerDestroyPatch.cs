using HarmonyLib;
using OdinOnDemand.Components;
using OdinOnDemand.MPlayer;
using OdinOnDemand.Utils.Net;
using UnityEngine;

namespace OdinOnDemand.Patches
{
    /// <summary>
    ///     ZNetScene.Destroy runs only when a piece is removed or broken, never when it unloads, so
    ///     this is the one place a speaker link can safely be dropped.
    /// </summary>
    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Destroy))]
    public class SpeakerDestroyPatch
    {
        private static void Prefix(GameObject go)
        {
            var speaker = go ? go.GetComponentInChildren<SpeakerComponent>() : null;
            if (!speaker) return;

            foreach (var list in ComponentLists.MediaComponentLists.Values)
            {
                foreach (BasePlayer player in list)
                {
                    if (player && player.IsLinkedTo(speaker)) player.RemoveSpeaker(speaker);
                }
            }
        }
    }
}
