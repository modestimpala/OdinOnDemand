using System;
using AngleSharp.Text;
using HarmonyLib;
using OdinOnDemand.Utils;
using OdinOnDemand.Utils.Config;

namespace OdinOnDemand.Patches
{
    public class PlacePiecePatch
    {
        private static readonly string[] Pieces = {"flatscreen", "theaterscreen", "tabletv", "monitor", "oldtv", "laptop", "radio", "boombox", "gramophone"};
        
        [HarmonyPatch(typeof(Player), "PlacePiece", new Type[]
        {
            typeof(Piece)
        })]
        private static class PlacePiece_Patch
        {
            private static bool Prefix(Piece piece)
            {
                if (Pieces.Contains(piece.gameObject.name) && OODConfig.VipMode.Value)
                {
                    var rank = RankSystem.GetRank(Steamworks.SteamUser.GetSteamID().ToString());
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