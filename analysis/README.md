# Local IDM analysis

- `ghidra-report.md` — final bounded report: engine validation, provenance limits, principal Evidence IDs, exported/closed session status.
- `IDM-NATIVE-ANALYSIS.md` — findings, exact Evidence IDs, observed/inferred/unknown boundaries.
- `IMPLEMENTATION-HANDOFF.md` — abstract behavioral contracts; read this instead of raw decompilation in implementation session.
- `artifact-identities.json` — original executable SHA-256, size, file version, Authenticode status.
- `toolchain-provenance.json`, `toolchain-file-identities.json` — installed runtime versions/hashes and explicit unknown official provenance.
- `idmmsghost-evidence.bundle.json` — first complete host export (13 records).
- `idmmsghost-worker-evidence.bundle.json` — supplemental host export (14 records).
- `idman-first-evidence.bundle.json` — IDMan overview/string search (3 records).
- `idman-focused-evidence.bundle.json` — focused IDMan references/procedures/pseudocode (13 records; complete transport-limited inventory retained).
- `*-validated.bundle.json`, `*-bundle-validation.json` — canonical REA bundle validations.
- `*-close.json`, `*.snapshot.json` — explicit close receipts/provider-neutral retained analysis.
- `rea-local-session.mjs`, `rea-*-jobs.json` — analysis-only local REA transport and bounded queries. Process-local Ghidra/Java paths; no target execution.
- `verify-analysis.mjs` — runnable local evidence/hash/close-receipt check.
- `verification-result.json` — latest saved check result.

Raw native pseudocode and retained artifacts are local research evidence, not application source. No app/spec changes permitted from this directory. Timeout debris retained rather than broadly deleted; empty timed-out files are not evidence.
