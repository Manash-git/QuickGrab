using System.Net;
using System.Net.Http.Headers;
using QuickGrab.BrowserProtocol;
using DownloadManager.Core;

static class BrowserTests
{
    public static async Task Framing()
    {
        using var stream = new MemoryStream();
        var req = new BrowserRequest(1, "ping", Guid.NewGuid().ToString());
        await Protocol.WriteAsync(stream, req, default); stream.Position = 0;
        if (await Protocol.ReadAsync<BrowserRequest>(stream, default) != req) throw new Exception("Protocol round trip failed.");
        using var bad = new MemoryStream(new byte[] {255,255,255,127});
        try { await Protocol.ReadAsync<BrowserRequest>(bad, default); throw new Exception("Oversized frame accepted."); }
        catch (InvalidDataException) { }
        using var shortFrame = new MemoryStream(new byte[]{10,0,0,0,1});
        try { await Protocol.ReadAsync<BrowserRequest>(shortFrame, default); throw new Exception("Truncated frame accepted."); }
        catch (EndOfStreamException) { }
    }
    public static Task Validation()
    {
        foreach (var req in new[] {
            new BrowserRequest(2,"ping",Guid.NewGuid().ToString()),
            new BrowserRequest(1,"shell",Guid.NewGuid().ToString()),
            new BrowserRequest(1,"prepare",Guid.NewGuid().ToString(),"file:///tmp/secret"),
            new BrowserRequest(1,"prepare",Guid.NewGuid().ToString(),"https://u:p@example.test/file"),
            new BrowserRequest(1,"ping","not-a-guid")})
        {
            try { Protocol.Validate(req); throw new Exception("Invalid protocol accepted."); } catch (InvalidDataException) { }
        }
        if (BrowserHandoff.SafeName("../../CON.exe", "https://example.test/file") != "_CON.exe") throw new Exception("Unsafe filename.");
        return Task.CompletedTask;
    }
    public static Task Success() => Scenario("normal");
    public static Task Html() => Scenario("html");
    public static Task SizeMismatch() => Scenario("size");
    public static Task Abort() => Scenario("abort");
    private static async Task Scenario(string mode)
    {
        var root = Path.Combine(Path.GetTempPath(), "QuickGrabBridgeTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var http = new HttpClient(new ProbeHandler(mode));
        var store = new JobStore(Path.Combine(root, "jobs.db"));
        var engine = new HttpDownloadEngine(http, store, new BandwidthLimiter());
        var queue = new DownloadQueue(store, engine);
        try
        {
            var handoff = new BrowserHandoff(queue,http,Path.Combine(root,"downloads"));
            var req = new BrowserRequest(1,"prepare",Guid.NewGuid().ToString(),"https://example.test/file.zip", "../../file.zip", mode == "size" ? 999 : 4, Incognito:true);
            var ready = await handoff.HandleAsync(req,default);
            if (mode is "html" or "size")
            { if (ready.Ok || store.All().Count != 0) throw new Exception("Unsupported browser file accepted."); return; }
            if (!ready.Ok || queue.Jobs.Single().State != JobState.BrowserPending) throw new Exception("Preparation started a download or failed.");
            var duplicate = await handoff.HandleAsync(req,default);
            if (duplicate.JobId != ready.JobId || queue.Jobs.Count != 1) throw new Exception("Duplicate preparation created another job.");
            if (mode == "abort")
            {
                await handoff.HandleAsync(req with { Command="abort" },default);
                if (queue.Jobs.Count != 0) throw new Exception("Pending record was not removed."); return;
            }
            var commit = await handoff.HandleAsync(req with { Command="commit" },default);
            if (!commit.Ok) throw new Exception("Commit failed.");
            for (int i=0;i<100 && queue.Jobs.Single().State != JobState.Completed;i++) await Task.Delay(20);
            if (queue.Jobs.Single().State != JobState.Completed) throw new Exception("Committed job did not download.");
            var result = await handoff.HandleAsync(req with { Command="commit" },default);
            if (!result.Ok || queue.Jobs.Count != 1 || !File.ReadAllBytes(queue.Jobs.Single().Destination).SequenceEqual(new byte[]{1,2,3,4})) throw new Exception("Idempotence or bytes failed.");
            await handoff.HandleAsync(req with { Command="abort" },default);
            if (queue.Jobs.Count != 1) throw new Exception("Abort removed an already committed job.");
        }
        finally { await queue.StopAsync(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root,true); }
    }
    private sealed class ProbeHandler(string mode):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken ct)
        {
            bool probe=req.Headers.Range?.Ranges.Single().To==0;
            var response=new HttpResponseMessage(probe?HttpStatusCode.PartialContent:HttpStatusCode.OK)
                {Content=new ByteArrayContent(probe?new byte[]{1}:new byte[]{1,2,3,4})};
            response.Headers.ETag=new EntityTagHeaderValue("\"v1\"");
            response.Content.Headers.ContentType=new MediaTypeHeaderValue(mode=="html"?"text/html":"application/zip");
            if(probe)response.Content.Headers.ContentRange=new ContentRangeHeaderValue(0,0,4);
            return Task.FromResult(response);
        }
    }
}
