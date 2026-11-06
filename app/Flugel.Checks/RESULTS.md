# Flugel Download Manager production-engine checks

Executed: 2026-11-06T13:04:19.3492213+00:00

Runtime: .NET 10.0.13; Microsoft Windows 10.0.26300

Command: `dotnet run --project app/Flugel.Checks/Flugel.Checks.csproj -c Release`

- PASS sequential known length (0,26s)
- PASS segmented exact ranges (0,15s)
- PASS large bounded streaming 64 MiB (0,61s)
- PASS ignored ranges restart sequential (0,17s)
- PASS false Accept-Ranges ignored (0,05s)
- PASS malformed Content-Range (0,14s)
- PASS wrong segmented Content-Range fallback (0,14s)
- PASS range rejection fallback (0,17s)
- PASS 416 changed range fallback (0,16s)
- PASS changed validator cannot mix bytes (0,16s)
- PASS weak validator forces sequential (0,14s)
- PASS Last-Modified validator (0,16s)
- PASS unknown chunked length (0,17s)
- PASS empty zero bytes (0,12s)
- PASS finite redirect (0,15s)
- PASS dropped body retries (0,41s)
- PASS 503 bounded retry then success (0,94s)
- PASS 404 permanent failure without retries (0,12s)
- PASS 503 exhausted bounded retries (0,88s)
- PASS pause resume retains validated ranges (1,70s)
- PASS restart mid-transfer revalidates partials (1,33s)
- PASS process kill recovers atomic checkpoint (5,87s)
- PASS partial length mismatch restarts safely (1,19s)
- PASS missing validator restarts rather than append (4,64s)
- PASS validator changed across pause (1,56s)
- PASS immediate pause resume race (1,27s)
- PASS idle read timeout bounded (15,15s)
- PASS cancel stalled body promptly (0,13s)
- PASS redirect loop bounded (0,12s)
- PASS encoded range response rejected (0,03s)
- PASS queue order concurrency schedule (2,14s)
- PASS parallel jobs (1,18s)
- PASS remove active safe; completed file preserved (0,49s)
- PASS filename URL settings validation (0,00s)
- PASS destination collisions never overwrite (1,16s)
- PASS settings persistence and data lock (0,01s)
- PASS corrupt state fails without overwrite (0,00s)
- PASS incomplete metadata fails without overwrite (0,00s)
- PASS unsaved invalid settings cannot poison persistence (0,15s)
- PASS structured logs exclude secrets and URL (0,04s)
- PASS category auto-classification and subfolder routing (0,14s)
- PASS named queues start stop and per-queue concurrency (0,39s)
- PASS per-download speed limiter caps throughput (1,51s)
- PASS Content-Disposition server filename resolution (0,16s)
- PASS request context headers and basic auth without log leak (0,14s)
- PASS batch wildcard URL expansion numeric and alphabetic (0,00s)
- PASS HLS master and media playlist download and DRM rejection (0,14s)
- PASS DASH MPD manifest parsing and ContentProtection detection (0,01s)
- PASS Site Grabber discovers page links and HLS variants (0,02s)
- PASS Import and Export structured and text lists (0,02s)
- PASS Single-instance Named Pipe IPC concurrent forwarding (0,02s)

Total: 51/51 passed.

Artifacts: `C:\Users\Flugel\AppData\Local\Temp\opencode\harbor-checks-b4220c2d82b54056a0ddb8aae02b7905`

Limitations: local HTTP only; no public TLS/proxy integration, power-loss durability simulation, GUI, or browser checks. Real child-process termination exercised; power-loss persistence not claimed. Memory bounded by fixed 64 KiB transfer buffers per segment plus fixed copy buffers; 64 MiB file hashed without whole-file allocation. Destination volume needs space for partials plus assembled file before atomic publication. External mutation of private partials by same-user software is outside trust boundary; length checked, not cryptographically authenticated.
