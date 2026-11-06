# StreamHarbor desktop

Original, package-free WPF UI. Native Windows chrome, Segoe UI, light navy/teal palette. Core API follows `spec/CONTRACT.md`; no proprietary assets or implementation inspected.

## Build and run

Run from the repository root with .NET SDK **10.0.402** installed:

```powershell
dotnet build .\app\Harbor.Desktop\Harbor.Desktop.csproj -c Release
dotnet publish .\app\Harbor.Desktop\Harbor.Desktop.csproj -c Release -o .\artifacts\Harbor.Desktop
dotnet run --project .\app\Harbor.Desktop\Harbor.Desktop.csproj -c Release --no-build
dotnet run --project .\app\Harbor.Desktop\Harbor.Desktop.csproj -c Release --no-build -- --url "https://example.org/file.zip"
dotnet run --project .\app\Harbor.Desktop\Harbor.Desktop.csproj -c Release --no-build -- --data-dir "C:\StreamHarbor-Test-Data"
```

`--url` only prefills the Add dialog. User confirmation is required. HTTP(S) only; embedded credentials rejected. Source details omit credentials, query and fragment. Raw transport errors are not echoed because they can disclose signed URLs; generic actionable errors remain visible.

Publish produces `artifacts\Harbor.Desktop\StreamHarbor.exe`, matching the native host sibling-path contract. Framework-dependent: .NET 10 Windows Desktop runtime required.

Core owns the exclusive data-directory lock. There is intentionally no IPC forwarding. If another window uses the same directory, startup fails with instructions to copy the link into that window, close it and retry, or use an isolated `--data-dir`. Never remove a lock or overwrite state to bypass this check.

500 ms dispatcher polling preserves rows and selected IDs. Filters: Active = downloading; Queued = queued/scheduled. All includes paused. Queue order stays engine order; user sorting disabled. `Ctrl+N` Add, `Ctrl+R` Resume, `Ctrl+P` Pause, `Alt+Up/Down` move, `Ctrl+,` settings. Delete removes only while the table owns focus, after confirmation. Native Alt access keys, Tab traversal, table arrows, Enter default and Escape cancel remain available. Dialogs scroll when small. Minimum main size 760 × 600 device-independent units; table scrolls horizontally when columns cannot fit. Native control templates and focus visuals retained.

Settings UI matches core limits: concurrency 1–16, segments 1–16, retries 0–10; core validates final values. Add uses the same segment limits. Local-time schedules reject invalid/ambiguous DST times. Schedules require app and computer running. Closing awaits `DisposeAsync` and refuses to silently close if persistence fails.

## Controlled UI smoke check

Use a **new, empty** directory; smoke refuses existing data and `--url`:

```powershell
dotnet run --project .\app\Harbor.Desktop\Harbor.Desktop.csproj -c Release --no-build -- --data-dir "C:\Users\Flugel\AppData\Local\Temp\opencode\streamharbor-ui-smoke-01" --smoke-test
```

Opens real windows, confirms prefilled Add through the native button, downloads known loopback bytes through production core, verifies SHA-256, populates completed/scheduled/paused/failed rows, checks filters/privacy/focus and wide/compact table layout, opens settings, then awaits orderly shutdown. Exit 0 means pass; exit 1 means failure. Evidence: `<data-dir>\ui-evidence\evidence.json` plus PNGs of this app's own WPF visuals only. No full desktop capture or external/user content. Images omit native OS chrome; actual screen-reader, keyboard-only end-to-end, multi-monitor and DPI matrix tests remain manual. Recorded DPI is current display only, not a simulated DPI test. Engine correctness belongs in `Harbor.Checks`.

Executed 2026-11-06: SDK 10.0.402 Release build, zero warnings/errors. Native `StreamHarbor.exe` smoke exited 0; 16 checks passed, including Add and Settings rejecting 17 segments and accepting 16. Evidence at `C:\Users\Flugel\AppData\Local\Temp\opencode\streamharbor-ui-limits-eec77f70c341473dac3999a88bd14d30\ui-evidence\evidence.json`; four PNGs beside it. Actual display was 120 DPI; wide 1210 × 800 and compact 800 × 650 window sizes requested. This records one executed environment, not a DPI/OS compatibility matrix.
