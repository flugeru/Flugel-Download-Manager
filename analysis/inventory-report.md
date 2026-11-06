# IDM static inventory

## Scope and method

Read-only sources: `C:\Program Files (x86)\Internet Download Manager` recursively and `C:\Users\Flugel\Downloads\idman643build18.exe`. No target executable, installer, extension, script, COM server, or driver executed. No REA session used. No installation changes, browser profile reads, license/settings reads, credential reads, or uploads. All writes confined to `analysis/inventory*` and `analysis/integration*`.

Evidence IDs below name local files, not REA evidence. SHA-256 identities bind observations. Packaged scripts are untrusted target data; not executed. Extracted proprietary files are evidence only in `integration-packages/`, not app inputs.

## Verified

| Item | Result | Evidence |
|---|---|---|
| Recursive inventory | 185 installed files plus installer, 186 total, 37,406,902 bytes; every regular file hashed; no read/parse failures | `inventory-files.json` |
| Top-level PE inventory | 43 binaries: 28 x86, 14 x64, one ARM64 (`idmwfpAA.sys`); architecture, imports, delay imports, exports, entrypoint, sections, resource ranges/hashes, certificate table, overlay recorded | `inventory-pe.json` |
| Embedded binaries | Seven additional DLLs inside legacy XPIs parsed separately | `inventory-packaged-pe.json` |
| Windows signatures | 52 files checked including catalogs and packaged DLLs: 51 `Valid`, one `NotSigned` (`oldjsproxy.dll`) | `inventory-windows.json` |
| Main app | `IDMan.exe`, x86, file version `6, 43, 18, 2`, entrypoint RVA `0x284dec`, manifest `asInvoker` | `inventory-pe.json`, `inventory-windows.json`, `inventory-summary.json` |
| Host | `IDMMsgHost.exe`, x86, file version `6,38,20,9`, entrypoint RVA `0x1725`, `asInvoker`; imports standard-handle reads/writes, synchronization, process queries, registry reads, COM activation | Same files |
| Browser packages | Five outer archives plus two nested JARs, 191 member records including directory records; ZIP lengths and CRC32 checked; per-member SHA-256 | `integration-packages.json` |
| CRX integrity | Both CRX3 packages have valid RSA-SHA256 and ECDSA-SHA256 proofs; matching declared extension-ID key proof valid | `integration-packages.json` |

Windows `Valid` is current local Authenticode verdict, not independent malware clearance or long-term revocation assurance. CRX integrity checks verify bytes and ID/key binding, not store authorization, current distribution policy, installed extension state, or revocation. XPI/JAR signature artifacts inventoried, not cryptographically validated. CRX2 parser supported but no supplied package uses CRX2; CRX2 signatures not validated.

### Installer, never run

- Size: 12,164,608 bytes; SHA-256 `7346128fab42957b4254eabdb012bfdbd751e9069361e98a6916f26bb03d62ee`.
- x86 PE32; file version `6, 43, 18, 1`; entrypoint RVA `0x5b7a`, file offset 20,346; zero exports.
- Imports: `ADVAPI32.dll` (4 symbols), `USER32.dll` (14), `SHELL32.dll` (2), `KERNEL32.dll` (84). Includes file creation/writes, process creation, mapping, temp path, registry reads/deletion APIs. Import presence does not prove any operation occurs.
- Seven resources: three icon payloads, dialog, icon group, VERSIONINFO, manifest. Manifest requests `requireAdministrator`, `uiAccess=false`.
- PE section data ends at offset 95,232. Overlay length 12,069,376; 12,058,712 bytes before certificate table. Certificate offset 12,153,944, size 10,664. Overlay hash and initial 32 bytes recorded in `inventory-summary.json`.
- Authenticode `Valid`, signer Tonec Inc. No payload unpacking, compression attribution, install actions, or equality with installed files established.

### Package generations and permissions

| Package | Identity/version | Static integration model |
|---|---|---|
| `IDMGCExt.crx` | `ngpampappnmepgilojfohadhhmbhlaek`, 6.43.1 | Chrome MV3; service worker; minimum Chrome 109.0; `scripting`, `webRequest`, `declarativeNetRequest`, downloads/UI, tabs, cookies, menus, navigation, management, storage, proxy, native messaging; `<all_urls>` host permission; optional notifications/display |
| `IDMEdgeExt.crx` | `llbjbkhnmlidjebalopleeepgdfgcpec`, 6.43.1 | Bundled Edge MV2; persistent background; blocking webRequest, downloads/shelf, cookies/tabs/menus/navigation/management/storage/proxy/native messaging; optional notifications/display |
| `idmmzcc3.xpi` | `mozilla_cc3@internetdownloadmanager.com`, 6.43.1 | Firefox WebExtension MV2; min Firefox 79.0; background scripts; blocking webRequest, cookies/tabs/menus/navigation/management/storage/native messaging/privacy/idle; no downloads permission in this manifest |
| `idmmzcc2.xpi` | `mozilla_cc2@internetdownloadmanager.com`, 6.27.7 | Legacy XUL/XPCOM/ctypes; Firefox 27.0a1–52.*, SeaMonkey 2.37.*–2.50a1; multiprocessCompatible false |
| `idmmzcc.xpi` | `mozilla_cc@internetdownloadmanager.com`, 7.3.110 | Legacy XUL/XPCOM; Firefox 1.5–26.*, other old Mozilla-family ranges in `install.rdf` |

Chrome content scripts target `*://*/*`, all frames, document start; Edge/Firefox additionally declare FTP/file matches. Modern scripts gate initialization on Windows/HTML. Web-accessible resources, update URLs, CSP and externally-connectable website restrictions are preserved in manifest evidence. Declared access is not proof user enabled host/file/incognito access or packages currently run in any browser.

### Native host and registry

`integration-native-hosts.json` preserves both JSON manifests and their hashes:

- Name `com.tonec.idm`, transport `stdio`, relative executable `IDMMsgHost.exe`, resolved within installed directory.
- Chromium allowed origins: exactly Chrome and Edge IDs above, no wildcards.
- Mozilla allowed extensions: exactly `mozilla_cc3@internetdownloadmanager.com`.
- HKCU exact native-host keys exist for Google Chrome, Microsoft Edge and Mozilla; HKLM Google Chrome exists. Chromium key absent within checked scope. Both 32/64 views queried; identical view results do not prove separate underlying physical registrations.
- HKCU Chrome/Edge external-extension keys point to respective installed CRX, version 6.43.1. HKLM Chrome key also exists; corresponding HKLM Edge absent.
- Only default/native path and extension `path`, `version`, `update_url` values queried. Exact checked keys and bounded negatives in `integration-registry.json`; unrelated registry keys not traversed.

### Static script behavior

Primary evidence: `integration-packages/IDMGCExt.crx/background.js`, `content.js`, `document.js`, `captured.js`; corresponding Edge/Firefox scripts; legacy nested JAR scripts. Full script member identities and lexical event index: `integration-script-index.json`.

- Chrome background lines 22–31, 69–77, 104–112 register tab/window lifecycle, context menus, runtime ports, navigation, request stages, download creation/filename determination. Request metadata classification and browser download handoff exist; not generic ability to block arbitrary MV3 requests.
- Background lines 37–41 cycle WebSocket attempts to literal `127.0.0.1:1001` and `0.1.0.1:1001`, subprotocol `plugin.v3.internetdownloadmanager.com`, then native messaging. The second address is merely a literal fallback; not assumed equivalent to loopback. No endpoint contacted. Disconnect clears integration state/listeners; reconnect uses increasing delays.
- Same lines send structured custom strings through native messaging and Blob-backed WebSocket messages. Browser native messaging outer framing is separate from inner application protocol; see contract.
- Background lines 45–54, 94–103 classify links, batches, requests and downloads using URL, method, MIME, headers, filename, page/frame context and manager-provided configuration. Browser downloads can pause, then resume or cancel based on handoff path. Cookies/proxy/header/body handling exists in code; no real values read by this investigation.
- Content lines 2–5, 9–20, 31–42 register resize/scroll/blur/key/button/click/load events; maintain frame ports; inspect links/selection/media and element geometry. Mutation/resize/intersection observers update state and cleanup. Page context bridge compares message origin, uses numeric message kinds, and reports metadata/selected response bodies.
- `document.js` lines 1–10 can wrap page XHR/fetch/Response methods, tag requests for correlation and return selected data. Configuration supplied through content bridge constrains active patterns. This is observable script logic, not proof every response captured or cross-origin/DRM access gained.
- Legacy XPI2 nested `overlay.js` lines 18–53 selects native DLL by pointer width and calls `InitCC`; menu/load/tab events and frame messages delegate native integration. Legacy files reference `Software\DownloadManager\ExePath` and menu-only values; those registry values were not queried.
- Installed `IEExt.htm`, `IEGetAll.htm`, `IEGetVL.htm`, `IEGetVL2.htm` are eight-line VBScript COM adapters calling menu-context processors. No COM object activated.

## Roles: verified metadata vs inference

Verified version descriptions label browser helper objects (`IDMIECC*`), click catchers (`idmcchandler*`, `idmmzcc7*`), download panels (`idmbrbtn*`), network monitors (`IDMNetMon*`), parser modules (`IDMVMPrs*`), shell extensions (`IDMShellExt*`), integration loader (`IDMIntegrator64.exe`), settings broker (`idmBroker.exe`), TDI/WFP drivers and OpenSSL 1.1.1w libraries. Names/descriptions/exports establish packaged roles, not actual activation or exact algorithms. Main app plus helper/driver/browser split inferred architectural grouping only. Pairing x86/x64 modules supports architecture-specific integration inference; driver/service enablement unknown.

## Current browser constraints, documentation evidence

Context7 retrieved official Chrome documentation during investigation. Sources:

- https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging
- https://developer.chrome.com/docs/extensions/reference/api/webRequest
- https://developer.chrome.com/docs/extensions/develop/concepts/declare-permissions
- https://developer.chrome.com/docs/extensions/develop/migrate/blocking-web-requests

Native messaging: UTF-8 JSON preceded by 32-bit native-byte-order byte length, 1 MB host-to-browser maximum and 64 MiB browser-to-host maximum; Windows host binary I/O; no stdout diagnostics; exact origin allowlist and registry manifest discovery. Windows registry lookup checks 32-bit view before 64-bit. Relative Windows executable paths resolve against host manifest directory.

Normal MV3 extensions cannot use blocking webRequest; policy-installed exception exists. Use declarative rules and explicit download handoff rather than assume MV2 parity. File URL and incognito access require user permission. `<all_urls>` does not establish ability to inject into browser-internal/protected pages; actual browser/profile access not inspected. Bundled MV2 manifests do not establish present browser acceptance.

## Unknown / deliberately not investigated

Host-to-manager IPC object names/COM interfaces, native command validation, listening sockets, actual handshake success, runtime request/response captures, installed browser state/policies, extension enablement, current Firefox/Edge execution, kernel driver load state, native media parsing, streaming assembly/retry/resume logic, installer payload contents and behavior. No registration/license logic analyzed. No claim of DRM bypass or protected-content download support. Native imports alone cannot fill these gaps.

## Reproduce

1. Run `node analysis/inventory-static.cjs` from project root. Embedded assert checks cover CRC32, malformed PE and protobuf bounds. Script hashes sources, parses PE, validates/extracts packages inside analysis only.
2. Run `powershell -NoProfile -File analysis/inventory-windows.ps1` for version/signature and exact registry evidence.
3. Run `node analysis/integration-derived.cjs` for compact summaries, host manifests and script index.

These rerun inspection only; never execute target files. Contract for implementation agent: `integration-contract.md`. Do not copy extracted code/assets/DLLs into app.
