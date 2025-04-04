using System;
using AngleSharp.Text;
using HarmonyLib;
using OdinOnDemand.Utils;
using OdinOnDemand.Utils.Config;
using UnityEngine;
using Logger = Jotunn.Logger;

namespace OdinOnDemand.Patches
{
    [HarmonyPatch(typeof(Player), "RemovePiece")]
    public class RemovePiecePatch
    {
        private static readonly string[] Pieces = {"flatscreen", "theaterscreen", "tabletv", "monitor", "oldtv", "laptop", "radio", "boombox", "gramophone"};
        
        [HarmonyPrefix]
        public static bool PrefixRemovePiece(ref Player __instance)
        {
            if (!OODConfig.VipMode.Value)
            {
                return true;
            }
            RaycastHit hitInfo;
            if (!Physics.Raycast(GameCamera.instance.transform.position, GameCamera.instance.transform.forward,
                    out hitInfo, 50f, __instance.m_removeRayMask) ||
                (double)Vector3.Distance(hitInfo.point, __instance.m_eye.position) >= (double)__instance.m_maxPlaceDistance)
            {
                return false;
            }
            var piece = hitInfo.collider.GetComponentInParent<Piece>();
            if (!piece || !piece.m_canBeRemoved)
            {
                return false;
            }

            if (Pieces.Contains(piece.gameObject.name.Replace("(Clone)","")) && OODConfig.VipMode.Value)
            {
                var rank = RankSystem.GetRank();
                //
                if (rank != RankSystem.PlayerRank.Admin && rank != RankSystem.PlayerRank.Vip)
                {
                    RankSystem.DisplayBlockMenu();
                    return false;
                }
            }
            return true;
        }
    }
}