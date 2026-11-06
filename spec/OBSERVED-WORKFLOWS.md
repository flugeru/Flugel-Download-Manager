# Verified workflow addendum

Behavior-only handoff. No implementation logic or proprietary assets.

## Observed installed application, controlled local run
- Main window presents toolbar download actions, category navigation, and table columns for filename, queue, size, progress, remaining time, speed, last attempt and description.
- Adding a URL first asks for URL and optionally authorization. Subsequent confirmation shows destination/category, known size, immediate start, deferred download and cancellation.
- Main menus expose pause/stop all, clear completed, queue start/stop and scheduling, sort/search, speed limit, import/export, batch downloads, link capture and site grabber. Menu availability establishes UI affordance, not verified implementation of each operation.
- General settings expose startup, clipboard monitoring, browser capture selection, modifier-key behavior and browser context-menu/panel customization.
- One controlled 16 MiB HTTP response completed with matching SHA-256. Server trace observed initial GET and multiple byte-range GETs using identity encoding. No claim about exact connection count/segmentation policy, retry behavior or durable recovery follows from this one run.
- Completion dialog offers Open, Open with, Open folder, Close and optional suppression. Independent application must never execute completed content automatically.

## Independent scope decision
Core API already covers equivalent add/confirm/start/defer, pause/resume, queue/schedule, progress/speed/ETA and completion/error workflows. Optional credentials UI, import/export, global clipboard capture, site grabber, media extraction, speed limiting, legacy integration and category rules remain outside baseline. Do not present these as implemented.

## Integration observation
Original browser packages use background/page/native-host roles and correlation/configuration handshakes. Full proprietary protocol and downstream IPC not established. Independent bridge intentionally uses new host identity, explicit context-menu action, bounded JSON framing and user confirmation. Browser transfers must remain untouched if handoff fails. No cookies/auth/header capture in baseline.
