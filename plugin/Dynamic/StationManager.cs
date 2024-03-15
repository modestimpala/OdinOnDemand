using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using Jotunn;
using OdinOnDemand.Utils.Net;
using UnityEngine;
using Logger = Jotunn.Logger;

namespace OdinOnDemand.Dynamic
{
    public class StationManager : MonoBehaviour
    {
        public List<DynamicStation> DynamicStations { get; set; }
        public static StationManager Instance { get; private set; }

        private static readonly RpcHandler RPCHandler = OdinOnDemandPlugin.RPCHandlers;

        public void Awake() 
        {
            DynamicStations = new List<DynamicStation>();
            Instance = this;
            SetupStations();
            Jotunn.Managers.PieceManager.OnPiecesRegistered += StartStationPlayback;
        }

        private void StartStationPlayback()
        {
            if(ZNet.instance.IsDedicated() || ZNet.instance.IsServer())
            {
                foreach (var station in DynamicStations)
                {
                    StartCoroutine(SimulateStationPlayback(station));
                }
            }
        }

        public void SetCurrentTrackIndex(string stationName, int trackIndex)
        {
            Instance.DynamicStations.Find(x => x.Title == stationName).CurrentTrackIndex = trackIndex;
        }

        public void AddStation(DynamicStation station)
        {
            DynamicStations.Add(station);
        }
        public void RemoveStation(DynamicStation station)
        {
            DynamicStations.Remove(station);
        }
        public void RemoveStation(string stationName)
        {
            DynamicStations.RemoveAll(x => x.Title == stationName);
        }
        public DynamicStation GetStation(string stationName)
        {
            return DynamicStations.Find(x => x.Title == stationName);
        }
        public void ClearStations()
        {
            DynamicStations.Clear();
        }
        
        private void SetupStations()
        {
            //find all asset bundles in radio folder subfolder from bepinex plugin folder
            var pluginPath = Assembly.GetExecutingAssembly().Location.Replace("OdinOnDemand.dll", "");
            foreach (var assetBundlePath in System.IO.Directory.GetFiles(pluginPath + "assetbundles"))
            {
                var assetBundle = AssetBundle.LoadFromFile(assetBundlePath);
                AddStation(AudioLoader.CreateFromAssetBundle(assetBundle));
            }
            //find all audio clips in radio folder subfolder from bepinex plugin folder
            foreach (var folderPath in System.IO.Directory.GetDirectories(pluginPath + "radio"))
            {
                StartCoroutine(AudioLoader.CreateFromFolderCoroutine(folderPath, (station) =>
                {
                    if (station != null)
                    {
                        AddStation(station);
                    }
                }));
            }
            //check for radio stations in the plugin folder, ood addons
            var pluginFolder = Paths.PluginPath;
            if (Directory.Exists(pluginFolder))
            {
                foreach(var folder in Directory.GetDirectories(pluginFolder))
                {
                    // Check if the folder is a radio station
                    var radioTagPath = Path.Combine(folder, "odinondemand.txt");
                    if(!File.Exists(radioTagPath)) continue;
                    var assetBundleFolder = Path.Combine(folder, "assetbundles");
                    if (Directory.Exists(assetBundleFolder))
                    {
                        var assetBundles = Directory.GetFiles(assetBundleFolder);
                        foreach (var assetBundlePath in assetBundles)
                        {
                            var assetBundle = AssetBundle.LoadFromFile(assetBundlePath);
                            if(!assetBundle) continue;
                            Logger.LogInfo("Found radio station addon: " + assetBundle.name);
                            AddStation(AudioLoader.CreateFromAssetBundle(assetBundle));
                        }
                    }
                    var audioFiles = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories)
                        .Where(s => s.EndsWith(".ogg") || s.EndsWith(".wav") || s.EndsWith(".mp3") || s.EndsWith(".flac"));
                    
                    var titlePath = Path.Combine(folder, "title.txt");
                    if(!File.Exists(titlePath) && !audioFiles.Any()) continue;
                   
                    StartCoroutine(AudioLoader.CreateFromFolderCoroutine(folder, (station) =>
                    {
                        if (station != null)
                        {
                            Logger.LogInfo("Found radio station addon: " + station.Title);
                            AddStation(station);
                        }
                    }));
                    
                }
            }
        }
        private IEnumerator SimulateStationPlayback(DynamicStation station)
        {
            while (true)
            {
                for (station.CurrentTrackIndex = 0; station.CurrentTrackIndex < station.Tracks.Count; station.CurrentTrackIndex++)
                {
                    if (!ZNet.instance) yield break;
                    if (!ZNet.instance.IsClientInstance() && !ZNet.instance.IsServer()) yield break;
                    
                    var track = station.Tracks[station.CurrentTrackIndex];
                    track.CurrentTime = 0;
            
                    // Simulate track playback
                    while (track.CurrentTime < track.TrackLength)
                    {
                        track.CurrentTime += Time.deltaTime;
                        yield return null;
                    }

                    track.CurrentTime = 0; // Reset for the next play if needed
                }

                // If shuffle is enabled, shuffle the tracks at the end of the playlist
                if (station.Shuffle)
                {
                    ShuffleTracks(station.Tracks);
                }

                // Check for player presence and network connection, break if conditions are not met
                
            }
        }

        
        private void ShuffleTracks(List<Track> tracks)
        {
            for (int i = tracks.Count - 1; i > 0; i--)
            {
                int randomIndex = Random.Range(0, i + 1);
                (tracks[i], tracks[randomIndex]) = (tracks[randomIndex], tracks[i]);
            }
        }

        
    }
    
    public class DynamicStation
    {
        public string Title { get; set; }
        public AssetBundle AssetBundle { get; set; }
        public Sprite Thumbnail { get; set; }
        public List<Track> Tracks { get; set; }
        public int CurrentTrackIndex { get; set; }
        public bool Shuffle { get; set; }
        
    }

    public class Track
    {
        public string Title { get; set; }
        public AudioClip AudioClip { get; set; }
        public string FilePath { get; set; }
        public float TrackLength { get; set; }
        public float CurrentTime { get; set; } = 0;
    }
    
}