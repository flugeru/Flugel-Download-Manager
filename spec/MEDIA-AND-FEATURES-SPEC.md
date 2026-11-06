# Flugel Download Manager media and features spec (independent implementation)

This spec is derived from product requirements and observed behavioral needs for the
Flugel Download Manager browser/desktop integration. It does **not** copy proprietary code,
assets, UI text, activation logic, DRM handling, or unpublished implementation
details from any third-party product.

## Browser integration (MV3)

- **Content detection**: content script runs at `document_idle` in all frames on
  `http/https` pages. It detects `<video>` elements (including `<source>`
  children) using DOM observation (`MutationObserver`), periodic scans, and
  layout observers (`ResizeObserver`/`IntersectionObserver`) to support SPA and
  dynamically inserted players.
- **Balloon**: a shadow-DOM "Download this video" control is positioned near the
  visible player (fixed viewport coordinates), inset to avoid obstructing playback.
  It never modifies playback state. Users can dismiss it for the current page view.
- **Variants**: direct media (`.mp4/.webm/.m4v/.ogv/.mov` and `video.currentSrc`),
  HLS playlist hints (`.m3u8`, `application/vnd.apple.mpegurl`), and DASH manifest
  hints (`.mpd`, DASH MIME) are listed when discoverable from DOM attributes or
  observed network URLs. Quality labels are inferred from URL tokens or
  `video.videoHeight` when available.
- **Protected streams**: DRM/protected streams are not bypassed. The UI states
  that protected streams are unavailable. HLS/DASH parsing in the desktop engine
  rejects manifests indicating encryption/DRM (`EXT-X-KEY` methods other than
  `NONE`, DASH `ContentProtection`).
- **Native messaging**: the service worker sends `enqueue` / `enqueue-media`
  actions to the registered native host (`com.flugel.downloadmanager`). Requests are
  validated (http/https, no credentials/userinfo, size-limited JSON). On host or
  messaging failure, the extension reports the failure and must not cancel or
  discard browser downloads.
- **Settings**: global enable, balloon enable, and auto-intercept opt-in are
  stored in `chrome.storage.local`. Per-site enable is stored as `site:<host>`.
  Content scripts and the service worker honor these settings.

## Desktop engine/media handling

- **Direct files**: segmented HTTP range downloads with resume where supported.
- **HLS**: master playlists are parsed to list variants; selected media playlists
  are resolved to segment URLs and concatenated into a local `.ts` output in the
  desktop engine (unprotected only). Encryption/DRM markers cause rejection.
- **DASH**: MPD is parsed for adaptation sets/representations (unprotected only);
  `ContentProtection` causes rejection.
- **IPC**: same-user named pipe `FlugelDownloadManager.IPC.<sha256(user|dataDir)>[..24]`
  (CurrentUserOnly, JSON line protocol). Actions include `enqueue`,
  `enqueue-media`, `activate`, `query-config`. The native host forwards to a
  running desktop instance when present.

## User-facing behavior

- Browser capture always routes to desktop confirmation/add flow; it does not
  silently complete downloads.
- Interception is opt-in and must preserve browser download behavior on failure.
- Errors shown to users must not expose credentials, query secrets, or raw
  internal exception details.