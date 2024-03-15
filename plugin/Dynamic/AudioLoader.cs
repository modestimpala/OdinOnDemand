using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using UnityEngine;
using UnityEngine.Networking;
using Logger = Jotunn.Logger;

namespace OdinOnDemand.Dynamic
{
    public static class AudioLoader
    {
        public static DynamicStation CreateFromAssetBundle(AssetBundle assetBundle)
        {
            var audioClips = assetBundle.LoadAllAssets<AudioClip>();
            var title = assetBundle.LoadAsset<TextAsset>("title");
            if (title != null)
            {
                var radioStationName = title.text;
                var trackList = new List<Track>();
                foreach (var audioClip in audioClips)
                {
                    var track = new Track
                    {
                        Title = audioClip.name,
                        AudioClip = audioClip,
                        TrackLength = audioClip.length
                    };
                    trackList.Add(track);
                }

                var thumbnail = assetBundle.LoadAsset<Sprite>("thumbnail");
                bool shuffle = assetBundle.LoadAsset<TextAsset>("shuffle");
                var station = new DynamicStation
                {
                    Title = radioStationName,
                    Thumbnail = thumbnail,
                    AssetBundle = assetBundle,
                    Shuffle = shuffle,
                    Tracks = trackList
                };

                return station;
            }

            return null;
        }

        public static IEnumerator CreateFromFolderCoroutine(string folderPath, Action<DynamicStation> callback)
        {
            var audioFiles = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
                .Where(s => s.EndsWith(".ogg") || s.EndsWith(".wav") || s.EndsWith(".mp3") || s.EndsWith(".flac"));

            var titlePath = Path.Combine(folderPath, "title.txt");
            if (File.Exists(titlePath))
            {
                var radioStationName = File.ReadAllText(titlePath);
                var trackList = new List<Track>();
                foreach (var filePath in audioFiles)
                {
                    if (filePath.Contains("#"))
                    {
                        Logger.LogError("File path contains illegal character # for " + filePath);
                        continue;
                    }
                    using var www = UnityWebRequestMultimedia.GetAudioClip("file://" + filePath, AudioType.UNKNOWN);
                    yield return www.SendWebRequest();

                    if (www.result == UnityWebRequest.Result.Success)
                    {
                        var audioClip = DownloadHandlerAudioClip.GetContent(www);
                        var track = new Track
                        {
                            Title = Path.GetFileNameWithoutExtension(filePath),
                            AudioClip = audioClip,
                            FilePath = filePath,
                            TrackLength = audioClip.length
                        };
                        trackList.Add(track);
                    }
                    else
                    {
                        Logger.LogError(www.error + "from " + www.url);
                    }
                }
                Sprite sprite = null;
                string[] imageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".gif" };
                foreach (var extension in imageExtensions)
                {
                    var thumbnailPath = Path.Combine(folderPath, "thumbnail" + extension);
                    if (File.Exists(thumbnailPath))
                    {
                        var bytes = File.ReadAllBytes(thumbnailPath);
                        var texture = new Texture2D(2, 2);
                        texture.LoadImage(bytes);
                        sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                            new Vector2(0.5f, 0.5f));
                        break;
                    }
                }

                var shuffle = File.Exists(Path.Combine(folderPath, "shuffle.txt"));
                var station = new DynamicStation
                {
                    Title = radioStationName,
                    Thumbnail = sprite,
                    AssetBundle = null,
                    Shuffle = shuffle,
                    Tracks = trackList
                };

                callback(station);
            }
            else
            {
                callback(null);
            }
        }
    }
}