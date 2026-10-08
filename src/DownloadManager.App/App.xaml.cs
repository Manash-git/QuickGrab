using System.Threading;
using System.Windows;
namespace DownloadManager.App;
public partial class App : Application
{
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            ErrorReporter.Show("Unexpected interface error", args.Exception);
            Shutdown(1);
        };
        instance = new Mutex(true, @"Local\ClearDownload.Personal.v01", out bool created);
        if (!created) { if (!e.Args.Contains("--browser-start")) MessageBox.Show("QuickGrab is already running."); Shutdown(); return; }
        try { new MainWindow().Show(); }
        catch (Exception ex) { ErrorReporter.Show("Starting QuickGrab", ex); Shutdown(1); }
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
