using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
namespace DownloadManager.App;

internal static class ErrorReporter
{
    public static void Show(string operation, Exception exception)
    {
        var details = new StringBuilder();
        details.AppendLine($"QuickGrab 0.2.0 | {DateTimeOffset.UtcNow:O} | {operation}");
        Exception root = exception;
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            root = current;
            details.AppendLine(current.GetType().FullName + $" (0x{current.HResult:X8})");
            details.AppendLine(Redact(current.Message));
            details.AppendLine(current.StackTrace);
        }
        string? log = null;
        foreach (var folder in new[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClearDownload", "logs"),
            Path.Combine(Path.GetTempPath(), "QuickGrab-logs") })
        {
            try
            {
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, $"error-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.txt");
                File.WriteAllText(path, details.ToString()); log = path; break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        string message = Redact(root.Message);
        if (message.Length > 900) message = message[..900] + "…";
        MessageBox.Show($"{operation}\n\n{root.GetType().Name}: {message}\n\n" +
            (log is null ? "Could not save an error report. Please send a screenshot of this message." : "Error report saved to:\n" + log + "\n\nPlease send this message or the report if the problem continues."),
            "QuickGrab — error details", MessageBoxButton.OK, MessageBoxImage.Error);
    }
    private static string Redact(string message) => Regex.Replace(message, @"(?i)\b(?:https?|ftp)://[^\s<>""']+", "[URL redacted]");
}
