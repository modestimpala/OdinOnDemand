using System.Linq;
using OdinOnDemand.MPlayer;
using OdinOnDemand.Utils;
using OdinOnDemand.Utils.Config;
using OdinOnDemand.Utils.Net;
using OdinOnDemand.Utils.UI;
using UnityEngine;
using UnityEngine.Video;

namespace OdinOnDemand.Components
{
    public class CartPlayerComponent : BasePlayer
    {
        
        public new void Awake()
        {
            base.Awake();
            //init component
            mPiece = gameObject.GetComponentInParent<Piece>();
            mName = mPiece.m_name;
            //Network
            WaveParticleSystem = gameObject.GetComponentInChildren<ParticleSystem>();
            
            PlayerSettings.PlayerType = CinemaPackage.MediaPlayers.CartPlayer;

            SetupCartPlayer();
            
            
            if(WaveParticleSystem)
                WaveParticleSystem.Stop();
            
        }
        
        public void OnEnable()
        {
            if (mPiece.IsPlacedByPlayer()) LoadZDO(); // If the player is placed by a player, load the zdo data to init
        }
        
        public void OnDisable()
        {
            SaveZDO();
        }

        private void SetupCartPlayer()
        {
            mScreen.url = "";
            mScreen.Pause();
        }

    }
}