using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;
using Logger = Jotunn.Logger;

namespace OdinOnDemand.MPlayer
{
    /// <summary>
    ///     Feeds LibVLC from bounded HTTP range requests instead of one open-ended GET.
    ///     YouTube throttles an open-ended stream to roughly its own bitrate, which leaves no
    ///     headroom: the decoder starves on the first hiccup and playback stalls for seconds while
    ///     it refills. Measured on the same stream, an open-ended request sustains about
    ///     2 Mbit/s while bounded chunk requests reach 8 Mbit/s, so reading in chunks keeps the
    ///     buffer ahead of playback. LibVLC pulls through these callbacks on its own input thread,
    ///     so blocking here is expected and never touches the Unity thread.
    /// </summary>
    internal sealed class ChunkedHttpMediaInput : MediaInput
    {
        // The first request gates the first frame, so it stays small and the size grows from
        // there: large chunks are what buy the throughput headroom, but only after playback
        // has started.
        private const int FirstChunkSize = 256 * 1024;
        private const int MaxChunkSize = 4 * 1024 * 1024;
        private const int RequestTimeoutMs = 30000;
        private const int MaxAttempts = 3;

        private readonly string url;
        private readonly IDictionary<string, string> headers;

        private byte[] chunk;
        private long chunkStart;
        private int chunkLength;
        private int chunkSize = FirstChunkSize;
        private long position;
        private long totalLength = -1;

        public ChunkedHttpMediaInput(string url, IDictionary<string, string> headers)
        {
            this.url = url;
            this.headers = headers;
        }

        public override bool Open(out ulong size)
        {
            size = 0;
            try
            {
                // The first chunk doubles as the length probe, so playback starts on one request.
                if (!FetchChunk(0)) return false;
                if (totalLength < 0) return false;

                position = 0;
                size = (ulong)totalLength;
                return true;
            }
            catch (Exception exception)
            {
                Logger.LogWarning($"Could not open the media stream: {exception.Message}");
                return false;
            }
        }

        public override int Read(IntPtr buf, uint len)
        {
            if (len == 0) return 0;

            try
            {
                if (totalLength >= 0 && position >= totalLength) return 0;
                if (!EnsureChunkContains(position)) return -1;

                var offsetInChunk = (int)(position - chunkStart);
                var available = chunkLength - offsetInChunk;
                if (available <= 0) return 0;

                var count = Math.Min(available, (int)len);
                Marshal.Copy(chunk, offsetInChunk, buf, count);
                position += count;
                return count;
            }
            catch (Exception exception)
            {
                Logger.LogWarning($"Media stream read failed: {exception.Message}");
                return -1;
            }
        }

        public override bool Seek(ulong offset)
        {
            var target = (long)offset;
            if (totalLength >= 0 && target > totalLength) return false;

            position = target;
            return true;
        }

        public override void Close()
        {
            chunk = null;
            chunkLength = 0;
            chunkStart = 0;
            chunkSize = FirstChunkSize;
            position = 0;
        }

        private bool EnsureChunkContains(long offset)
        {
            if (chunk != null && offset >= chunkStart && offset < chunkStart + chunkLength) return true;
            return FetchChunk(offset);
        }

        private bool FetchChunk(long offset)
        {
            var last = offset + chunkSize - 1;
            if (totalLength >= 0 && last > totalLength - 1) last = totalLength - 1;
            if (last < offset) return false;

            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                try
                {
                    var request = (HttpWebRequest)WebRequest.Create(url);
                    request.Method = "GET";
                    request.Timeout = RequestTimeoutMs;
                    request.ReadWriteTimeout = RequestTimeoutMs;
                    request.AllowAutoRedirect = true;
                    request.KeepAlive = true;
                    ApplyHeaders(request);
                    request.AddRange(offset, last);

                    using (var response = (HttpWebResponse)request.GetResponse())
                    using (var stream = response.GetResponseStream())
                    {
                        if (stream == null) return false;

                        ReadTotalLength(response);

                        var expected = (int)(last - offset + 1);
                        var buffer = chunk != null && chunk.Length >= expected ? chunk : new byte[expected];
                        var read = 0;
                        while (read < expected)
                        {
                            var got = stream.Read(buffer, read, expected - read);
                            if (got <= 0) break;
                            read += got;
                        }

                        if (read <= 0) return false;

                        chunk = buffer;
                        chunkStart = offset;
                        chunkLength = read;
                        if (chunkSize < MaxChunkSize) chunkSize = Math.Min(chunkSize * 4, MaxChunkSize);
                        return true;
                    }
                }
                catch (WebException exception)
                {
                    Logger.LogWarning(
                        $"Media chunk {offset}-{last} failed (attempt {attempt}/{MaxAttempts}): {exception.Message}");
                    if (attempt == MaxAttempts) return false;
                }
            }

            return false;
        }

        private void ReadTotalLength(HttpWebResponse response)
        {
            if (totalLength >= 0) return;

            // "bytes <start>-<end>/<total>"
            var contentRange = response.Headers["Content-Range"];
            if (!string.IsNullOrEmpty(contentRange))
            {
                var slash = contentRange.LastIndexOf('/');
                long parsed;
                if (slash >= 0 && long.TryParse(contentRange.Substring(slash + 1), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out parsed))
                {
                    totalLength = parsed;
                    return;
                }
            }

            if (response.StatusCode == HttpStatusCode.OK && response.ContentLength >= 0)
                totalLength = response.ContentLength;
        }

        private void ApplyHeaders(HttpWebRequest request)
        {
            // Replay the extractor's headers; YouTube ties stream URLs to the requesting client.
            request.UserAgent =
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
                "Chrome/145.0.0.0 Safari/537.36";
            if (headers == null) return;

            foreach (var header in headers)
            {
                var name = header.Key;
                if (name.Equals("Range", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Connection", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Accept-Encoding", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (name.Equals("User-Agent", StringComparison.OrdinalIgnoreCase)) request.UserAgent = header.Value;
                else if (name.Equals("Accept", StringComparison.OrdinalIgnoreCase)) request.Accept = header.Value;
                else if (name.Equals("Referer", StringComparison.OrdinalIgnoreCase)) request.Referer = header.Value;
                else request.Headers[name] = header.Value;
            }
        }
    }
}
