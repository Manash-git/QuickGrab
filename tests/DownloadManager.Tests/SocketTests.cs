using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using DownloadManager.Core;

static class SocketTests
{
    public static async Task Worker(string root, string url)
    {
        var store = new JobStore(Path.Combine(root, "jobs.db"));
        var job = new DownloadJob("crash-test", url, Path.Combine(root, "payload.bin"), "Other");
        store.Save(job);
        using var client = HttpDownloadEngine.CreateClient();
        await new HttpDownloadEngine(client, store, new BandwidthLimiter()).RunAsync(job.Id, null, default);
    }
    public static Task LiveResume() => RealResume(false);
    public static Task DateLiveResume() => RealResume(true);
    private static async Task RealResume(bool useDate)
    {
        await using var server = new LocalServer(useDate ? "date" : "etag");
        string root = Path.Combine(Path.GetTempPath(), "QuickGrabSocket-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new JobStore(Path.Combine(root, "jobs.db"));
            var job = new DownloadJob("socket", server.Url, Path.Combine(root, "payload.bin"), "Other"); store.Save(job);
            using var http = HttpDownloadEngine.CreateClient();
            var engine = new HttpDownloadEngine(http, store, new BandwidthLimiter());
            using var cts = new CancellationTokenSource();
            try { await engine.RunAsync(job.Id, p => { if (p.Bytes >= 131072) cts.Cancel(); }, cts.Token); }
            catch (OperationCanceledException) { }
            await engine.RunAsync(job.Id, null, default);
            if (await HttpDownloadEngine.HashAsync(job.Destination) != server.Hash || !server.SawRange)
                throw new Exception("Real HTTP resume hash/range mismatch.");
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
    public static Task CrashRecovery() => Crash(false);
    public static Task NoValidatorCrashRecovery() => Crash(true);
    private static async Task Crash(bool noValidator)
    {
        await using var server = new LocalServer(noValidator ? "none" : "etag");
        string root = Path.Combine(Path.GetTempPath(), "QuickGrabCrash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Process? child = null;
        try
        {
            // Works under dotnet run and when launched via the test apphost.
            var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
            var start = new ProcessStartInfo(host) { UseShellExecute = false };
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            start.ArgumentList.Add("--crash-worker"); start.ArgumentList.Add(root); start.ArgumentList.Add(server.Url);
            child = Process.Start(start) ?? throw new Exception("Could not start crash worker.");
            var store = new JobStore(Path.Combine(root, "jobs.db"));
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed.TotalSeconds < 15 && !child.HasExited && !store.All().Any(j => j.Bytes >= 1048576))
                await Task.Delay(20);
            if (child.HasExited || !store.All().Any(j => j.Bytes >= 1048576)) throw new Exception("No live durable checkpoint reached.");
            child.Kill(entireProcessTree: true); await child.WaitForExitAsync();
            using var http = HttpDownloadEngine.CreateClient();
            var engine = new HttpDownloadEngine(http, store, new BandwidthLimiter());
            var queue = new DownloadQueue(store, engine);
            if (store.Get("crash-test").State != JobState.Paused) throw new Exception("Recovery state not paused.");
            await engine.RunAsync("crash-test", null, default);
            if (await HttpDownloadEngine.HashAsync(Path.Combine(root, "payload.bin")) != server.Hash || (!noValidator && !server.SawRange))
                throw new Exception("Crash recovery hash mismatch.");
            await queue.StopAsync();
        }
        finally
        {
            if (child is { HasExited: false }) { child.Kill(true); await child.WaitForExitAsync(); }
            child?.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true);
        }
    }
}

sealed class LocalServer : IAsyncDisposable
{
    private readonly HttpListener listener = new();
    private readonly CancellationTokenSource stop = new();
    private readonly Task loop;
    private readonly List<Task> requests = [];
    private readonly byte[] data = Enumerable.Range(0, 4 * 1024 * 1024).Select(i => (byte)(i % 251)).ToArray();
    public string Url { get; }
    public string Hash => Convert.ToHexString(SHA256.HashData(data));
    public bool SawRange;
    private readonly string mode;
    public LocalServer(string mode = "etag")
    {
        this.mode = mode;
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        Url = $"http://127.0.0.1:{port}/file";
        listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
        loop = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try { var context = await listener.GetContextAsync().WaitAsync(stop.Token); requests.Add(Serve(context)); }
                catch (Exception) when (stop.IsCancellationRequested) { break; }
            }
        });
    }
    private async Task Serve(HttpListenerContext ctx)
    {
        try
        {
            long start = 0;
            if (ctx.Request.Headers["Range"] is { } range)
            {
                SawRange = true; start = long.Parse(range[6..].TrimEnd('-'));
                if (ctx.Request.Headers["If-Range"] != (mode == "date" ? "Thu, 01 Oct 2026 10:00:00 GMT" : "\"socket-v1\"")) throw new Exception("Missing validator");
                ctx.Response.StatusCode = 206;
                ctx.Response.Headers["Content-Range"] = $"bytes {start}-{data.Length - 1}/{data.Length}";
            }
            if (mode == "etag") ctx.Response.Headers["ETag"] = "\"socket-v1\"";
            if (mode == "date") ctx.Response.Headers["Last-Modified"] = "Thu, 01 Oct 2026 10:00:00 GMT";
            ctx.Response.ContentLength64 = data.Length - start;
            for (int pos = (int)start; pos < data.Length; pos += 32768)
            {
                await ctx.Response.OutputStream.WriteAsync(data.AsMemory(pos, Math.Min(32768, data.Length - pos)), stop.Token);
                await Task.Delay(8, stop.Token);
            }
        }
        catch (Exception ex) when (ex is HttpListenerException or IOException or OperationCanceledException or ObjectDisposedException) { }
        finally { ctx.Response.Close(); }
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel(); listener.Stop(); await loop; await Task.WhenAll(requests); listener.Close(); stop.Dispose();
    }
}
