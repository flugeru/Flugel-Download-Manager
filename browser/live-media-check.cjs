// Real browser extension + registered native host + WPF UIA; local HTTP bytes only.
// Run: node browser/live-media-check.cjs <browser.exe>
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const { spawn, spawnSync } = require('node:child_process');
const { createHash } = require('node:crypto');
const { setTimeout: delay } = require('node:timers/promises');
const root = path.resolve(__dirname, '..');
const output = fs.mkdtempSync(path.join(process.env.LOCALAPPDATA, 'Temp/opencode/media-live-'));
const downloads = path.join(output, 'downloads');
fs.mkdirSync(downloads);
// This verifies exact MP4 URL transport, not video codec playback.
const payload = Buffer.alloc(256 * 1024, 0x5a);
const server = http.createServer((req, res) => {
  if (req.url === '/588340.mp4') {
    res.writeHead(200, { 'Content-Type': 'video/mp4', 'Content-Length': payload.length });
    return res.end(req.method === 'HEAD' ? undefined : payload);
  }
  res.writeHead(200, { 'Content-Type': 'text/html' });
  res.end('<!doctype html><title>Controlled media bridge check</title><video width="640" height="360" controls src="/588340.mp4"></video>');
});
let browser, socket;
let seq = 0;
const pending = new Map();
function cdp(method, params = {}) {
  return new Promise((resolve, reject) => {
    const id = ++seq;
    const timeout = setTimeout(() => { pending.delete(id); reject(new Error('CDP timeout: ' + method)); }, 15000);
    pending.set(id, { resolve, reject, timeout });
    socket.send(JSON.stringify({ id, method, params }));
  });
}
(async () => {
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const url = `http://127.0.0.1:${server.address().port}/`;
  browser = spawn(process.argv[2], [`--user-data-dir=${path.join(output, 'profile')}`, '--no-first-run',
    '--no-default-browser-check', '--remote-debugging-port=0', '--disable-features=ChromeWhatsNewUI',
    `--disable-extensions-except=${path.join(root, 'artifacts/browser/extension')}`,
    `--load-extension=${path.join(root, 'artifacts/browser/extension')}`, url], { stdio: 'ignore' });
  let port;
  for (let i = 0; i < 150; i++) {
    try { port = Number(fs.readFileSync(path.join(output, 'profile/DevToolsActivePort'), 'utf8').split('\n')[0]); break; }
    catch { await delay(100); }
  }
  assert.ok(port, 'Browser remote debugging did not start');
  const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
  const target = targets.find(t => t.type === 'page' && t.url.startsWith(url));
  assert.ok(target, 'Local page target missing');
  socket = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
  socket.onmessage = e => {
    const r = JSON.parse(e.data), p = pending.get(r.id);
    if (!p) return;
    clearTimeout(p.timeout); pending.delete(r.id);
    if (r.error) p.reject(new Error(r.error.message)); else p.resolve(r.result);
  };
  async function evaluate(expression) {
    const result = await cdp('Runtime.evaluate', { expression, returnByValue: true });
    assert.ok(!result.exceptionDetails, JSON.stringify(result.exceptionDetails));
    return result.result.value;
  }
  for (let i = 0; i < 100; i++) {
    if (await evaluate('!!document.querySelector("[data-flugel-download-manager=balloon]")')) break;
    await delay(100);
  }
  assert.equal(await evaluate('!!document.querySelector("[data-flugel-download-manager=balloon]")'), true, 'Real extension balloon missing');
  await evaluate('document.querySelector("[data-flugel-download-manager=balloon]").shadowRoot.querySelector(".btn").click()');
  await delay(500);
  const label = await evaluate('document.querySelector("[data-flugel-download-manager=balloon]").shadowRoot.querySelector(".item").innerText');
  assert.match(label, /588340.mp4/);
  const shot = await cdp('Page.captureScreenshot', { format: 'png' });
  fs.writeFileSync(path.join(output, 'balloon-menu.png'), Buffer.from(shot.data, 'base64'));
  await evaluate('document.querySelector("[data-flugel-download-manager=balloon]").shadowRoot.querySelector(".item").click()');
  await delay(1000);
  console.log('Capture notice:', await evaluate('document.body.innerText.slice(-1200)'));
  const ui = spawnSync('powershell.exe', ['-NoProfile', '-File', path.join(__dirname, 'Check-MediaDialog.ps1'), '-Destination', downloads], { timeout: 25000, encoding: 'utf8' });
  assert.equal(ui.status, 0, ui.stderr || ui.stdout);
  const file = path.join(downloads, '588340.mp4');
  for (let i = 0; i < 150 && !fs.existsSync(file); i++) await delay(100);
  assert.ok(fs.existsSync(file), 'Confirmed media did not finish');
  const hash = bytes => createHash('sha256').update(bytes).digest('hex');
  assert.equal(hash(fs.readFileSync(file)), hash(payload));
  const report = { passed: true, capturedAtUtc: new Date().toISOString(), browser: process.argv[2], label,
    scope: 'Real unpacked extension balloon click, registered native host, WPF confirmation, Start now, local HTTP MP4 URL exact bytes. Codec playback and external website not tested.',
    bytes: payload.length, sha256: hash(payload) };
  fs.writeFileSync(path.join(output, 'evidence.json'), JSON.stringify(report, null, 2));
  console.log(JSON.stringify({ ...report, evidence: output }, null, 2));
})().catch(error => { console.error(error); console.error('Evidence directory:', output); process.exitCode = 1; })
.finally(async () => {
  if (socket?.readyState === WebSocket.OPEN) {
    try { await cdp('Browser.close'); } catch { }
    socket.close();
  } else if (browser) browser.kill();
  server.closeAllConnections(); server.close();
});
