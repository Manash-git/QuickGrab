using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Diagnostics;

namespace DownloadManager.Core;

public sealed class HttpDownloadEngine(HttpClient http, JobStore store, BandwidthLimiter limiter)
{
    public static HttpClient CreateClient() => new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.None,
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 8,
        ConnectTimeout = TimeSpan.FromSeconds(20),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        UseCookies = false
    }) { Timeout = Timeout.InfiniteTimeSpan };

    public async Task RunAsync(string id, Action<TransferProgress>? progress, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { await TransferAsync(id, progress, ct); return; }
            catch (Exception ex) when (!ct.IsCancellationRequested && attempt < 3 &&
                ex is HttpRequestException or TemporaryDownloadException or OperationCanceledException)
            {
                var delay = ex is TemporaryDownloadException t && t.RetryAfter is { } retry ? retry :
                    TimeSpan.FromSeconds(Math.Pow(2, attempt) + Random.Shared.NextDouble());
                var j = store.Get(id);
                store.Save(j with { State = JobState.Connecting, Error = $"Temporary network failure. Retry {attempt + 1}/3." });
                await Task.Delay(delay, ct);
            }
        }
    }

    private async Task TransferAsync(string id, Action<TransferProgress>? progress, CancellationToken ct)
    {
        var j = store.Get(id);
        DownloadInput.ValidateUrl(j.Url);
        if (File.Exists(j.Destination))
        {
            // Recover the crash window between atomic rename and marking the database complete.
            if (j.Sha256 is not null && !File.Exists(j.PartPath) &&
                new FileInfo(j.Destination).Length == j.Bytes && await HashAsync(j.Destination, ct) == j.Sha256)
            { store.Save(j with { State = JobState.Completed, Error = null }); return; }
            throw new DownloadProtocolException("The destination already exists. Choose a different filename for a new job.");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(j.Destination)!);
        if (j.Bytes > 0 && (!File.Exists(j.PartPath) || new FileInfo(j.PartPath).Length < j.Bytes))
            throw new RestartRequiredException("The partial file is missing or shorter than its saved checkpoint. Restart is required.");
        if (j.Total == j.Bytes && j.Total is not null && File.Exists(j.PartPath))
        { await FinalizeAsync(j, ct); return; }
        bool useRange = j.Bytes > 0 && (j.ETag is not null || j.LastModified is not null);
        bool verifyPrefix = j.Bytes > 0 && !useRange;

        using var request = new HttpRequestMessage(HttpMethod.Get, j.Url);
        request.Headers.AcceptEncoding.ParseAdd("identity");
        request.Headers.UserAgent.ParseAdd("QuickGrab/0.1.2");
        if (useRange)
        {
            request.Headers.Range = new RangeHeaderValue(j.Bytes, null);
            request.Headers.IfRange = j.ETag is not null
                ? new RangeConditionHeaderValue(EntityTagHeaderValue.Parse(j.ETag))
                : new RangeConditionHeaderValue(DateTimeOffset.Parse(j.LastModified!, System.Globalization.CultureInfo.InvariantCulture));
        }
        using var headerTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        headerTimeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headerTimeout.Token);
        if ((int)response.StatusCode is 408 or 429 or 500 or 502 or 503 or 504)
        {
            var after = response.Headers.RetryAfter;
            var delay = after?.Delta ?? (after?.Date is { } date ? date - DateTimeOffset.UtcNow : (TimeSpan?)null);
            if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
            throw new TemporaryDownloadException($"Server returned {(int)response.StatusCode}.", delay);
        }
        if (useRange && response.StatusCode is HttpStatusCode.OK or HttpStatusCode.RequestedRangeNotSatisfiable)
            throw new RestartRequiredException("The server cannot resume this file or the file has changed. Restart is required.");
        if (!response.IsSuccessStatusCode)
            throw new DownloadProtocolException($"HTTP {(int)response.StatusCode}. Check access or obtain a fresh link.");
        if (response.Content.Headers.ContentEncoding.Any(e => !e.Equals("identity", StringComparison.OrdinalIgnoreCase)))
            throw new DownloadProtocolException("The server sent encoded content despite an identity request. This transfer is unsupported.");

        var tag = response.Headers.ETag is { IsWeak: false } etag ? etag.ToString() : null;
        // Only use an HTTP date for If-Range when no ETag was supplied and the
        // Date/Last-Modified gap conservatively establishes a strong timestamp.
        var modified = response.Content.Headers.LastModified;
        string? usableModified = response.Headers.ETag is null && modified is { } lm &&
            response.Headers.Date is { } responseDate && responseDate - lm >= TimeSpan.FromSeconds(60)
            ? lm.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : null;
        long? total;
        if (useRange)
        {
            var range = response.Content.Headers.ContentRange;
            if (response.StatusCode != HttpStatusCode.PartialContent || range is null ||
                range.Unit != "bytes" || range.From != j.Bytes || range.Length is null ||
                range.To != range.Length - 1 || (j.Total is not null && range.Length != j.Total) ||
                (j.ETag is not null ? tag != j.ETag : modified?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) != j.LastModified) ||
                (response.Content.Headers.ContentLength is { } length && length != range.Length - j.Bytes))
                throw new RestartRequiredException("The resume response does not match the saved file. Nothing was appended.");
            total = range.Length;
        }
        else
        {
            if (response.StatusCode != HttpStatusCode.OK)
                throw new DownloadProtocolException("Expected a complete HTTP 200 response for a new download.");
            total = response.Content.Headers.ContentLength;
        }
        if (verifyPrefix && total is { } currentSize &&
            (currentSize < j.Bytes || (j.Total is { } oldSize && currentSize != oldSize)))
            throw new RestartRequiredException("The server file size has changed. Your partial data was kept; restart only if you want the new file.");
        if (verifyPrefix)
            store.Save(j with { State = JobState.Verifying, Error = "Verifying saved data before continuing. Progress is retained; verification uses network data." });
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        if (verifyPrefix) await VerifyPrefixAsync(j, input, ct);
        j = j with { Total = total, ETag = tag, LastModified = usableModified,
            State = JobState.Downloading, Error = null, Sha256 = null };
        store.Save(j);
        await using (var file = new FileStream(j.PartPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read,
                         64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            // Discard bytes beyond the last durable checkpoint after a crash or failed write.
            file.SetLength(j.Bytes); file.Position = j.Bytes;
            var buffer = new byte[32 * 1024];
            long position = j.Bytes;
            var checkpointClock = Stopwatch.StartNew();
            void Checkpoint()
            {
                file.Flush(flushToDisk: true);
                j = j with { Bytes = position };
                store.Save(j);
                checkpointClock.Restart();
            }
            try
            {
                while (true)
                {
                    using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    readTimeout.CancelAfter(TimeSpan.FromSeconds(30));
                    int count = await input.ReadAsync(buffer, readTimeout.Token);
                    if (count == 0) break;
                    if (total is { } expected && position + count > expected)
                        throw new DownloadProtocolException("The response exceeded its declared file size.");
                    await limiter.ConsumeAsync(count, ct);
                    await file.WriteAsync(buffer.AsMemory(0, count), ct);
                    position += count;
                    progress?.Invoke(new(position, total));
                    if (position - j.Bytes >= 1024 * 1024 || checkpointClock.Elapsed.TotalSeconds >= 2) Checkpoint();
                }
                if (total is { } expectedTotal && position != expectedTotal)
                    throw new TemporaryDownloadException("The connection ended before the complete file arrived.");
                Checkpoint();
            }
            catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or TemporaryDownloadException)
            {
                // A canceled write may have partially written; preserve only completed writes.
                file.SetLength(position);
                Checkpoint();
                throw;
            }
        }
        await FinalizeAsync(j, ct);
    }

    private async Task VerifyPrefixAsync(DownloadJob job, Stream input, CancellationToken ct)
    {
        await using var existing = new FileStream(job.PartPath, FileMode.Open, FileAccess.Read,
            FileShare.Read, 32768, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var remote = new byte[32768]; var local = new byte[32768];
        long checkedBytes = 0;
        while (checkedBytes < job.Bytes)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            int count = await input.ReadAsync(remote.AsMemory(0, (int)Math.Min(remote.Length, job.Bytes - checkedBytes)), timeout.Token);
            if (count == 0) throw new TemporaryDownloadException("The server stopped during saved-data verification. Resume to retry; your partial file was kept.");
            await limiter.ConsumeAsync(count, ct);
            await existing.ReadExactlyAsync(local.AsMemory(0, count), ct);
            if (!remote.AsSpan(0, count).SequenceEqual(local.AsSpan(0, count)))
                throw new RestartRequiredException("The server content differs from your saved data. Your partial file was kept; restart only if you want the new file.");
            checkedBytes += count;
        }
        // Continue the SAME response stream at the saved offset: no unvalidated
        // second request and no overwriting the successfully compared prefix.
    }

    private async Task FinalizeAsync(DownloadJob j, CancellationToken ct)
    {
        // Also truncate uncommitted trailing bytes on the fast finalization recovery path.
        using (var file = new FileStream(j.PartPath, FileMode.Open, FileAccess.Write, FileShare.None))
        { file.SetLength(j.Bytes); file.Flush(true); }
        var hash = await HashAsync(j.PartPath, ct);
        j = j with { Sha256 = hash, State = JobState.Finalizing, Total = j.Bytes };
        store.Save(j);
        ct.ThrowIfCancellationRequested();
        File.Move(j.PartPath, j.Destination, overwrite: false);
        store.Save(j with { State = JobState.Completed, Error = null });
    }
    public static async Task<string> HashAsync(string path, CancellationToken ct = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
    }
}
