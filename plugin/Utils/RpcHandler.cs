using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.Serialization.Formatters.Binary;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.Video;
using static OdinOnDemand.Utils.CinemaPackage;
using CompressionLevel = System.IO.Compression.CompressionLevel;
using Logger = Jotunn.Logger;

namespace OdinOnDemand.Utils
{
    internal class RpcHandler
    {
        public static List<MediaPlayerComponent> mediaPlayerList;

        private static CustomRPC OODRPC;

        public void Create()
        {
            mediaPlayerList = new List<MediaPlayerComponent>();

            OODRPC = NetworkManager.Instance.AddRPC("OODRPC", OODRPCServerReceive, OODRPCClientReceive);
            if (OODConfig.DebugEnabled.Value) Logger.LogDebug("Created OODRPC");
        }

        public void SendData(RPCDataType type, MediaPlayers player, Vector3 pos, string url = "",
            PlayerStatus status = PlayerStatus.NULL, float volume = 1.0f, bool toggleBool = false, long peer = 0)
        {
            if (OODConfig.DebugEnabled.Value) Logger.LogDebug("Sending package");
            OODRPC.SendPackage(peer == 0 ? ZRoutedRpc.instance.GetServerPeerID() : peer,
                new CinemaPackage().Pack(type, player, pos, url, status, volume, toggleBool));
        }

        public static void HandleCinemaPackage(CinemaPackage package, long sender)
        {
            if ((package.type == RPCDataType.SetVideoUrl || package.type == RPCDataType.SetAudioUrl) &&
                package.data.url == "") return;
            ////////////////////////////////////////////
            /////////// RADIO RPC ////////////////////
            if (package.player == MediaPlayers.Radio)
            {
                var pos = new Vector3(package.data.x, package.data.y, package.data.z);
                var mp = mediaPlayerList.Find(x => x.transform.position == pos);
                if (mp != null)
                    /*
                    Dictionary<RPCDataType, Action<Boombox, CinemaPackage.Data>> actions = new Dictionary<RPCDataType, Action<Boombox, CinemaPackage.Data>>
                    {
                        { RPCDataType.SetVideoUrl, (b, p) => b.PlayVideo(p.url, true) },
                        { RPCDataType.SetAudioUrl, (b, p) => b.SetURL(p.url, true) },
                        { RPCDataType.Stop, (b, p) => b.Stop(true) },
                        { RPCDataType.Pause, (b, p) => b.Pause(true) },
                        { RPCDataType.Play, (b, p) => b.Play(true) },
                        { RPCDataType.SetLoop, (b, p) => b.SetLoop(p.toggleBool) }
                    };

                    actions[package.type](boombox, package.data);
                    */
                    switch (package.type)
                    {
                        case RPCDataType.SetVideoUrl:
                            mp.RPC_BoomboxPlayVideo(package.data.url);
                            break;
                        case RPCDataType.SetAudioUrl:
                            mp.RPC_SetURL(package.data.url, true);
                            break;
                        case RPCDataType.Stop:
                            mp.Stop(true);
                            break;
                        case RPCDataType.Pause:
                            mp.Pause(true);
                            break;
                        case RPCDataType.Play:
                            mp.Play(true);
                            break;
                        case RPCDataType.SetLoop:
                            mp.UIController.SetLoop(package.data.toggleBool);
                            break;
                        case RPCDataType.SetLock:
                            mp.SetLock(package.data.toggleBool);
                            break;
                        case RPCDataType.UpdateZDO:
                            mp.RPC_UpdateZDO();
                            break;
                    }
                //Jotunn.Logger.LogDebug("boombox NOT detected"); 
            }
            ////////////////////////////////////////////
            /////////// CinemaScreen RPC ////////////////////
            else if (package.player == MediaPlayers.CinemaScreen)
            {
                var pos = new Vector3(package.data.x, package.data.y, package.data.z);
                var mp = mediaPlayerList.Find(x => x.transform.position == pos);
                if (mp != null)
                {
                    var videoPlayer = mp.GetComponentInChildren<VideoPlayer>();

                    switch (package.type)
                    {
                        case RPCDataType.SetVideoUrl:
                            if (package.status == PlayerStatus.Playing)
                                mp.RPC_SetURL(package.data.url, true);
                            else
                                mp.RPC_SetURL(package.data.url, false);
                            break;
                        case RPCDataType.SetAudioUrl: return;
                        case RPCDataType.Stop:
                            mp.Stop(true);
                            break;
                        case RPCDataType.Pause:
                            mp.Pause(true);
                            break;
                        case RPCDataType.Play:
                            mp.Play(true);
                            break;
                        case RPCDataType.TrackForward:
                            mp.UIController.ToggleTrackForward();
                            break;
                        case RPCDataType.Login: // not using this
                            break;
                        case RPCDataType.SetLoop:
                            mp.UIController.SetLoop(package.data.toggleBool);
                            break;
                        case RPCDataType.SetLock:
                            mp.SetLock(package.data.toggleBool);
                            break;
                        case RPCDataType.UpdateZDO:
                            mp.RPC_UpdateZDO();
                            break;
                        case RPCDataType.SetOwner:

                            break;
                    }
                }
            }

        }

        // React to the RPC call on server
        private IEnumerator OODRPCServerReceive(long sender, ZPackage package)
        {
            Unpack(package, sender);
            if (OODConfig.DebugEnabled.Value) Logger.LogDebug("server broadcasting cinema packet to all clients");
            OODRPC.SendPackage(ZNet.instance.m_peers, package);
            yield return null;
        }

        // React to the RPC call on a client
        private IEnumerator OODRPCClientReceive(long sender, ZPackage package)
        {
            Unpack(package, sender);
            
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
            Speaker = 3
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
            Login = 9005,
            TrackForward = 9006,
            SetLoop = 9007,
            SetVolume = 9008,
            SetLock = 9009,
            UpdateZDO = 9010,
            SetOwner = 9011
        }

        public Data data;
        public RPCDataType type;
        public MediaPlayers player;
        public PlayerStatus status;

        protected virtual void Prepare(RPCDataType type, MediaPlayers player, Data data)
        {
            this.type = type;
            this.player = player;
            this.data = data;
        }

        protected virtual void AfterUnpack(object obj, long sender)
        {
            var cinema = obj as CinemaPackage;
            RpcHandler.HandleCinemaPackage(cinema, sender);
            if (OODConfig.DebugEnabled.Value)
                Logger.LogDebug("** RECIEVED CINEMA RPC ** LOGGING INFO: " +
                                "\n url: " + cinema.data.url +
                                "\n type: " + cinema.type +
                                "\n mp: " + cinema.player +
                                "\n pos: " + cinema.data.x + ", " + cinema.data.y + ", " + cinema.data.z);
        }

        public ZPackage Pack(RPCDataType type, MediaPlayers player, Vector3 pos, string url,
            PlayerStatus status = PlayerStatus.NULL, float volume = 1f, bool toggleBool = false)
        {
            if (OODConfig.DebugEnabled.Value)
                Logger.LogDebug("Recieved request to pack data type of " + type + " with url of " + url);

            var data = new Data();
            data.url = url;
            data.x = pos.x;
            data.y = pos.y;
            data.z = pos.z;
            data.volume = volume;
            data.toggleBool = toggleBool;

            Prepare(type, player, data);
            var zpackage = new ZPackage();
            using (var memoryStream = new MemoryStream())
            {
                using (var gzipStream = new GZipStream(memoryStream, CompressionLevel.Optimal))
                {
                    new BinaryFormatter().Serialize(gzipStream, this);
                }

                var array = memoryStream.ToArray();
                if (OODConfig.DebugEnabled.Value)
                    Logger.LogDebug(string.Format("Serialized size: {0} bytes", array.Length));
                zpackage.Write(array);
            }

            return zpackage;
        }

        public static void Unpack(ZPackage package, long sender)
        {
            var array = package.ReadByteArray();
            if (OODConfig.DebugEnabled.Value)
                Logger.LogDebug(string.Format("Deserializing package size: {0} bytes", array.Length));
            using (var memoryStream = new MemoryStream(array))
            {
                using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress, true))
                {
                    var obj = new BinaryFormatter().Deserialize(gzipStream);
                    if (obj is CinemaPackage compressedPackage) compressedPackage.AfterUnpack(obj, sender);
                }
            }
        }

        [Serializable]
        public struct Data
        {
            public string url;
            public float x;
            public float y;
            public float z;
            public float volume;
            public bool toggleBool;
        }
    }
}