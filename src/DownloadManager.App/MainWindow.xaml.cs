using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using DownloadManager.Core;
using Microsoft.Win32;

namespace DownloadManager.App;
public partial class MainWindow : Window
{
    private readonly JobStore store;
    private readonly DownloadQueue queue;
    private readonly BandwidthLimiter limiter = new();
    private readonly HttpClient client;
    private readonly BrowserPipeServer browser;
    private readonly ObservableCollection<JobRow> rows = [];
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool closing, canClose;
    public MainWindow()
    {
        InitializeComponent();
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClearDownload");
        store = new JobStore(Path.Combine(data, "downloads.db"));
        client = HttpDownloadEngine.CreateClient();
        queue = new DownloadQueue(store, new HttpDownloadEngine(client, store, limiter));
        limiter.BytesPerSecond = Math.Clamp(store.Setting("limitKib", 0), 0, 1000000) * 1024L;
        LimitBox.Text = (limiter.BytesPerSecond / 1024).ToString();
        ConcurrencyBox.ItemsSource = Enumerable.Range(1, 8); ConcurrencyBox.SelectedItem = queue.Concurrency;
        Downloads.ItemsSource = rows;
        CollectionViewSource.GetDefaultView(rows).Filter = o => Categories.SelectedIndex == 0 ||
            o is JobRow row && row.Job.Category == (Categories.SelectedItem as ListBoxItem)?.Content.ToString();
        var browserFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "QuickGrab");
        browser = new BrowserPipeServer(new BrowserHandoff(queue, client, browserFolder));
        timer.Tick += (_, _) => Refresh(); timer.Start(); Refresh();
    }
    private void AddClick(object sender, RoutedEventArgs e) => Add();
    private void UrlKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) Add(); }
    private void Add() => Guard(() =>
    {
        var url = UrlBox.Text.Trim();
        var dialog = new SaveFileDialog { FileName = DownloadInput.SuggestedName(url), Title = "Choose a new destination filename", Filter = "All files|*.*", OverwritePrompt = true };
        if (dialog.ShowDialog(this) == true) { queue.Add(url, dialog.FileName); UrlBox.Clear(); Refresh(); }
    });
    private string? SelectedId => (Downloads.SelectedItem as JobRow)?.Job.Id;
    private void ResumeClick(object sender, RoutedEventArgs e) => Guard(() => { if (SelectedId is { } id) queue.Resume(id); });
    private void PauseClick(object sender, RoutedEventArgs e) => Guard(() => { if (SelectedId is { } id) queue.Pause(id); });
    private void RestartClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        if (SelectedId is { } id && MessageBox.Show(this, "Discard this job's partial data and download again from the beginning?", "Restart download", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) queue.Restart(id);
    });
    private void FolderClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        if (Downloads.SelectedItem is JobRow row)
        {
            var folder = Path.GetDirectoryName(row.Job.Destination)!;
            Directory.CreateDirectory(folder);
            var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            start.ArgumentList.Add(folder); Process.Start(start);
        }
    });
    private void ApplyClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        if (!int.TryParse(LimitBox.Text, out int limit) || limit < 0 || limit > 1000000) throw new ArgumentException("Enter a speed limit from 0 to 1000000 KiB/s.");
        queue.Concurrency = (int)(ConcurrencyBox.SelectedItem ?? 3);
        limiter.BytesPerSecond = limit * 1024L; store.SetSetting("limitKib", limit);
    });
    private void CategoryChanged(object sender, SelectionChangedEventArgs e) { if (Downloads?.ItemsSource is not null) CollectionViewSource.GetDefaultView(rows).Refresh(); }
    private void DownloadSelected(object sender, SelectionChangedEventArgs e) => ShowDetails();
    private void ShowDetails()
    {
        if (Details is null || Downloads.SelectedItem is not JobRow r) return;
        Details.Text = $"{r.Job.Destination}\n{r.Job.Error ?? (r.Job.ETag is not null || r.Job.LastModified is not null ? "Resume can request the remaining bytes using a saved file validator." : "Resume will verify saved data, then continue at the saved position. Verification uses network data.")}\n" +
            (r.Job.Sha256 is { } hash ? "SHA-256: " + hash : "Progress is checkpointed to disk. Downloaded files are never opened automatically.");
    }
    private void Refresh()
    {
        try
        {
            var jobs = queue.Jobs;
            foreach (var j in jobs)
            {
                var row = rows.FirstOrDefault(r => r.Job.Id == j.Id);
                if (row is null) { row = new JobRow(j); rows.Add(row); }
                queue.Progress.TryGetValue(j.Id, out var p); row.Update(j, p);
            }
            Summary.Text = $"{jobs.Count} downloads • {jobs.Count(j => j.State == JobState.Completed)} completed";
            ShowDetails();
        }
        catch (Exception ex) { timer.Stop(); ErrorReporter.Show("Refreshing downloads", ex); }
    }
    private void Guard(Action action)
    {
        try { action(); Refresh(); }
        catch (Exception ex) { ErrorReporter.Show("Download action", ex); }
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (canClose) return;
        e.Cancel = true; if (closing) return;
        closing = true; IsEnabled = false; timer.Stop();
        // Always leave the current WPF Closing event before attempting another
        // Close. StopAsync may complete synchronously when the queue is idle.
        Dispatcher.BeginInvoke(new Action(async () => await FinishClosingAsync()), DispatcherPriority.Background);
    }
    private async Task FinishClosingAsync()
    {
        try { await browser.DisposeAsync(); await queue.StopAsync(); }
        catch (Exception ex) { ErrorReporter.Show("Stopping downloads", ex); }
        finally
        {
            client.Dispose();
            canClose = true;
            Close();
        }
    }
}

public sealed class JobRow(DownloadJob job) : INotifyPropertyChanged
{
    public DownloadJob Job { get; private set; } = job;
    private long bytes;
    private long? total;
    private DateTime last = DateTime.UtcNow;
    private double speed;
    public string Name => Job.Name;
    public string Status => Job.State.ToString();
    public double Percent => total > 0 ? Math.Clamp(bytes * 100.0 / total.Value, 0, 100) : Job.State == JobState.Completed ? 100 : 0;
    public string PercentText => total is null ? "Unknown size" : $"{Percent:0}%";
    public string Size => Format(bytes) + " / " + (total is { } size ? Format(size) : "?");
    public string Speed => Job.State == JobState.Downloading ? Format((long)speed) + "/s" : "—";
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Update(DownloadJob job, TransferProgress? p)
    {
        var now = DateTime.UtcNow; var next = p?.Bytes ?? job.Bytes;
        speed = Math.Max(0, (next - bytes) / Math.Max(0.01, (now - last).TotalSeconds));
        last = now; bytes = next; total = p?.Total ?? job.Total; Job = job;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
    private static string Format(long n) => n >= 1073741824 ? $"{n / 1073741824.0:0.00} GiB" : n >= 1048576 ? $"{n / 1048576.0:0.0} MiB" : $"{n / 1024.0:0.0} KiB";
}
