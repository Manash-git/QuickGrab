# QuickGrab cumulative build history

Records are ordered from v0.1.1 to the latest build. See docs/PROJECT-HANDBOOK.md for architecture and setup.

## v0.1.1 — Progress display and error reporting

Release record: 2026-10-07 | Windows x64

### New implementations

- Added error reports with exception type, message and stack trace; URL strings are redacted. Reports are written to the local logs folder, with a temporary-folder fallback.

### Modifications and why

- Set progress and other read-only display bindings to Mode=OneWay. Why: A display control must read computed progress without trying to write into a read-only property.
- Replaced the generic startup message and added error reporting to UI actions and refresh failures. Why: The old message suggested disk or permission problems for unrelated interface exceptions.

### Bug fixes

- Addressed the progress-binding defect associated with the error seen when adding a download. The original generic message did not expose the precise exception, so the exact original cause was not independently captured.
- Removed the misleading implication that every startup/interface failure was a local-data access problem.

### Validation

- Windows-targeted app compiled; the user confirmed the app worked after installing this build.
- The 17 engine tests were inherited from v0.1; a new 17-test run was not claimed for this UI-only patch.

### Upgrade impact

No database migration. Existing queue records and partial files were preserved.

### Known limitations

- Pause/resume still required a strong ETag. The next release addressed servers that omitted one.

## v0.1.2 — Resume compatibility and saved data verification

Release record: 2026-10-07 | Windows x64

### New implementations

- Added Last-Modified based range resume when no ETag is supplied and the server date supports treating the timestamp as a strong validator.
- Added the Verifying state and a fallback that compares the complete saved prefix with the current response before appending.
- Added last_modified storage and a transactional, additive schema migration to version 2.

### Modifications and why

- Resume may retry a NeedsRestart job without clearing its partial data. Only explicit Restart resets the saved bytes. Why: Older jobs rejected solely for missing ETags should be recoverable.
- Updated progress details to explain direct resume versus verification. Why: During verification, saved file progress stays unchanged even though network data is being read.

### Bug fixes

- Removed the blanket refusal to resume a partial file simply because it had no strong ETag.
- Extended reopen/crash recovery to jobs without a saved validator while retaining mismatch protection.

### Validation

- 25 automated C# tests passed, including real HTTP Last-Modified resume, forced-process termination, no-validator recovery, migration and changed-content rejection.
- The user confirmed Resume worked after installing this build.

### Upgrade impact

Adds nullable last_modified to jobs; existing offsets and destinations remain intact. Older jobs may verify their saved prefix before obtaining a usable validator.

### Known limitations

- Fallback verification rereads the saved prefix over the network. It is not a bandwidth-free range resume.
- Changed server content, missing partial files or invalid range responses can still require explicit restart.

## v0.1.3 — Clean application shutdown

Release record: 2026-10-07 | Windows x64

### New implementations

- Added a dedicated FinishClosingAsync shutdown routine scheduled through the WPF dispatcher.

### Modifications and why

- Cancel the first Closing event and dispatch shutdown work after that event returns. Stop and checkpoint jobs before the final Close call. Why: Awaiting an already completed StopAsync task does not yield; the old code could call Close while WPF was already closing the same window.
- Keep the existing closing/canClose guards and stop the refresh timer during shutdown. Why: Repeated X clicks must not start multiple shutdown sequences.

### Bug fixes

- Fixed InvalidOperationException reporting that Close, Show or visibility changes cannot occur while a window is closing.

### Validation

- Windows app was cross-compiled and packaged. The engine was unchanged; the 25-test result belonged to v0.1.2, not a new UI test run.
- The user confirmed that the close behavior worked in v0.1.3.

### Upgrade impact

No schema change. Queue data and partial downloads remain compatible.

### Known limitations

- The app does not continue downloading as a background Windows service after it closes.

## v0.2.0 — Chrome and Edge browser integration

Release record: 2026-10-07 | Windows x64

### New implementations

- Added a Manifest V3 extension with automatic capture of eligible public file downloads and a Download with QuickGrab context-menu action.
- Added split incognito/InPrivate extension contexts, private-capture settings and a connection-test popup.
- Added QuickGrab.BrowserProtocol, QuickGrab.NativeHost, a current-user named-pipe server and per-user registration/removal scripts.
- Added prepare, commit, abort and ping messages; BrowserPending jobs; request-ID based duplicate protection and handoff recovery.

### Modifications and why

- The desktop app starts the pipe server, accepts browser jobs and stops the bridge during shutdown. The native host can launch the app with --browser-start. Why: Browser downloads need a controlled entry point into the same durable queue.
- Persist a pending job before cancelling the browser transfer; start it only after commit. Why: A failed bridge must not silently discard the download or start duplicate browser/app copies.
- Package app, bridge, extension and setup instructions together. Why: The browser needs a registered native executable as well as the extension.

### Bug fixes

- No separate user-reported v0.2.0 defect was resolved in this feature release. Earlier download, resume, UI and close fixes were retained.
- Failure handling prevents duplicate preparation/commit, removes aborted pending records where safe and restores the browser transfer when handoff fails before cancellation. These are new defensive behaviors, not previously confirmed user bugs.

### Validation

- 31 C# tests and 7 JavaScript handoff tests passed; the WPF app and native host were cross-compiled for Windows x64.
- Actual Chrome/Edge interception, registry installation and private-window behavior have not yet been confirmed on the user PC.

### Upgrade impact

Adds the BrowserPending state, not a new database column. Register the native host for this extracted folder and load the extension in each browser. Keep the app, bridge and extension folders together.

### Known limitations

- Only compatible public HTTP/HTTPS file links are supported. Browser cookies, authenticated requests, form-generated files, blob/data URLs and media-page extraction are not reproduced.
- Incognito/InPrivate jobs and URLs remain in desktop history and on disk. Private browser mode does not make QuickGrab storage private.
