using System.IO;
using System.IO.Pipes;
using DownloadManager.Core;
using QuickGrab.BrowserProtocol;
namespace DownloadManager.App;

internal sealed class BrowserPipeServer : IAsyncDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly Task loop;
    public BrowserPipeServer(BrowserHandoff handoff)
    {
        loop = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    await using var pipe = new NamedPipeServerStream(Protocol.PipeName, PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(stop.Token);
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                    deadline.CancelAfter(TimeSpan.FromSeconds(20));
                    var request = await Protocol.ReadAsync<BrowserRequest>(pipe, deadline.Token);
                    var reply = await handoff.HandleAsync(request, deadline.Token);
                    await Protocol.WriteAsync(pipe, reply, deadline.Token);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or System.Text.Json.JsonException or UnauthorizedAccessException)
                {
                    // Malformed/disconnected clients must not crash the desktop app.
                    if (!stop.IsCancellationRequested) await Task.Delay(100, stop.Token).ConfigureAwait(false);
                }
            }
        });
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        try { await loop; } catch (OperationCanceledException) { }
        stop.Dispose();
    }
}
