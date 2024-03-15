using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Jotunn;
using SoundCloudExplode;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Playlists;
using YoutubeExplode.Videos.Streams;

namespace OdinOnDemand.Utils.Net.Explode
{
    public static class Async
    {
        [ItemCanBeNull]
        private static async Task<T> ExecuteSafeAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken token)
        {
            try
            {
                return await operation(token);
            }
            catch (Exception ex)
            {
                if (ex is TaskCanceledException)
                {
                    Logger.LogInfo($"Task Canceled");
                    Logger.LogDebug(ex);
                    return default; // Return the default value for type T if the task is canceled.
                }

                Logger.LogError($"Exception caught in ExecuteSafeAsync: {ex}");
                return default; // Return the default value for type T if an exception occurs.
            }
        }
        
        public static async Task<StreamManifest> GetYoutubeManifestAsync(YoutubeClient youtube, string url, CancellationToken token)
        {
            return await ExecuteSafeAsync(async (cancellationToken) => 
                await youtube.Videos.Streams.GetManifestAsync(url, cancellationToken), token);
        }

        [ItemCanBeNull]
        public static async Task<SoundCloudExplode.Tracks.Track> GetSoundCloudTrackAsync(SoundCloudClient soundcloud, string url, CancellationToken token)
        {
            return await ExecuteSafeAsync(async (cancellationToken) => 
                await soundcloud.Tracks.GetAsync(url, cancellationToken), token);
        }
        
        [ItemCanBeNull]
        public static async Task<string> GetSoundCloudTrackUrlAsync(SoundCloudClient soundcloud, SoundCloudExplode.Tracks.Track track, CancellationToken token)
        {
            return await ExecuteSafeAsync(async (cancellationToken) => 
                await soundcloud.Tracks.GetDownloadUrlAsync(track, cancellationToken), token);
        }
        
        [ItemCanBeNull]
        public static async Task<IReadOnlyList<PlaylistVideo>> GetPlaylistVideoStreamsAsync(YoutubeClient youtube, string url, CancellationToken token)
        {
            return await ExecuteSafeAsync(async (cancellationToken) => 
                await youtube.Playlists.GetVideosAsync(url, cancellationToken), token);
        }
 
    }
}