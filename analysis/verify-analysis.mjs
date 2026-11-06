import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { resolve } from 'node:path';

const root = import.meta.dirname;
const read = async name => JSON.parse(await readFile(resolve(root, name), 'utf8'));
const bundle = await read('idmmsghost-evidence.bundle.json');
const records = new Map(bundle.records.map(record => [record.evidence_id, record]));
assert.equal(records.size, 13);
for (const record of (await read('idmmsghost-worker-evidence.bundle.json')).records) records.set(record.evidence_id, record);
for (const record of (await read('idman-focused-evidence.bundle.json')).records) records.set(record.evidence_id, record);
const report = (await Promise.all(['IDM-NATIVE-ANALYSIS.md', 'ghidra-report.md'].map(name => readFile(resolve(root, name), 'utf8')))).join('\n');
for (const id of report.matchAll(/ev_[a-f0-9]{64}/g)) {
  if (!records.has(id[0])) {
    const idman = await read('idman-first-evidence.bundle.json');
    assert(idman.records.some(record => record.evidence_id === id[0]), `Missing report evidence ${id[0]}`);
  }
}
const readPowerShell = async name => {
  const bytes = await readFile(resolve(root, name));
  return JSON.parse(bytes.toString(bytes[0] === 0xff && bytes[1] === 0xfe ? 'utf16le' : 'utf8').replace(/^\uFEFF/, ''));
};
const identities = await readPowerShell('artifact-identities.json');
const toolchain = await readPowerShell('toolchain-file-identities.json');
for (const identity of [...identities, ...toolchain]) {
  const digest = createHash('sha256').update(await readFile(identity.path)).digest('hex');
  assert.equal(digest, identity.sha256.toLowerCase(), `Artifact changed: ${identity.path}`);
}
const closed = await read('host-close.json');
assert.equal(closed.isError, undefined);
assert.equal(closed.structuredContent.result.evidence_records, 13);
assert.equal((await read('host-worker-close.json')).structuredContent.result.evidence_records, 14);
assert.equal((await read('idman-first-close.json')).structuredContent.result.evidence_records, 3);
assert.equal((await read('idman-focused-close.json')).structuredContent.result.evidence_records, 13);
for (const name of ['host-export.json', 'host-worker-export.json', 'idman-first-export.json', 'idman-focused-export.json']) {
  const receipt = await read(name);
  assert.equal(receipt.isError, undefined);
  assert(receipt.structuredContent.result.records > 0);
}
console.log('PASS: cited evidence retained; target/toolchain hashes unchanged; host close receipts saved.');
