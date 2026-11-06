# Independent implementation handoff

Analysis report: `IDM-NATIVE-ANALYSIS.md`. Evidence IDs and limitations there are authoritative for this pass. Raw decompiler output stays local to analysis; do not copy implementation, data layouts, or proprietary handshake into app.

## Verified host-level contracts

- Browser-facing stream uses four-byte little-endian payload byte length; inspected payload is JSON string. Framing parser must retain incomplete header/body state. [H4]
- JSON string envelope and manager command payload are separate layers; host decodes outbound text and escapes inbound text. Use platform JSON implementation rather than copying legacy escape logic. [H4–H6, H11]
- Partial reads/writes tracked independently for both directions. Transport cannot assume one OS read/write equals one message. [H4, H12, H13]
- Manager endpoint routing uses current OS session. Independent app should choose own endpoint and authorization. [H4, H9]
- Bootstrap and active forwarding are distinct phases. Existing COM bootstrap is product-specific, not required architecture for independent app. [H4, H8]

## Design choices, not verified IDM behavior

Strict length ceiling, complete JSON/Unicode validation, permission checks, cancellation, bounded retry/reconnect, stdout-only protocol, diagnostics to stderr, and durable state are independent implementation requirements. Current analysis does not establish IDM's exact malformed-input behavior or runtime guarantees.

## Download engine boundary

Focused IDMan pseudocode supports absent/open-ended/bounded Range construction with 64-bit offsets; one branch computes inclusive endpoint from start plus length minus one. Header parser keeps range start/end/total handling separate from Content-Length/ETag and transfer/compression flags. Queue loader reads persisted space-delimited decimal item IDs. [D6, D9, D12]

No verified 200/206/416 transitions, entity-change rejection, If-Range emission policy, complete crash recovery, queue ordering/concurrency, retry/backoff, or scheduler contract. Use independently designed persistence and standards-correct validation; do not label those choices as IDM guarantees. No source, internal layouts, unexplained segment constants, or legacy registry serialization should be copied.

## Reproducibility and trust

Original target hashes unchanged in runnable check `node .\verify-analysis.mjs`. Both target Authenticode signatures reported valid. Ghidra/Java installed-file provenance remains unknown: official distribution ZIP unavailable in bounded user-home search; no official digest comparison performed. No target or installer executed; app/spec untouched; only process-local environment used.
