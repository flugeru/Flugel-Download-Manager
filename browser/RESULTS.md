# Browser bridge implementation results

Date: 2026-11-06. Windows x64. .NET SDK `10.0.402`; Node `v26.12.0`. No external package references.

## Follow-up: real desktop pipe-lifetime regression

This section supersedes initial "desktop not launched/tested" statements below. Still **not live browser-tested**. Only original desktop `App.xaml.cs` public launch-option matches were inspected for `--data-dir`; no proprietary/analysis files read. No desktop source modified.

### Final framework-dependent artifact refresh

- Published fresh framework-dependent host with `dotnet publish app/Harbor.NativeHost/Harbor.NativeHost.csproj -c Release -r win-x64 --self-contained false -o app/Harbor.NativeHost/publish/framework-dependent` (exit 0).
- Before cleanup: `artifacts/Harbor.NativeHost` contained **192 files, 80,604,795 bytes**. Compared against fresh five-file publish. Exactly **187 stale runtime files, 80,374,232 bytes**, remained from prior self-contained publish; every stale file matched same-name/hash file in worker-generated `app/Harbor.NativeHost/publish/win-x64` (zero unverified files). Only these explicit verified generated files removed, without recursive/broad deletion; five current publish outputs copied in.
- Final `artifacts/Harbor.NativeHost`: **5 files, 202,909 bytes**: `.deps.json` 499; `.dll` 24,576; `.exe` 162,304; `.pdb` 15,188; `.runtimeconfig.json` 342. Runtime config references `Microsoft.NETCore.App` `10.0.0`; no bundled runtime. Older ignored `app/Harbor.NativeHost/publish/win-x64` remains experimental self-contained output, not release.
- Final framework-dependent real GUI regression: **188 ms**, status **0**, exact accepted-for-confirmation framed response, empty stderr. Exact isolated desktop PID **3280** stayed open after host EOF; Cancel and WindowPattern.Close via UIA closed only own desktop normally. Fresh retained data directory: `C:\Users\Flugel\AppData\Local\Temp\opencode\harbor-host-framework-dependent-20261106`. Default profile untouched.
- Final artifact `--self-test`: **13 groups passed**. `node browser/checks.cjs artifacts/Harbor.NativeHost/Harbor.NativeHost.exe`: mocked MV3 plus invalid real-subprocess checks passed. Ready for coordinator's final full build. No root docs edited; not live browser-tested.

- Reproduced coordinator timeout using real published sibling desktop and valid framed enqueue request. Added explicit `--self-test-desktop <fresh-directory>` host mode solely to pass desktop `--data-dir` without touching default profile. Framing, validation, process launch and replies use same production methods; only extra desktop data-directory argument differs. No executable override. Data directory must not already exist.
- **Before fix:** Node `spawnSync` elapsed **15,001 ms**, `ETIMEDOUT`, host status **0**, complete 50-byte `{"version":1,"status":"accepted-for-confirmation"}` response already present, stderr empty. Exact real desktop PID **23504** remained with confirmation dialog. It was cancelled and closed normally via UIA, not killed.
- **Cause:** Windows .NET `Process.Start` calls `CreateProcess` with handle inheritance enabled. Redirected child standard handles replace stdio but do not exclude other inheritable handles, including original native messaging/Node pipe handles. Host had already replied/exited; desktop retained inherited original pipe handles, so Node waited for pipe EOF until timeout. Confirmed against public .NET runtime Windows implementation and reproduced process-lifetime behavior.
- **Minimal fix:** clear `HANDLE_FLAG_INHERIT` using Windows `GetStdHandle`/`SetHandleInformation` for host stdin/stdout/stderr immediately before desktop launch; fail safely if clearing fails. Keep `ProcessStartInfo.ArgumentList`, `UseShellExecute=false` and redirected child streams. No shell, wait-for-desktop, forced desktop termination, or desktop modification.
- Refreshed `artifacts/Harbor.NativeHost` via `dotnet publish app/Harbor.NativeHost/Harbor.NativeHost.csproj -c Release -r win-x64 --self-contained true -o artifacts/Harbor.NativeHost` (exit 0).
- **After fix:** `browser/Check-DesktopLaunch.ps1` against refreshed host returned in **190 ms**, host status **0**, exact framed accepted-for-confirmation response, no stderr, no timeout. Real desktop PID **3224** remained with Add confirmation after host EOF/exit. UIA invoked Cancel, then normal WindowPattern.Close on only that PID; desktop exited normally.
- Isolated directories retained: `C:\Users\Flugel\AppData\Local\Temp\opencode\harbor-host-before-20261106` and `C:\Users\Flugel\AppData\Local\Temp\opencode\harbor-host-after-20261106`. Neither check submitted a download. Coordinator's preexisting default-profile desktop PID **10204** was not manipulated. Exact original default-profile command was not rerun, intentionally.
- Post-fix `dotnet build ... -c Release`: zero warnings/errors. Refreshed artifact `--self-test`: all **13 groups passed**. `node browser/checks.cjs artifacts/Harbor.NativeHost/Harbor.NativeHost.exe`: mocked extension checks and invalid real-subprocess checks passed.
- Executable regression retained as `browser/Check-DesktopLaunch.ps1`; uses real published desktop, not mock. Test-only mode still prints protocol only. No browser installation, registry modification, or browser setting change performed.

## Scope and boundary

- Read baseline `spec/CONTRACT.md` only; authored/read original source under `app/Harbor.NativeHost` and `browser/`. No IDM/proprietary artifacts, analysis, extracted extensions or assets accessed.
- Implemented `net10.0` BCL host, strict framed request parser, fixed sibling desktop launch using `ProcessStartInfo.ArgumentList` and `UseShellExecute=false`, confirmation-only replies, MV3 link context-menu extension, explicit opt-in Chrome/Edge HKCU scripts, publish/manual setup guide.
- No browser settings changed. No extension installed or host registered/unregistered. No desktop launched by checks. No git commits. No proprietary assets packaged.
- Integration requirement for coordinator: published desktop must be `../Harbor.Desktop/StreamHarbor.exe` relative to host directory and handle `--url URL` by showing confirmation. Host neither reads desktop source nor verifies dialog behavior.

## Executed successfully

1. `dotnet build app/Harbor.NativeHost/Harbor.NativeHost.csproj -c Release`
   - Exit 0, zero warnings, zero errors.
2. `dotnet run --project app/Harbor.NativeHost/Harbor.NativeHost.csproj -c Release --no-build -- --self-test`
   - Exit 0, 13 named self-test groups passed. Exercises production framing/parser/run loop with short-read streams and injected launch callback; does not launch real desktop.
   - Valid HTTP(S), IPv6 and Unicode; preserved escaped query; rejection of credentials/userinfo, non-HTTP(S), controls, whitespace, backslashes and malformed URLs.
   - Exact schema/version/action; malformed JSON, duplicate/unknown fields, wrong types, trailing garbage, invalid UTF-8 and lone surrogate rejected.
   - Clean EOF, one-byte reads, partial headers/bodies, zero/65,537/uint-max lengths rejected; exact 65,536-byte valid request accepted.
   - Separate `--url` and URL arguments, no shell, redirected child streams; two-frame success loop returns only `accepted-for-confirmation`, no URL echo; bad input never calls launcher; launch failure returns `desktop-unavailable`.
3. `node browser/checks.cjs app/Harbor.NativeHost/bin/Release/net10.0/Harbor.NativeHost.exe`
   - Exit 0.
   - **Mocked extension APIs:** validates MV3 permissions, link-only menu/patterns, exact native host request, URL/userinfo/control/size rejection, confirmation-only badge, rejection handling and `runtime.lastError` handling without copying sensitive error text.
   - **Actual host subprocess:** clean EOF, partial header/body, 65,537-byte announced length, invalid URL and invalid UTF-8 rejected with exact framed protocol replies; no stray stdout/stderr. Invalid inputs only, so no desktop launch.
4. `dotnet publish app/Harbor.NativeHost/Harbor.NativeHost.csproj -c Release -r win-x64 --self-contained true -o app/Harbor.NativeHost/publish/win-x64`
   - Exit 0. Self-contained host publish output exists inside owned source directory. Not a complete two-application release package.
5. `.\app\Harbor.NativeHost\publish\win-x64\Harbor.NativeHost.exe --self-test`
   - Exit 0; same 13 self-test groups passed from self-contained executable.
6. PowerShell `System.Management.Automation.Language.Parser.ParseFile` on both registration scripts.
   - Both AST parses passed; scripts were **not executed**, even with `-WhatIf`. Registry/manifest write/removal paths untested at runtime.

## Documentation consulted

Context7 `resolve-library-id` resolved Chrome Extensions to `/websites/developer_chrome_extensions`. Two public generic `query-docs` calls covered native messaging framing/manifests/HKCU registration and MV3 contextMenus/service-worker events. No project or proprietary code sent in queries. Public Microsoft Learn Edge native messaging documentation also read to verify Edge registry path and origin format. References linked in `README.md`.

## Not tested / known limits

- **Not live browser-tested.** No claims about Chrome/Edge actual extension loading, context-menu lifecycle, messaging dispatch, badges, registration discovery, desktop window confirmation or downloads.
- Registration scripts AST-checked only. Exact-ID validation, path-safe JSON, 32/64-bit HKCU leaf writes, ownership checks and nonrecursive removal are source implementation, not live registry-test results. Existing differing registration is refused. A partial OS/write failure can leave partial registration; guide describes cleanup. Unregister deliberately retains manifest/binaries and refuses missing/corrupt manifest or extra key data.
- Desktop launch tested through injected callback plus `ProcessStartInfo` assertions, not actual GUI process creation. Success means process creation accepted for confirmation only; no promise of a visible dialog, queued item or download completion.
- Host accepts only fixed three-field protocol and 64 KiB payload ceiling. Malformed frame/request ends session; desktop-unavailable rejection permits later frames. Child GUI standard streams closed to preserve protocol stdout.
- URLs may carry private query strings, which remain in process arguments sent to desktop. Host/status replies do not echo URLs; same-user process inspection is outside this bridge's privacy boundary.

## Authored files

- `app/Harbor.NativeHost/Harbor.NativeHost.csproj`
- `app/Harbor.NativeHost/Program.cs`
- `app/Harbor.NativeHost/SelfTests.cs`
- `browser/extension/manifest.json`
- `browser/extension/service-worker.js`
- `browser/Register-NativeHost.ps1`
- `browser/Unregister-NativeHost.ps1`
- `browser/checks.cjs`
- `browser/README.md`
- `browser/RESULTS.md`
