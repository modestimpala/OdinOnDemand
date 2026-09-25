using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Jotunn;
using Jotunn.Entities;
using Jotunn.Managers;
using OdinOnDemand.Components;
using OdinOnDemand.Dynamic;
using OdinOnDemand.MPlayer;
using OdinOnDemand.Utils.Config;
using UnityEngine;
using static OdinOnDemand.Utils.Net.CinemaPackage;
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
            if (station == null || station.Tracks.Count == 0) return;
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
                playerStatus = cinemaPackage.data.playerStatus
            };
            package.Prepare(RPCDataType.SendStation, cinemaPackage.player, data);
            var zpackage = package.ToZPackage();
            
            Vector2 targetPos = new Vector2(cinemaPackage.data.x, cinemaPackage.data.z);
            if (ZNet.instance.IsLocalInstance())
            {
                HandlePackageClient(package, 0);
            }
            SendPackageToPeersInRange(zpackage, targetPos, 284);
        }
        
        public static void HandlePackageServer(ZPackage package, long sender)
        {
            var cinemaPackage = TryUnpack(package, sender);
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
            var timedStation = cinemaPackage.type == RPCDataType.RequestTime
                ? StationManager.Instance.GetStation(cinemaPackage.data.url)
                : null;
            if (timedStation != null)
            {
                if (timedStation.Tracks.Count == 0) return;
                Vector3 pos = new Vector3(cinemaPackage.data.x, cinemaPackage.data.y, cinemaPackage.data.z);
                OdinOnDemandPlugin.RPCHandlers.SendData(sender, RPCDataType.SyncTime, cinemaPackage.player, cinemaPackage.data.mediaPlayerID, pos, timedStation.Tracks[timedStation.CurrentTrackIndex].CurrentTime);
                return;
            }
            
            // Relay the package as re-encoded here.
            Vector2 targetPos = new Vector2(cinemaPackage.data.x, cinemaPackage.data.z);
            SendPackageToPeersInRange(cinemaPackage.ToZPackage(), targetPos);
        }

        private static void SendPackageToPeersInRange(ZPackage package, Vector2 targetPos, float radius = 128f)
        {
            // One send for everyone in range. Each SendPackage starts its own coroutine, and this
            // used to start one per connected peer, in range or not, for every relayed package.
            var peersInRange = new List<ZNetPeer>();
            foreach (var peer in ZNet.instance.m_peers)
            {
                if (!peer.IsReady()) continue;
                var peerPos = new Vector2(peer.m_refPos.x, peer.m_refPos.z);
                if (Vector2.Distance(peerPos, targetPos) <= radius) peersInRange.Add(peer);
            }

            if (peersInRange.Count > 0) _oodrpc.SendPackage(peersInRange, package);
        }

        /// <summary>A malformed package from one client is dropped, not thrown on the server.</summary>
        private static CinemaPackage TryUnpack(ZPackage package, long sender)
        {
            try
            {
                var cinemaPackage = Unpack(package);
                if (cinemaPackage == null)
                    Logger.LogWarning("Dropping OdinOnDemand package from peer " + sender + " sent by another version of the mod.");
                return cinemaPackage;
            }
            catch (Exception e)
            {
                Logger.LogWarning("Dropping unreadable OdinOnDemand package from peer " + sender + ": " + e.Message);
                return null;
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
                // Match on the ZDO id. Older clients sent an empty id, which used to pick the first
                // girdle or receiver in the list; placed pieces still fall back to their position.
                var id = package.data.mediaPlayerID;
                var players = list.Cast<BasePlayer>().Where(x => x).ToList();
                mp = (string.IsNullOrEmpty(id) ? null : players.FirstOrDefault(x => x.MediaPlayerID == id)) ??
                     players.FirstOrDefault(x => (x is MediaPlayerComponent || x is ReceiverComponent) &&
                                                 x.transform.position == pos);
            }

            if (mp == null)
            {
                if(OODConfig.DebugEnabled.Value) Logger.LogWarning("No player found for package type " + package.type + " at position " + pos);
                return;
            }

            Action action = package.type switch
            {
                RPCDataType.SetVideoUrl => () => mp.RPC_SetURL(package.data.url, package.data.playerStatus == PlayerStatus.Paused, package.data.time),
                RPCDataType.SetAudioUrl => () => { }, 
                RPCDataType.Stop => () => mp.Stop(true),
                RPCDataType.Pause => () => { mp.Pause(true); mp.UpdatePlayerTime(package.data.time); },
                RPCDataType.Play => () => { mp.UpdatePlayerTime(package.data.time); mp.Play(true); },
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
            var cinemaPackage = TryUnpack(package, sender);
            if (cinemaPackage != null) HandlePackageClient(cinemaPackage, sender);
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

        // Leads every package. Change it with any change to the fields below.
        private const int ProtocolVersion = 2;

        public Data data;
        public RPCDataType type;
        public MediaPlayers player;

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
            return ToZPackage();
        }

        /// <summary>Writes the fields one by one, in a fixed order.</summary>
        public ZPackage ToZPackage()
        {
            var package = new ZPackage();
            package.Write(ProtocolVersion);
            package.Write((int)type);
            package.Write((int)player);
            package.Write(data.url ?? "");
            package.Write(data.mediaPlayerID ?? "");
            package.Write(data.currentTrackTitle ?? "");
            package.Write(new Vector3(data.x, data.y, data.z));
            package.Write(data.time);
            package.Write(data.toggleBool);
            package.Write((int)data.playerStatus);
            return package;
        }

        /// <summary>Reads a package, or returns null when it is not this protocol version.</summary>
        public static CinemaPackage Unpack(ZPackage package)
        {
            if (package.ReadInt() != ProtocolVersion) return null;
            var cinemaPackage = new CinemaPackage
            {
                type = (RPCDataType)package.ReadInt(),
                player = (MediaPlayers)package.ReadInt()
            };
            var data = new Data
            {
                url = package.ReadString(),
                mediaPlayerID = package.ReadString(),
                currentTrackTitle = package.ReadString()
            };
            var pos = package.ReadVector3();
            data.x = pos.x;
            data.y = pos.y;
            data.z = pos.z;
            data.time = package.ReadSingle();
            data.toggleBool = package.ReadBool();
            data.playerStatus = (PlayerStatus)package.ReadInt();
            cinemaPackage.data = data;
            return cinemaPackage;
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