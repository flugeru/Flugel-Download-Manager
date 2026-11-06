# Browser media handoff regression — 2026-11-06

## Confirmed defects and fixes

- No native host registration for the installed Helium extension. Registered HKCU Chrome native messaging fallback for exact extension ID `lbhlebbjhdkbchojmgkmeepgglohmepm`, loaded from `artifacts/browser/extension`. No browser preferences were edited.
- Extension serialized optional `segmentUrls` as null; native host rejected the direct MP4 request. Sender now omits absent metadata; host also accepts null optional fields for older extension compatibility. Strict three-field `enqueue` remains supported.
- Link menu sent extra metadata with strict `enqueue`. Metadata now selects `enqueue-media`.
- Desktop IPC awaited a modal confirmation before replying. Desktop now reserves the confirmation slot, acknowledges, then opens the dialog. Busy capture returns an explicit rejection rather than dropping the URL.
- Extension discarded host error reasons. Content notices now distinguish missing registration, forbidden extension ID, invalid request, busy desktop and unavailable desktop. Popup shows extension ID and checks native host readiness.
- Network observer lacked `webRequest` permission. Manifest now declares it.

## Verified

- Engine: 51/51 checks passed before final host-only error-reason adjustment.
- Desktop: 19/19 smoke checks passed, including acknowledgment while confirmation remains open and busy rejection.
- `node browser/check-media-bridge.cjs`: passed payload/schema/error propagation and unsafe URL regression.
- `node browser/live-media-check.cjs <Helium executable>`: passed in an isolated real Helium profile with actual extension, registered native host and production desktop. Clicked the rendered balloon and MP4 entry, confirmed Start now using UI Automation, downloaded 262144 controlled HTTP bytes with exact SHA-256 `81d0141af58569f91edf2d036b67a6b82c062d1829fd93aec50ed96b4773d225`.
- Live evidence: `C:\Users\Flugel\AppData\Local\Temp\opencode\media-live-qPYKtN\evidence.json` and `balloon-menu.png`.

## Scope limits

The local payload tests transport and MP4 URL handoff, not codec playback. The external website in the user's screenshot was not downloaded or validated. Site-specific expiring URLs, cookies and server restrictions remain separate compatibility issues. DRM is not bypassed. HLS/DASH support is not universal; do not describe discovery/parser checks as complete playback/remux support.

Reload the installed extension in `chrome://extensions`, then reload the video page to replace old content scripts. No browser restart is required.
