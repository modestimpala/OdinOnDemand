using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;
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
    public class ReceiverComponent : BasePlayer, Hoverable, Interactable
    {
        public new void Awake()
        {
            base.Awake();
            
            mPiece = gameObject.GetComponentInChildren<Piece>();
            mName = mPiece.m_name;
            PlayerSettings.PlayerType = CinemaPackage.MediaPlayers.Receiver;
            if (Headless) return;
            
            SetupReceiver();
            
            InvokeRepeating(nameof(DropoffUpdate), 0.05f, 0.05f);
        }

        // The receiver is a hub for its speakers, not a speaker itself.
        protected override bool EmitsFromSelf => false;

        private void SetupReceiver()
        {
            //Screen events
            mScreen.url = "";
            mScreen.Pause();
            
        }
   
        private void DropoffUpdate()
        {
            if (PlayerSettings.IsPaused || UnparsedURL == null) return;
            //vertical distance dropoff
            if (PlayerSettings.VerticalDistanceDropoff != 0f)
            {
                // Get the player's position
                if (Player.m_localPlayer == null) return;
                Vector3 playerPosition = Player.m_localPlayer.transform.position;

                // Calculate the absolute difference in height
                float heightDifference = Mathf.Abs(playerPosition.y - this.transform.position.y);

                // If the height difference is greater than the dropoff distance
                if (heightDifference > PlayerSettings.VerticalDistanceDropoff)
                {
                    // Decrease the volume based on the square of the height difference
                    var volumeDecrease = 1 - 1 / Mathf.Pow(heightDifference + 1, PlayerSettings.DropoffPower);
                    SetOutputVolume(Mathf.Max(0, PlayerSettings.Volume - volumeDecrease));
                }
                else
                {
                    // If the player is within the dropoff distance, set the volume to the original volume
                    SetOutputVolume(PlayerSettings.Volume);
                }
            }
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

        private new void OnDestroy()
        {
            base.OnDestroy();
            if (PlayerSettings
                .IsGuiActive) // If the GUI is active, close it so it's not stuck open. Solves edge case where player is destroyed while GUI is open
            {
                PlayerSettings.IsGuiActive = false;
                if (UIController.URLPanelObj) UIController.URLPanelObj.SetActive(false);
                GUIManager.BlockInput(PlayerSettings.IsGuiActive);
            }

            if (UIController.URLPanelObj) Destroy(UIController.URLPanelObj);
        }
        
        public override void LoadZDO()
        {
            base.LoadZDO();
            var zdo = ZNetView.GetZDO();
            if (zdo == null) return;
            PlayerSettings.VerticalDistanceDropoff = zdo.GetFloat("verticalDropoff");
            if (zdo.GetFloat("dropoffPower") != 0f)
            {
                PlayerSettings.DropoffPower = zdo.GetFloat("dropoffPower");
            }
        }

        public override void UpdateZDO() // For periodic updates to the ZDO, usually from RPC when a player changes something
        {
            base.UpdateZDO();
            var zdo = ZNetView.GetZDO();
            if (zdo == null) return;
            PlayerSettings.VerticalDistanceDropoff = zdo.GetFloat("verticalDropoff");
        }
        
        public override void SaveZDO(bool saveTime = true) // Save all our ZDO settings
        {
            base.SaveZDO();
            var zdo = ZNetView.GetZDO();
            if (zdo == null || mAudio == null) return;
            zdo.Set("verticalDropoff", PlayerSettings.VerticalDistanceDropoff);
            zdo.Set("dropoffPower", PlayerSettings.DropoffPower);
        }
        
        public string GetHoverText()
        {
            if (PlayerSettings.AdminOnly && !SynchronizationManager.Instance.PlayerIsAdmin) return "";
            //TODO: control volume with no access
            if (OODConfig.VipMode.Value)
            {
                var rank = RankSystem.GetRank();
                if (rank != RankSystem.PlayerRank.Admin && rank != RankSystem.PlayerRank.Vip)
                {
                    return Localization.instance.Localize(mName + "\n$piece_noaccess");
                }
            }

            if (!PrivateArea.CheckAccess(transform.position, 0f, false) && PlayerSettings.IsLocked)
                return Localization.instance.Localize(mName + "\n$piece_noaccess");
            if (PlayerSettings.IsLinkedToParent)
            {
                return Localization.instance.Localize(
                    string.Concat("[<color=yellow><b>$KEY_Use</b></color>] $piece_use (Linked)"));
            }

            return Localization.instance.Localize(string.Concat("[<color=yellow><b>$KEY_Use</b></color>] $piece_use "));
        }

        

        public string GetHoverName()
        {
            return mName;
        }

        public float GetHoverOffset()
        {
            return 0f;
        }

        public bool Interact(Humanoid user, bool hold, bool alt) //Open screen UI
        {
            if (OODConfig.VipMode.Value)
            {
                var rank = RankSystem.GetRank();
                if (rank != RankSystem.PlayerRank.Admin && rank != RankSystem.PlayerRank.Vip)
                {
                    return false;
                }
            }

            if ((!PrivateArea.CheckAccess(transform.position) && PlayerSettings.IsLocked) ||
                (PlayerSettings.AdminOnly && !SynchronizationManager.Instance.PlayerIsAdmin)) return false;

            UIController.ToggleMainPanel();

            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            return false;
        }
        
      
    }
    
    /// <summary>A saved receiver-to-speaker link: the speaker's guid and where it stood when linked.</summary>
    public readonly struct SpeakerLink
    {
        public readonly string Guid;
        public readonly Vector3 Position;

        public SpeakerLink(string guid, Vector3 position)
        {
            Guid = guid ?? "";
            Position = position;
        }
    }

    public class SpeakerHelper
    {
        /// <summary>Average of the speakers, counting <paramref name="self" /> as one more when given.</summary>
        public static Vector3 CalculateAudioCenter(List<SpeakerLink> speakers, Vector3? self = null)
        {
            var center = self ?? Vector3.zero;
            foreach (var speaker in speakers)
            {
                center += speaker.Position;
            }
            center /= speakers.Count + (self.HasValue ? 1 : 0);
            return center;
        }
        
        public static byte[] CompressSpeakerLinks(List<SpeakerLink> speakers)
        {
            if (speakers == null) return default;
            using var memoryStream = new MemoryStream();
            using (var binaryWriter = new BinaryWriter(memoryStream))
            {
                // Write the count of speakers first
                binaryWriter.Write(speakers.Count);

                foreach (var speaker in speakers)
                {
                    // Write the mGuid and position of each speaker
                    binaryWriter.Write(speaker.Guid);
                    binaryWriter.Write(speaker.Position.x);
                    binaryWriter.Write(speaker.Position.y);
                    binaryWriter.Write(speaker.Position.z);
                }
            }
            return memoryStream.ToArray();
        }

        public static List<SpeakerLink> DecompressSpeakerLinks(byte[] data)
        {
            var speakers = new List<SpeakerLink>();
            if (data == null || data.Length < sizeof(int)) return speakers;
            try
            {
                using var memoryStream = new MemoryStream(data);
                using var binaryReader = new BinaryReader(memoryStream);
                int count = binaryReader.ReadInt32();  // Read the count of speakers

                for (int i = 0; i < count; i++)
                {
                    string mGuid = binaryReader.ReadString();
                    Vector3 position = new Vector3(binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle());
                    if (!speakers.Exists(x => x.Guid == mGuid && x.Position == position))
                    {
                        speakers.Add(new SpeakerLink(mGuid, position));
                    }
                }
            }
            catch (EndOfStreamException)
            {
                Jotunn.Logger.LogWarning("Ignoring truncated speaker link data.");
            }
            return speakers;
        }

    }
}
