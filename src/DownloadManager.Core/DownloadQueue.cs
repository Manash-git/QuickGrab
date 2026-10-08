using System.Collections.Concurrent;

namespace DownloadManager.Core;

public sealed class DownloadQueue
{
    private readonly JobStore store;
    private readonly HttpDownloadEngine engine;
    private readonly object gate = new();
    private readonly Dictionary<string, (CancellationTokenSource Cts, Task Task)> active = [];
    private bool stopping;
    private int concurrency;
    public ConcurrentDictionary<string, TransferProgress> Progress { get; } = new();
    public int Concurrency
    {
        get { lock (gate) return concurrency; }
        set { lock (gate) { concurrency = Math.Clamp(value, 1, 8); store.SetSetting("concurrency", concurrency); Pump(); } }
    }
    public DownloadQueue(JobStore store, HttpDownloadEngine engine)
    {
        this.store = store; this.engine = engine;
        concurrency = Math.Clamp(store.Setting("concurrency", 3), 1, 8);
        // Never silently restart network activity after reopening the app.
        foreach (var j in store.All().Where(j => j.State is JobState.Downloading or JobState.Connecting or JobState.Queued or JobState.Finalizing or JobState.Verifying))
            store.Save(j with { State = JobState.Paused, Error = "Recovered after app closure. Select Resume to continue." });
    }
    public IReadOnlyList<DownloadJob> Jobs => store.All();
    public DownloadJob Add(string url, string destination)
    {
        DownloadInput.ValidateUrl(url);
        destination = Path.GetFullPath(destination);
        lock (gate)
        {
            if (stopping) throw new InvalidOperationException("The app is closing.");
            if (File.Exists(destination) || store.All().Any(j => j.Destination.Equals(destination, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("That destination already exists or belongs to another download. Choose a new filename.");
            var job = new DownloadJob(Guid.NewGuid().ToString("N"), url, destination, DownloadInput.Category(destination));
            store.Save(job); Pump(); return job;
        }
    }
    public DownloadJob PrepareBrowser(string id, string url, string destination)
    {
        DownloadInput.ValidateUrl(url);
        lock (gate)
        {
            if (stopping) throw new InvalidOperationException("QuickGrab is closing.");
            var existing = store.All().SingleOrDefault(j => j.Id == id);
            if (existing is not null)
            {
                if (existing.Url != url) throw new InvalidOperationException("Request identifier was reused for another URL.");
                return existing;
            }
            if (File.Exists(destination) || store.All().Any(j => j.Destination.Equals(destination, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Browser destination is already occupied.");
            var job = new DownloadJob(id, url, destination, DownloadInput.Category(destination),
                JobState.BrowserPending, Error: "Browser handoff pending. If the browser copy was cancelled, select Resume to recover this job.");
            store.Save(job); return job;
        }
    }
    public void CommitBrowser(string id)
    {
        lock (gate)
        {
            if (stopping) throw new InvalidOperationException("QuickGrab is closing.");
            var j = store.Get(id);
            if (j.State != JobState.BrowserPending) return; // idempotent commit never restarts a finished or failed job
            store.Save(j with { State = JobState.Queued, Error = null }); Pump();
        }
    }
    public void AbortBrowser(string id)
    {
        lock (gate) { if (!active.ContainsKey(id)) store.DeletePending(id); }
    }
    public void Pause(string id)
    {
        lock (gate)
        {
            if (active.TryGetValue(id, out var running)) running.Cts.Cancel();
            else
            {
                var j = store.Get(id);
                if (j.State == JobState.Queued) store.Save(j with { State = JobState.Paused });
            }
        }
    }
    public void Resume(string id)
    {
        lock (gate)
        {
            if (stopping || active.ContainsKey(id)) return;
            var j = store.Get(id);
            if (j.State is JobState.Completed) return;
            store.Save(j with { State = JobState.Queued, Error = null }); Pump();
        }
    }
    public void Restart(string id)
    {
        lock (gate)
        {
            if (stopping) return;
            if (active.ContainsKey(id)) throw new InvalidOperationException("Pause the download and wait for it to stop first.");
            var j = store.Get(id);
            if (j.State == JobState.Completed || File.Exists(j.Destination)) throw new InvalidOperationException("A completed destination will not be overwritten.");
            // Save zero first: a crash before deletion will lead to truncation on the next attempt.
            store.Save(j with { Bytes = 0, Total = null, ETag = null, LastModified = null, Sha256 = null, State = JobState.Paused, Error = null });
            File.Delete(j.PartPath);
            store.Save(store.Get(id) with { State = JobState.Queued }); Pump();
        }
    }
    private void Pump()
    {
        if (stopping) return;
        foreach (var j in store.All().Where(j => j.State == JobState.Queued && !active.ContainsKey(j.Id)).Take(Math.Max(0, concurrency - active.Count)))
        {
            var cts = new CancellationTokenSource();
            store.Save(j with { State = JobState.Connecting });
            var task = Task.Run(() => RunAsync(j.Id, cts));
            active.Add(j.Id, (cts, task));
        }
    }
    private async Task RunAsync(string id, CancellationTokenSource cts)
    {
        try { await engine.RunAsync(id, p => Progress[id] = p, cts.Token); }
        catch (Exception ex)
        {
            var state = ex is RestartRequiredException ? JobState.NeedsRestart :
                cts.IsCancellationRequested ? JobState.Paused : JobState.Failed;
            var message = ex switch
            {
                RestartRequiredException or DownloadProtocolException or TemporaryDownloadException => ex.Message,
                OperationCanceledException when cts.IsCancellationRequested => null,
                OperationCanceledException => "Network timeout. Select Resume to retry.",
                HttpRequestException => "Network or TLS failure. Check your connection, then Resume.",
                IOException => "File access failed. Check free space, folder access, and the partial file.",
                UnauthorizedAccessException => "Access denied to the destination folder.",
                _ => "Unexpected error (" + ex.GetType().Name + "). Close and reopen the app before retrying."
            };
            try { store.Save(store.Get(id) with { State = state, Error = message }); }
            catch { /* Keep the earlier durable checkpoint; startup recovery will restore it. */ }
        }
        finally
        {
            lock (gate)
            {
                Progress.TryRemove(id, out _); active.Remove(id); cts.Dispose();
                try { Pump(); } catch { stopping = true; }
            }
        }
    }
    public async Task StopAsync()
    {
        Task[] tasks;
        lock (gate)
        {
            stopping = true;
            foreach (var item in active.Values) item.Cts.Cancel();
            tasks = active.Values.Select(v => v.Task).ToArray();
        }
        await Task.WhenAll(tasks);
    }
}
