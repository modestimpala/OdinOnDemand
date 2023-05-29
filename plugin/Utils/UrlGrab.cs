using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jotunn;
using SoundCloudExplode;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Playlists;
using YoutubeExplode.Videos.Streams;

namespace OdinOnDemand.Utils
{
    internal class URLGrab
    {
        public bool FailBool { get; private set; }
        public bool LoadingBool { get; private set; }
        public string StatusMessage { get; private set; }

        public void Reset()
        {
            FailBool = false;
            LoadingBool = false;
        }
        
        public async Task<string> GetYoutubeExplode(string url)
        {
            if (!OODConfig.IsYtEnabled.Value)
            {
                LoadingBool = false;
                return "YT DISABLED";
            }

            return await ExecuteWithTimeout<string>(async cancellationToken =>
            {

                var youtube = new YoutubeClient();

                var vidManifest = await youtube.Videos.Streams.GetManifestAsync(url).ConfigureAwait(false);
                var vidInfo = vidManifest.GetMuxedStreams().GetWithHighestVideoQuality();
                var youtubeExplodeURL = CleanUrl(vidInfo.Url);
                if (youtubeExplodeURL != null)
                {
                    LoadingBool = false;
                    return youtubeExplodeURL.AbsoluteUri;
                }
                else
                {
                    Logger.LogDebug("YouTubeExplode download url is null");
                    LoadingBool = false;
                    FailBool = true;

                    return null;
                }
                
            }, OODConfig.YouTubeExplodeTimeout.Value, "YouTubeExplode");
        }
        public async Task<Uri> GetSoundcloudExplode(string url)
        {
            return await ExecuteWithTimeout<Uri>(async cancellationToken =>
            {
                var soundcloud = new SoundCloudClient();
                var track = await soundcloud.Tracks.GetAsync(url);
                var downloadUrl = await soundcloud.Tracks.GetDownloadUrlAsync(track);

                return CleanUrl(downloadUrl);
            }, OODConfig.SoundCloudExplodeTimeout.Value, "SoundCloudExplode");
        }

        public async Task<List<VideoInfo>> GetYouTubePlaylist(string url)
        {
            try
            {
                var source = new CancellationTokenSource();
                var cancellationToken = source.Token;
                FailBool = false;
                LoadingBool = true;
                StatusMessage = "Loading Playlist";
                var timeout = 30000;
                var thing = YouTubeExplodeGetPlaylist(url, cancellationToken);
                if (await Task.WhenAny(thing, Task.Delay(timeout, cancellationToken)) == thing)
                {
                    var result = thing.Result;
                    if (result != null)
                    {
                        var info = new List<VideoInfo>();
                        result.ToList().ForEach(x =>
                        {
                            var playlist = new VideoInfo();
                            playlist.Title = x.Title;
                            playlist.Duration = x.Duration;
                            var newUrl = x.Url.Substring(0, x.Url.IndexOf("&"));
                            playlist.Url = newUrl;
                            playlist.thumbnail = x.Thumbnails.GetWithHighestResolution();
                            info.Add(playlist);
                        });

                        FailBool = false;
                        LoadingBool = false;
                        StatusMessage = "Success";
                        return info;
                    }

                    Logger.LogDebug("Playlist info is null");
                    FailBool = true;
                    LoadingBool = false;
                    StatusMessage = "Null";
                    return null;
                }

                FailBool = true;
                LoadingBool = false;
                StatusMessage = "Timeout";
                return null;
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex);
                FailBool = true;
                LoadingBool = false;
                StatusMessage = "Error";
                return null;
            }
        }

        public async Task<IReadOnlyList<PlaylistVideo>> YouTubeExplodeGetPlaylist(string url,
            CancellationToken cancellationToken)
        {
            var youtube = new YoutubeClient();

            return await youtube.Playlists.GetVideosAsync(url);
        }
        
        private async Task<T> ExecuteWithTimeout<T>(Func<CancellationToken, Task<T>> operation, int timeout, string operationName)
        {
            var source = new CancellationTokenSource();
            var cancellationToken = source.Token;

            try
            {
                FailBool = false;
                LoadingBool = true;
                StatusMessage = $"Loading {operationName}";

                var task = operation(cancellationToken);

                if (await Task.WhenAny(task, Task.Delay(timeout, cancellationToken)) == task)
                {
                    // task completed within timeout.
                    // re-await the task so that any exceptions/cancellation is rethrown.
                    T result = await task;
                    FailBool = false;
                    LoadingBool = false;
                    StatusMessage = "Success";
                    return result;
                }

                FailBool = true;
                LoadingBool = false;
                StatusMessage = "Timeout";
                return default(T);
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex);
                FailBool = true;
                LoadingBool = false;
                StatusMessage = "Error";
                return default(T);
            }
        }
        public Uri CleanUrl(string url)
        {
            Uri cleanUri;
            if (Uri.TryCreate(url, UriKind.Absolute, out cleanUri))
                return cleanUri;
            if (OODConfig.DebugEnabled.Value) Logger.LogDebug("Invalid URI: " + url);
            return null;
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