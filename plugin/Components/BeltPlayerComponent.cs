using System.Linq;
using Jotunn.Managers;
using OdinOnDemand.MPlayer;
using OdinOnDemand.Utils;
using OdinOnDemand.Utils.Config;
using OdinOnDemand.Utils.Net;
using OdinOnDemand.Utils.UI;
using UnityEngine;
using UnityEngine.Video;

namespace OdinOnDemand.Components
{
    public class BeltPlayerComponent : BasePlayer
    {
        public new void Awake()
        {
            base.Awake();
            WaveParticleSystem = gameObject.GetComponentInChildren<ParticleSystem>();
            WaveParticleSystem.Stop();
            mName = "Skald's Girdle";
            
            PlayerSettings.PlayerType = CinemaPackage.MediaPlayers.BeltPlayer;
            PlayerSettings.IsLooping = true;
            
            SetupBeltPlayer();
            
            mAudio.maxDistance = OODConfig.MobileListeningDistance.Value;
            mScreen.isLooping = true;
            mAudio.loop = true;
        }
        
        private new void OnDestroy()
        {
            base.OnDestroy();
            //cLean up
            if (PlayerSettings
                .IsGuiActive) // If the GUI is active, close it so it's not stuck open. Solves edge case where player is destroyed while GUI is open
            {
                PlayerSettings.IsGuiActive = false;
                if (UIController.URLPanelObj) UIController.URLPanelObj.SetActive(false);
                GUIManager.BlockInput(PlayerSettings.IsGuiActive);
            }

            UIController.DestroyUI();
            UIController = null;
            
        }

        private void SetupBeltPlayer()
        {
            //Screen events
            mScreen.url = "";
            mScreen.Pause();
        }
       
    }
}