# Verification and debugging

## Verified in the build environment

- Linux .NET 10 SDK 10.0.401 cross-compilation of the WPF project: zero warnings/errors.
- Windows x64 framework-dependent publish.
- 25 automated tests passed in the included console test harness.
- Tests use both controlled HttpMessageHandler responses and a real loopback HTTP
  server. One test forcibly kills a separate download process, then reopens the
  database and verifies the resumed file hash.

Test coverage:
1. Real HTTP streaming and pause/resume.
2. Real HTTP Last-Modified range resume.
3. Killed process without validators retains and verifies prefix.
4. Killed download process recovers from SQLite checkpoint.
5. Complete download matches SHA-256.
6. Pause and resume uses Range + If-Range.
7. Ignored Range never appends.
8. Changed ETag never appends.
9. Wrong Content-Range never appends.
10. Weak validator resumes after full prefix verification.
11. Missing validator preserves prefix and resumes.
12. Changed prefix is rejected without modifying saved file.
13. Last-Modified resume requests only remaining bytes.
14. Changed Last-Modified is rejected.
15. Legacy database migration preserves checkpoints.
16. NeedsRestart jobs can retry resume without discarding data.
17. Uncommitted trailing bytes are discarded.
18. Short partial file requires restart.
19. Final rename crash window recovers.
20. Existing destination is not overwritten.
21. Transient server errors retry.
22. Global limiter combines simultaneous transfers.
23. Queue concurrency is bounded.
24. Startup restores active jobs paused.
25. URL schemes and filenames are constrained.

Run `scripts/Test.ps1` or `dotnet run --project tests/DownloadManager.Tests -c Release`.
The executable returns nonzero on failure. This deliberately uses a small console
harness without a test framework dependency; migrate to xUnit as the suite grows.
The loopback server needs permission to listen on 127.0.0.1. No external server is
contacted by these tests.

## Not verified here

Windows UI rendering, Windows-specific disk behavior, actual Windows reboot,
physical power loss, real internet throughput, and clean Windows installation.
There is no claim of 1 Gbps saturation or production readiness.

## Windows acceptance checklist

1. Run on Windows 10 and Windows 11 x64; record exact build numbers and .NET runtime.
2. Inspect UI at 100%, 125%, and 150% scale, keyboard navigation and screen-reader labels.
3. Download a known file larger than 1 GiB; compare `Get-FileHash -Algorithm SHA256`
   to the publisher's expected value. Confirm bounded memory in Task Manager.
4. Pause/resume a strong-ETag/range-enabled origin. Confirm the final hash.
5. Close/reopen, then test Task Manager End Task, then Windows restart separately.
6. Disconnect/reconnect the network. Confirm retry or a clear resumable failure.
7. Test HTTP 200 instead of 206, changed content, expired links, no ETag,
   redirects, unknown lengths and zero-length files.
8. Use a small disposable test volume for disk-full behavior; do not fill a system disk.
9. Queue several files with concurrency 1 and 3; lower the setting during activity.
10. Apply a 1024 KiB/s limit; measure steady-state aggregate payload over 10+ seconds.
11. Test destination collisions, locked files, read-only directories and long paths.
12. Confirm no downloads start merely by reopening the app and no files auto-execute.

## Debugging

Set breakpoints in TransferAsync around header validation and Checkpoint, and in
DownloadQueue.RunAsync for failure mapping. Inspect job State/Bytes/ETag alongside
part-file size. Do not log full URLs, cookies, authorization headers, or response
bodies. Database URLs may contain sensitive tokens; redact before sharing anything.

For SQLite failures, preserve the database and its WAL/SHM together after closing
the app; do not delete state as a first troubleshooting step. A future diagnostics
panel should show sanitized structured events and correlation/job IDs.

## Packaging and release

`Publish.ps1` emits a folder; ship every file together. Framework-dependent builds
require .NET 10 Desktop Runtime x64. `-SelfContained` bundles the runtime and makes
its security updates the application's responsibility. Do not enable trimming for
WPF without separate compatibility work. Add per-user installation and signing
later; never remove user databases or partial files during upgrade/uninstall by
default. Browser extension/native-host registration will need its own installer tests.

## 0.1.3 scope and validation

This patch changes only WPF shutdown scheduling and the displayed version. The
25-test engine results in TEST-RESULTS.txt were recorded for 0.1.2; the engine is
unchanged. The updated Windows app is cross-compiled, but this environment cannot
execute the WPF window. Manual Windows checks still required:

- Close with no active transfers (StopAsync returns synchronously).
- Close with completed and paused jobs in the list.
- Close during a transfer, reopen and resume from the saved checkpoint.
- Click X repeatedly during shutdown; only one shutdown sequence should run.

Root cause: awaiting an already-completed StopAsync task does not yield execution.
The old finally block re-entered Close while WPF was still in the Closing event.
The new handler cancels that event and posts shutdown work to the dispatcher, so
the event returns before the final Close call.

## 0.2.0 browser integration validation

31 C# tests passed (including all prior engine tests and six new protocol/handoff
tests). Seven JavaScript handoff tests passed; see BROWSER-TEST-RESULTS.txt. Test
connection, normal/private automatic capture, right-click links and registration
on actual Windows Chrome/Edge before considering this integration validated there.
The native-host and WPF app are cross-compiled for Windows x64.

Commands: scripts/Test.ps1 and scripts/TestBrowser.ps1 (Node.js required for the
JavaScript tests only; not required for using the extension).
