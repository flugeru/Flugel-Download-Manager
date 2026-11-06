# Executed validation

Windows x64, 2026-11-06; .NET10.0.13, SDK10.0.402, display120DPI. All downloads controlled loopback resources. Tests exercise original reconstruction production engine and actual native executable, not mock downloader. Original IDM exited before reconstruction validation.

## Results

| Suite | Executed result | Evidence |
|---|---|---|
| Release build/publish | Core/Desktop/NativeHost succeed, zero warnings/errors | `build.ps1`; `artifacts/` |
| Production engine | **54/54 checks passed** (latest recorded run) | `app/Flugel.Checks/RESULTS.md`, generated temp artifacts recorded there |
| Native messaging host | **19/19 self-tests passed** (includes `requestId`/`job-status`/durable-handoff statuses) | `browser/RESULTS.md` (historical) + `dotnet run --project app/Flugel.NativeHost/Flugel.NativeHost.csproj -c Release -- --self-test` |
| Browser bridge/mock + real host frames | **Passed** durable-acceptance handoff, MV3 suspension, strict schema, takeover/fallback, and real-host `job-status` frame acceptance | `node browser/checks.cjs artifacts/Flugel.NativeHost/Flugel.NativeHost.exe` (delegates to `browser/check-media-bridge.cjs`) |
| Real native-host/desktop handoff | **Historical 2026-11-06 regression passed** (pipe lifetime/GUI confirmation). Current protocol adds forwarding to a running instance and durable `waiting-user-confirmation`/`confirmed`/`rejected`/`job-status` semantics; **live browser + desktop dialog outcome E2E not re-proven in this round** | `browser/Check-DesktopLaunch.ps1`; `browser/REAL-SITE-CHECK.md`; `C:\Users\Flugel\Desktop\FDM-handoff.md` |
| Published native WPF smoke | **16 checks passed, exit0** | `test-output/ui-final/evidence.json` + four PNGs |
| Windows MCP desktop observation | Actual Flugel Download Manager native window observed,1513×1000 physical pixels | MCP Snapshot during run; native UIA list `test-output/live-uia.json` |
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
- Valid native-host handoff initially timed out because desktop inherited protocol pipe handles, despite host process exit. Cleared standard-handle inheritance before launch; historical real desktop regression passed in 128 ms. Removed 187 hash-verified stale runtime files from experimental self-contained host publishing; final release is framework-dependent.
- First final republish hit locked DLLs from an earlier owned browser-test desktop. Closed that exact process normally, reran full build/tests/publish successfully, then reran UI smoke and real host handoff. Final build log: `test-output/final-build.log`.
- 2026-11-06 durable-acceptance P0 (b3fc9ba): extension/host/desktop now correlate browser handoffs with `requestId` and cancel the browser copy only after `confirmed` (persisted job). Mock/harness + host self-test + clean Release builds passed; **live browser dialog close/confirm/restart E2E still pending re-verification** with extension 2.2.3 reloaded.
- Original-app automation initially targeted wrong nested URL edit, later composite destination control. Corrected URL targeting; resulting default Downloads path documented rather than fabricating isolation. These were observation-script issues, not reconstruction engine defects.

## Outstanding validation / unsupported behavior

Public/enterprise HTTPS/proxy/auth integration, live Chrome/Edge installation lifecycle details, full browser service-worker/restart matrix, screen-reader/full keyboard audit, other DPI/monitors, power-loss/volume-full/permissions faults, multi-gigabyte transfer, long-running soak/performance and adversarial same-user filesystem tampering remain untested. No signing/update pipeline (unsigned installer workflow exists). Scheduling requires running app. Browser bridge forwards to a running desktop instance over same-user named-pipe IPC and uses durable acceptance (`requestId`/`job-status`; cancel browser copy only on `confirmed`), but **live browser + desktop dialog close/confirm/restart E2E still needs re-verification** with the reloaded 2.2.3 extension. Per-site session cookies are opt-in for a user-started transfer only (never persisted/logged); no credential capture UI, no DRM bypass, no universal interception, and POST/body replay is unsupported. See `COVERAGE.md` and `browser/REAL-SITE-CHECK.md`.

Test directories and logs remain local, excluded from release source compilation; some fixture files retained intentionally. Analysis fixture source listens only loopback. Build/check reruns can create new temp directories; no broad cleanup command provided.
