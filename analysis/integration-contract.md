# Browser-to-desktop behavioral contract

Clean-room behavioral guidance for Flugel Download Manager. Not proprietary source, not ABI compatibility promise. **Observed** means static packaged logic or configuration; **recommended** means new design; **unknown** means not proven here. Implementation agent should consume this file and inventory report, not extracted source/assets/binaries.

## 1. Boundary and roles

**Observed:** browser background owns discovery, browser lifecycle, request context, handoff and desktop connection. Content scripts own page/frame/element observations and selection/link enumeration. Optional page bridge observes page-world response data. Native host bridges browser transport to desktop; exact downstream IPC unknown.

**Recommended:** separate public browser-facing actions from desktop transfer engine. Use new application-owned extension ID, native-host name, manifest, registry registration and protocol. Do not reuse IDM IDs, registration keys, URL schemes, DLLs, source code or host executable. Do not require kernel/browser injection or legacy XUL/IE integration for initial product.

## 2. Connection lifecycle

**Observed:** detached/connecting/connected states; handshake advertises browser/extension/protocol/platform information; desktop returns configuration; IDs correlate request/reply; disconnect invalidates stale requests, frame associations and enabled capture state. Packaged implementation tries WebSocket then native messaging and retries with increasing delay.

**Recommended:** use native messaging first; no local HTTP/WebSocket listener needed unless separately justified. Host discovery uses app-owned manifest registered under current user. Exact extension origin allowlist; standard-user execution. Expose `unavailable`, `connecting`, `ready`, `permission-denied`, `disconnected` states, pending request IDs and bounded reconnect. Desktop absence must not break browser downloads or page rendering. Restore browser-controlled transfers when handoff cannot complete.

## 3. Transport: proven boundaries only

**Platform verified:** Windows native messaging uses little-endian 32-bit byte length followed by UTF-8 JSON; JSON values need not be objects. Length counts bytes, not JS string length. Chrome documents max 1 MB host-to-browser, 64 MiB browser-to-host. Handle fragmented reads, EOF, malformed JSON, invalid lengths and bounded buffering. No logs on stdout. Native messaging permission and exact host-origin allowlist required.

**Observed inner IDM format:** extension posts a JSON string, not a JSON object. String holds custom records with `MSG#` prefix; four numeric header fields represent message ID, message kind, secondary code and flags; colon-separated positional numbers; comma-separated numeric-key fields; semicolon record terminator. String fields length-prefixed using UTF-8 byte count. Native binary payload branches can base64 encode with `%` marker; WebSocket branch can carry bytes directly. Parser can consume successive records. Definitions supported by Chrome `background.js` lines 15–20, 37–41, 73–80, 125–126; matching basic flow exists in other modern packages.

**Unknown:** complete numeric field dictionary, host decoder bounds/validation, every flag, binary variant compatibility, protocol negotiation across all versions and host-to-app transport. This evidence is insufficient for a reliable IDM-compatible client. Do not reconstruct protocol by guessing unknown opcodes.

**Recommended new protocol:** bounded JSON object envelope with version, request ID, action, typed payload and explicit accepted/rejected/error response. No proprietary numeric fields needed. Transfer acceptance and completion are distinct; do not claim completed download on enqueue acknowledgment. Unknown actions/versions rejected without executing commands.

## 4. Supported action categories

| Abstract action | Minimum inputs | Expected output/state | Evidence level |
|---|---|---|---|
| Download selected link/media | Valid absolute URL; optional suggested name, source page/referrer and human label | Accepted transfer ID or explicit rejection; preserve browser fallback | Observed menu and link handoff |
| Download all/selected links | Per-frame candidate URLs and optional names/labels; page context | Deduplicated ordered batch; empty-state message | Observed DOM enumeration and batching |
| Candidate discovery | URL, request/frame/tab identity, method, MIME, disposition, filename, status and available byte length | Eligible/ignored candidate with reason; no automatic protected-content claim | Observed request classifier |
| Browser download handoff | Browser download identity, URL/final URL, filename, MIME/size and minimal permitted context | Pause while deciding; cancel browser copy only when safely accepted; resume if rejected/disconnected | Observed pause/resume/cancel paths; acceptance durability is recommended safety |
| Media affordance | Candidate identity, frame/element identity, current geometry, visibility, page context | Show/update/hide downloadable-media action; remove on navigation/deletion | Observed DOM/geometry/window messaging; exact native presentation unknown |
| Capture toggle | User choice scoped to tab/site/global | Immediate visible state; no stale capture after disable | Observed action click and per-tab states |
| Configuration/health | Protocol/browser/version/features; user settings | Capability report, enabled features, host unavailable guidance | Observed handshake/configuration and missing-host pages |

Page bridge is optional. Initial product can discover ordinary links and permitted network metadata without wrapping site code. Add bridge only for owned/authorized pages when concrete requirement exceeds normal extension APIs. Do not implement site-specific signature transforms, license flows, token extraction, or DRM circumvention from packaged code.

## 5. Events and cleanup

**Observed background events:** action click; context menu click/show; content runtime connect/message/disconnect; window create/remove/focus/bounds; tab activate/update/attach/remove; navigation commit/history/new target; request before-request/send-headers/headers-received/response-start/completion/error; Chrome download create/filename determination; proxy setting changes where available.

**Observed page events:** DOM ready, resize, scroll, blur, mouse actions, modifier key state, link click, legacy beforeload; mutation, resize and intersection observation. Frame/element associations are invalidated on removal and navigation. Main/subframe ports remain separate.

**Recommended:** only subscribe to events product needs. Debounce geometry updates; never poll every DOM node. Clean listeners/observers/timers/pending requests on disconnect or navigation. Handle same-document navigation and detached frames. Content-script data is untrusted even when origin checks pass.

## 6. Request-context safety

**Observed:** package can serialize cookies, headers, user agent, referrer, selected POST body data and proxy settings into desktop handoff. This analysis records capability only; no real credentials or browsing records captured.

**Required for new implementation:** validate URL scheme, host, length and type at every boundary. Accept only supported download schemes; no executable shell commands, arbitrary filesystem paths or opaque script evaluation. Ask for least permissions. Do not collect cookies, Authorization, proxy credentials, POST bodies, page source or storage by default. If authenticated downloads become explicit authorized requirement, minimize data to selected request, keep ephemeral, redact logs, and document permission/consent boundaries. Suggested filename never grants arbitrary destination traversal; desktop chooses validated safe destination. Page messages cannot grant native privileges.

## 7. Browser restrictions and portability

- Chrome package evidence is MV3; bundled Edge/Firefox evidence MV2. New Chromium target should use MV3, not port persistent MV2 background assumptions.
- Ordinary MV3 `webRequest` can observe allowed metadata, but `webRequestBlocking` unavailable except policy installation. Declarative rules are not arbitrary async request interception. Do not promise universal seamless interception.
- Service worker restart must not silently lose durable jobs; persist only necessary non-secret state and reconcile with desktop. Reconnect explicitly; avoid relying on immortal background process.
- `<all_urls>` still subject to user/site/browser restrictions. Missing host permission, protected/internal pages, inaccessible frames, denied incognito/file access and non-Windows platform need explicit unavailable/fallback states. Current installed browser/policies unknown.
- Native host registration and allowlist do not automatically install/enable browser extension. Packaging/distribution is separate, app-owned, user-approved work.

Documentation: official Chrome native messaging, webRequest and declare-permissions pages linked in `inventory-report.md`, retrieved via Context7. No Firefox/Edge current-policy assurance inferred from packaged manifest.

## 8. Minimal acceptance checks for implementation agent

1. Missing host or denied permission produces clear state; ordinary browser download continues.
2. Single URL and duplicate-containing batch produce safe transfer records; unsupported schemes and traversal filenames rejected or sanitized safely.
3. Disconnect during pending handoff resumes browser transfer unless desktop acknowledgment establishes durable acceptance.
4. Multiple frames, same-document navigation and removed elements cannot route candidate to stale tab/frame.
5. Partial native frames, malformed JSON, oversized payloads, unknown actions/versions and invalid origins fail closed without process commands.
6. No proprietary files enter app bundle; no secrets/page bodies logged; no IDM registry or install writes.

## Unknowns not implementation requirements

Exact native media parsing and segment assembly; DRM/protected-content support; resumability algorithm, scheduling, queue persistence, driver/injection behavior; complete IDM wire/COM interface; real runtime download success; installer actions. Do not claim feature parity from this inventory.
