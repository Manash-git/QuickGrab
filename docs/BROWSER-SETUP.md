# QuickGrab 0.2.0 — Chrome / Edge setup

This is a personal-use unpacked extension, not a Chrome Web Store / Edge Add-ons
release. It supports public, direct HTTP/HTTPS file links. It also works in private
windows after you grant the browser's private-window permission.

## 1. Put the package in a stable folder

1. Close any older QuickGrab version.
2. Extract the whole ZIP somewhere writable, for example `C:\Users\YOURNAME\Apps\QuickGrab`.
3. Keep the `app`, `bridge`, and `extension` folders together.
4. Run `app\QuickGrab.exe`; verify version **0.2.0**.
5. Double-click **RegisterBrowser.cmd** in the main package folder. It should report
   successful registration for Chrome and Edge. No administrator elevation is needed.

The previously required .NET 10 Desktop Runtime x64 is still needed. Registration
writes only your account's Chrome and Edge NativeMessagingHosts keys, pointing to
this package's native-host manifest. It does not change browser private-window
permissions or install an extension silently. Moving the folder later requires
running RegisterBrowser.cmd again.

## 2. Chrome

1. Enter `chrome://extensions` in Chrome's address bar.
2. Turn on **Developer mode**.
3. Click **Load unpacked**.
4. Select this package's **extension** folder (the folder with manifest.json).
5. Open **Details** for **QuickGrab Browser Integration**.
6. Turn on **Allow in incognito**.
7. Pin QuickGrab from Chrome's extensions menu if desired.
8. Click the QuickGrab extension icon, then **Test connection**.
9. Expect **Connected to QuickGrab 0.2.0**.

## 3. Edge

1. Enter `edge://extensions` in Edge's address bar.
2. Turn on **Developer mode**.
3. Click **Load unpacked** and select the same **extension** folder.
4. Open **Details** for **QuickGrab Browser Integration**.
5. Turn on **Allow in InPrivate**.
6. Click the QuickGrab extension icon, then **Test connection**.
7. Expect **Connected to QuickGrab 0.2.0**.

The fixed development extension ID is `fckcedkcpdadokdiialdjckjljkkagji`.
Do not remove the `key` field from manifest.json: it keeps the native-host allowlist
and extension ID aligned. Browser store publication may require new IDs/registration.

## 4. Use it

- **Automatic:** click a normal direct-file download link. QuickGrab checks and saves
  a pending job, then the browser copy is cancelled and QuickGrab starts downloading.
  The browser's Downloads list may show a cancelled entry: that is the handoff.
- **Manual:** right-click an HTTP/HTTPS link and choose **Download with QuickGrab**.
- **Incognito/InPrivate:** open a private window and use either method. Leave
  **Capture in private windows** enabled in the QuickGrab extension popup, in
  addition to granting the browser permission above.
- **Destination:** `%USERPROFILE%\Downloads\QuickGrab`. Filenames get a short unique
  suffix to avoid overwriting files. Use **Open folder** in the desktop app.
- The bridge can launch QuickGrab if it is not already open. Existing older versions
  must be closed first; they do not provide the new bridge.
- Toggle **Automatically capture downloads** off in the extension to keep all new
  automatic downloads in the browser. The right-click command remains available.

## Incognito privacy — read before use

Incognito/InPrivate applies to the browser, not the external desktop app.
**Files and URLs sent to QuickGrab remain on disk and in QuickGrab's saved history**,
including after the private window closes. URL query tokens are currently stored in
the desktop database as plaintext. Do not send sensitive signed URLs if this is
unacceptable; turn off private capture and use the browser instead.

The extension does not collect cookies, browsing history, passwords, authorization
headers or page contents. It has no content scripts, no external messaging endpoint,
and no telemetry. Its temporary session records store request/download IDs and
handoff state rather than URLs. The private flag is forwarded explicitly. Normal
and private extension workers are separated with `incognito: split`.

## Supported and unsupported downloads

Automatic capture requires a public HTTP/HTTPS file, known positive browser file
size, safe browser status, and matching app-side probe size. HTML pages, browser
blob/data URLs, dangerous downloads, other extensions' downloads and unknown-size
files remain in the browser. Very small files can complete before capture and
remain there too. Manually paused downloads are not automatically taken over.

Authenticated / cookie-dependent / form-generated downloads, DRM and media-page
extraction are not supported in this release. The app-side probe has no browser
cookies. Login pages, forbidden responses and size mismatches are rejected. There
is no promise to intercept every download. Use the browser for unsupported cases.

## Handoff failure recovery

- Before browser cancellation: failed acceptance attempts restore the paused browser
  transfer and remove an uncommitted QuickGrab job where possible.
- After browser cancellation: the durable QuickGrab job owns the download. A failed
  final message does not restart a second browser copy. Open QuickGrab and **Resume**
  the **BrowserPending** job if the extension cannot recover automatically.
- Interrupted service workers try to reconcile pending handoffs when they restart.
  Closing the entire browser clears session records; use the browser Downloads page
  and QuickGrab pending list to resolve a handoff interrupted at that instant.
- Already committed requests are idempotent: a repeated commit does not start a
  second job or restart a completed job.

## Troubleshooting

- **Connection failed:** close old QuickGrab copies; run RegisterBrowser.cmd from
  this exact package; launch app/QuickGrab.exe; retry Test connection.
- **Works normally but not privately:** enable the browser's incognito/InPrivate
  permission AND the extension's private capture setting. Managed browser policies
  may prohibit extensions or private mode; this package does not bypass them.
- **Download stays in browser:** it may be unsupported or already complete. Test a
  larger public direct file. Do not treat this as permission to bypass a login.
- **Pending job:** if the browser entry is cancelled, open QuickGrab and Resume it.
- **Moved the app:** register again from the new folder and reload the unpacked
  extension if its path changed.

## Remove browser integration

Run **UnregisterBrowser.cmd**, then remove the extension from each browser's
extensions page. The script only removes registration pointing to this copy. Your
QuickGrab database, downloaded files and partial data remain intact.

## Validation status

Automated protocol and queue tests plus JavaScript handoff tests are included.
Windows registration, UI launch, actual Chrome/Edge interception and private-window
behavior still need end-to-end verification on your Windows PC. This is not a claim
that the browser/OS combination has been exercised in this Linux build environment.

Primary API references:
- https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging
- https://developer.chrome.com/docs/extensions/reference/manifest/incognito
- https://developer.chrome.com/docs/extensions/reference/api/downloads
- https://learn.microsoft.com/en-us/microsoft-edge/extensions/developer-guide/native-messaging
