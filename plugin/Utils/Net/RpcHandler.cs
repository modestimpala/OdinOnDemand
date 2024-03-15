using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using Jotunn;
using Jotunn.Entities;
using Jotunn.Managers;
using OdinOnDemand.Components;
using OdinOnDemand.Dynamic;
using OdinOnDemand.MPlayer;
using OdinOnDemand.Utils.Config;
using UnityEngine;
using UnityEngine.Serialization;
using static OdinOnDemand.Utils.Net.CinemaPackage;
using CompressionLevel = System.IO.Compression.CompressionLevel;
using Logger = Jotunn.Logger;

namespace OdinOnDemand.Utils.Net
{
    public class RpcHandler
    {

        private static CustomRPC _oodrpc;

        public void Create()
        {
            _oodrpc = NetworkManager.Instance.AddRPC("OODRPC", OODRPCServerReceive, OODRPCClientReceive);
            if (OODConfig.DebugEnabled.Value) Logger.LogDebug("Created OODRPC");
        }

        public void SendData(long peer, RPCDataType type, MediaPlayers player = default, string mediaPlayerID = "", Vector3 pos = default, float time = 0, string url = "",
            PlayerStatus status = PlayerStatus.NULL, float volume = 1.0f, bool toggleBool = false)
        {
            _oodrpc.SendPackage(peer == 0 ? ZRoutedRpc.instance.GetServerPeerID() : peer,
                new CinemaPackage().Pack(type, player, mediaPlayerID, pos, time, url, status, toggleBool));
        }

        private static void SendStationData(CinemaPackage cinemaPackage)
        {
            var station = StationManager.Instance.GetStation(cinemaPackage.data.url);
            if (station == default) return;
            var package = new CinemaPackage();
            var data = new Data
            {
                url = station.Title,
                currentTrackTitle = station.Tracks[station.CurrentTrackIndex].Title,
                time = station.Tracks[station.CurrentTrackIndex].CurrentTime,
                x = cinemaPackage.data.x,
                y = cinemaPackage.data.y,
                z = cinemaPackage.data.z,
                mediaPlayerID = cinemaPackage.data.mediaPlayerID,
                playerStatus = cinemaPackage.playerStatus
            };
            package.Prepare(RPCDataType.SendStation, cinemaPackage.player, data);
            
            var zpackage = new ZPackage();
            using (var memoryStream = new MemoryStream())
            {
                using (var gzipStream = new GZipStream(memoryStream, CompressionLevel.Optimal))
                {
                    var formatter = new BinaryFormatter();
                    formatter.Serialize(gzipStream, package);
                }

                var array = memoryStream.ToArray();
                if (OODConfig.DebugEnabled.Value)
                    Logger.LogDebug($"Serialized and compressed size: {array.Length} bytes");
                try
                {
                    zpackage.Write(array);
                }
                catch (Exception ex)
                {
                    Logger.LogError("Error writing to ZPackage: " + ex.Message);
                }
            }
            
            Vector2 targetPos = new Vector2(cinemaPackage.data.x, cinemaPackage.data.z);
            if (ZNet.instance.IsLocalInstance())
            {
                HandlePackageClient(package, 0);
            }
            SendPackageToPeersInRange(zpackage, targetPos, 284);
        }
        
        public static void HandlePackageServer(ZPackage package, long sender)
        {
            var cinemaPackage = Unpack(package);
            if (cinemaPackage == null) return;
            if (ZNet.instance.IsLocalInstance())
            {
                HandlePackageClient(cinemaPackage, sender);
            }
            if(cinemaPackage.type == RPCDataType.RequestStation)
            {
                if (StationManager.Instance.GetStation(cinemaPackage.data.url) == null) return;
                SendStationData(cinemaPackage);
                return;
            }
            if (cinemaPackage.type == RPCDataType.RequestTime && StationManager.Instance.GetStation(cinemaPackage.data.url) != null)
            {  
                Vector3 pos = new Vector3(cinemaPackage.data.x, cinemaPackage.data.y, cinemaPackage.data.z);
                OdinOnDemandPlugin.RPCHandlers.SendData(sender, RPCDataType.SyncTime, cinemaPackage.player, cinemaPackage.data.mediaPlayerID, pos, StationManager.Instance.GetStation(cinemaPackage.data.url).Tracks[StationManager.Instance.GetStation(cinemaPackage.data.url).CurrentTrackIndex].CurrentTime);
                return;
            }
            
            Vector2 targetPos = new Vector2(cinemaPackage.data.x, cinemaPackage.data.z);
            SendPackageToPeersInRange(package, targetPos);
        }

        private static void SendPackageToPeersInRange(ZPackage package, Vector3 targetPos, float radius = 128f)
        {
            var peers = ZNet.instance.m_peers;
            foreach (var peer in peers)
            {
                Vector2 peerPos = new Vector2(peer.m_refPos.x, peer.m_refPos.z);
                float distance = Vector2.Distance(peerPos, targetPos);
                List<ZNetPeer> peersInRange = new List<ZNetPeer>();
                // If the distance is less than or equal to the radius, add the player to the list
                if (distance <= radius)
                {
                    peersInRange.Add(peer);
                }
                
                _oodrpc.SendPackage(peersInRange, package);
            }
        }

        public static void HandlePackageClient(CinemaPackage package, long sender)
        {
            if ((package.type == RPCDataType.SetVideoUrl || package.type == RPCDataType.SetAudioUrl) && package.data.url == "")
                return;

            var pos = new Vector3(package.data.x, package.data.y, package.data.z);
            BasePlayer mp = null;

            var playerType = package.player switch
            {
                MediaPlayers.Radio => typeof(MediaPlayerComponent),
                MediaPlayers.CinemaScreen => typeof(MediaPlayerComponent), 
                MediaPlayers.BeltPlayer => typeof(BeltPlayerComponent),
                MediaPlayers.CartPlayer => typeof(CartPlayerComponent),
                MediaPlayers.Receiver => typeof(ReceiverComponent),
                _ => null
            };

            if (playerType != null && ComponentLists.MediaComponentLists.TryGetValue(playerType, out var list))
            {
                mp = list.Cast<BasePlayer>().FirstOrDefault(x => x is MediaPlayerComponent mpc && mpc.transform.position == pos) ?? 
                     list.Cast<BasePlayer>().FirstOrDefault(x => x.MediaPlayerID == package.data.mediaPlayerID);
            }

            if (mp == null)
            {
                if(OODConfig.DebugEnabled.Value) Logger.LogWarning("No player found for package type " + package.type + " at position " + pos);
                return;
            }

            Action action = package.type switch
            {
                RPCDataType.SetVideoUrl => () => mp.RPC_SetURL(package.data.url, package.playerStatus == PlayerStatus.Paused, package.data.time),
                RPCDataType.SetAudioUrl => () => { }, 
                RPCDataType.Stop => () => mp.Stop(true),
                RPCDataType.Pause => () => mp.Pause(true),
                RPCDataType.Play => () => mp.Play(true),
                RPCDataType.SetLoop => () => mp.UIController.SetLoop(package.data.toggleBool),
                RPCDataType.SetLock => () => mp.SetLock(package.data.toggleBool),
                RPCDataType.UpdateZDO => () => mp.RPC_UpdateZDO(),
                RPCDataType.SendStation => () =>
                {
                    mp.RPC_PlayStation(package.data.url, 
                        package.data.currentTrackTitle, package.data.time);
                },
                RPCDataType.RequestTime => () => { mp.BroadcastTime(); },
                RPCDataType.SyncTime => () => { mp.UpdatePlayerTime(package.data.time); },
                RPCDataType.RequestOwnership => () => mp.SetOwnership(sender),
                _ => () => { } // Handle other cases or do nothing
            };

            action.Invoke();
        }

        // React to the RPC call on server
        private IEnumerator OODRPCServerReceive(long sender, ZPackage package)
        {
            HandlePackageServer(package, sender);
            yield return null;
        }

        // React to the RPC call on a client
        private IEnumerator OODRPCClientReceive(long sender, ZPackage package)
        {
            HandlePackageClient(Unpack(package), sender);
            yield return null;
        }
        
    }

    //custom rpc package
    [Serializable]
    public class CinemaPackage
    {
        [Serializable]
        public enum MediaPlayers
        {
            CinemaScreen = 1,
            Radio = 2,
            Receiver = 3,
            BeltPlayer = 4,
            CartPlayer = 5,
            NULL = 0
        }

        [Serializable]
        public enum PlayerStatus
        {
            Playing = 1,
            Stopped = 2,
            Paused = 3,
            NULL = 0
        }

        [Serializable]
        public enum RPCDataType
        {
            SetVideoUrl = 9000,
            SetAudioUrl = 9001,
            Stop = 9002,
            Pause = 9003,
            Play = 9004,
            SetLoop = 9007,
            SetLock = 9009,
            UpdateZDO = 9010,
            SendStation = 9012,
            SendStationRaw = 90133,
            SyncTime = 9013,
            RequestTime = 9014,
            RequestOwnership = 9015,
            RequestStation = 90211,
        }

        public Data data;
        public RPCDataType type;
        public MediaPlayers player;
        [FormerlySerializedAs("status")] public PlayerStatus playerStatus;

        public virtual void Prepare(RPCDataType type, MediaPlayers player, Data data)
        {
            this.type = type;
            this.player = player;
            this.data = data;
        }

        public ZPackage Pack(RPCDataType type, MediaPlayers player, string mediaPlayerID, Vector3 pos, float time, string url,
            PlayerStatus status = PlayerStatus.NULL, bool toggleBool = false)
        {
            if (OODConfig.DebugEnabled.Value)
                Logger.LogDebug("Received request to pack data type of " + type + " with url of " + url);

            var dataToPack = new Data
            {
                url = url,
                x = pos.x,
                y = pos.y,
                z = pos.z,
                time = time,
                toggleBool = toggleBool,
                mediaPlayerID = mediaPlayerID,
                playerStatus = status
            };

            Prepare(type, player, dataToPack);
            var package = new ZPackage();
            using (var memoryStream = new MemoryStream())
            {
                using (var gzipStream = new GZipStream(memoryStream, CompressionLevel.Optimal))
                {
                    new BinaryFormatter().Serialize(gzipStream, this);
                }

                var array = memoryStream.ToArray();
                if (OODConfig.DebugEnabled.Value)
                    Logger.LogDebug(string.Format("Serialized size: {0} bytes", array.Length));
                
                try
                {
                    package.Write(array);
                }
                catch (Exception ex)
                {
                    Logger.LogError("Error writing to ZPackage: " + ex.Message);
                }
            }

            return package;
        }

        public static CinemaPackage Unpack(ZPackage package)
        {
            var array = package.ReadByteArray();
            if (OODConfig.DebugEnabled.Value)
                Logger.LogDebug(string.Format("Deserializing package size: {0} bytes", array.Length));
            using (var memoryStream = new MemoryStream(array))
            {
                using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress, true))
                {
                    var obj = new BinaryFormatter().Deserialize(gzipStream);
                    if (obj is CinemaPackage compressedPackage)
                    {
                        return compressedPackage;
                    }
                }
            }

            return null;
        }

        [Serializable]
        public struct Data
        {
            public string url;
            public float x;
            public float y;
            public float z;
            public float time;
            public bool toggleBool;
            public string mediaPlayerID;
            //Station data
            public string currentTrackTitle;
            public PlayerStatus playerStatus;
        }
    }
}