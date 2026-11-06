# IDM filename behavior audit (REA/Ghidra artifacts, read-only)

**Scope:** filename-selection behavior relevant to HTTP download naming in the locally installed IDM copy. This is an audit of *behavior/policy evidence*, not an implementation copy. No installer was executed; no IDM config, licensing, credentials, or runtime state were modified; no proprietary code is reproduced.

**Targets (observed in artifacts):**
- `C:\Program Files (x86)\Internet Download Manager\IDMan.exe` — PE x86, SHA-256 `a705c0154fafc7cd509decf165cb6f6c050b6521b40adb6f730dd827c86051c7` (from REA evidence records).
- Local installer present at `C:\Users\Flugel\Downloads\idman643build18.exe` was in scope for the audit request, but the final report was synthesized from the `IDMan.exe`/analysis artifacts produced in `analysis/*filename*` (2026-11-06 ~14:51–15:00 UTC). Where the installer itself was not directly evidenced in these files, that is a limitation.

**Evidence sources (this repository, untrusted target-derived analysis):** `analysis/filename-strings.json`, `filename-fallback-strings.json`, `filename-disposition-xrefs.json`, `filename-disposition-lower-xrefs.json`, `filename-disposition-xrefs.json.pseudocode-0.json`, `filename-search-evidence.bundle.json`, `filename-xref-evidence.bundle.json`. REA/Ghidra metadata in those artifacts reports Ghidra 12.1, PDB `IDMan.pdb` referenced with load state unknown, and defined-string/xref inventories (not proof of absence in raw bytes).

## Observed (from REA artifacts)

### Content-Disposition / filename parsing
- **Observed:** string `Content-Disposition:` at `0x712c14` and `content-disposition` at `0x718c48` (`filename-strings.json`).
- **Observed:** xrefs to `0x712c14` resolve to `0x58d4cd`; xrefs to `0x718c48` resolve to **none** in the direct xref result (`filename-disposition-xrefs.json`, `filename-disposition-lower-xrefs.json`). This is evidence that the capitalized/literal header form had an observed reference path in that inventory; it is **not** proof the lowercase form is never handled elsewhere.
- **Observed (decompilation evidence, summarized, not reproduced):** the procedure analyzed as `0x58c770` (`procedure_pseudo_code` in `filename-disposition-xrefs.json.pseudocode-0.json`) contains header parsing that, in observed branches, recognizes `Content-Disposition` and extracts a `filename` value. The observed logic includes handling for `attachment`/`inline`; quoted and unquoted filename values; case-insensitive `filename` matching; and multiple encoding/value forms including `utf-8''`-style extended values, RFC 2047-like encoded-word prefixes (`=?UTF-8?` / `=?UTF8?B?` in observed checks), and `ISO-8859''`/`ISO-8859-`/`windows-125*` forms with subsequent decoding/normalization in observed paths. Percent-encoded content is handled in at least some observed paths, and the resulting name undergoes normalization/sanitization and length limiting before being applied in the observed flow.
- **Observed (decompilation evidence, summarized):** when a server-supplied filename is considered/applied, the procedure also contains comparison/prompt-related branches (including registry-gated behavior referencing `notaskfn2` in the observed decompilation and UI/message interactions such as `SendMessageA`/MessageBox-style calls in observed paths). This is consistent with IDM confirming or reconciling filename changes in some cases; the exact user-visible policy depends on flags/state not fully resolved here.

### Fallback / title-related strings (filename/title search)
- **Observed:** filename-related UI/log strings exist, e.g. prompts about the server sending a different file name and keeping the server name, local/alternate filename handling, duplicate numbered names, etc. (`filename-fallback-strings.json` sample includes `0x6fe6f8`, `0x6ffd20`, `0x6f7d28`, and others).
- **Observed:** title-related string/API hits include `, Title: ` (`0x6fd034`), `title` (`0x717170`), `Untitled` (`0xa0519a`), and `GetFileTitleA` (`0x7c3a40`). `GetFileTitleA` is a Windows API name; by itself it does **not** establish browser page/document title fallback.
- **Not observed in the scoped artifacts:** no evidence in `filename-fallback-strings.json`/the disposition xref set ties `title`/`, Title: `/`Untitled` to **browser HTML page/document title used as an automatic download filename fallback**. The user's hypothesis (“IDM may use page title when the browser/server name is missing, as a last resort”) is **plausible but not proven** by this audit. These strings may relate to UI, media metadata, subtitles, or other components; that remains **inferred/unknown** from the available evidence.

## Comparison to Flugel (implemented policy, 2.2.2/2.2.3)

| Area | IDM (observed/inferred) | Flugel (implemented) | Status |
|---|---|---|---|
| Server filename from `Content-Disposition` | Observed: sophisticated parsing/encoding handling in `0x58c770` region; exact RFC completeness not fully proven. | Browser/extension parses observed headers and filename candidates; desktop can resolve server filename when name is empty (`ResolveServerFileName` default for empty Add path); UTF-8 collisions/explicit-name preservation covered by engine checks. | **Directionally aligned; not a byte-for-byte policy match.** |
| Browser-determined filename | IDM integrates with browser context via its own mechanisms (not audited here). | Extension captures at `downloads.onDeterminingFilename` and passes the browser-determined name; never overrides `suggest()`. | **Flugel uses browser as primary source in interception path.** |
| Page/document title fallback | **Not observed** as an automatic download filename fallback in scoped artifacts; hypothesis unproven. | Extension uses tab title only as a guarded fallback when a useful server/browser/URL name is unavailable, with sanitization and MIME-known extension; treated as suggestion, not proof. | **Flugel implements the hypothesis conservatively; IDM equivalence unproven.** |
| Overwriting existing files | Observed UI/prompt/rename behavior in filename-related strings/branches (details not fully mapped). | Content-Disposition/automatic resolved names get unique collision handling (`EnsureUniqueFileName`); explicit user names preserved. | **Safety goal aligned in effect; mechanism differs.** |
| POST/body replay | Not audited. | Not supported; stays in browser. | **Known Flugel limit.** |

## Limitations / required follow-up
- REA artifacts report Ghidra-defined string/xref inventories and PDB load-state uncertainty; **absence of an observed string/xref is not proof of absence** in the binary/runtime behavior.
- The decompilation is target-derived and was treated as untrusted data; this report summarizes observed patterns and cites artifacts/addresses without reproducing code.
- The final subagent-authored `filename-behavior-audit.md` was not present when this synthesis was written; this file consolidates the raw `analysis/*filename*` artifacts into a factual audit. If the delegated audit (`ses_e4ea95254ffeqB09MUGB85m053`) later delivers a more complete report, reconcile by preferring directly observed evidence and marking conflicts explicitly.
- To strengthen the title-fallback question: trace xrefs/callers for `0x717170` (`title`), `0x6fd034` (`, Title: `), and the UI/message paths around `0x58c770` under a read-only session, and determine whether any path consumes browser page/document title for the saved filename (not media/UI title). No new REA session was opened in this synthesis step.
