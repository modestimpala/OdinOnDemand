using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using OdinOnDemand.Utils.Config;
using SoundCloudExplode;
using UnityEngine;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Playlists;
using YoutubeExplode.Videos.Streams;
using Logger = Jotunn.Logger;

namespace OdinOnDemand.Utils.Net.Explode
{
    public class URLGrab
    {
        public bool FailBool { get; private set; }
        public bool LoadingBool { get; private set; }
        public string StatusMessage { get; private set; }
        private CancellationTokenSource Source { get; } = new CancellationTokenSource();
        
        public void Reset()
        {
            FailBool = false;
            LoadingBool = false;
        }
        
        public IEnumerator GetYoutubeExplodeCoroutine(string url, Action<string> callback)
        {
            if (!OODConfig.IsYtEnabled.Value)
            {
                Logger.LogWarning("A youtube link was attempted to be played, but youtube is disabled in the config. Returning null.");
                callback(null);
                yield break;
            }

            LoadingBool = true;
            var youtube = new YoutubeClient();

            var manifestTask = Async.GetYoutubeManifestAsync(youtube, url, Source.Token);

            // Wait for the task to complete.
            yield return new WaitUntil(() => manifestTask.IsCompleted);

            // Check the result of the task.
            var vidManifest = manifestTask.Result;
            if (vidManifest == null)
            {
                // The result is null, indicating an exception was caught.
                Logger.LogError("Failed to get video manifest, check for exceptions");
                LoadingBool = false;
                FailBool = true;
                callback(null);
                yield break;
            }

            // Process the result if no exception was caught.
            var vidInfo = vidManifest.GetMuxedStreams().GetWithHighestVideoQuality();
            var youtubeExplodeURL = CleanUrl(vidInfo.Url);
            LoadingBool = false;
            callback(youtubeExplodeURL?.AbsoluteUri);
        }
        

        public IEnumerator GetSoundcloudExplodeCoroutine(Uri url, Action<Uri, Uri> callback)
        {
            var urlString = url.AbsoluteUri;
            LoadingBool = true;
            var soundcloud = new SoundCloudClient();
            var trackTask = Async.GetSoundCloudTrackAsync(soundcloud, urlString, Source.Token);

            // Wait for the task to complete.
            yield return new WaitUntil(() => trackTask.IsCompleted);

            // Check the result of the task.
            var soundCloudTrack = trackTask.Result;
            if (soundCloudTrack == null)
            {
                // The result is null, indicating an exception was caught.
                Logger.LogError("Failed to get download URL, check for exceptions");
                LoadingBool = false;
                FailBool = true;
                callback(null, null);
                yield break;
            }
            
            var urlTask = Async.GetSoundCloudTrackUrlAsync(soundcloud, soundCloudTrack, Source.Token);
            yield return new WaitUntil(() => urlTask.IsCompleted);
            var soundCloudUrl = urlTask.Result;
            
            if (soundCloudUrl == null)
            {
                // The result is null, indicating an exception was caught.
                Logger.LogError("Failed to get download URL, check for exceptions");
                LoadingBool = false;
                FailBool = true;
                callback(null, null);
                yield break;
            }
            var soundCloudUri = CleanUrl(soundCloudUrl);
            var soundCloudArt = soundCloudTrack.ArtworkUrl;
            // Process the result if no exception was caught.
            LoadingBool = false;
            callback(soundCloudUri, soundCloudArt);
        }
        
        public IEnumerator GetYouTubePlaylistCoroutine(string url, Action<List<VideoInfo>> callback)
        {
            LoadingBool = true;
            var youtube = new YoutubeClient();
            var playlistTask = Async.GetPlaylistVideoStreamsAsync(youtube, url, Source.Token);

            // Wait for the task to complete
            yield return new WaitUntil(() => playlistTask.IsCompleted);

            if (playlistTask.IsFaulted)
            {
                // Handle error
                Logger.LogError($"Failed to get playlist: {playlistTask.Exception}");
                FailBool = true;
                LoadingBool = false;
                callback(null);
                yield break;
            }

            var playlist = playlistTask.Result;
            if (playlist == null)
            {
                // Handle null result
                FailBool = true;
                LoadingBool = false;
                callback(null);
            }
            else
            {
                var videoInfos = new List<VideoInfo>();
                foreach (var video in playlist)
                {
                    var newUrl = video.Url.Substring(0, video.Url.IndexOf("&", StringComparison.Ordinal));
                    var videoInfo = new VideoInfo
                    {
                        Title = video.Title,
                        Duration = video.Duration,
                        Url = newUrl,
                        thumbnail = video.Thumbnails.GetWithHighestResolution()
                    };
                    videoInfos.Add(videoInfo);
                }

                FailBool = false;
                LoadingBool = false;
                callback(videoInfos);
            }
        }
        
        public Uri CleanUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri cleanUri))
            {
                Logger.LogError("Invalid URI: " + url);
                return null;
            }

            var queryIndex = cleanUri.Query.IndexOf('&');
            if (cleanUri.AbsolutePath.Contains("/watch") && queryIndex > -1)
            {
                url = cleanUri.GetLeftPart(UriPartial.Path) + cleanUri.Query.Substring(0, queryIndex);
                Uri.TryCreate(url, UriKind.Absolute, out cleanUri);
            }
            
            return cleanUri;
        }

        
        public string GetRelativeURL(string url)
        {
            string relativeURL = "";

            if (url.Contains("local:\\\\"))
            {
                relativeURL = Path.Combine(Paths.PluginPath, url).Replace("local:\\\\", "");
            }
            else if (url.Contains("local://"))
            {
                relativeURL = Path.Combine(Paths.PluginPath, url).Replace("local://", "");
            }

            if (File.Exists(relativeURL))
            {
                return relativeURL;
            }

            return "";
        }
        
        public bool IsAudioFile(string url)
        {
            return url.Contains(".mp3") || url.Contains(".wav") || url.Contains(".ogg") || url.Contains(".flac") ||
                   url.Contains(".m4a") || url.Contains(".aac") || url.Contains(".aif");
        }
    }

    public class VideoInfo
    {
        public string Title { get; set; }
        public TimeSpan? Duration { get; set; }
        public string Url { get; set; }
        public Thumbnail thumbnail { get; set; }
    }
}