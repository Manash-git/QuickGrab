# QuickGrab 0.2.0 — browser integration preview

A personal Windows download manager in C#/.NET 10 and WPF. This is the browser-integration increment of the roadmap, **not the complete planned MVP or an IDM replacement**.

## Implemented

- Pasted HTTP/HTTPS file URLs, streamed to disk with bounded buffers.
- Download queue; 1–8 simultaneous jobs (default 3).
- Pause/resume using byte ranges with a strong ETag or usable Last-Modified timestamp.
- Without a usable validator, verify the entire saved prefix before continuing.
- SQLite queue and durable file checkpoints; interrupted jobs reopen paused.
- One shared bandwidth limit for HTTP payloads, adjustable during transfers.
- Categories, progress, speed, destination details, and completed SHA-256 display.
- Bounded retries for selected temporary failures; respects Retry-After.
- Collision protection: existing destinations are never overwritten.
- Safe filenames suggested from URL paths; user selects the actual destination.

## Not implemented yet

Firefox integration, media-page extraction, segmented transfer,
clipboard capture, scheduler, custom proxy/authentication/cookie settings, automatic
updates, installer, and expected-checksum comparison. Direct `.mp4`/`.mp3` file URLs
work as ordinary downloads; video page URLs do not extract video. A media webpage
may be saved as HTML, so use an actual file URL in this version.

## Chrome, Edge and incognito/InPrivate

Read **[docs/BROWSER-SETUP.md](docs/BROWSER-SETUP.md)** for complete setup. Run
RegisterBrowser.cmd, load the extension folder into each browser, and enable the
browser's private-window permission. Direct public files are supported; cookies and
media-page extraction are not. Incognito downloads remain in the desktop history.

## Run on Windows

1. Extract the complete ZIP to a normal writable folder.
2. Install the **.NET 10 Desktop Runtime, x64** from
   https://dotnet.microsoft.com/en-us/download/dotnet/10.0
   (the plain .NET runtime does not include WPF).
3. If this package includes `app/QuickGrab.exe`, run `app/Run.cmd`.
4. Paste a direct HTTP/HTTPS URL, click **Add download**, and choose a new filename.
5. Select a row to pause/resume or inspect details. Apply concurrency and speed
   settings at the bottom. `1024 KiB/s` is approximately `1 MiB/s`; `0` is unlimited.

Use Windows 11 x64 or a Windows 10 x64 build compatible with the chosen .NET 10 runtime.
Confirm the exact Windows build in the current .NET OS support matrix; this package
has not been manually validated on either Windows version in the build environment.

Download official tools only from their publishers. The executable is unsigned.
No installer or code-signing certificate is provided in this increment.

## Build from source on Windows

Install the **.NET 10 SDK, x64**, then open PowerShell in this directory:

```powershell
# Compile and run the independent core tests
.\scripts\Test.ps1

# Compile the WPF app
.\scripts\Build.ps1

# Run from source
dotnet run --project .\src\DownloadManager.App

# Create a portable folder including the runtime (larger)
.\scripts\Publish.ps1 -SelfContained
```

If local PowerShell policy prevents script execution, use the underlying `dotnet`
commands shown in the scripts; no policy changes are required. Visual Studio is
optional; if used, install its .NET desktop development workload and a version
supporting .NET 10. Open `QuickGrab.slnx`.

## Data and recovery

Queue and settings: `%LOCALAPPDATA%\ClearDownload\downloads.db` plus SQLite WAL/SHM.
Partials live beside their final destination: `<filename>.<job-id>.part`.
Do not delete or manually edit these while a job is active. Keep them when upgrading.

On reopening, interrupted jobs are **Paused**. Select **Resume** to continue.
If the server changed the file or rejected a validated range,
**NeedsRestart** is shown. **Restart** asks before discarding partial data.
Closing the app cancels work, saves checkpoints, and stops downloads. It does not
run as a Windows service or automatically launch after reboot.

A SHA-256 shown at completion is a fingerprint, **not evidence that the publisher
or file is trustworthy**. Expected publisher-hash verification is a later feature.

URLs are currently stored in the local database, including any query tokens.
Do not share this database; do not use sensitive signed URLs in this prototype.
Credentials in URLs are rejected and browser cookies are not imported. A subsequent
security increment should encrypt sensitive URL components using Windows DPAPI.

## Architecture and next work

Read [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for modules, API, schema, recovery
ordering, and segmentation design. Read [docs/TESTING.md](docs/TESTING.md) for the
Windows and crash-test checklist. Read [docs/ROADMAP.md](docs/ROADMAP.md) for milestones.

This original project is MIT-licensed. Dependencies retain their own licenses.
Use only content you are authorized to download. Do not bypass DRM or access
controls, and respect server limits and applicable site terms.

The legacy data-folder and single-instance identifiers are retained so existing downloads and settings remain available after the QuickGrab rename.

## 0.1.1 fix

Progress and read-only display bindings explicitly use OneWay. This prevents WPF
from attempting to write progress into the read-only Percent property when a row
appears. Startup, UI and action errors now show the underlying exception and save
a URL-redacted diagnostic report. Existing queue data is retained.

The reported screenshot used a generic startup error; the precise cause on that
PC is not confirmed. This release fixes an identified binding problem and provides
diagnostics if another problem remains. Windows UI execution still needs validation.

## 0.1.2 resume update

Close the older app and extract this release into a separate folder. Existing data
and partial files stay in their current locations. Select the old job, including
a NeedsRestart job, and click **Resume**, not Restart. Do not delete its `.part` file.
If you already confirmed Restart and discarded the old partial, those bytes cannot
be recovered by this update.

There are two resume paths:

- **Direct range resume:** requests only remaining bytes using a saved strong ETag
  or an eligible Last-Modified date. Saved progress advances immediately.
- **Verify then continue:** when no validator was saved, status is **Verifying**.
  The saved progress stays unchanged while the server's initial bytes are compared
  against the existing file. Then appending continues at the saved offset. This
  rereads the saved portion over the network; it does not discard or rewrite it.
  A changed prefix is rejected. The same response supplies both verification and
  new bytes, so there is no unsafe second-request version gap.

Older jobs from 0.1.1 have no saved Last-Modified value. They may need verification
on their first resumed transfer. Subsequent pauses can use a timestamp if the server
supplies an eligible one. A missing validator never automatically discards progress.

Schema migration adds a nullable last_modified column and preserves job records.
Do not run an older QuickGrab version against the upgraded data.

## 0.1.3 close-button fix

The final Close is dispatched after the original Closing event returns. This avoids
WPF close reentrancy when StopAsync completes synchronously on an idle queue. Active
downloads still stop and checkpoint before the final close. Multiple close clicks
are ignored while shutdown is in progress. Windows validation: close with an empty
queue, completed/paused jobs, then active downloads; reopen and resume the latter.

## Project documentation

Read [the project handbook](docs/PROJECT-HANDBOOK.md) for architecture, stack, prerequisites, setup, privacy, testing and recovery. [Build history](CHANGELOG.md) records every version from v0.1.1 onward. Future delivered builds must update these files and pass `scripts/CheckReleaseDocs.ps1`.
