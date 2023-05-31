using System.Collections.Generic;
using Jotunn.Managers;
using OdinOnDemand.Utils;
using UnityEngine;
using Logger = Jotunn.Logger;

namespace OdinOnDemand
{
    internal class RemoteControlItem : MonoBehaviour
    {
        private Camera cam;
        private Player localPlayer;
        private MediaPlayerComponent mp;


        private void Start()
        {
            cam = Camera.main;
            localPlayer = Player.m_localPlayer;
        }

        private void Update()
        {
            if (localPlayer)
                if (ZInput.instance != null)
                {
                    // USE REMOTE CONTROL TO OPEN SCREEN
                    if (KeyConfig.UseRemoteButton != null && MessageHud.instance != null &&
                        Player.m_localPlayer != null && Player.m_localPlayer.m_visEquipment.m_rightItem == "remote" &&
                        transform.parent.name.Contains("RightHand"))
                        if (ZInput.GetButton(KeyConfig.UseRemoteButton.Name))
                        {
                            if (transform.GetComponentInParent<Player>().GetPlayerID() != localPlayer.GetPlayerID())
                                return;
                            if (mp != null)
                            {
                                if (mp.PlayerSettings.IsGuiActive)
                                    return;
                                mp = null;
                            }
                            
                            if (Physics.Raycast(cam.transform.position, cam.transform.forward, out var raycastHit,
                                    OODConfig.RemoteControlDistance.Value, BaseAI.m_solidRayMask))
                            {
                                mp = raycastHit.transform.gameObject.GetComponent<MediaPlayerComponent>() ??
                                     raycastHit.transform.gameObject.GetComponentInParent<MediaPlayerComponent>();
                                
                                if (mp != null)
                                {
                                    if (!mp.mPiece.IsCreator() && OODConfig.RemoteControlOwnerOnly.Value) return;
                                    if (!mp.PlayerSettings.IsGuiActive && PrivateArea.CheckAccess(transform.position))
                                    {
                                        mp.UIController.ToggleMainPanel();
                                    }
                                }
                            }
                        }
                }
        }
    }
}