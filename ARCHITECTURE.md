# StreamHarbor architecture

Original .NET 10 / C# design, no external packages. No dependency on original download manager, driver, COM registration, extension or runtime.

## Modules

| Project | Responsibility |
|---|---|
| `app/Harbor.Core` | Queue, state machine, scheduling, file ownership, segmented/sequential HTTP transfers, validation, retries, atomic checkpoints and diagnostics. Platform-neutral target `net10.0`, tested Windows. |
| `app/Harbor.Desktop` | Native WPF `net10.0-windows`; original Segoe UI interface, dialogs, keyboard commands and 500 ms snapshot polling. No HTTP performed on dispatcher. |
| `app/Harbor.NativeHost` | Optional standard-input/output JSON framing, strict schema validation, shell-free launch of fixed sibling desktop executable for user confirmation. |
| `app/Harbor.Checks` | Framework-free console integration checks exercising production core against deterministic loopback HTTP server. |
| `browser/extension` | Original opt-in MV3 context-menu handoff. Permissions only contextMenus/nativeMessaging. No interception or secrets capture. |

Public core API frozen in `spec/CONTRACT.md`; `DownloadManager` serializes state mutations with one lock, returns immutable `DownloadSnapshot` copies. UI retains row identity and selection across polling. Settings editable separately, validation occurs before persistent active settings change.

## Transfer lifecycle

Add validates absolute HTTP(S) URL without userinfo, safe filename, destination collisions and numeric limits; reserves name and durably records queued/scheduled job. Scheduler starts jobs in queue order within concurrency limit; due schedules become runnable. Pause cancels owned work; Resume queues safely after old worker finishes. Failed jobs require explicit Resume. Interrupted Downloading state loads as Paused, avoiding surprise resumed traffic.

Worker probes byte range 0–0 with identity encoding. Exact Content-Range, response identity, total length and strong ETag or eligible Last-Modified decide resumability. With validated byte ranges, disjoint segment files use 64 KiB buffers; each request carries If-Range. Any inconsistent/rejected range cancels siblings, clears private partials and safely restarts sequentially. Missing validators/unknown lengths use sequential transfer and restart instead of unsafe appends.

Response/body handling validates lengths and encoding, applies bounded connection/header/idle timeouts, disposes responses/streams, and retries transient network errors/408/429/5xx with bounded exponential backoff. Permanent HTTP errors fail without retry loop. Redirect limit eight; platform TLS verification remains enabled. No custom headers/cookies/credentials means no credential forwarding feature. Retry-After handling, proxy configuration UI and authenticated request context are not implemented.

Completion assembles validated segments on destination volume, flushes output and uses no-overwrite rename. Existing files remain untouched, even if created after initial reservation. Workspace `.streamharbor-<GUID>` contains only engine partials; cleanup deletes known private filenames, not broad trees. Reparse-point workspace/partial checks reduce accidental link misuse; malicious same-user concurrent filesystem tampering is outside supported trust boundary. Extra free disk space required during assembly: partial files plus assembled output.

## Persistence and diagnostics

Default desktop data `%LOCALAPPDATA%\StreamHarbor`; `--data-dir` permits explicit isolated profile. Exclusive `instance.lock` prevents two managers writing same state. `state.json` includes job metadata, URLs, scheduling, settings, validators and segment checkpoints. Same-directory temporary write, flush and replace protect against process-interrupted JSON writes; power-loss durability not asserted. Corrupt/unsupported state fails without overwrite. State/write errors stop scheduling rather than silently lose history.

`events.jsonl` contains timestamps/job IDs/actions/retry counts, no full URLs or credentials. State necessarily contains submitted URLs, including query strings. Store profile in user-private directory; no encryption-at-rest promised. UI hides query/fragment in source display and suppresses raw transport errors that could echo signed links. Downloads never execute automatically. Removing history never deletes completed files.

## Browser boundaries

Browser action passes only selected HTTP(S) URL. Host accepts strict bounded UTF-8 JSON through little-endian framing and launches fixed sibling `Harbor.Desktop\StreamHarbor.exe --url <URL>` without shell. Reply `accepted-for-confirmation` means process launched, not durable enqueue or transfer completion. Desktop user chooses destination and confirms. Registration explicitly opt-in, exact extension ID, HKCU Chrome/Edge only.

No IPC forwarding into existing instance. If default profile busy, second desktop reports actionable failure. Browser host cannot detect that later UI failure; full seamless handoff to running manager remains partial. A future same-user named-pipe channel with explicit confirmation/durable acknowledgment can close this gap. Current host never claims completed/queued success.

## UI and scheduling ceiling

WPF native chrome/templates/focus, scalable DIPs, min 760×600, scrollable compact table/dialogs. Current 120 DPI and wide/compact sizes tested. No custom-drawn title bar or proprietary artwork. Scheduled work requires application and machine awake; no Windows Task Scheduler service, background service or wake timer installed. No shutdown/execute-file automation.
