using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Diagnostics;
using DownloadManager.Core;

if (args.Length == 3 && args[0] == "--crash-worker")
{
    await SocketTests.Worker(args[1], args[2]); return 0;
}
var tests = new (string, Func<Task>)[]
{
    ("Browser protocol framing and bounds", BrowserTests.Framing),
    ("Browser protocol validation and safe filenames", BrowserTests.Validation),
    ("Browser prepare/commit idempotence and downloaded bytes", BrowserTests.Success),
    ("Browser HTML response stays out of queue", BrowserTests.Html),
    ("Browser size mismatch stays out of queue", BrowserTests.SizeMismatch),
    ("Browser abort removes only pending job", BrowserTests.Abort),
    ("Real HTTP streaming and pause/resume", SocketTests.LiveResume),
    ("Real HTTP Last-Modified range resume", SocketTests.DateLiveResume),
    ("Killed process without validators retains and verifies prefix", SocketTests.NoValidatorCrashRecovery),
    ("Killed download process recovers from SQLite checkpoint", SocketTests.CrashRecovery),
    ("Complete download matches SHA-256", Complete),
    ("Pause and resume uses Range + If-Range", PauseResume),
    ("Ignored Range never appends", () => BadResume("ignore")),
    ("Changed ETag never appends", () => BadResume("changed")),
    ("Wrong Content-Range never appends", () => BadResume("range")),
    ("Weak validator resumes after full prefix verification", Weak),
    ("Missing validator preserves prefix and resumes", () => NoValidator(false)),
    ("Changed prefix is rejected without modifying saved file", () => NoValidator(true)),
    ("Last-Modified resume requests only remaining bytes", DateResume),
    ("Changed Last-Modified is rejected", ChangedDate),
    ("Legacy database migration preserves checkpoints", Migration),
    ("NeedsRestart jobs can retry resume without discarding data", RetryResume),
    ("Uncommitted trailing bytes are discarded", Truncate),
    ("Short partial file requires restart", ShortPartial),
    ("Final rename crash window recovers", RenameRecovery),
    ("Existing destination is not overwritten", Collision),
    ("Transient server errors retry", Retry),
    ("Global limiter combines simultaneous transfers", Limit),
    ("Queue concurrency is bounded", QueueLimit),
    ("Startup restores active jobs paused", Startup),
    ("URL schemes and filenames are constrained", Inputs)
};
int failures = 0;
foreach (var (name, test) in tests)
{
    try { await test(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed");
return failures == 0 ? 0 : 1;

static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
static async Task Expect<T>(Func<Task> action) where T : Exception
{
    try { await action(); } catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}
static async Task Complete()
{
    using var f = new Fixture(); await f.Engine.RunAsync(f.Job.Id, null, default);
    var job = f.Store.Get(f.Job.Id);
    Assert(job.State == JobState.Completed, "state");
    Assert(File.ReadAllBytes(job.Destination).SequenceEqual(f.Data), "bytes");
    Assert(job.Sha256 == Convert.ToHexString(SHA256.HashData(f.Data)), "hash");
    Assert(!File.Exists(job.PartPath), "part remains");
}
static async Task PauseResume()
{
    using var f = new Fixture(); await f.Pause();
    var offset = f.Store.Get(f.Job.Id).Bytes; Assert(offset > 0 && offset < f.Data.Length, "offset");
    await f.Engine.RunAsync(f.Job.Id, null, default);
    Assert(f.LastRange == offset && f.LastIfRange == "\"v1\"", "resume headers");
    Assert(File.ReadAllBytes(f.Job.Destination).SequenceEqual(f.Data), "resumed bytes");
}
static async Task BadResume(string mode)
{
    using var f = new Fixture(); await f.Pause(); var before = File.ReadAllBytes(f.Job.PartPath);
    f.Mode = mode;
    await Expect<RestartRequiredException>(() => f.Engine.RunAsync(f.Job.Id, null, default));
    Assert(before.SequenceEqual(File.ReadAllBytes(f.Job.PartPath)), "unsafe append");
}
static async Task Weak()
{
    using var f = new Fixture { Mode = "weak" }; await f.Pause();
    await f.Engine.RunAsync(f.Job.Id, null, default);
    Assert(f.LastRange is null && File.ReadAllBytes(f.Job.Destination).SequenceEqual(f.Data), "weak validator fallback");
}
static async Task NoValidator(bool changed)
{
    using var f = new Fixture { Mode = "none" }; await f.Pause();
    var before = File.ReadAllBytes(f.Job.PartPath);
    var saved = f.Store.Get(f.Job.Id).Bytes;
    if (changed)
    {
        f.Data[10] ^= 1;
        await Expect<RestartRequiredException>(() => f.Engine.RunAsync(f.Job.Id, null, default));
        Assert(before.SequenceEqual(File.ReadAllBytes(f.Job.PartPath)), "changed partial was overwritten");
        Assert(f.Store.Get(f.Job.Id).Bytes == saved, "checkpoint changed");
    }
    else
    {
        await f.Engine.RunAsync(f.Job.Id, p => Assert(p.Bytes >= saved, "progress reset"), default);
        Assert(File.ReadAllBytes(f.Job.Destination).SequenceEqual(f.Data), "fallback bytes");
    }
}
static async Task DateResume()
{
    using var f = new Fixture { Mode = "date" }; await f.Pause();
    var j = f.Store.Get(f.Job.Id); Assert(j.LastModified is not null, "timestamp not saved");
    // Reopen the database/queue to test persistence across application sessions.
    var reopened = new JobStore(Path.Combine(f.Root, "jobs.db"));
    Assert(reopened.Get(j.Id).LastModified == j.LastModified, "timestamp not persisted");
    await f.Engine.RunAsync(j.Id, null, default);
    Assert(f.LastRange == j.Bytes && f.LastIfRange == j.LastModified, "date resume headers");
    Assert(File.ReadAllBytes(j.Destination).SequenceEqual(f.Data), "date resume bytes");
}
static async Task ChangedDate()
{
    using var f = new Fixture { Mode = "date" }; await f.Pause();
    var before = File.ReadAllBytes(f.Job.PartPath); f.Mode = "date-changed";
    await Expect<RestartRequiredException>(() => f.Engine.RunAsync(f.Job.Id, null, default));
    Assert(before.SequenceEqual(File.ReadAllBytes(f.Job.PartPath)), "changed date appended");
}
static Task Migration()
{
    using var f = new Fixture();
    using (var db = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + Path.Combine(f.Root, "jobs.db")))
    {
        db.Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "ALTER TABLE jobs DROP COLUMN last_modified; PRAGMA user_version=1;"; cmd.ExecuteNonQuery();
    }
    var upgraded = new JobStore(Path.Combine(f.Root, "jobs.db"));
    Assert(upgraded.Get(f.Job.Id).Url == f.Job.Url && upgraded.Get(f.Job.Id).LastModified is null, "migration lost record");
    return Task.CompletedTask;
}
static async Task RetryResume()
{
    using var f = new Fixture { Mode = "none" }; await f.Pause();
    var j = f.Store.Get(f.Job.Id); f.Store.Save(j with { State = JobState.NeedsRestart });
    var q = new DownloadQueue(f.Store, f.Engine); q.Resume(j.Id);
    var watch = Stopwatch.StartNew();
    while (f.Store.Get(j.Id).State != JobState.Completed && watch.Elapsed.TotalSeconds < 5) await Task.Delay(20);
    await q.StopAsync(); Assert(f.Store.Get(j.Id).State == JobState.Completed, "NeedsRestart resume blocked");
    Assert(File.ReadAllBytes(j.Destination).SequenceEqual(f.Data), "resumed queue bytes");
}
static async Task Truncate()
{
    using var f = new Fixture(); await f.Pause();
    await using (var s = new FileStream(f.Job.PartPath, FileMode.Append)) await s.WriteAsync(new byte[10000]);
    await f.Engine.RunAsync(f.Job.Id, null, default);
    Assert(File.ReadAllBytes(f.Job.Destination).SequenceEqual(f.Data), "trailing garbage");
}
static async Task ShortPartial()
{
    using var f = new Fixture(); await f.Pause(); File.WriteAllBytes(f.Job.PartPath, [1]);
    await Expect<RestartRequiredException>(() => f.Engine.RunAsync(f.Job.Id, null, default));
}
static async Task RenameRecovery()
{
    using var f = new Fixture(); File.WriteAllBytes(f.Job.Destination, f.Data);
    f.Store.Save(f.Job with { Bytes = f.Data.Length, Total = f.Data.Length, State = JobState.Finalizing, Sha256 = Convert.ToHexString(SHA256.HashData(f.Data)) });
    await f.Engine.RunAsync(f.Job.Id, null, default);
    Assert(f.Store.Get(f.Job.Id).State == JobState.Completed && f.Requests == 0, "reconcile");
}
static async Task Collision()
{
    using var f = new Fixture(); File.WriteAllText(f.Job.Destination, "existing");
    await Expect<DownloadProtocolException>(() => f.Engine.RunAsync(f.Job.Id, null, default));
    Assert(File.ReadAllText(f.Job.Destination) == "existing", "overwritten");
}
static async Task Retry()
{
    using var f = new Fixture { Mode = "retry" }; await f.Engine.RunAsync(f.Job.Id, null, default);
    Assert(f.Requests == 2 && f.Store.Get(f.Job.Id).State == JobState.Completed, "retry");
}
static async Task Limit()
{
    var limiter = new BandwidthLimiter { BytesPerSecond = 100000 };
    var sw = Stopwatch.StartNew();
    await Task.WhenAll(limiter.ConsumeAsync(50000, default), limiter.ConsumeAsync(50000, default));
    Assert(sw.Elapsed.TotalSeconds >= 0.90, "aggregate limit bypassed");
}
static async Task QueueLimit()
{
    using var f = new Fixture { Delay = true }; var queue = new DownloadQueue(f.Store, f.Engine) { Concurrency = 2 };
    for (int i = 0; i < 4; i++) queue.Add("https://example.test/file", Path.Combine(f.Root, "queue" + i));
    var sw = Stopwatch.StartNew();
    while (queue.Jobs.Count(j => j.State == JobState.Completed) < 4 && sw.Elapsed.TotalSeconds < 10) await Task.Delay(25);
    await queue.StopAsync();
    Assert(queue.Jobs.Count(j => j.State == JobState.Completed) == 4, "queue did not complete");
    Assert(f.Peak <= 2 && f.Peak == 2, "concurrency");
}
static Task Startup()
{
    using var f = new Fixture(); f.Store.Save(f.Job with { State = JobState.Downloading });
    _ = new DownloadQueue(f.Store, f.Engine);
    Assert(f.Store.Get(f.Job.Id).State == JobState.Paused, "startup auto-download"); return Task.CompletedTask;
}
static Task Inputs()
{
    foreach (var url in new[] { "file:///a", "ftp://example.test/a", "https://user:pass@example.test/a", "javascript:alert(1)" })
    { try { DownloadInput.ValidateUrl(url); throw new Exception("accepted " + url); } catch (ArgumentException) { } }
    Assert(!DownloadInput.SuggestedName("https://example.test/a%2Fb%3Ac.exe").Contains('/'), "unsafe filename");
    Assert(DownloadInput.SuggestedName("https://example.test/CON") == "_CON", "DOS reserved name");
    return Task.CompletedTask;
}

sealed class Fixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "QuickGrabTests-" + Guid.NewGuid().ToString("N"));
    public byte[] Data { get; } = Enumerable.Range(0, 262144).Select(i => (byte)(i % 251)).ToArray();
    public JobStore Store { get; }
    public HttpDownloadEngine Engine { get; }
    public DownloadJob Job { get; }
    public string Mode = "normal";
    public long? LastRange;
    public string? LastIfRange;
    public int Requests;
    public bool Delay;
    private int active;
    public int Peak;
    private readonly HttpClient client;
    public Fixture()
    {
        Directory.CreateDirectory(Root); Store = new JobStore(Path.Combine(Root, "jobs.db"));
        Job = new(Guid.NewGuid().ToString("N"), "https://example.test/file", Path.Combine(Root, "test.bin"), "Other"); Store.Save(Job);
        client = new HttpClient(new Handler(Respond)) { Timeout = Timeout.InfiniteTimeSpan };
        Engine = new HttpDownloadEngine(client, Store, new BandwidthLimiter());
    }
    private async Task<HttpResponseMessage> Respond(HttpRequestMessage req, CancellationToken ct)
    {
        int count = Interlocked.Increment(ref Requests);
        int n = Interlocked.Increment(ref active);
        lock (this) Peak = Math.Max(Peak, n);
        try { if (Delay) await Task.Delay(100, ct); }
        finally { Interlocked.Decrement(ref active); }
        if (Mode == "retry" && count == 1)
        {
            var retry = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            retry.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero); return retry;
        }
        var from = req.Headers.Range?.Ranges.Single().From;
        LastRange = from; LastIfRange = req.Headers.IfRange?.ToString();
        bool partial = from is not null && Mode != "ignore";
        long start = partial ? from!.Value : 0;
        var response = new HttpResponseMessage(partial ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
        { Content = new ByteArrayContent(Data[(int)start..]) };
        if (Mode != "none" && !Mode.StartsWith("date")) response.Headers.ETag = new EntityTagHeaderValue(Mode == "changed" ? "\"v2\"" : "\"v1\"", Mode == "weak");
        if (Mode.StartsWith("date"))
        {
            response.Headers.Date = new DateTimeOffset(2026, 10, 7, 10, 0, 0, TimeSpan.Zero);
            response.Content.Headers.LastModified = new DateTimeOffset(2026, 10, Mode == "date-changed" ? 2 : 1, 10, 0, 0, TimeSpan.Zero);
        }
        if (partial) response.Content.Headers.ContentRange = new ContentRangeHeaderValue(Mode == "range" ? start + 1 : start, Data.Length - 1, Data.Length);
        return response;
    }
    public async Task Pause()
    {
        using var cts = new CancellationTokenSource();
        try { await Engine.RunAsync(Job.Id, p => { if (p.Bytes >= 65536) cts.Cancel(); }, cts.Token); }
        catch (OperationCanceledException) { }
    }
    public void Dispose()
    {
        client.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(Root, true);
    }
}
sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
}
