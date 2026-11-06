# Controlled UI and HTTP observations

## Method and boundaries
Coordinator launched installed IDMan.exe, inspected its native window/menu/control hierarchy, opened Add, General settings and Scheduler without applying settings. Original app exited through its own Exit command after observation. Installer never executed. No original installed files edited. Opening original app can update its own normal per-user runtime/history settings; this was not a forensic read-only execution or VM sandbox. One local test download created `C:\Users\Flugel\Downloads\sample.bin`; retained as explicit test artifact, never opened/executed.

Windows MCP Snapshot/Screenshot supplied desktop inspection; native UIAutomationClient and Win32 read APIs supplied text/menu evidence. Original controls are largely custom Win32 panes: UI Automation alone lacked useful patterns. Small local scripts used exact existing menu IDs and button controls. These are analysis automation, not reconstruction source.

## Verified UI
- Main: title Internet Download Manager 6.43; category tree, toolbar, table. Screenshot `ui-main.png`; menu paths/IDs `ui-main-menus.json`.
- Categories observed: all downloads, compressed files, documents, music, programs, video, incomplete, complete, grabber projects, tasks.
- Table headers observed filename, queue, size, progress, remaining time, transfer speed, last attempt, description. Sort menus also expose path, referrer and parent page.
- Add URL dialog: URL field, optional authorization/user/password, OK/Cancel. No credential entered or extracted.
- File confirmation: URL, category, destination, size, description, Start download/Download later/Cancel. Actual known size displayed 16.00 MB.
- Completion: Open/Open with/Open folder/Close, optional suppress-next-time. Test file never executed.
- General settings: auto-start, clipboard URL monitoring, browser capture selection, modifier-key and context-menu/panel configuration. No settings applied.
- Scheduler: task/queue list, one-time/weekday/time scheduling, periodic synchronization, stop time, per-file retries, queue limits, optional exit/shutdown/open-file actions. UI presence verified only; schedules/shutdown/open-file actions never exercised.
- Menu shortcuts explicitly visible: Ctrl-F search and F3 next. Other shortcuts not established.

Detailed controls: `ui-add.json`, `ui-confirm.json`, `dialog-settings.json`, `dialog-scheduler.json`, `dialog-completed.json`; local screenshots separate. Original localized text Portuguese/Brazilian; reconstruction English and independently styled.

## Controlled transfer
Fixture `http-fixture.cjs` listens only `127.0.0.1:18743`, deterministic 16 MiB bytes `i % 251`, ETag and Last-Modified, bounded throttled response with range support. Request metadata log excludes credentials/cookies.

`idm-http-requests.jsonl` records initial GET plus eight further GETs carrying byte ranges, all `Accept-Encoding: identity`. Some requested ranges overlap at boundaries; this log does not reveal buffer coordination or algorithm and is not copied into implementation. No If-Range observed in this bounded trace; absence here does not prove original app lacks validator checks.

Completed `C:\Users\Flugel\Downloads\sample.bin`: 16,777,216 bytes, SHA-256 `287507f403176f1f5b22b9a4d9cb49f7d7f88ac19e406b5ae87ce109564846bd`, matches fixture. Source identities `fixture.json` and `idm-download-result.json`.

One failed automation attempt set wrong nested edit control and produced 'invalid URL'; corrected by targeting first URL edit. Later destination customization targeted a composite control incorrectly: original default Downloads destination retained, recorded here rather than claiming isolated output. No engine conclusions drawn from these automation errors.

## Coverage limits
Original pause/resume, restart recovery, errors, redirects, auth/cookies, unknown-length and large-file behavior not runtime-tested in this pass. Reconstruction checks cover independently specified safe semantics, not differential equivalence. No kernel hooks, drivers, media parsers, browser extension execution, activation dialogs or licensing touched. Runtime process snapshot `runtime-processes.json` records only matching IDM process identities; no comprehensive startup lineage/IPC trace claimed.
