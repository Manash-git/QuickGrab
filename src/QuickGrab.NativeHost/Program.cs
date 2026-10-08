using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using QuickGrab.BrowserProtocol;

if (args.Length == 1 && args[0] is "--register" or "--unregister")
{
    try
    {
        var manifestPath = Path.Combine(AppContext.BaseDirectory, "com.quickgrab.bridge.json");
        var registryPaths = new[] { @"Software\Google\Chrome\NativeMessagingHosts\", @"Software\Microsoft\Edge\NativeMessagingHosts\" };
        if (args[0] == "--register")
        {
            var manifest = new { name = Protocol.HostName, description = "QuickGrab browser bridge", path = Path.Combine(AppContext.BaseDirectory, "QuickGrab.NativeHost.exe"), type = "stdio", allowed_origins = new[] { ExtensionIdentity.Origin } };
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest), new UTF8Encoding(false));
            foreach (var registryPath in registryPaths)
            {
                using var key = Registry.CurrentUser.CreateSubKey(registryPath + Protocol.HostName);
                key.SetValue("", manifestPath);
            }
            Console.WriteLine("QuickGrab registered for Chrome and Edge for this Windows account.");
            Console.WriteLine("Extension ID: " + ExtensionIdentity.Id);
            Console.WriteLine("Keep this folder in place. Next load the extension folder in your browser.");
        }
        else
        {
            foreach (var registryPath in registryPaths)
            {
                using var key = Registry.CurrentUser.OpenSubKey(registryPath + Protocol.HostName);
                if (string.Equals(key?.GetValue("") as string, manifestPath, StringComparison.OrdinalIgnoreCase))
                    Registry.CurrentUser.DeleteSubKeyTree(registryPath + Protocol.HostName, false);
            }
            Console.WriteLine("This copy's browser registration was removed. Download data was kept.");
        }
        return 0;
    }
    catch (Exception ex) { Console.Error.WriteLine("Setup failed: " + ex.GetType().Name + ". Use a writable extracted folder."); return 1; }
}
// Reject callers outside the packaged extension. Also enforced by browser host manifest.
if (args.Length == 0 || args[0] != ExtensionIdentity.Origin) return 1;
try
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    var request = await Protocol.ReadAsync<BrowserRequest>(Console.OpenStandardInput(), deadline.Token);
    Protocol.Validate(request);
    using var pipe = new NamedPipeClientStream(".", Protocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    try { await pipe.ConnectAsync(700, deadline.Token); }
    catch (TimeoutException)
    {
        var app = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "app", "QuickGrab.exe"));
        if (!File.Exists(app)) throw new FileNotFoundException("QuickGrab app is missing.");
        Process.Start(new ProcessStartInfo(app) { UseShellExecute = false, ArgumentList = { "--browser-start" } });
        await pipe.ConnectAsync(12000, deadline.Token);
    }
    await Protocol.WriteAsync(pipe, request, deadline.Token);
    var reply = await Protocol.ReadAsync<BrowserReply>(pipe, deadline.Token);
    await Protocol.WriteAsync(Console.OpenStandardOutput(), reply, deadline.Token);
    return 0;
}
catch
{
    // stdout must contain only framed protocol, never exception details or URLs.
    try { await Protocol.WriteAsync(Console.OpenStandardOutput(), new BrowserReply(false, "unavailable", Message: "QuickGrab is unavailable. Open version 0.2.0 and check browser registration."), CancellationToken.None); }
    catch { }
    return 1;
}
