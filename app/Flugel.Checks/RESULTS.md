# Flugel Download Manager production-engine checks

Executed: 2026-11-06T14:53:55.3738379+00:00

Runtime: .NET 10.0.13; Microsoft Windows 10.0.26300

Command: `dotnet run --project app/Flugel.Checks/Flugel.Checks.csproj -c Release`

- PASS sequential known length (0,25s)
- PASS segmented exact ranges (0,18s)
- PASS large bounded streaming 64 MiB (1,31s)
- PASS ignored ranges restart sequential (0,17s)
- PASS false Accept-Ranges ignored (0,05s)
- PASS malformed Content-Range (0,14s)
- PASS wrong segmented Content-Range fallback (0,17s)
- PASS range rejection fallback (0,17s)
- PASS 416 changed range fallback (0,05s)
- PASS changed validator cannot mix bytes (0,17s)
- PASS weak validator forces sequential (0,17s)
- PASS Last-Modified validator (0,17s)
- PASS unknown chunked length (0,19s)
- PASS empty zero bytes (0,13s)
- PASS finite redirect (0,07s)
- PASS redirect strips credentials across origins and retains them on same origin (0,30s)
- PASS dropped body retries (0,44s)
- PASS 503 bounded retry then success (0,94s)
- PASS 404 permanent failure without retries (0,13s)
- PASS 503 exhausted bounded retries (0,91s)
- PASS pause resume retains validated ranges (1,66s)
- PASS restart mid-transfer revalidates partials (1,32s)
- PASS process kill recovers atomic checkpoint (4,98s)
- PASS partial length mismatch restarts safely (1,17s)
- PASS missing validator restarts rather than append (4,60s)
- PASS validator changed across pause (1,52s)
- PASS immediate pause resume race (1,25s)
- PASS idle read timeout bounded (15,14s)
- PASS cancel stalled body promptly (0,13s)
- PASS redirect loop bounded (0,15s)
- PASS encoded range response rejected (0,14s)
- PASS queue order concurrency schedule (2,35s)
- PASS parallel jobs (1,47s)
- PASS remove active safe; completed file preserved (0,62s)
- PASS filename URL settings validation (0,00s)
- PASS destination collisions never overwrite (1,26s)
- PASS settings persistence and data lock (0,04s)
- PASS corrupt state fails without overwrite (0,01s)
- PASS incomplete metadata fails without overwrite (0,00s)
- PASS unsaved invalid settings cannot poison persistence (0,20s)
- PASS structured logs exclude secrets and URL (0,11s)
- PASS category auto-classification and subfolder routing (0,22s)
- PASS named queues start stop and per-queue concurrency (0,41s)
- PASS per-download speed limiter caps throughput (1,52s)
- PASS Content-Disposition server filename resolution (0,22s)
- PASS automatic server filename handles UTF-8 collisions and preserves explicit names (0,25s)
- PASS request context headers and basic auth without log leak (0,14s)
- PASS batch wildcard URL expansion numeric and alphabetic (0,00s)
- PASS HLS master and media playlist download and DRM rejection (0,13s)
- PASS DASH MPD manifest parsing and ContentProtection detection (0,01s)
- PASS Site Grabber discovers page links and HLS variants (0,03s)
- PASS Import and Export structured and text lists (0,02s)
- PASS Single-instance Named Pipe IPC concurrent forwarding (0,02s)

Total: 53/53 passed.

Artifacts: `C:\Users\Flugel\AppData\Local\Temp\opencode\harbor-checks-8a883cc9e6d947ec994234cf0c901574`

Limitations: local HTTP only; no public TLS/proxy integration, power-loss durability simulation, GUI, or browser checks. Real child-process termination exercised; power-loss persistence not claimed. Memory bounded by fixed 64 KiB transfer buffers per segment plus fixed copy buffers; 64 MiB file hashed without whole-file allocation. Destination volume needs space for partials plus assembled file before atomic publication. External mutation of private partials by same-user software is outside trust boundary; length checked, not cryptographically authenticated.
