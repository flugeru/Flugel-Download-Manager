# Bounded native analysis report

## Outcome

**Process-local REA/Ghidra bridge succeeded. All completed native sessions exported Evidence and explicitly closed. Global MCP/client configuration unchanged.** No app/spec edits, target/installer execution, activation/licensing analysis, or original installation changes.

Detailed local findings and full Evidence index: [IDM-NATIVE-ANALYSIS.md](IDM-NATIVE-ANALYSIS.md). Abstract implementation handoff: [IMPLEMENTATION-HANDOFF.md](IMPLEMENTATION-HANDOFF.md). Neither document embeds raw pseudocode. Raw decompiler evidence remains local to analysis directory.

## Engine validation and authenticity coverage

| Item | Verified observation | Limitation |
|---|---|---|
| REA | Existing cached `rea-agents` **6.7.0**, exact installed CLI help and MCP schemas inspected. | No new package/download required. |
| Ghidra | Existing `C:\Users\Flugel\ghidra\ghidra_12.1_PUBLIC`; `application.properties` reports **12.1**, build `2026-May-13 1544 GMT`. REA reports provider `ghidra`, version `12.1`; native overview, references, bytes, function dossier, pseudocode, export/close succeeded. | REA bridge reports tested against 12.1.4; installed 12.1 accepted, not identical tested build. Successful launch does not authenticate installation. |
| Java | Existing `C:\Users\Flugel\jdk21\jdk-21.0.12.1+1`; direct `java -version` reports Temurin **21.0.12.1+1**, 64-bit. | Official archive digest not checked; installed-file provenance unknown. |
| Ghidra release digest | Recursive `**/*ghidra*.zip` search under `C:\Users\Flugel` found bundled extension ZIPs only, no official distribution ZIP. | **Official release digest not validated. Installed Ghidra provenance unknown.** No substitute claim based on version metadata. No download performed. |
| Local reproducibility | Selected toolchain file SHA-256 recorded in `toolchain-file-identities.json`. | Local hashes are identity anchors, not comparison against official release hashes. |
| Original targets | SHA-256, size, file version, Windows Authenticode observations in `artifact-identities.json`; both signatures reported `Valid`, signer `Tonec Inc.`. Final check rehashes originals. | Authenticode result is Windows policy observation, not behavioral or whole-toolchain provenance proof. |

Connected MCP originally lacked `GHIDRA_INSTALL_DIR`/`JAVA_HOME`. Repaired **only process-local launch environment** for installed REA CLI/local stdio MCP: `GHIDRA_INSTALL_DIR`, `JAVA_HOME`, `TEMP`, `TMP`. Temporary Ghidra projects and retained evidence stayed under analysis directory. No global environment, MCP registration, or client configuration edits. REA remained primary; direct Ghidra-headless fallback unnecessary (REA internally uses official headless launcher).

## Immutable artifact identities

| Target | Bytes / version | SHA-256 |
|---|---|---|
| `C:\Program Files (x86)\Internet Download Manager\IDMMsgHost.exe` | 39,168 / `6,38,20,9` | `dba25a49adb88f675db26d2dc7a0fa9d4a5db2326858cb9d2515f6f34b8e0b65` |
| `C:\Program Files (x86)\Internet Download Manager\IDMan.exe` | 6,517,616 / `6, 43, 18, 2` | `a705c0154fafc7cd509decf165cb6f6c050b6521b40adb6f730dd827c86051c7` |

## Observed findings

### IDMMsgHost.exe first

- x86 PE, base `0x400000`, 165 procedure records (82 external, 83 local/compiler helpers/thunks), 152 defined strings. [H1, H3]
- Inspected bridge `0x402830` uses stdin/stdout, four-byte little-endian message length, JSON-string envelope conversion, per-direction read/write progress, and existing Windows-session-scoped named pipe. Pipe-name references at `0x4028cc`, `0x4028d9`; decoder call reference at `0x403117`. [H4, H9, H10]
- JSON escaping/unescaping supported by separate decompiled helpers and complete 256-byte escape-table read. Standard JSON parser/serializer should replace legacy escape quirks in independently authored code. [H5, H6, H11]
- I/O dossier and worker pseudocode corroborate partial-I/O/stateful forwarding, synchronous helper worker, events/semaphores, and overlapped-completion bridge. [H12, H13]
- COM bootstrap path requests manager monitor startup; success on current installation not executed or verified. [H8]

### Focused IDMan.exe pass

- x86 PE, base `0x400000`, 21,458 procedure records, 7,335 defined strings; complete external inventory has 631 records, including Winsock, file I/O, registry, and WinINet URL/cookie helpers. [D1, D3]
- Eight bounded Range-template references found; first resolves to request builder `0x58b2b0`. Pseudocode constructs absent/open-ended/bounded Range forms using 64-bit offsets; one branch computes inclusive endpoint as start plus length minus one. [D4–D6]
- Three Content-Range references found; first resolves to header parser `0x581cf0`. Pseudocode handles case-insensitive header-name comparison, range quantities, Content-Length/ETag branches, and separate transfer/compression flags. [D7–D9]
- Queue diagnostic reference `0x49e83f` resolves to `0x49e710`; pseudocode loads persisted space-delimited decimal item IDs from per-user registry value and passes parsed items to insertion helper. No full scheduler reconstruction. [D10–D12]

## Evidence IDs for principal claims

| Alias | Evidence ID |
|---|---|
| H1 host overview | `ev_b31126f24bc8b52511b9de94b7340e744d89ff9093bb4bc7f5cd8e8a5489f2bb` |
| H3 host procedures/imports | `ev_25a8ff4ea0f39d25f3056680f10d13926fc460109a1d20d1a3d15fbc2234ceb6` |
| H4 host bridge | `ev_f2bba580ef73fd90541827743174df1a5492fa821ffcd2120e244257f486b2e8` |
| H5 escaping | `ev_d806c1aeec73833b7a7ef4c44cbbc153627006dcafbe05bb58648821b7d1f710` |
| H6 unescaping | `ev_0b016c5ba9939b96d37a217f985ef4d005fb124cee6750f9ee0dacd1ae692cea` |
| H8 bootstrap | `ev_2360727b761e7ba70c60c4ca59da00f841d47003e6cea58b4083239aa6f7be77` |
| H9 pipe references | `ev_97f44c772d84850332b99118debed81db8f336b90c1eec0cdc1127596d04e906` |
| H10 decoder reference | `ev_5cf8cca15939d13edb4d91482109beab014b6b3bc1645abe4e24087234361930` |
| H11 escape table | `ev_f3f0cf0b15d274f4fe7ee8ca52c0c6ebd731544f6957be5ef6c3aad242cafead` |
| H12 I/O dossier | `ev_1a2781c447923e4ddffdf258d95ba059d691af82121c23dd0b9cde19b05e260c` |
| H13 I/O worker | `ev_2fbfdd6196410e4ecc9665395b272977629849307d3a468d4e047cdde1ff4304` |
| D1 IDMan overview | `ev_a3216db54a0e18a90b059847473ad53c1530c80e6a03efde060c2350fffbd1b0` |
| D3 complete procedures/imports | `ev_763ae48c514d688613383b932a511d039db090a90f16915749d739535b10fe52` |
| D4 range references | `ev_0542b057ed0c18669c48653fb70472a9e3d06f635e6233364fc1f58939cfba04` |
| D5 request-builder ownership | `ev_9ed1347b18dc293e6b4f1ea8c2991982241b94c10ba1c08022ce74280ebfd1f5` |
| D6 request builder | `ev_7a75ae18ad32f4bf5cc911ce4c7db833906f98979843662b4fe9dd7459aea5e8` |
| D7 Content-Range references | `ev_b0b7fc0b93d7653c5678fa841a9eeb53f3bb5063a4db2659b0f29c14a409c782` |
| D8 header-parser ownership | `ev_0cf3f5dd2171289a5f3c7ed2df9246c4b37ad0f2340b56dd1be82fc05fb537a2` |
| D9 header parser | `ev_6ed89a6b1dd41de5cfd029fe8302a33705086538156b61e31a5593edcb18a7b6` |
| D10 queue reference | `ev_81507e7e690eb0df0738ab88f60b05f525d492b85c1aeb7ab1963e3612419cb3` |
| D11 queue ownership | `ev_d40c2fb7d1d8ef5f4b5fc3a143817a7c30a6979698c5c1c8158b9a165b18bc21` |
| D12 queue loader | `ev_211065602046b9451394e663091ecd0adcd9d8c41cb0da23ea98c7ec27c3dee2` |

## Independently authored implementation contracts

1. Separate stream framing, JSON envelope, and own command schema. Track incomplete headers, bodies, and writes independently. Inspected host supports this layered contract; proprietary pipe handshake is not implementation input.
2. Represent byte offsets with 64-bit capacity. Range state supports absent/open-ended/bounded forms; inclusive endpoints require explicit arithmetic. Choose own segmentation and validation rules.
3. Keep parsed response range metadata, total length, validators, transfer framing, and compression state distinct. Parsing alone is not safe resume authorization.
4. Persist queue membership as stable item identities with independently chosen storage. Observed legacy serialization does not dictate new format, ordering, concurrency, or retry policy.

**Independent safety policy, not verified IDM behavior:** strict maximum message size, standards-correct JSON/Unicode handling, peer authorization, cancellation, response/entity validation before append, durable checkpoints, and bounded retry/backoff.

## Inferred / unknown / blockers

**Inferred:** inspected host primarily adapts browser messaging to manager IPC rather than downloading. Import inventory and bridge functions support this inference, not exhaustive proof over all possible dynamic behavior.

**Unknown:** browser command grammar, full internal handshake, actual maximum frame length, malformed-message behavior, reconnect/exit semantics, runtime compatibility, safe-resume 200/206/416 transitions, entity-change rejection, If-Range emission, full crash recovery, queue ordering/concurrency, retry/backoff, scheduler semantics. No live transfers or queue operations observed. Only first owner decompiled for each of three IDMan seeds; remaining reference owners and downstream validators/insertion helpers not followed.

**Backend limits handled:** early 120-second shell timeouts produced no usable record; later bounded 600-second REA sessions completed. Complete IDMan procedure response exceeded MCP transport budget, but operation completed and exact retained record exported successfully; no false empty-inventory claim. Ghidra PDB load state unknown for both targets; no matching PDB supplied. PE resource conflicts/default-analysis warnings remain; no resource/UI completeness claim. Queue cleanup helper marked non-returning by decompiler; displayed tail is not reliable termination contract. Ghidra pseudocode/types/names are analysis artifacts, not original source. No active native backend blocker remains; authenticity and behavioral coverage limits above remain explicit.

## Export, close, and checks

| Evidence bundle | Records | Explicit close receipt |
|---|---:|---|
| `idmmsghost-evidence.bundle.json` | 13 | `host-close.json` |
| `idmmsghost-worker-evidence.bundle.json` | 14 | `host-worker-close.json` |
| `idman-first-evidence.bundle.json` | 3 | `idman-first-close.json` |
| `idman-focused-evidence.bundle.json` | 13 | `idman-focused-close.json` |

Corresponding provider-neutral snapshots retained. Canonical REA `evidence-export` validations retained as `*-bundle-validation.json` and `*-validated.bundle.json`. Run `node .\verify-analysis.mjs` from analysis directory to validate cited Evidence retention, unchanged original target/toolchain hashes, and close receipts. Latest result: `verification-result.json`.
Final check at `2026-11-06T07:00:38.3374029Z` passed, exit code 0. Targeted process inventory found no remaining `rea-local-session` or analysis-owned `StreamHarbor...rea-ghidra` Java/Node processes; unrelated connected MCP servers left untouched.

Investigation stopped after completed bounded pass; no further broad IDMan analysis launched.
