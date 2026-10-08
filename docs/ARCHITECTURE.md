# QuickGrab architecture — v0.2.0

The current implementation is documented in [PROJECT-HANDBOOK.md](PROJECT-HANDBOOK.md).
See its architecture, download engine, data/schema and browser protocol sections.
The desktop WPF process owns the SQLite queue and asynchronous HTTP engine.
Chrome/Edge communicate through the native host and a current-user named pipe.
Each file currently has one stream; segmented downloading is future work.

[CHANGELOG.md](../CHANGELOG.md) contains the complete chronological build history.
