# Executed validation

Windows x64, 2026-11-06; .NET10.0.13, SDK10.0.402, display120DPI. All downloads controlled loopback resources. Tests exercise original reconstruction production engine and actual native executable, not mock downloader. Original IDM exited before reconstruction validation.

## Results

| Suite | Executed result | Evidence |
|---|---|---|
| Release build/publish | Core/Desktop/NativeHost succeed, zero warnings/errors | `build.ps1`; `artifacts/` |
| Production engine | **40/40 passed** | `app/Harbor.Checks/RESULTS.md`, generated temp artifacts recorded there |
| Native messaging host | **13/13 self-tests passed** | `browser/RESULTS.md`; `dotnet run --project app/Harbor.NativeHost -c Release -- --self-test` |
| Browser bridge subprocess/mock | **Passed** actual host invalid-frame process tests and mocked MV3 events/status/errors | `node browser/checks.cjs artifacts/Harbor.NativeHost/Harbor.NativeHost.exe` |
| Real native-host/desktop handoff | **Passed** final published host replied/exited in128ms while isolated desktop confirmation stayed open; UIA cancelled/closed normally | `browser/Check-DesktopLaunch.ps1`; `browser/RESULTS.md`; no live browser installation |
| Published native WPF smoke | **16 checks passed, exit0** | `test-output/ui-final/evidence.json` + four PNGs |
| Windows MCP desktop observation | Actual StreamHarbor native window observed,1513×1000 physical pixels | MCP Snapshot during run; native UIA list `test-output/live-uia.json` |
| Original-product control transfer | 16MiB file completed, SHA-256 matches | `analysis/UI-RUNTIME.md`, `analysis/idm-download-result.json` |
| Native analysis integrity | **Passed** retained cited evidence, unchanged target/toolchain hashes, explicit close receipts | `analysis/ghidra-report.md`; run `node ./verify-analysis.mjs` from `analysis/` |
| Release separation/original integrity | **Passed** all21 final release files checked, no original inventory file hash in release; all186 original files unchanged | `verify-release.ps1`, `test-output/release-verification.json` |

## Engine scenarios

Single/parallel files; disjoint segmented exact ranges;64MiB bounded streaming/hash; ignored/false Accept-Ranges; malformed/wrong Content-Range;400/416-style range fallback; changed/weak/missing ETag; Last-Modified; chunked unknown size; empty file; redirects/loop; dropped body; bounded503 retry success/exhaustion; permanent404; pause/resume; manager restart; actual child-process kill/recovery; partial-length mismatch; validator change across pause; immediate pause/resume race;15s idle timeout; prompt cancellation; rejected encoding; concurrency/order/due time; remove while active; preserve completed file; URL/filename/settings validation; collision/no overwrite; persistence/lock; corrupt/incomplete state fail-safe; unsaved invalid settings isolation; privacy-safe structured logs.

No RSS benchmark recorded: bounded-memory claim follows production fixed64KiB segment/copy buffers plus64MiB streamed/hash check, not measured peak-memory certification. No multi-gigabyte test;64MiB is largest executed file. Hash equality validates exercised transfers, not all internet servers.

## Native UI scenarios

Real WPF dispatcher, production manager, loopback download and SHA-256; actual Add confirmation; scheduled/paused/completed/failed rows; active/queue filters; source privacy and credential rejection; table focus; expected columns; wide/compact viewport; Settings open/close; delayed schedule retained; async close; Add/Settings segment limits17 rejected/16 accepted. Actual monitor120DPI only. Screenshot PNGs capture own WPF client visuals, not full desktop/native chrome.

Coordinator launched published app independently after worker tests and reran smoke against final rebuilt executable. Windows MCP separately enumerated real native window; local UI Automation confirmed named buttons/radios and keyboard focusability. App then closed via normal CloseMainWindow. No original screen/assets imported into reconstruction.

## Defects corrected / reruns

- Engine worker checks cover range-fallback sibling cancellation, immediate pause/resume and child-process checkpoint/handle-release races. Final40-check suite passed after corrections; no claim of exhaustive concurrency proof.
- Integration review found desktop allowed32 segments while core accepted16. Corrected Add/Settings validation, hints/docs; added two executable smoke assertions; final16-check published smoke passed.
- Build packaging initially risked nested browser folder on repeat Copy-Item. Corrected to copy folder contents into explicit output directory; repeated publishing supported. Source packaging independent of binary evidence.
- Valid native-host handoff initially timed out because desktop inherited protocol pipe handles, despite host process exit. Cleared standard-handle inheritance before launch; final real desktop regression passed in128ms. Removed187 hash-verified stale runtime files from experimental self-contained host publishing; final release is framework-dependent.
- First final republish hit locked DLLs from an earlier owned browser-test desktop. Closed that exact process normally, reran full build/tests/publish successfully, then reran16-check UI smoke and real host handoff. Final build log: `test-output/final-build.log`.
- Original-app automation initially targeted wrong nested URL edit, later composite destination control. Corrected URL targeting; resulting default Downloads path documented rather than fabricating isolation. These were observation-script issues, not reconstruction engine defects.

## Outstanding validation / unsupported behavior

Public/enterprise HTTPS/proxy/auth integration, live Chrome/Edge installation, browser service-worker restart, screen-reader/full keyboard audit, other DPI/monitors, power-loss/volume-full/permissions faults, multi-gigabyte transfer, long-running soak/performance and adversarial same-user filesystem tampering remain untested. No installer/signing/update pipeline. Scheduling requires running app. Bridge cannot forward to already-running instance; acceptance reply only confirms process creation. No credential/context capture, universal interception, media/site grabber or DRM support. See `COVERAGE.md`.

Test directories and logs remain local, excluded from release source compilation; some fixture files retained intentionally. Analysis fixture source listens only loopback. Build/check reruns can create new temp directories; no broad cleanup command provided.
