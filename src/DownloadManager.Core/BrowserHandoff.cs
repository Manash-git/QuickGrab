using System.Net;
using System.Net.Http.Headers;
using QuickGrab.BrowserProtocol;
namespace DownloadManager.Core;

public sealed class BrowserHandoff(DownloadQueue queue, HttpClient probeClient, string directory)
{
    public async Task<BrowserReply> HandleAsync(BrowserRequest request, CancellationToken ct)
    {
        try
        {
            Protocol.Validate(request);
            var id = Guid.Parse(request.RequestId).ToString("N");
            if (request.Command == "ping") return new(true, "connected");
            if (request.Command == "abort") { queue.AbortBrowser(id); return new(true, "aborted", id); }
            if (request.Command == "commit") { queue.CommitBrowser(id); return new(true, "accepted", id); }
            // Retry a previously prepared request without creating a second job.
            var existing = queue.Jobs.SingleOrDefault(j => j.Id == id);
            if (existing is not null)
            {
                if (existing.Url != request.Url) return new(false, "rejected", Message: "Mismatched request identifier.");
                return new(true, "ready", id);
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var get = new HttpRequestMessage(HttpMethod.Get, request.Url);
            get.Headers.Range = new RangeHeaderValue(0, 0);
            get.Headers.AcceptEncoding.ParseAdd("identity");
            get.Headers.UserAgent.ParseAdd("QuickGrab/0.2.0");
            using var response = await probeClient.SendAsync(get, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.PartialContent))
                return new(false, "unsupported", Message: "This link needs browser access or is unavailable. Keep the download in the browser.");
            var mime = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
            if (mime is "text/html" or "application/xhtml+xml" ||
                response.Content.Headers.ContentEncoding.Any(e => e != "identity"))
                return new(false, "unsupported", Message: "This is a web page or encoded response, not a supported direct file.");
            long? size = response.Content.Headers.ContentLength;
            if (response.StatusCode == HttpStatusCode.PartialContent)
            {
                var range = response.Content.Headers.ContentRange;
                if (range?.Unit != "bytes" || range.From != 0 || range.To != 0 || range.Length is null ||
                    (size is not null && size != 1)) return new(false, "unsupported", Message: "The server returned an invalid range response.");
                size = range.Length;
            }
            if (request.ExpectedBytes is > 0 && size != request.ExpectedBytes)
                return new(false, "unsupported", Message: "The link does not match the browser's file size. Keep this download in the browser.");
            var name = SafeName(request.SuggestedName, request.Url!);
            var extension = Path.GetExtension(name);
            name = Path.GetFileNameWithoutExtension(name) + "-" + id[..8] + extension;
            Directory.CreateDirectory(directory);
            var job = queue.PrepareBrowser(id, request.Url!, Path.Combine(directory, name));
            return new(true, "ready", job.Id);
        }
        catch (OperationCanceledException) { return new(false, "unavailable", Message: "QuickGrab timed out or is closing. Keep the browser download."); }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or InvalidOperationException or IOException or HttpRequestException or UnauthorizedAccessException)
        { return new(false, "rejected", Message: "QuickGrab could not accept this link. Check the destination or keep the browser download."); }
    }
    public static string SafeName(string? suggested, string url)
    {
        if (string.IsNullOrWhiteSpace(suggested)) return DownloadInput.SuggestedName(url);
        var basename = suggested.Replace('\\', '/').Split('/').Last();
        return DownloadInput.SuggestedName("https://filename.invalid/" + Uri.EscapeDataString(basename));
    }
}
