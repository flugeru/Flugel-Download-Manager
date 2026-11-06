// Node built-ins only. Legacy entrypoint kept for compatibility.
// The maintained harness is check-media-bridge.cjs (covers manifest, request schema,
// per-site cookies, MV3 suspension, durable-acceptance handoff, and browser fallback).
"use strict";
const path = require("path");
const { spawnSync } = require("node:child_process");

const harness = path.join(__dirname, "check-media-bridge.cjs");
const args = process.argv[2] ? [harness, path.resolve(process.argv[2])] : [harness];
const run = spawnSync(process.execPath, args, { stdio: "inherit", windowsHide: true });
if (run.error) throw run.error;
process.exitCode = run.status == null ? 1 : run.status;

if (process.argv[2]) {
  // Preserved contract: optional host-process protocol checks are exercised by the
  // maintained harness when a host path is supplied (it feeds real frames and asserts
  // no invalid-request). The host's own --self-test covers EOF/partial/malformed frames.
}