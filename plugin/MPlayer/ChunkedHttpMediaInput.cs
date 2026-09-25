using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
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
    ///     The next chunk downloads in the background while the current one is read. LibVLC only
    ///     caches about 300 ms ahead of a callback input, so fetching on demand stalled playback
    ///     (and dropped its audio) for as long as a 4 MB request took on a slow connection.
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
        private const int RetryDelayMs = 500;

        private readonly string url;
        private readonly IDictionary<string, string> headers;
        private readonly string urlDescription;

        /// <summary>
        ///     Set once a read the player needed has failed for good. LibVLC does not report a
        ///     failing callback input, it stalls, so the decoder polls this instead.
        /// </summary>
        public volatile bool Failed;

        /// <summary>True when the server refused the URL itself (403/410): only a new extraction helps.</summary>
        public volatile bool Rejected;

        private byte[] chunk;
        private long chunkStart;
        private int chunkLength;
        private int chunkSize = FirstChunkSize;
        private long position;
        private long totalLength = -1;

        // Background download of the chunk after the current one, and a buffer to reuse for it.
        private Prefetch prefetch;
        private byte[] spareBuffer;

        private sealed class Prefetch
        {
            public long Offset;
            public int Size;
            public Task<ChunkData> Download;
            public CancellationTokenSource Cancellation;
        }

        private sealed class ChunkData
        {
            public byte[] Buffer;
            public int Length;
        }

        public ChunkedHttpMediaInput(string url, IDictionary<string, string> headers)
        {
            this.url = url;
            this.headers = headers;
            urlDescription = DescribeUrl(url);
        }

        /// <summary>
        ///     The googlevideo parameters that explain a rejection: the client the URL was issued
        ///     to (c=), the time left before it expires, and whether it only works from one IP.
        /// </summary>
        private static string DescribeUrl(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || string.IsNullOrEmpty(uri.Query)) return "";
            string client = null, expire = null;
            var ipBound = false;
            foreach (var pair in uri.Query.TrimStart('?').Split('&'))
            {
                var equals = pair.IndexOf('=');
                var name = equals < 0 ? pair : pair.Substring(0, equals);
                var value = equals < 0 ? "" : Uri.UnescapeDataString(pair.Substring(equals + 1));
                if (name == "c") client = value;
                else if (name == "expire") expire = value;
                else if (name == "ip") ipBound = true;
            }
            if (client == null && expire == null) return "";

            var parts = new List<string>();
            if (client != null) parts.Add("client " + client);
            long expireUnix;
            if (expire != null && long.TryParse(expire, NumberStyles.Integer, CultureInfo.InvariantCulture, out expireUnix))
            {
                var left = DateTimeOffset.FromUnixTimeSeconds(expireUnix) - DateTimeOffset.UtcNow;
                parts.Add(left.TotalSeconds > 0 ? $"expires in {(int)left.TotalMinutes} min" : "expired");
            }
            if (ipBound) parts.Add("bound to the extracting IP");
            return " [" + string.Join(", ", parts.ToArray()) + "]";
        }

        public override bool Open(out ulong size)
        {
            size = 0;
            try
            {
                // The first chunk doubles as the length probe, so playback starts on one request.
                var first = Download(0, chunkSize, null, true, CancellationToken.None);
                if (first == null || totalLength < 0)
                {
                    Failed = true;
                    return false;
                }
                UseChunk(0, first);

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
            CancelPrefetch();
            spareBuffer = null;
            chunk = null;
            chunkLength = 0;
            chunkStart = 0;
            chunkSize = FirstChunkSize;
            position = 0;
        }

        private bool EnsureChunkContains(long offset)
        {
            if (chunk != null && offset >= chunkStart && offset < chunkStart + chunkLength) return true;

            var pending = prefetch;
            prefetch = null;
            if (pending != null && offset >= pending.Offset && offset < pending.Offset + pending.Size)
            {
                // Usually already finished; otherwise this waits on the part still in flight.
                var data = pending.Download.Result;
                pending.Cancellation.Dispose();
                if (data != null && offset < pending.Offset + data.Length)
                {
                    UseChunk(pending.Offset, data);
                    return true;
                }
            }
            else if (pending != null)
            {
                Cancel(pending);
            }

            var fetched = Download(offset, chunkSize, TakeSpareBuffer(), false, CancellationToken.None);
            if (fetched == null)
            {
                Failed = true;
                return false;
            }
            UseChunk(offset, fetched);
            return true;
        }

        private void UseChunk(long offset, ChunkData data)
        {
            if (chunk != null && chunk != data.Buffer) spareBuffer = chunk;
            chunk = data.Buffer;
            chunkStart = offset;
            chunkLength = data.Length;
            if (chunkSize < MaxChunkSize) chunkSize = Math.Min(chunkSize * 4, MaxChunkSize);
            StartPrefetch(chunkStart + chunkLength);
        }

        private void StartPrefetch(long offset)
        {
            if (totalLength >= 0 && offset >= totalLength) return;

            var size = chunkSize;
            var buffer = TakeSpareBuffer();
            var cancellation = new CancellationTokenSource();
            prefetch = new Prefetch
            {
                Offset = offset,
                Size = size,
                Cancellation = cancellation,
                Download = Task.Run(() => Download(offset, size, buffer, false, cancellation.Token))
            };
        }

        private void CancelPrefetch()
        {
            var pending = prefetch;
            prefetch = null;
            if (pending != null) Cancel(pending);
        }

        private static void Cancel(Prefetch pending)
        {
            pending.Cancellation.Cancel();
            // Dispose once the aborted request has unwound; it still holds the token.
            pending.Download.ContinueWith(_ => pending.Cancellation.Dispose(), TaskScheduler.Default);
        }

        private byte[] TakeSpareBuffer()
        {
            var buffer = spareBuffer;
            spareBuffer = null;
            return buffer;
        }

        /// <summary>
        ///     Downloads [offset, offset + size) clamped to the media length. Returns null on failure
        ///     or cancellation. Only the synchronous first request records the total length, so the
        ///     background download never writes shared state.
        /// </summary>
        private ChunkData Download(long offset, int size, byte[] buffer, bool readLength,
            CancellationToken cancellation)
        {
            var last = offset + size - 1;
            if (totalLength >= 0 && last > totalLength - 1) last = totalLength - 1;
            if (last < offset) return null;

            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                if (cancellation.IsCancellationRequested) return null;
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

                    using (cancellation.Register(request.Abort))
                    using (var response = (HttpWebResponse)request.GetResponse())
                    using (var stream = response.GetResponseStream())
                    {
                        if (stream == null) return null;

                        if (readLength) ReadTotalLength(response);

                        var expected = (int)(last - offset + 1);
                        if (buffer == null || buffer.Length < expected) buffer = new byte[expected];
                        var read = 0;
                        while (read < expected)
                        {
                            var got = stream.Read(buffer, read, expected - read);
                            if (got <= 0) break;
                            read += got;
                        }

                        if (read <= 0 || cancellation.IsCancellationRequested) return null;
                        return new ChunkData { Buffer = buffer, Length = read };
                    }
                }
                catch (Exception exception) when (exception is WebException || exception is IOException)
                {
                    if (cancellation.IsCancellationRequested) return null;
                    var status = ((exception as WebException)?.Response as HttpWebResponse)?.StatusCode;
                    // A refused URL fails the same way on every retry, and each retry delays the
                    // new extraction that can actually fix it.
                    var refused = status == HttpStatusCode.Forbidden || status == HttpStatusCode.Gone;
                    Logger.LogWarning(
                        $"Media chunk {offset}-{last} failed (attempt {attempt}/{MaxAttempts}): {exception.Message}" +
                        urlDescription);
                    if (refused)
                    {
                        Rejected = true;
                        return null;
                    }
                    if (attempt == MaxAttempts) return null;
                    if (cancellation.WaitHandle.WaitOne(RetryDelayMs * attempt)) return null;
                }
            }

            return null;
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
