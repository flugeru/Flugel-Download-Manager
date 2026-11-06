# Media and IPC analysis (sanitized)

Status: bounded, independent analysis for implementation guidance. No proprietary
binaries were executed or modified as part of this document. Prior reverse
engineering evidence in `analysis/` is treated as reference material only; this
summary records **sanitized behavioral requirements** implemented in Flugel Download Manager.

## IPC / single-instance forwarding

- **Requirement**: a second launch (desktop or browser native host) must forward
  the requested URL/media to an already-running same-user instance and report an
  accurate acceptance/forwarding result.
- **Implementation**: `Flugel.Core.SingleInstanceIpc` provides a per-user+data-dir
  named pipe server/client. The desktop app starts the server on launch. The
  native host attempts IPC forwarding before launching a new desktop process.
- **Evidence**: `Flugel.Checks` includes IPC enqueue coverage (51/51 total);
  `Flugel.NativeHost` self-tests cover structured schema and forwarding contract
  (14/14 total); prior baseline recorded framed host reply/forwarding behavior.

## Browser media detection

- **Requirement**: detect HTML5 players, including dynamic/SPA updates and
  all-frames contexts, show a non-obstructive balloon, list direct and
  unprotected streaming variants with quality/format, and start a desktop
  download from the selected variant.
- **Implementation**: MV3 extension (`browser/extension/`) uses a content script
  (`all_frames: true`) with shadow DOM UI, DOM `<video>/<source>` inspection,
  mutation/layout/visibility observers, service-worker webRequest URL correlation
  (metadata only, no response body), and native messaging to
  `Flugel.NativeHost.exe`.
- **Limitations**: network correlation is best-effort and subject to browser
  MV3/service-worker constraints. Cross-origin iframe behavior depends on browser
  injection/permission rules. DRM/protected streams are intentionally not
  offered. Full E2E "balloon click → registered native host → desktop download"
  requires explicit per-browser native-host registration for the installed
  extension ID and a user/desktop session where the browser can display UI.

## Real-browser verification status

- Fixture: `test-output/fixtures/video-balloon.html` served locally for DOM/player
  detection tests.
- Chromium automation attempt: Playwright-bundled Chromium launched with
  `--load-extension` and the fixture URL; desktop screenshot captured to
  `test-output/browser/chromium-balloon-test.png` as evidence artifact. The
  current agent session cannot render image input, so visual confirmation of the
  balloon in that capture is **not claimed here**; the artifact exists for human
  review.
- Edge 156 is installed on this machine; unpacked-extension/native-host E2E in a
  fully registered profile remains a required follow-up for acceptance sign-off.