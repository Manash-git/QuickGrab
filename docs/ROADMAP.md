# Delivery roadmap

## Increment 0.1 — delivered source foundation

HTTP/HTTPS, durable queue, conservative resume, crash checkpoint recovery, global
HTTP speed limiter, categories and progress. Core tests and Windows publish scripts.

## Next increment — verify on the user's Windows PC

Run the packaged app, exercise real downloads, confirm Windows version and disk
behavior, add DPAPI protection for sensitive URL data, expected-hash verification,
Windows Mark-of-the-Web/attachment handling, and improve error diagnostics.

## Remaining month-one goals

1. Chrome/Edge native messaging prototype with idempotent handoff and fallback.
2. Basic supported media-page URL adapter using separately installed yt-dlp/FFmpeg.
3. End-to-end Windows verification and a per-user installer.

Firefox is a target, with timing dependent on the above validation. Browser and
media integration are separate subsystems, not features silently included in 0.1.

## v1

Segmented engine; Firefox integration; scheduler; optional clipboard detection;
proxy/auth/cookie workflows; per-job limits; media quality selection; robust
installer/update strategy. Validate license obligations before bundling tools.

## Advanced

Adaptive segment count; supported media detection; playlists/batches; FTP/FTPS;
performance profiling; signed releases; hardened extension distribution; commercial
readiness review. No guarantee of saturating a link without measured testing.

## Legal/ethical scope

Only authorized downloads. No DRM circumvention or authentication bypass. Review
site terms for integrations, obey rate limits, and honor Retry-After. robots.txt
becomes relevant to planned crawling/site discovery; it is not permission to copy
content. Do not reproduce IDM branding or proprietary assets. The original source
is MIT; retain dependency notices and review yt-dlp/FFmpeg build-specific terms
before redistribution. This is an engineering scope statement, not legal advice.

## 0.2.0 delivered increment

Chrome/Edge unpacked extension, right-click link handoff, conservative automatic
capture, incognito/InPrivate support requiring explicit browser permission,
per-user native-host setup/removal, pending-job recovery and protocol tests.
Windows/browser end-to-end verification is the next acceptance gate.
