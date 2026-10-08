using System.Diagnostics;
namespace DownloadManager.Core;

// One shared limiter across every active HTTP transfer. Limits payload delivery, not TCP overhead.
public sealed class BandwidthLimiter
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private long bytesPerSecond;
    public long BytesPerSecond { get => Interlocked.Read(ref bytesPerSecond); set => Interlocked.Exchange(ref bytesPerSecond, Math.Max(0, value)); }
    public async Task ConsumeAsync(int bytes, CancellationToken ct)
    {
        if (BytesPerSecond == 0) return;
        await gate.WaitAsync(ct);
        try
        {
            var watch = Stopwatch.StartNew();
            double remaining = bytes;
            while (remaining > 0)
            {
                var rate = BytesPerSecond;
                if (rate == 0) return;
                var delay = TimeSpan.FromSeconds(Math.Min(0.1, remaining / rate));
                watch.Restart();
                await Task.Delay(delay, ct);
                remaining -= watch.Elapsed.TotalSeconds * rate;
            }
        }
        finally { gate.Release(); }
    }
}
