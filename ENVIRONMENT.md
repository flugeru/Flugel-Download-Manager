# Environment and capability report

Windows x64, OS build 10.0.26300. .NET SDK 10.0.402 / MSBuild 18.9.15; .NET runtime and Windows Desktop runtime 10.0.13 installed. Also SDK 8.0.426. Node, Git and PowerShell available. Python command points Windows Store alias; not relied upon. No 7z command found. Display: 2560×1440, effective DPI 120 (125%), one active monitor.

## Enumerated MCP catalog

Complete discovered catalog: `analysis/mcp-catalog.txt` (schemas) and `analysis/mcp-catalog-summary.json` (names/descriptions), 310 tools, zero undiscovered remainder at enumeration.

| Server | Tools | Useful capabilities / limits |
|---|---:|---|
| rea | 207 | Native PE/Ghidra, procedures/strings/references/CFG/decompiler, safe artifacts/PE resources, JS graph, managed metadata, browser/Electron/runtime and capture interfaces. Advertisement alone not proof backend installed; `binary_session` reports availability. |
| windows-mcp | 4 | Snapshot, Screenshot, DisplayInventory, ControlStatus. No click/type action tool exposed; exact Win32/UI Automation used locally for controlled interactions. |
| context7 | 2 | Public current .NET and browser extension API documentation. No local code, URLs with secrets or binaries submitted. |
| github | 46 | Official release metadata available. No repository publishing or artifact uploads performed. |
| figma | 43 | Original editable synthetic UI mockup created; Starter call cap blocked final draft polish. Not release acceptance evidence. |
| opencode | 8 | Sessions/worktrees/models and MCP resources. |

Local harness tools additionally supply filesystem read/glob/grep/patch, PowerShell shell, subagent sessions and HTTP fetch. No dedicated filesystem/terminal/browser-automation MCP server discovered. REA browser tools require actual supported browser endpoint; not assumed from catalog presence.

## REA identity and initial diagnostics

REA `rea-agents` 6.7.0, server 6.7.0, package under existing npm cache, 207 MCP tools / 153 CLI commands / 6 prompts. Catalog tool SHA-256 `af39310c9591bb981893ec0d2188b1bcf6e9cc8ba3ac513d5bbefc8ddcb85639`; protocol `2025-11-25`. Initial target closed. Full diagnostic `analysis/rea-session-initial.json`.

Initial MCP process had no `GHIDRA_INSTALL_DIR` or `JAVA_HOME`. Its bare `java.exe` launch also failed REA's Windows absolute-path requirement. Initial Ghidra route therefore unavailable, not silently treated as successful. Hopper unsupported on Windows; IDA/Kuna not configured. Safe artifact and managed-static inspections available without native disassembler; macOS-native tools unavailable.

Existing local Ghidra `C:\Users\Flugel\ghidra\ghidra_12.1_PUBLIC`, application.version 12.1, build 2026-May-13, minimum Java 21. Existing Java `C:\Users\Flugel\jdk21\jdk-21.0.12.1+1`, Temurin OpenJDK 21.0.12.1+1 x64. Default PATH instead chooses Java 17. No reinstall required. Process-local REA/Ghidra bridge succeeded: native overview, references, function dossiers, pseudocode, canonical evidence export and explicit session closure. Global MCP/client registration unchanged. Final results: `analysis/ghidra-report.md`. Official Ghidra distribution ZIP unavailable; official release digest not verified, installed Ghidra/Java provenance unknown. Local toolchain and target hashes verified unchanged. Initial unavailable state above remains historical.

## Isolation

Project: `C:\Users\Flugel\Projects\Flugel Download Manager`; original installed directory never edited. Analysis evidence excluded from app builds. Implementation agents receive only `spec/` behavioral contracts. No VM or enforced filesystem separation: see `spec/CLEAN-ROOM.md`. Test servers bind IPv4 loopback; generated artifacts retained locally. Browser registration remains opt-in and was not applied.
