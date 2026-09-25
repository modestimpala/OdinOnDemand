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
            if (Headless) return;

            SetupCartPlayer();
            
            
            if(WaveParticleSystem)
                WaveParticleSystem.Stop();
            
        }
        
        public void OnEnable()
        {
            if (mPiece.IsPlacedByPlayer()) LoadZDO(); // If the player is placed by a player, load the zdo data to init
        }
        
        // Every change is saved as it happens. Saving the whole state here let any client that
        // walked away overwrite the shared ZDO (speakers it had not loaded, stale play state) and
        // pulled ownership to that client.
        public void OnDisable()
        {
            SaveTimeOnUnload();
        }

        private void SetupCartPlayer()
        {
            mScreen.url = "";
            mScreen.Pause();
        }

    }
}