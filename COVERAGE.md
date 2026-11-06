# Feature coverage

Status describes completed implementation and executed evidence, not full original-product equivalence. Original observations come from bounded static/UI/one-transfer work; unchecked behavior remains unknown.

| Capability | Original observation | Reconstruction status / evidence |
|---|---|---|
| Add URL and destination confirmation | Runtime dialogs | **Completed, tested** native Add and production download/SHA-256 |
| HTTP single file | 16 MiB loopback transfer/hash | **Completed, tested** known/unknown length, zero-byte |
| HTTPS | Static TLS library presence; no original TLS runtime trace | **Implemented through platform TLS, integration untested**; certificate checks unchanged |
| Parallel segments | Runtime range GET trace | **Completed, tested** exact ranges, identity, ETag/Last-Modified |
| Resume/pause/recovery | Stop/pause UI; algorithm not established | **Completed, tested** pause/resume, changed/missing validators, process-kill restart |
| Concurrent jobs | Queue/download UI, original runtime not stressed | **Completed, tested** concurrency bounds and parallel jobs |
| Queue ordering | Main/synchronization queue menus; bounded native persisted-ID loader | **Completed, tested** one ordered queue; multiple named/sync queues unsupported |
| Scheduling | Native Scheduler examined | **Partial, tested** per-download due time while app awake; recurring/day-of-week/stop times unsupported |
| Retry/errors/timeouts | Retry controls in Scheduler | **Completed, tested** bounded backoff, drop, stall, 404/503, redirects; Retry-After unsupported |
| Range rejection/corruption prevention | Native Range builder/header parser examined; full original resume validation unresolved | **Completed, tested** ignored/false/malformed ranges, 416, changed representation, encoded responses |
| State/settings persistence | Registry/config artifacts and UI | **Completed, tested** state JSON, exclusive lock, corrupt-state fail-safe; power-loss durability untested |
| Progress/speed/ETA/error feedback | Runtime table/dialog | **Completed, tested** table bindings/state/details; quantitative ETA accuracy under changing bandwidth untested |
| Safe filenames/no overwrite | Confirmation UI | **Completed, tested** invalid/reserved/traversal/collision; Content-Disposition filename selection unsupported |
| Browser link handoff | Packaged native host/manifests/scripts; native framing/IPC examined | **Partial** independently authored MV3 extension + new host built;13 host checks/subprocess/mock tests and real host-to-desktop confirmation passed; live browser flow untested |
| Seamless handoff to running desktop | Native framing/named-pipe bridge examined; full handshake/grammar unresolved | **Unsupported** existing-instance conflict shown; host acknowledgment not durable enqueue |
| Automatic browser download interception | Static packaged classifier/handoff | **Unsupported** explicit user-selected link only; browser transfers never cancelled |
| Cookies/auth/referrer/POST session context | Static packaged capability, no credentials read | **Unsupported** no secrets capture or credential UI |
| Proxy configuration | Static imports/package permission | **Partial** platform defaults only; custom proxy UI/auth/integration tests unsupported |
| Category rules/auto naming | Runtime category tree | **Unsupported** explicit folder/filename; no category engine |
| Import/export/batch/site grabber | Menus observed | **Unsupported** |
| Media discovery/panels/stream assembly | Static packaged roles/events | **Unsupported** no media extraction/DRM handling |
| Speed limiter | Menu observed | **Unsupported** |
| Clipboard/drop target/startup hooks | Settings/menu observed | **Unsupported** no global monitoring or startup registration |
| Notifications/tray | Original completion/tray affordance | **Partial** completion row/error feedback; OS toast/tray unsupported |
| Shutdown/open-file automation | Scheduler affordance | **Unsupported by design** never execute downloads/shutdown automatically |
| Resizing/DPI | Original screen inspected current monitor | **Partial, tested** native windows, wide/compact layout at120DPI; multi-monitor/DPI matrix untested |
| Accessibility/keyboard | Original custom panes difficult UIA | **Partial, tested** native control names/focus/access keys and table focus; screen-reader/full keyboard audit untested |
| Reproducible Windows release | Not relevant | **Completed** source/build script/framework-dependent win-x64 publish; unsigned, no installer |

No claim that all native subsystems were reverse engineered. Deep analysis limited to evidence recorded in native report; proprietary algorithms not translated. See `TESTING.md` for precise checks and ceilings.
