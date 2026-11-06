# Flugel Download Manager independent implementation contract

Source: user-requested workflows, public HTTP semantics. Baseline engineering requirements, NOT claims that IDM implements these internals. Analysis addenda must contain behavior only, never proprietary source/pseudocode/assets.

## Boundary
Implementation workers may read this directory and original app source they author. They must not read IDM binaries, extracted extensions, raw analysis, decompiler output, or original resources. Coordinator reviews evidence and supplies only abstract behavior. Separate agent contexts provide practical separation, not audited legal clean-room certification; shared filesystem is not access controlled.

## Stack and layout
.NET 10, C#, Windows x64. No external packages. `app/Flugel.Core` net10.0 class library; `app/Flugel.Desktop` net10.0-windows WPF executable; `app/Flugel.Checks` net10.0 console executable; optional `app/Flugel.NativeHost` net10.0 console executable and original MV3 extension. Product name Flugel Download Manager, no original branding/assets.

## Required Core public API (freeze for parallel UI work)
Namespace `Flugel.Core`.
`public enum DownloadState { Queued, Scheduled, Downloading, Paused, Completed, Failed }`
`public sealed record DownloadSnapshot(string Id, string Url, string FileName, string Destination, DownloadState State, long Received, long? Total, double BytesPerSecond, string? Error, DateTimeOffset? ScheduledAt, int Segments)`
Record computed properties `double Percent` (0 when unknown), `string ProgressText`, `string SpeedText`, `string EtaText` usable directly by WPF bindings. URL snapshots may contain URL query, never log URL query or credentials.
`public sealed class ManagerSettings` mutable properties `string DownloadDirectory`, `int MaxConcurrent = 2`, `int Segments = 4`, `int MaxRetries = 3`. Validate values at boundaries.
`public sealed class DownloadManager : IAsyncDisposable`
- constructor `DownloadManager(string dataDirectory)` loads persistent state or fails safely without silently overwriting corrupt data.
- `ManagerSettings Settings { get; }`
- `IReadOnlyList<DownloadSnapshot> GetSnapshots()` thread-safe copies.
- `string Add(string url, string destinationDirectory, string? fileName = null, DateTimeOffset? scheduledAt = null, int? segments = null)` validates absolute HTTP(S), no embedded credentials, safe filename, no collisions/overwrites. Starts queue automatically.
- `void Pause(string id)`, `void Resume(string id)`, `void Remove(string id)` removes history only, never completed file; safely stops active worker before private partial removal.
- `void Move(string id, int delta)` queue order.
- `void SaveSettings()` validates and persists atomically.
- `ValueTask DisposeAsync()` stops workers safely and persists resumable state.
Core owns scheduling; UI polls snapshots every 500 ms, no callbacks required. Single app instance/data-directory lock required.

## Transfer contracts
Stream bounded buffers, async cancellation, no whole-file buffering. HTTP redirects with finite limit, standard TLS verification, no forwarding custom secrets cross-origin. Identity encoding when using byte ranges. Discover total/range support via validated response, not Accept-Ranges alone. Segment disjoint ranges only with stable strong ETag or valid Last-Modified; validate exact Content-Range and entity identity on all responses. No mixing representations after server changes. Ranges rejected/ignored: safely restart sequentially, never append full response to partial file. Unknown length: sequential. Empty file: valid completed zero-byte file. Partial bodies/early EOF: retry bounded, exponential backoff, idle timeout. Pause/restart must not mark incomplete bytes complete. Resume must revalidate entity and partial-file length. Missing validator: restart rather than risk corruption. Preserve existing destination files; publish completed file with no-overwrite rename. Persist state atomically, fail safe on corrupt metadata, prevent overlapping concurrent writes to state. File names cannot escape destination or use reserved Windows device names. No automatic execution of downloads. Completed file can expose SHA-256 or independently verify in tests.

## Desktop workflows
Original modern light interface, Segoe UI, native window chrome, keyboard focus and access keys, scalable layout. Toolbar Add / Resume / Pause / Remove / Move up / Move down / Settings. Filter All / Active / Queued / Completed / Failed. Download table name/state/progress/speed/ETA; progress bars, selection details and errors. Add dialog URL, destination folder, optional filename, segments, optional local-time schedule. Settings directory/concurrency/segments/retries. Confirm removal is history/partials only. Errors visible without crashing dispatcher. Close waits for orderly cancellation and persistence. Never mutate original app.

## Browser contract (independent, no IDM compatibility claim)
Opt-in context-menu HTTP(S) link submission via native messaging host `com.flugel.downloadmanager`, four-byte little-endian length plus UTF-8 JSON, maximum 64 KiB input. Message `{ "version": 1, "action": "enqueue", "url": "https://..." }`. Host validates schema and launches desktop with URL argument via argument list (no shell), desktop shows Add dialog/confirmation. Success means accepted for confirmation, NOT completed download. No cookie capture, broad request interception, automatic browser cancellation, credentials transfer, host registration or extension installation without explicit user opt-in. Registration script restricted HKCU and explicit extension ID.

## Executable checks
Use local controlled HTTP server. Verify SHA-256 and exact lengths: sequential, concurrent, segmented, large file, pause/resume, restart mid-transfer, dropped connection, ignored ranges, false/malformed ranges, changed validator, unknown length, zero bytes, redirect, 404/503 bounded retries, filename traversal/reserved names/collision, order/schedule, settings persistence and corrupt-state handling. Tests exercise production engine, not independent downloader. Each test reports result; no claims without execution.
