# QuickGrab project handbook

Architecture and cumulative build history

Software baseline 0.2.0  |  Documentation revision 1  |  7 October 2026

QuickGrab is a personal Windows download manager built incrementally in C# and WPF. This handbook explains how the current implementation works, how to install and maintain it, and what changed in every documented build from v0.1.1 through v0.2.0.

The desktop download, resume, speed-limit and close functions have been confirmed working by the user. Browser integration is implemented and has automated test coverage, but still needs an end-to-end Windows browser check. QuickGrab is not yet a complete IDM replacement or a commercial-ready release.

## Scope and current status

| Capability | Current position |
| --- | --- |
| HTTP and HTTPS | Implemented with streamed file writes and a persistent queue |
| Pause and resume | Direct range resume or verified-prefix fallback |
| Crash recovery | Checkpoint restoration; user chooses Resume after reopening |
| Speed control | One shared HTTP payload limit; 0 means unlimited |
| Chrome and Edge | Unpacked extension and native bridge; Windows acceptance pending |
| Incognito and InPrivate | Implemented with explicit browser permission; desktop history persists |
| Segmentation and media pages | Planned; not implemented in v0.2.0 |

## Reading guide

Pages 2 to 7 cover technology, architecture, the engine, data, setup and verification. Pages 8 to 11 contain the cumulative release record in version order. Page 12 defines the documentation policy for future builds and the remaining roadmap.

The earlier v0.1 foundation introduced basic downloading, persistence and the desktop UI. The software was renamed from ClearDownload to QuickGrab before v0.1.1; the old local-data folder name is deliberately retained for compatibility.

# Technology stack and prerequisites

| Layer | Implemented choice | Purpose |
| --- | --- | --- |
| Language | C# on .NET 10 | Desktop app, engine, protocol and native host |
| Interface | WPF and XAML | Windows download table, categories and dialogs |
| UI structure | Event handlers and observable rows | Small shell with INotifyPropertyChanged; not a full MVVM framework |
| Networking | HttpClient and SocketsHttpHandler | Asynchronous HTTP streams and bounded timeouts |
| Database | Microsoft.Data.Sqlite 10.0.12 | Parameterized SQL over local SQLite |
| SQLite bindings | SQLitePCLRaw 3.0.5 | Native SQLite provider; bundled SQLite 3.53.4 |
| Extension | JavaScript ES modules, HTML, CSS | Manifest V3 service worker and popup |
| Local transport | Native messaging and named pipes | Length-prefixed JSON; current-user-only pipe |
| Testing | C# console harness and Node.js | 31 C# and 7 JavaScript tests at v0.2.0 |

## To run the packaged build

- Windows x64 on an Intel/AMD PC; Windows 10 and 11 are the project targets. Check the exact OS build against the installed .NET runtime requirements.
- Install the .NET 10 Desktop Runtime x64. The plain base runtime alone does not provide WPF.
- Use a writable extraction folder, writable download destination and enough disk space for final and partial files.
- Chrome or Edge is needed for browser capture. Unpacked extensions and native messaging must be permitted by any browser-management policies.

## To develop or rebuild

Install the .NET 10 SDK x64 and use PowerShell or a terminal. Visual Studio with the .NET desktop workload is optional. Node.js is required only to run the JavaScript tests; it is not required to use the extension. Builds restore NuGet dependencies, so an initial restore needs network access.

WPF was selected for a Windows-only desktop interface; C# keeps asynchronous I/O and maintenance approachable. SQLite avoids a database server. CommunityToolkit.Mvvm, xUnit, yt-dlp and FFmpeg were discussed as options but are not current runtime dependencies.

# Architecture and module responsibilities

The desktop process owns the queue and database. The extension never writes the database directly. A separate native host translates browser messages into requests over a current-user-only named pipe.

| Module or file | Responsibility |
| --- | --- |
| DownloadManager.App | WPF application, single-instance guard and error dialogs |
| MainWindow and JobRow | URL entry, actions, categories and 500 ms progress refresh |
| DownloadQueue | Concurrency, cancellation, durable job transitions and browser commits |
| HttpDownloadEngine | Requests, resume validation, writes, checkpoints, retries and finalization |
| JobStore | SQLite records, settings and additive schema migration |
| BandwidthLimiter | Shared pacing across active HTTP transfers |
| BrowserHandoff and BrowserPipeServer | Public-link probe, pending jobs and named-pipe dispatch |
| QuickGrab.BrowserProtocol | Bounded message framing, request validation and extension identity |
| QuickGrab.NativeHost | Origin check, registry setup, pipe connection and app launch |
| extension folder | Browser event capture, two-step handoff, popup and recovery |

## Desktop download flow

URL entry and destination selection create a durable queue job. The queue starts a worker when a slot is available. The engine validates the response, streams bytes through the limiter into a partial file, checkpoints to SQLite, and finally renames the verified complete file. The UI reads durable state plus live byte counts.

## Browser download flow

The extension pauses an eligible browser download and sends prepare. QuickGrab probes the public URL and saves BrowserPending. After receiving ready, the extension cancels the browser copy and sends commit. Commit queues that same job. If preparation fails, the extension attempts to restore the browser transfer; after cancellation, the saved QuickGrab job is the recovery point.

No public HTTP API or unauthenticated localhost port is exposed. The desktop process serves the named pipe and stops it during orderly application shutdown.

# Download engine and recovery design

## Transfer model

Each file currently uses one asynchronous stream. Several files may run concurrently: the setting allows 1 to 8 active jobs and defaults to 3. This is multi-download concurrency, not segmented downloading within one file. A 32 KiB application buffer keeps memory independent of file size.

## Three resume decisions

- Strong ETag: send Range starting at the saved byte offset with an If-Range entity tag. Require a matching 206 response, range boundaries, total size and validator.
- Eligible Last-Modified: when no ETag was supplied and the response Date is at least 60 seconds later, save the timestamp and use it for If-Range on a later request.
- No saved usable validator: read a complete response and compare every saved prefix byte with the partial file. Keep the saved percentage while Verifying, then append from that offset using the same response stream. This costs network traffic for the existing prefix.

## Checkpoint and completion order

- Write data, then flush file contents to disk before recording the new byte offset in SQLite. Checkpoint after 1 MiB or 2 seconds of transfer, and on pause or completion.
- On recovery, reject a missing or too-short partial file and discard only trailing bytes beyond the durable checkpoint. A small amount of uncommitted transfer may be repeated.
- At completion, check the expected size when known, calculate SHA-256, save Finalizing with the hash, rename without overwrite, then mark Completed.
- If termination occurs after rename but before the final database update, compare the final file length and saved hash before marking it complete.

## States and retry behavior

Job states are Queued, Connecting, Downloading, Paused, NeedsRestart, Failed, Finalizing, Completed, Verifying and BrowserPending. Active/interrupted transfer states recover to Paused on startup. BrowserPending stays available for handoff recovery.

Selected network failures and HTTP 408, 429, 500, 502, 503 and 504 get up to three retries after the initial attempt. Backoff includes jitter unless Retry-After is supplied. Header and read deadlines are 30 seconds; connection timeout is 20 seconds. Changed content and invalid range responses must not be blindly appended.

The speed limiter paces payload writes after network reads; socket buffering and a buffer per active job can run ahead. A local SHA-256 is a fingerprint, not publisher authentication. No 1 Gbps throughput guarantee or physical power-loss certification has been established.

# Database files and application interfaces

The database remains at %LOCALAPPDATA%\ClearDownload\downloads.db. Retaining the legacy folder avoids losing existing records after the QuickGrab rename. Partial files stay beside their chosen destination as filename.job-id.part.

| jobs column | Meaning |
| --- | --- |
| id | Primary-key job ID; browser request UUID normalized to the same identity |
| url | Source URL, currently plaintext, including possible query tokens |
| destination and category | Unique case-insensitive destination and file category |
| state and bytes | Job status and durably checkpointed prefix length |
| total | Expected complete length, or null if unknown |
| etag and last_modified | Saved validators for direct resume |
| sha256 | Final-file fingerprint and rename recovery value |
| error and created_utc | Actionable state message and UTC creation timestamp |

The settings table stores key/value integers for concurrency and limitKib. SQLite uses WAL, synchronous=FULL and a 5000 ms busy timeout. Schema version 2 adds nullable last_modified transactionally; browser integration adds a state but no column.

## Core entry points

```text
queue.Add(url, absoluteDestination)
queue.Pause(jobId)
queue.Resume(jobId)
queue.Restart(jobId)  // after user confirmation
await queue.StopAsync()
```

PrepareBrowser, CommitBrowser and AbortBrowser support the browser transaction. Preparing does not start the download. Duplicate request IDs return the existing job; commit does not restart an already committed job. Abort deletes only a zero-byte BrowserPending record.

## Browser protocol

Requests use version 1 with command, requestId and optional url, suggestedName, expectedBytes, mime and incognito fields. Commands are ping, prepare, commit and abort. Replies include ok, status, optional jobId/message, and the software version. Messages are UTF-8 JSON preceded by a four-byte little-endian length, capped by QuickGrab at 64 KiB.

Source folders are src/DownloadManager.App, src/DownloadManager.Core, src/QuickGrab.BrowserProtocol and src/QuickGrab.NativeHost. Tests are under tests/DownloadManager.Tests and tests/browser. The extension is plain JavaScript; there is no TypeScript compilation step.

# Installation build and everyday operation

## Install the desktop application

- Close the old version and extract the complete release ZIP to a stable writable folder. Keep app, bridge and extension together.
- Install .NET 10 Desktop Runtime x64 if needed, then run app/QuickGrab.exe. Do not move just the EXE away from its adjacent libraries.
- For a manual download, paste a direct HTTP/HTTPS file URL, click Add download and choose a new destination. Existing files are not overwritten.

## Set up Chrome and Edge

- Run RegisterBrowser.cmd from the extracted root. It registers the native host under your Windows account for both browsers; elevation is not required.
- Open chrome://extensions or edge://extensions, enable Developer mode, select Load unpacked and choose the extension folder.
- In extension Details, enable Allow in incognito or Allow in InPrivate. The extension cannot enable this permission itself.
- Open the extension popup and click Test connection. Expect Connected to QuickGrab 0.2.0. Leave private capture enabled if wanted.
- Test a larger public direct-file link normally and in a private window. Right-click Download with QuickGrab provides a manual alternative.

Browser jobs go to %USERPROFILE%\Downloads\QuickGrab with a short unique filename suffix. A cancelled entry in the browser list can be the expected result of a successful handoff. If the app folder moves, run registration again and reload the unpacked extension as needed.

## Build and test from source

```text
.\scripts\CheckReleaseDocs.ps1
.\scripts\Build.ps1
.\scripts\Test.ps1
.\scripts\TestBrowser.ps1
.\scripts\Publish.ps1
```

The scripts use dotnet and, for browser tests, Node.js. If PowerShell blocks a script, run the equivalent commands shown inside it; changing system policy is not required. Publish.ps1 -SelfContained includes the runtime and makes runtime updates the project maintainer’s responsibility.

## Upgrade backup and removal

Close the app before copying its database and associated WAL/SHM files as a consistent backup. Preserve partial files and their paths. Do not run an older version against newer states or schema without a tested downgrade procedure. To remove browser integration, run UnregisterBrowser.cmd and remove the extension in each browser; download data is retained.

# Security testing and troubleshooting

## Security and privacy boundaries

The app validates HTTP/HTTPS URLs, rejects embedded credentials, uses normal TLS validation, parameterized SQL and bounded protocol messages. Native-host origins are allowlisted and the pipe is restricted to the current user. Downloaded files are never executed automatically. This is not a security audit or a production-readiness claim.

Incognito/InPrivate does not erase QuickGrab history or files. Accepted URLs, including signed query tokens, are currently plaintext in the local database. The extension does not import cookies, authorization headers, passwords or page contents. Secure URL storage, Windows attachment handling and code signing remain future work.

## Verified and still pending

At v0.2.0, 31 C# tests and 7 JavaScript handoff tests passed. Coverage includes bytes/hashes, range validation, timestamp resume, verified prefixes, forced-process termination, migration, collision protection, protocol bounds, queue limits and handoff failures. Test result files are included in docs.

The user confirmed the earlier desktop download, resume, speed-limit and close fixes. Windows Chrome/Edge registration, actual browser interception and private-window capture still need acceptance testing. Physical power loss, Windows restart under every condition, disk-full behavior and maximum throughput are not established by these tests.

| Symptom | Action |
| --- | --- |
| NeedsRestart | Retry Resume in the current app; preserve the part. Restart only after accepting loss of partial data. |
| Verifying at the old percentage | Wait while the saved prefix is checked. Network use is expected. |
| BrowserPending | Check whether the browser copy is cancelled; if so, Resume the saved QuickGrab job. |
| Connection test fails | Close old app versions, register this folder, open v0.2.0 and retry. |
| Private capture fails | Enable both browser private permission and the extension private-capture setting. |
| Unexpected dialog | Send the new error details or the URL-redacted report from the logs folder. |

## Use and distribution

Download only authorized content and respect site terms and server rate limits. No DRM or access-control bypass is implemented. Review robots.txt if future crawling is introduced. Original project code is MIT-licensed; Microsoft.Data.Sqlite is MIT and SQLitePCLRaw is Apache-2.0. Retain the bundled license notices and review actual media-tool licenses before redistribution.

# Build 0.1.1 progress display and error reporting

Version 0.1.1  |  Release record 2026-10-07  |  Windows x64

## New implementations

- Added error reports with exception type, message and stack trace; URL strings are redacted. Reports are written to the local logs folder, with a temporary-folder fallback.

## Modifications and reasons

Set progress and other read-only display bindings to Mode=OneWay. A display control must read computed progress without trying to write into a read-only property.

Replaced the generic startup message and added error reporting to UI actions and refresh failures. The old message suggested disk or permission problems for unrelated interface exceptions.

## Bug fixes

- Addressed the progress-binding defect associated with the error seen when adding a download. The original generic message did not expose the precise exception, so the exact original cause was not independently captured.
- Removed the misleading implication that every startup/interface failure was a local-data access problem.

## Validation

- Windows-targeted app compiled; the user confirmed the app worked after installing this build.
- The 17 engine tests were inherited from v0.1; a new 17-test run was not claimed for this UI-only patch.

## Upgrade impact

No database migration. Existing queue records and partial files were preserved.

## Remaining limitations

- Pause/resume still required a strong ETag. The next release addressed servers that omitted one.

# Build 0.1.2 resume compatibility and saved data verification

Version 0.1.2  |  Release record 2026-10-07  |  Windows x64

## New implementations

- Added Last-Modified based range resume when no ETag is supplied and the server date supports treating the timestamp as a strong validator.
- Added the Verifying state and a fallback that compares the complete saved prefix with the current response before appending.
- Added last_modified storage and a transactional, additive schema migration to version 2.

## Modifications and reasons

Resume may retry a NeedsRestart job without clearing its partial data. Only explicit Restart resets the saved bytes. Older jobs rejected solely for missing ETags should be recoverable.

Updated progress details to explain direct resume versus verification. During verification, saved file progress stays unchanged even though network data is being read.

## Bug fixes

- Removed the blanket refusal to resume a partial file simply because it had no strong ETag.
- Extended reopen/crash recovery to jobs without a saved validator while retaining mismatch protection.

## Validation

- 25 automated C# tests passed, including real HTTP Last-Modified resume, forced-process termination, no-validator recovery, migration and changed-content rejection.
- The user confirmed Resume worked after installing this build.

## Upgrade impact

Adds nullable last_modified to jobs; existing offsets and destinations remain intact. Older jobs may verify their saved prefix before obtaining a usable validator.

## Remaining limitations

- Fallback verification rereads the saved prefix over the network. It is not a bandwidth-free range resume.
- Changed server content, missing partial files or invalid range responses can still require explicit restart.

# Build 0.1.3 clean application shutdown

Version 0.1.3  |  Release record 2026-10-07  |  Windows x64

## New implementations

- Added a dedicated FinishClosingAsync shutdown routine scheduled through the WPF dispatcher.

## Modifications and reasons

Cancel the first Closing event and dispatch shutdown work after that event returns. Stop and checkpoint jobs before the final Close call. Awaiting an already completed StopAsync task does not yield; the old code could call Close while WPF was already closing the same window.

Keep the existing closing/canClose guards and stop the refresh timer during shutdown. Repeated X clicks must not start multiple shutdown sequences.

## Bug fixes

- Fixed InvalidOperationException reporting that Close, Show or visibility changes cannot occur while a window is closing.

## Validation

- Windows app was cross-compiled and packaged. The engine was unchanged; the 25-test result belonged to v0.1.2, not a new UI test run.
- The user confirmed that the close behavior worked in v0.1.3.

## Upgrade impact

No schema change. Queue data and partial downloads remain compatible.

## Remaining limitations

- The app does not continue downloading as a background Windows service after it closes.

# Build 0.2.0 chrome and edge browser integration

Version 0.2.0  |  Release record 2026-10-07  |  Windows x64

## New implementations

- Added a Manifest V3 extension with automatic capture of eligible public file downloads and a Download with QuickGrab context-menu action.
- Added split incognito/InPrivate extension contexts, private-capture settings and a connection-test popup.
- Added QuickGrab.BrowserProtocol, QuickGrab.NativeHost, a current-user named-pipe server and per-user registration/removal scripts.
- Added prepare, commit, abort and ping messages; BrowserPending jobs; request-ID based duplicate protection and handoff recovery.

## Modifications and reasons

The desktop app starts the pipe server, accepts browser jobs and stops the bridge during shutdown. The native host can launch the app with --browser-start. Browser downloads need a controlled entry point into the same durable queue.

Persist a pending job before cancelling the browser transfer; start it only after commit. A failed bridge must not silently discard the download or start duplicate browser/app copies.

Package app, bridge, extension and setup instructions together. The browser needs a registered native executable as well as the extension.

## Bug fixes

- No separate user-reported v0.2.0 defect was resolved in this feature release. Earlier download, resume, UI and close fixes were retained.
- Failure handling prevents duplicate preparation/commit, removes aborted pending records where safe and restores the browser transfer when handoff fails before cancellation. These are new defensive behaviors, not previously confirmed user bugs.

## Validation

- 31 C# tests and 7 JavaScript handoff tests passed; the WPF app and native host were cross-compiled for Windows x64.
- Actual Chrome/Edge interception, registry installation and private-window behavior have not yet been confirmed on the user PC.

## Upgrade impact

Adds the BrowserPending state, not a new database column. Register the native host for this extracted folder and load the extension in each browser. Keep the app, bridge and extension folders together.

## Remaining limitations

- Only compatible public HTTP/HTTPS file links are supported. Browser cookies, authenticated requests, form-generated files, blob/data URLs and media-page extraction are not reproduced.
- Incognito/InPrivate jobs and URLs remain in desktop history and on disk. Private browser mode does not make QuickGrab storage private.

# Documentation policy and next milestones

Every future delivered build must include an updated cumulative history from v0.1.1 through that build, in ascending version order. Add a new record; do not replace previous release records or silently recast an unverified claim as confirmed.

## Required release record

- Version, date, release purpose and target platform.
- New implementations: name the features actually present in that build.
- Modifications and reasons: state what changed and why.
- Bug fixes: describe the symptom, cause when known, fix and verification. If none, say so.
- Validation: distinguish automated tests, compilation, manual Windows/browser checks and user confirmation.
- Upgrade/data impact: schema changes, compatibility, installation or registration steps.
- Known limitations, security/privacy impact and remaining acceptance checks.

## Release workflow

Update docs/releases.json, regenerate or update the cumulative handbook and CHANGELOG.md, and check the app/native-host/extension versions. Build.ps1 and Publish.ps1 call CheckReleaseDocs.ps1 to reject a missing or incomplete current release record. This gate checks record presence and structure, not whether the prose is accurate or tests truly passed.

Review the current architecture rather than treating old plans as implemented features. Run relevant tests, record evidence, package the cumulative documentation with the software, and identify outstanding Windows checks in the delivery message. A documentation-only revision must be labelled as such without inventing a new software build.

## Prioritized next work

- Validate v0.2.0 end to end in Windows Chrome and Edge, including incognito/InPrivate and interrupted handoffs.
- Harden URL storage and Windows attachment handling; improve installer, signing and diagnostics.
- Add supported media-page extraction and Firefox integration as separate tested increments.
- Implement segmented downloading only after range/validator tests and disk-space planning. Add scheduler, optional clipboard monitoring, explicit proxy/auth/cookie workflows and coordinated limits later.

## References and maintained project records

Current source, project files, extension manifest and test reports are the implementation records. The release history records the changes and user confirmations made during development; v0.1 is background only.

https://learn.microsoft.com/en-us/dotnet/desktop/wpf/

https://dotnet.microsoft.com/en-us/download/dotnet/10.0

https://www.rfc-editor.org/rfc/rfc9110.html

https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging

https://developer.chrome.com/docs/extensions/reference/manifest/incognito

https://learn.microsoft.com/en-us/microsoft-edge/extensions/developer-guide/native-messaging
