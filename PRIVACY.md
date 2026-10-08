# QuickGrab privacy policy

Last updated: 8 October 2026  
Applies to: QuickGrab v0.2.0 desktop application and Chrome/Edge extension

## Scope and maintainer

QuickGrab is an open-source Windows download manager maintained by
[Manash-git](https://github.com/Manash-git). This policy describes the current
application, extension and native browser bridge. Future features may require
policy updates.

## Information processed on your device

QuickGrab stores download URLs, destination paths, categories, job identifiers,
status, saved byte counts, file sizes, timestamps, server resume validators,
SHA-256 file fingerprints and error details. It also saves queue-concurrency
and bandwidth-limit settings. These records support the queue, resume and
recovery features.

The desktop database is normally stored at
`%LOCALAPPDATA%\ClearDownload\downloads.db`. The legacy folder name is retained
for compatibility. Download URLs, including query parameters that may contain
access tokens, are stored in plaintext. QuickGrab does not encrypt this database.
Anyone with sufficient access to these files may be able to read them.

Downloaded files and partial files are saved on your device. Browser-captured
files normally go to `%USERPROFILE%\Downloads\QuickGrab`; manually added downloads
use your selected destination.

## Browser integration and private windows

The extension uses browser download metadata to evaluate eligible downloads. It
passes the file URL, suggested filename, expected size, MIME type, request ID and
private-window flag, when applicable, to QuickGrab through a local native host.
The native host communicates with the desktop application over a current-user
named pipe.

The extension stores capture preferences in browser local extension storage and
handoff identifiers, phases and status messages in browser session storage. It
does not import browser cookies, passwords, authorization headers or page content.
It does not scan your general browsing history.

Automatic capture and private-window capture are enabled by default in the
extension settings. Private-window access additionally requires your explicit
permission in Chrome or Edge. You can disable either capture setting in the
extension popup or remove the extension.

**Incognito and InPrivate do not make QuickGrab's desktop storage private.**
Accepted download URLs, history and files can remain on disk after you close the
private browser window. QuickGrab has no automatic private-session cleanup.

## Network connections and sharing

QuickGrab contacts the download servers specified by your URLs, including
servers reached through redirects, to probe, download, verify saved data or
resume files. Browser capture can initiate these requests when you have enabled
capture. Those servers receive your IP address, requested URL and relevant HTTP
request headers and may record them under their own policies. Plain HTTP does
not provide HTTPS transport protection.

QuickGrab v0.2.0 has no advertising, analytics, telemetry, cloud history sync or
automatic crash-report upload. It does not send your download history or file
contents to the maintainer or SignPath. Browser, operating-system and network
services operate under their own settings and policies.

## Diagnostics and retention

Error reports are stored in `%LOCALAPPDATA%\ClearDownload\logs`, with
`%TEMP%\QuickGrab-logs` as a fallback. They contain error information and stack
traces; file paths may reveal your Windows username. The error reporter attempts
to redact URL strings from exception messages, but this is not a guarantee that
all sensitive information is removed. Review and redact reports before sharing.
Reports are shared with the maintainer only if you choose to send them.

There is no automatic retention deadline for desktop history or logs. Removing
the browser extension or unregistering its native host does not erase desktop
data or downloaded files.

To erase all desktop history and settings, close QuickGrab and prevent browser
capture from reopening it, then delete the `ClearDownload` data folder. This
also removes recovery records and prevents resuming jobs from those records.
Delete downloaded files, partial files and fallback logs separately if desired.
Backups may retain copies; ordinary deletion is not a secure-erasure guarantee.

## Project hosting and contact

Source code, downloads, automated builds and public issue discussions are hosted
on GitHub, which has its own
[privacy statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement).
Public issue reports are visible to others. Do not post tokens, private download
URLs, credentials or unredacted logs.

For general privacy questions, open an issue at
[QuickGrab issues](https://github.com/Manash-git/QuickGrab/issues) without including
sensitive data. If private details are needed, ask the maintainer to arrange a
private contact method before sharing them.
