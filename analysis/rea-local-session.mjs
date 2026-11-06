// Local analysis transport only. Never launches target executable.
import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import { readFile, writeFile, appendFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import assert from 'node:assert/strict';

const root = import.meta.dirname;
const cli = 'C:/Users/Flugel/AppData/Local/npm-cache/_npx/a1722018f28f9df2/node_modules/rea-agents/scripts/rea.mjs';
const child = spawn(process.execPath, [cli, '--mcp'], {
  cwd: root,
  env: { ...process.env, GHIDRA_INSTALL_DIR: 'C:/Users/Flugel/ghidra/ghidra_12.1_PUBLIC', JAVA_HOME: 'C:/Users/Flugel/jdk21/jdk-21.0.12.1+1', TEMP: root, TMP: root },
  stdio: ['pipe', 'pipe', 'pipe'],
});
let next = 0;
const pending = new Map();
child.stderr.on('data', chunk => appendFile(resolve(root, 'rea-local-session.stderr.txt'), chunk));
createInterface({ input: child.stdout }).on('line', line => {
  let message;
  try { message = JSON.parse(line); } catch { return; }
  const task = pending.get(message.id);
  if (!task) return;
  pending.delete(message.id);
  message.error ? task.reject(new Error(JSON.stringify(message.error))) : task.resolve(message.result);
});
child.on('exit', code => { for (const task of pending.values()) task.reject(new Error(`REA exited ${code}`)); });
function request(method, params) {
  const id = ++next;
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve, reject });
    child.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, params }) + '\n');
  });
}
try {
  await request('initialize', { protocolVersion: '2024-11-05', capabilities: {}, clientInfo: { name: 'local-analysis-only', version: '1' } });
  child.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');
  const catalog = await request('tools/list', {});
  await writeFile(resolve(root, 'rea-local-tools.json'), JSON.stringify(catalog, null, 2));
  const jobs = process.argv[2] ? JSON.parse(await readFile(resolve(root, process.argv[2]), 'utf8')) : [];
  for (const job of jobs) {
    assert(catalog.tools.some(tool => tool.name === job.name), `Unknown tool: ${job.name}`);
    assert.equal(resolve(root, job.output).startsWith(root + '\\'), true, 'Output must stay in analysis directory');
    const result = await request('tools/call', { name: job.name, arguments: job.arguments });
    await writeFile(resolve(root, job.output), JSON.stringify(result, null, 2));
    console.log(job.output, result.isError ? 'ERROR' : 'OK');
    if (job.follow_first_reference && !result.isError) {
      const references = result.structuredContent.normalized_result;
      for (const [index, address] of references.slice(0, job.follow_first_reference).entries()) {
        const owner = await request('tools/call', { name: 'resolve_containing_procedure', arguments: { address } });
        await writeFile(resolve(root, `${job.output}.owner-${index}.json`), JSON.stringify(owner, null, 2));
        const procedure = owner.structuredContent?.normalized_result?.procedure?.address;
        if (!procedure) continue;
        const code = await request('tools/call', { name: 'procedure_pseudo_code', arguments: { procedure } });
        await writeFile(resolve(root, `${job.output}.pseudocode-${index}.json`), JSON.stringify(code, null, 2));
      }
    }
  }
} finally {
  child.stdin.end();
}
