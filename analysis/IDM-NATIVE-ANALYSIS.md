# IDM native analysis — 2026-11-06

## Scope and status

Analysis-only. Outputs stay in `C:\Users\Flugel\Projects\Flugel Download Manager\analysis`. No app/spec edits, target execution, installer execution, installation changes, licensing/activation analysis, or secret collection. Recovered pseudocode stays local; contracts below describe behavior, not reusable proprietary source.

`IDMMsgHost.exe` inspected first with installed REA 6.7.0 CLI and Ghidra 12.1. Process-local `GHIDRA_INSTALL_DIR`, `JAVA_HOME`, `TEMP`, and `TMP`; no global MCP/client configuration changes. Local stdio MCP launched through same REA CLI for persistent queries, evidence export, and explicit close. Ghidra headless fallback not needed.

## Artifact identity

Identity record: `artifact-identities.json` (local PowerShell file/hash/version/Authenticode observations, not REA Evidence).

| Artifact | Bytes | File version | SHA-256 |
|---|---:|---|---|
| `C:\Program Files (x86)\Internet Download Manager\IDMMsgHost.exe` | 39,168 | `6,38,20,9` | `dba25a49adb88f675db26d2dc7a0fa9d4a5db2326858cb9d2515f6f34b8e0b65` |
| `C:\Program Files (x86)\Internet Download Manager\IDMan.exe` | 6,517,616 | `6, 43, 18, 2` | `a705c0154fafc7cd509decf165cb6f6c050b6521b40adb6f730dd827c86051c7` |

Windows reported both Authenticode signatures `Valid`, signer `Tonec Inc.`. Signature check does not establish runtime behavior, source authenticity beyond signature policy, or equivalence between differently versioned components.

Toolchain provenance: `toolchain-provenance.json`. Ghidra `application.properties` reports 12.1, build `2026-May-13 1544 GMT`; Java reports Temurin `21.0.12.1+1`. Recursive `**/*ghidra*.zip` search under `C:\Users\Flugel` found bundled extension ZIPs only, no distribution ZIP. **Official release digest not validated; installed Ghidra/Java file provenance unknown.** No downloads performed. Metadata and successful analysis are not integrity verification.
Selected installed-file SHA-256 values retained in `toolchain-file-identities.json`; those are reproducibility anchors, not comparisons against official release digests.

Exact CLI syntax was read from installed package help. Representative invocation (run inside analysis directory, with process-local environment):

```powershell
$env:GHIDRA_INSTALL_DIR='C:\Users\Flugel\ghidra\ghidra_12.1_PUBLIC'
$env:JAVA_HOME='C:\Users\Flugel\jdk21\jdk-21.0.12.1+1'
$env:TEMP=(Get-Location).Path
$env:TMP=$env:TEMP
$rea='C:\Users\Flugel\AppData\Local\npm-cache\_npx\a1722018f28f9df2\node_modules\rea-agents\scripts\rea.mjs'
node $rea analyze 'C:\Program Files (x86)\Internet Download Manager\IDMMsgHost.exe' --provider ghidra --snapshot '.\idmmsghost.snapshot.json' --format json
node $rea decompile 'C:\Program Files (x86)\Internet Download Manager\IDMMsgHost.exe' '0x402830' --provider ghidra --snapshot '.\idmmsghost.snapshot.json' --format json
```

`rea-local-session.mjs` and job JSON files are analysis transport scripts only. They start installed CLI as stdio MCP, read canonical tool schemas, query immutable artifacts, export Evidence, and close targets. They are not app/host reconstruction code.

## Evidence index — host

Complete export: `idmmsghost-evidence.bundle.json` (13 records). Close receipt: `host-close.json`. Retained snapshot: `idmmsghost-final.snapshot.json`.
Supplemental worker export: `idmmsghost-worker-evidence.bundle.json` (includes preceding evidence plus H13). Close receipt: `host-worker-close.json`.

| Alias | Evidence ID | Observation |
|---|---|---|
| H1 | `ev_b31126f24bc8b52511b9de94b7340e744d89ff9093bb4bc7f5cd8e8a5489f2bb` | Overview |
| H2 | `ev_51fdba4e17beca691e3238fcf40eb7f53d2e8dae584cde6e1964e511ce93a98c` | Defined strings |
| H3 | `ev_25a8ff4ea0f39d25f3056680f10d13926fc460109a1d20d1a3d15fbc2234ceb6` | Procedure/import-name inventory |
| H4 | `ev_f2bba580ef73fd90541827743174df1a5492fa821ffcd2120e244257f486b2e8` | Main bridge pseudocode, `0x402830` |
| H5 | `ev_d806c1aeec73833b7a7ef4c44cbbc153627006dcafbe05bb58648821b7d1f710` | Escaping pseudocode, `0x402420` |
| H6 | `ev_0b016c5ba9939b96d37a217f985ef4d005fb124cee6750f9ee0dacd1ae692cea` | Unescaping pseudocode, `0x402550` |
| H7 | `ev_180ae9f91fb65e4cfe6fbbddf1b2bc92075a2da71c60f2f957322a566fa07969` | Initial control frame/process attribution, `0x403530` |
| H8 | `ev_2360727b761e7ba70c60c4ca59da00f841d47003e6cea58b4083239aa6f7be77` | COM bootstrap, `0x403750` |
| H9 | `ev_97f44c772d84850332b99118debed81db8f336b90c1eec0cdc1127596d04e906` | Pipe-name references `0x4028cc`, `0x4028d9` |
| H10 | `ev_5cf8cca15939d13edb4d91482109beab014b6b3bc1645abe4e24087234361930` | Decoder reference `0x403117` |
| H11 | `ev_f3f0cf0b15d274f4fe7ee8ca52c0c6ebd731544f6957be5ef6c3aad242cafead` | Complete 256-byte escape table read |
| H12 | `ev_1a2781c447923e4ddffdf258d95ba059d691af82121c23dd0b9cde19b05e260c` | I/O function dossier, `0x403410` |
| H13 | `ev_2fbfdd6196410e4ecc9665395b272977629849307d3a468d4e047cdde1ff4304` | Synchronous I/O worker pseudocode, `0x4034a0` |

## Observed — host

- **Inventory:** x86 native PE, image base `0x400000`, 165 provider procedure records and 152 defined strings. Inventory comprises 82 external function records plus 83 local functions/thunks/compiler helpers; not 165 independently authored application functions. Ghidra names mostly synthetic. Referenced PDB name exists but matching PDB not supplied/load state unknown. [H1, H3]
- **Imports:** external inventory includes `ReadFile`, `WriteFile`, `GetStdHandle`, `CreateFileW`, `GetOverlappedResult`, events/semaphores, `CreateThread`, process/session queries, read-only registry APIs, COM, and C runtime. No Winsock/WinINet/WinHTTP external functions appear in this recovered inventory. Bounded negative finding only: dynamic loading and other binaries can provide networking. [H3]
- **Routing:** `0x402830` acquires stdin/stdout; gets current Windows session ID; opens `\\.\pipe\IDMNetworkMonitor.` followed by decimal session ID, read/write, existing endpoint, overlapped I/O. Pipe-name references land inside this function. This is an existing-pipe client, not pipe server creation. [H4, H9]
- **Framing:** bridge first accumulates four bytes, interprets them as native x86 little-endian length, then accumulates body. Stdio-to-pipe path requires double-quoted body before calling decoder and rebuilding pipe-side framing. Pipe-to-stdio path reserves one extra byte in its in-memory buffer, escapes payload, wraps it in double quotes, and writes four-byte length plus resulting JSON string. Reserved byte is not evidence of an extra byte on pipe wire. Both paths show length framing; payload representation differs, so bridge is not transparent byte forwarding. [H4–H6, H10]
- **Escaping:** encoder maps quote/backslash and control bytes to escapes; table uses short escapes for backspace/tab/newline/formfeed/carriage return and `\\u00xx` for other low controls. Decoder handles quoted-string escape forms and Unicode conversion helper; additional non-JSON legacy escapes are visible. No assertion of full JSON/Unicode standards compliance. [H5, H6, H11]
- **I/O:** main tracks accumulated read length and written offset; retries incomplete transfers, dispatches overlapped completion events, and separates both directions. `0x403410`, called from `0x402830`, delegates to `ReadFile`/`WriteFile` or hands synchronous work to helper-thread synchronization and signals pending status. Worker `0x4034a0` waits on semaphore, performs synchronous `ReadFile`/`WriteFile`, records result/byte count, then sets completion event. Provider records caller body as noncontiguous; full source control structure not recovered. [H4, H12, H13]
- **Bootstrap:** when monitor mutex missing, conditional registry-status path invokes COM helper. Helper initializes COM, creates instance, and invokes `idmcmd://startnetmon/`. Main then raises internal diagnostic/termination path on successful bootstrap. No target execution performed; success on current installed system unknown. [H4, H8]
- **Peer attribution:** startup frame builder obtains process information, uses dynamically resolved pipe process-ID APIs when present, and fallback process-image/parent-process information otherwise. Startup includes local peer/process attribution metadata. Exact proprietary handshake intentionally not promoted into implementation contract; authentication strength not established. [H4, H7]

## Behavioral contracts for independently authored implementation

These describe inspected host path only, not complete IDM compatibility requirements.

| Contract | Basis | Boundary |
|---|---|---|
| Native messaging transport separates framing from payload; consume four-byte little-endian length before message bytes. | Observed H4 | Validating strict maximum lengths is independent safety policy; observed allocation sizes are not proven protocol limits. |
| Payload on inspected browser-facing path is JSON string, not presumed JSON object. Decode/encode string envelope separately from internal command parser. | Observed H4–H6 | No complete command grammar or extension compatibility established. |
| Reads/writes may complete partially. Retain per-direction framing and progress state until complete. | Observed H4, H12 | Thread count, buffer growth algorithm, and proprietary internal layouts need not be copied. |
| Keep stdout protocol-only; route diagnostics separately in new host. | Inference from binary-framed stdout in H4 | Exact error-output behavior/exit codes not verified. |
| Isolate local manager connection per logged-in OS session. | Observed session-specific routing H4 | New implementation should use independently named endpoint and independently designed authorization. |
| Manager bootstrap and steady-state forwarding are separate lifecycle phases. | Observed H4, H8 | Exact launch/retry policy belongs to new design; current manager bootstrap success unknown. |
| Use standard JSON string serializer/parser, strict size limits, and cancellation-safe stream APIs rather than reproducing legacy escape quirks. | Independent design recommendation from observed H4–H6 | Not claim of IDM-equivalent malformed-input behavior. |

## Inferred and unknown

**Inferred:** host primarily acts as browser-to-manager IPC adapter, not downloader. Supported by scoped imports and inspected bridge routines, not exhaustive proof about every function. Own browser extension/host/manager can use same high-level separation without adopting proprietary command syntax.

**Unknown:** full browser message grammar, internal command IDs, complete pipe handshake, true message limits, malformed-input behavior, reconnect semantics, manager-side authorization, exact process-exit behavior, performance, or runtime compatibility. No browser manifests/extension code, process traces, or live pipe traffic inspected. No HTTP range/resume/queue behavior established from host alone.

## Analysis limitations

Static evidence only. Ghidra default auto-analysis and decompiler output are observations/inferences, not original source or recorded execution. Defined-string inventory excludes undefined/packed strings. External symbol inventory is not exhaustive runtime dependency proof. Synthetic names, recovered types, odd pointer arithmetic, and noncontiguous bodies remain decompiler artifacts. REA Windows Ghidra P0 bridge reports testing against 12.1.4; installed 12.1 accepted but not identical tested build. No global configuration change was used to repair MCP environment.

One early standalone CLI `xrefs` request exceeded shell timeout before producing output; empty `idmmsghost-pipe-xrefs.json` is not evidence. Persistent local MCP query subsequently succeeded as H9. Do not use empty file as negative finding.

`node .\verify-analysis.mjs` validates report citations against retained bundles, rehashes both original target executables, and checks host close receipt. Initial verification passed. `idmmsghost-bundle-validation.json` records REA canonical export validation: 13 records, 281,379 bytes.

## IDMan.exe — initial bounded pass

Initial export: `idman-first-evidence.bundle.json` (3 records). Close receipt: `idman-first-close.json`; snapshot `idman-first.snapshot.json`. Initial default-analysis launch exceeded 120-second shell budget; second launch used 600-second budget and completed. Prior timed-out launch produced no overview evidence and its process was no longer present in later targeted process inventory.

| Alias | Evidence ID | Observation |
|---|---|---|
| D1 | `ev_a3216db54a0e18a90b059847473ad53c1530c80e6a03efde060c2350fffbd1b0` | Overview: x86 PE, image base `0x400000`, 21,458 procedure records, 7,335 defined strings |
| D2 | `ev_a0e42d4ea2a55b5f9b4341060883b4564e71f5a367543a2d6cdd14363b618666` | Regex search `Range:|Content-Range|Accept-Ranges|If-Range|ETag|queue|resume`, case-insensitive; 108 defined-string hits |

**Observed strings, not verified execution:** request templates include `Range: bytes=0-`, `Range: bytes=%I64d-`, and bounded `Range: bytes=%I64d-%I64d`; response/request vocabulary includes `Content-Range:`, `ETag:`, `accept-ranges:`, `If-Range:`. Queue-related text includes registry path `Software\DownloadManager\Queue`, item ID/length diagnostics, start/stop/finished messages, queue naming, and resume prompts. [D2]

**Not established by these strings:** actual range response validation, 200/206/416 transitions, ETag/If-Range policy, segment overlap strategy, retry conditions, queue ordering/concurrency/persistence semantics, or scheduler state machine. UI text about resuming/restarting is not proof of implemented edge-case behavior. Focused references and small decompiler-supported findings follow below.

IDMan metadata references `IDMan.pdb`, GUID `975d8ac9-8a58-4ea1-9a11-eae0537431a8`, age 1; load state unknown, no matching PDB supplied. Local Ghidra logs contain PE bitmap-resource creation conflicts and default-analysis warnings. Resource decoding is not complete; no UI/resource completeness claim. Native-code observations remain bounded to retained provider queries. [D1]

## IDMan.exe — focused code pass

Export `idman-focused-evidence.bundle.json`: 13 records, 5,078,896 bytes. Explicit close receipt `idman-focused-close.json`; retained `idman-focused.snapshot.json`. `list_procedures` completed but exceeded MCP response budget; `idman-procedures.json` is transport-error receipt, **not failed analysis or empty inventory**. Complete record retained and consumed from exported bundle; no duplicate producer run. Import subset saved in `idman-imports-derived.json`.

| Alias | Evidence ID | Observation |
|---|---|---|
| D3 | `ev_763ae48c514d688613383b932a511d039db090a90f16915749d739535b10fe52` | Complete 21,458-procedure inventory, including 631 external records |
| D4 | `ev_0542b057ed0c18669c48653fb70472a9e3d06f635e6233364fc1f58939cfba04` | Eight bounded-range-template references |
| D5 | `ev_9ed1347b18dc293e6b4f1ea8c2991982241b94c10ba1c08022ce74280ebfd1f5` | First reference `0x58b79d` belongs to `0x58b2b0` |
| D6 | `ev_7a75ae18ad32f4bf5cc911ce4c7db833906f98979843662b4fe9dd7459aea5e8` | Request builder pseudocode `0x58b2b0` |
| D7 | `ev_b0b7fc0b93d7653c5678fa841a9eeb53f3bb5063a4db2659b0f29c14a409c782` | Three Content-Range references |
| D8 | `ev_0cf3f5dd2171289a5f3c7ed2df9246c4b37ad0f2340b56dd1be82fc05fb537a2` | First reference `0x5820b9` belongs to `0x581cf0` |
| D9 | `ev_6ed89a6b1dd41de5cfd029fe8302a33705086538156b61e31a5593edcb18a7b6` | Header parser pseudocode `0x581cf0` |
| D10 | `ev_81507e7e690eb0df0738ab88f60b05f525d492b85c1aeb7ab1963e3612419cb3` | Queue constructor diagnostic reference `0x49e83f` |
| D11 | `ev_d40c2fb7d1d8ef5f4b5fc3a143817a7c30a6979698c5c1c8158b9a165b18bc21` | Reference belongs to noncontiguous function `0x49e710` |
| D12 | `ev_211065602046b9451394e663091ecd0adcd9d8c41cb0da23ea98c7ec27c3dee2` | Queue loader/constructor pseudocode `0x49e710` |

### Observed code facts

- External inventory includes Winsock `socket`, `connect`, `send`, `recv`, `select`, file I/O/seeking/flushing, registry APIs, and WinINet URL/cookie helpers. Import availability alone does not show frequency or complete transport architecture. [D3]
- Request builder selects no Range header, open-ended `bytes=0-`/`bytes=start-`, or bounded `bytes=start-end` from state. Uses 64-bit decimal formatting. One branch computes endpoint as start plus length minus one before formatting, supporting inclusive endpoint semantics in that branch. Other mode/flag meanings remain unidentified. [D4–D6]
- Same builder assembles HTTP/1.1 request text and calls Winsock `send` on one transport branch; alternate helpers handle another branch. `Accept-Encoding: identity`, `gzip, deflate`, custom encoding, or omission are selected by flags. Do not infer identity is universal default or copy unexplained segment-adjustment constants. [D6]
- Header parser compares header names case-insensitively, recognizes Content-Range, seeks `bytes ` plus dash/slash delimiters, and parses signed 64-bit quantities into separate fields. Also handles ETag branch, Content-Length, chunked Transfer-Encoding, compression markers, and multipart/byteranges vocabulary. Full grammar/standards compliance and downstream bounds validation not established. [D7–D9]
- Queue routine opens/creates per-user registry key, queries `Queue` value, walks space-delimited decimal item IDs, and calls insertion helper for parsed entries. Invalid tokens lead to diagnostic call. This establishes one legacy queue-loading representation, not complete persistence format or scheduling order. [D10–D12]

### Abstract contracts and remaining limits

| Independently usable contract | Verification boundary |
|---|---|
| Range request state supports absent, open-ended, and bounded forms with 64-bit offsets. Inclusive endpoint arithmetic exists in inspected branch. | D6; no proof every download uses these forms or same segmentation policy. |
| Response metadata separates range start/end/total, length, and entity-tag handling. | D9; storing/parsing metadata does not prove safe resume or response validation. |
| Compression/transfer framing must remain distinct from byte-range bookkeeping. | D6/D9 show separate paths; correct handling in new implementation is independent standards-based design. |
| Queue identity and serialized membership survive beyond individual transfer objects. | D12 observes load of persisted item IDs; scheduler ordering/concurrency and write/commit durability unknown. Use own persistence format. |

Only first reference owner decompiled for each of three seeds; remaining five range-template references, two Content-Range references, callers, validators, and queue insertion helper not followed. **200/206/416 behavior, entity-change rejection, If-Range emission policy, complete resume recovery, queue ordering/concurrency, retry/backoff, and scheduler remain unresolved.** Queue decompilation misclassifies cleanup helper as non-returning; do not infer normal cleanup/termination behavior from displayed tail. No live HTTP, downloads, or queue operations executed.

## Clean-room boundary

Implementation session should consume behavioral tables and limitations, not raw decompiler files, proprietary handshake literals, internal data layouts, or generated pseudo-C. Internal protocol interoperability has not been requested or established. Independently authored transport may use documented platform-native messaging framing with its own command schema, endpoint names, authorization, lifecycle policy, and tests.

For HTTP transfer engine, standards-based range validation and durable download state are independent design requirements, **not yet IDM-verified contracts**. Do not infer safe append on status 200, stable entity identity from URL alone, fixed connection counts, queue retry limits, or scheduler guarantees from these string hits.
