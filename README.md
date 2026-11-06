# StreamHarbor

Independent Windows download manager. Original C#/.NET 10 implementation, native WPF desktop, resumable segmented HTTP transfers, queue/scheduling, persistent state and optional original browser bridge. Not IDM, not binary-compatible, not complete feature parity.

## Run compiled build

Executable: `artifacts\Harbor.Desktop\StreamHarbor.exe`.

Requires Windows x64 and **.NET 10 Windows Desktop Runtime**. Entire published folder required, not executable alone. Installed machine already has runtime 10.0.13. No original application needed; IDM was exited before reconstruction validation. Build is unsigned, local framework-dependent release, not installer/MSIX or production-certified distribution.

1. Launch `artifacts\Harbor.Desktop\StreamHarbor.exe`.
2. Choose **Add URL**, enter HTTP(S) URL and destination, optional filename/segments/schedule, then confirm.
3. Select row for Pause/Resume, queue ordering or details. Settings controls concurrency, default segments, retries and destination.
4. Close normally to flush state. Relaunch then Resume interrupted jobs. Scheduled downloads need app running.

Default state: `%LOCALAPPDATA%\StreamHarbor`. Isolated profile:

```powershell
.\artifacts\Harbor.Desktop\StreamHarbor.exe --data-dir "C:\Your Folder\HarborData"
```

One process per data directory. No existing-instance browser IPC forwarding yet. `--url "https://example.org/file.zip"` prefills Add confirmation; never silently downloads. Existing destination files are never overwritten; choose another filename. Keep enough free space for partials plus assembled result. URL query parameters persist in profile state and can appear in process arguments; avoid secret-bearing links if same-user exposure unacceptable. Downloaded files never execute automatically.

Keyboard: Ctrl+N Add, Ctrl+R Resume, Ctrl+P Pause, Alt+Up/Down queue move, Ctrl+, Settings; native access keys and Tab focus. Delete acts on focused table after confirmation.

## Reproduce build and checks

Requirements: .NET SDK **10.0.402** (pinned `global.json`), Windows x64; Node only for extra browser/analysis checks. No NuGet package dependencies.

```powershell
.\build.ps1
node .\browser\checks.cjs .\artifacts\Harbor.NativeHost\Harbor.NativeHost.exe
.\verify-release.ps1
```

`build.ps1` builds projects, runs production-engine and host checks, publishes both apps and browser sources, writes `artifacts\SHA256SUMS.txt`. `-SkipTests` skips tests explicitly. Optional self-contained publishing supported by .NET CLI but not this release; no claim of portable runtime-free executable.

Real UI smoke (use new directory):

```powershell
.\artifacts\Harbor.Desktop\StreamHarbor.exe --data-dir "C:\Your Folder\NewHarborSmoke" --smoke-test
```

Creates loopback fixture, exercises native windows and production engine, records app-only PNGs/JSON under profile `ui-evidence`, then exits. Exit code 0 means pass. Does not install browser extension or change original application.

Real native-host/desktop confirmation regression (fresh directory, opens/cancels own window): `./browser/Check-DesktopLaunch.ps1 -HostPath ./artifacts/Harbor.NativeHost/Harbor.NativeHost.exe -DataDirectory "C:\Your Folder\NewHostCheck"`. No download submitted; live browser integration remains untested. Close running published app before rebuilding to avoid locked DLLs.

Optional browser setup: `browser\README.md`. No registration performed automatically. Bridge supports explicit link submission only; no cookie capture, universal interception or media extraction.

## Deliverables

| Deliverable | Location |
|---|---|
| Environment/tools/backends | `ENVIRONMENT.md`, `analysis/ghidra-report.md`; local raw diagnostics in `analysis/rea-session-initial.json` |
| Binary inventory/imports/hashes/signatures/extensions | `analysis/inventory-report.md`, `analysis/inventory-*.json`, `analysis/integration-*.json` |
| Reverse engineering | `analysis/UI-RUNTIME.md`, `analysis/ghidra-report.md`, `analysis/IDM-NATIVE-ANALYSIS.md`; raw REA evidence retained locally only |
| Clean-room behavioral specification | `spec/CONTRACT.md`, `spec/OBSERVED-WORKFLOWS.md`, `spec/CLEAN-ROOM.md` |
| Architecture | `ARCHITECTURE.md` |
| Original source | `app/`, `browser/` |
| Compiled Windows applications/checksums | `artifacts/` |
| Test results and limitations | `TESTING.md`, `app/Harbor.Checks/RESULTS.md`, `browser/RESULTS.md`, `test-output/ui-final/` |
| Feature comparison | `COVERAGE.md` |
| Original editable UI concept (draft) | [Figma concept](https://www.figma.com/design/JRzeiv50SRhbCPIds1Yx6C?node-id=3-57) |

Figma concept uses synthetic content only; call cap prevented final clipping/progress polish and Segoe UI unavailable there. Native desktop screenshots, not this draft, establish implemented appearance. No original assets or source in app release. Practical clean-room separation not independently audited; full caveat in `spec/CLEAN-ROOM.md`.

Analysis reports and independent analysis scripts belong to source repository. Raw decompilation, evidence bundles, original UI captures, extracted packages and Ghidra workspaces stay local, excluded from source commits and application release. Run `./verify-release.ps1` after building to check release files against original inventory and confirm original files unchanged; run `node ./verify-analysis.mjs` from `analysis/` for local native-evidence integrity.
