# Harbor production-engine checks

Executed: 2026-11-06T06:55:15.9621226+00:00

Runtime: .NET 10.0.13; Microsoft Windows 10.0.26300

Command: `dotnet run --project app/Harbor.Checks/Harbor.Checks.csproj -c Release`

- PASS sequential known length (0,24s)
- PASS segmented exact ranges (0,14s)
- PASS large bounded streaming 64 MiB (0,51s)
- PASS ignored ranges restart sequential (0,17s)
- PASS false Accept-Ranges ignored (0,17s)
- PASS malformed Content-Range (0,14s)
- PASS wrong segmented Content-Range fallback (0,14s)
- PASS range rejection fallback (0,06s)
- PASS 416 changed range fallback (0,16s)
- PASS changed validator cannot mix bytes (0,04s)
- PASS weak validator forces sequential (0,13s)
- PASS Last-Modified validator (0,15s)
- PASS unknown chunked length (0,03s)
- PASS empty zero bytes (0,03s)
- PASS finite redirect (0,04s)
- PASS dropped body retries (0,42s)
- PASS 503 bounded retry then success (0,93s)
- PASS 404 permanent failure without retries (0,12s)
- PASS 503 exhausted bounded retries (0,91s)
- PASS pause resume retains validated ranges (1,59s)
- PASS restart mid-transfer revalidates partials (1,31s)
- PASS process kill recovers atomic checkpoint (5,13s)
- PASS partial length mismatch restarts safely (1,24s)
- PASS missing validator restarts rather than append (4,63s)
- PASS validator changed across pause (1,50s)
- PASS immediate pause resume race (1,25s)
- PASS idle read timeout bounded (15,14s)
- PASS cancel stalled body promptly (0,13s)
- PASS redirect loop bounded (0,12s)
- PASS encoded range response rejected (0,13s)
- PASS queue order concurrency schedule (2,17s)
- PASS parallel jobs (1,35s)
- PASS remove active safe; completed file preserved (0,52s)
- PASS filename URL settings validation (0,00s)
- PASS destination collisions never overwrite (1,21s)
- PASS settings persistence and data lock (0,01s)
- PASS corrupt state fails without overwrite (0,00s)
- PASS incomplete metadata fails without overwrite (0,00s)
- PASS unsaved invalid settings cannot poison persistence (0,07s)
- PASS structured logs exclude secrets and URL (0,11s)

Total: 40/40 passed.

Artifacts: `C:\Users\Flugel\AppData\Local\Temp\opencode\harbor-checks-bf3b4d37f75c4ab9bcf9a38ec2363762`

Limitations: local HTTP only; no public TLS/proxy integration, power-loss durability simulation, GUI, or browser checks. Real child-process termination exercised; power-loss persistence not claimed. Memory bounded by fixed 64 KiB transfer buffers per segment plus fixed copy buffers; 64 MiB file hashed without whole-file allocation. Destination volume needs space for partials plus assembled file before atomic publication. External mutation of private partials by same-user software is outside trust boundary; length checked, not cryptographically authenticated.
