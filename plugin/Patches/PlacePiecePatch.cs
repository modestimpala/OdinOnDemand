using System;
using AngleSharp.Text;
using HarmonyLib;
using OdinOnDemand.Utils;
using OdinOnDemand.Utils.Config;
using UnityEngine;

namespace OdinOnDemand.Patches
{
    public class PlacePiecePatch
    {
        private static readonly string[] Pieces = {"flatscreen", "theaterscreen", "tabletv", "monitor", "oldtv", "laptop", "radio", "boombox", "gramophone"};
        
        [HarmonyPatch(typeof(Player), "PlacePiece", new Type[]
        {
            typeof(Piece),
            typeof(Vector3),
            typeof(Quaternion),
            typeof(bool)
        })]
        private static class PlacePiece_Patch
        {
            private static bool Prefix(Piece piece, Vector3 pos, Quaternion rot, bool doAttack = true)
            {
                if (Pieces.Contains(piece.gameObject.name) && OODConfig.VipMode.Value)
                {
                    var rank = RankSystem.GetRank();
                    //rank != RankSystem.PlayerRank.Admin &&
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
}