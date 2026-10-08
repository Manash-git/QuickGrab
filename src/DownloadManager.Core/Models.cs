namespace DownloadManager.Core;

public enum JobState { Queued, Connecting, Downloading, Paused, NeedsRestart, Failed, Finalizing, Completed, Verifying, BrowserPending }
public sealed record DownloadJob(
    string Id, string Url, string Destination, string Category,
    JobState State = JobState.Queued, long Bytes = 0, long? Total = null,
    string? ETag = null, string? Sha256 = null, string? Error = null, string? LastModified = null)
{
    public string PartPath => Destination + "." + Id + ".part";
    public string Name => Path.GetFileName(Destination);
}
public sealed record TransferProgress(long Bytes, long? Total);
public sealed class RestartRequiredException(string message) : Exception(message);
public sealed class DownloadProtocolException(string message) : Exception(message);
public sealed class TemporaryDownloadException(string message, TimeSpan? retryAfter = null) : Exception(message)
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

public static class DownloadInput
{
    public static Uri ValidateUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && uri.Scheme != "http") || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("Enter an HTTP or HTTPS URL without embedded credentials.");
        return uri;
    }
    public static string SuggestedName(string url)
    {
        var uri = ValidateUrl(url);
        var name = Uri.UnescapeDataString(uri.AbsolutePath.Split('/').LastOrDefault() ?? "");
        foreach (var c in Path.GetInvalidFileNameChars().Concat("<>:\"/\\|?*")) name = name.Replace(c, '_');
        name = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim().TrimEnd('.');
        if (name.Length > 120) name = name[..120];
        // The user chooses the actual destination in a save dialog. Prefix reserved DOS names.
        var stem = name.Split('.')[0].ToUpperInvariant();
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem)) name = "_" + name;
        return string.IsNullOrWhiteSpace(name) ? "download.bin" : name;
    }
    public static string Category(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".mp4" or ".mkv" or ".webm" or ".avi" => "Video",
        ".mp3" or ".flac" or ".wav" or ".m4a" => "Audio",
        ".zip" or ".7z" or ".rar" or ".gz" => "Archives",
        ".pdf" or ".docx" or ".xlsx" or ".txt" => "Documents",
        ".exe" or ".msi" => "Programs",
        _ => "Other"
    };
}
