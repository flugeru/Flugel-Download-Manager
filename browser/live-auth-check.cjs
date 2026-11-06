// Real Chromium-family browser + unpacked extension + registered native host + WPF confirmation.
// Reproduces the logged-in download case: the file answers 401 unless the browser session cookie is sent.
// Run: node browser/live-auth-check.cjs <browser.exe>   (host must be registered for the extension ID)
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const { spawn, spawnSync } = require('node:child_process');
const { createHash } = require('node:crypto');
const { setTimeout: delay } = require('node:timers/promises');

const root = path.resolve(__dirname, '..');
const extension = path.join(root, 'artifacts/browser/extension');
const output = fs.mkdtempSync(path.join(process.env.LOCALAPPDATA, 'Temp/opencode/auth-live-'));
const downloads = path.join(output, 'downloads');
fs.mkdirSync(downloads);

const apiPath = 'Animes/Test Series/Sample Episode - 01 [WEB 1080p].mkv';
const apiUrlPath = '/api/download?path=' + encodeURIComponent(apiPath);
const payload = Buffer.alloc(256 * 1024, 0x6b);
const expectedName = 'Sample Episode - 01 [WEB 1080p].mkv';
const seen = [];

const server = http.createServer((req, res) => {
  const url = new URL(req.url, 'http://127.0.0.1');
  if (url.pathname === '/api/download') {
    const cookie = req.headers.cookie || '';
    const authorized = /(^|;\s*)session=abc(;|$)/.test(cookie);
    seen.push({ path: url.searchParams.get('path'), authorized });
    if (!authorized) {
      res.writeHead(401, { 'Content-Type': 'application/json' });
      return res.end('{"error":"unauthorized"}');
    }
    res.writeHead(200, { 'Content-Type': 'video/x-matroska', 'Content-Length': payload.length });
    return res.end(req.method === 'HEAD' ? undefined : payload);
  }
  res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
  res.end(`<!doctype html><title>Logged-in media check</title><video width="640" height="360" controls src="${apiUrlPath}"></video>`);
});

let browser, pageSocket, workerSocket;
const sockets = [];
function makeCdp(ws) {
  let seq = 0;
  const pending = new Map();
  ws.onmessage = e => {
    const r = JSON.parse(e.data), p = pending.get(r.id);
    if (!p) return;
    clearTimeout(p.timeout); pending.delete(r.id);
    if (r.error) p.reject(new Error(r.error.message)); else p.resolve(r.result);
  };
  return (method, params = {}) => new Promise((resolve, reject) => {
    const id = ++seq;
    const timeout = setTimeout(() => { pending.delete(id); reject(new Error('CDP timeout: ' + method)); }, 15000);
    pending.set(id, { resolve, reject, timeout });
    ws.send(JSON.stringify({ id, method, params }));
  });
}

(async () => {
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const base = `http://127.0.0.1:${server.address().port}`;
  const profile = path.join(output, 'profile');
  browser = spawn(process.argv[2], [`--user-data-dir=${profile}`, '--no-first-run', '--no-default-browser-check',
    '--remote-debugging-port=0', `--disable-extensions-except=${extension}`, `--load-extension=${extension}`,
    base + '/'], { stdio: 'ignore' });

  let port;
  for (let i = 0; i < 150 && !port; i++) {
    try { port = Number(fs.readFileSync(path.join(profile, 'DevToolsActivePort'), 'utf8').split('\n')[0]); }
    catch { await delay(100); }
  }
  assert.ok(port, 'Browser remote debugging did not start');
  const list = async () => (await fetch(`http://127.0.0.1:${port}/json/list`)).json();

  // Wait for the page and the extension service worker.
  let pageTarget, workerTarget;
  for (let i = 0; i < 150 && !(pageTarget && workerTarget); i++) {
    const targets = await list();
    pageTarget = targets.find(t => t.type === 'page' && t.url.startsWith(base));
    workerTarget = targets.find(t => t.type === 'service_worker' && t.url.startsWith('chrome-extension://'));
    if (!(pageTarget && workerTarget)) await delay(200);
  }
  assert.ok(pageTarget && workerTarget, 'Page or extension service worker not found');

  const openSocket = url => new Promise((resolve, reject) => {
    const ws = new WebSocket(url);
    sockets.push(ws);
    ws.onopen = () => resolve(ws);
    ws.onerror = reject;
  });
  pageSocket = await openSocket(pageTarget.webSocketDebuggerUrl);
  workerSocket = await openSocket(workerTarget.webSocketDebuggerUrl);
  const pageCdp = makeCdp(pageSocket), workerCdp = makeCdp(workerSocket);
  const evaluate = async (cdp, expression) => {
    const result = await cdp('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
    assert.ok(!result.exceptionDetails, JSON.stringify(result.exceptionDetails));
    return result.result.value;
  };

  // The browser session: a real cookie for this origin, as a logged-in user would have.
  await pageCdp('Network.enable');
  await pageCdp('Network.setCookie', { name: 'session', value: 'abc', url: base + '/' });

  // The user's explicit per-site permission, set through the extension's own storage.
  const negative = process.env.FDM_NO_SITE_PERMISSION === '1';
  if (!negative) await evaluate(workerCdp, `chrome.storage.local.set({ "cookies:127.0.0.1": true })`);

  await pageCdp('Page.reload');
  let balloon = false;
  for (let i = 0; i < 120 && !balloon; i++) {
    balloon = await evaluate(pageCdp, '!!document.querySelector("[data-flugel-download-manager=balloon]")');
    if (!balloon) await delay(150);
  }
  if (!balloon) {
    const diag = await evaluate(pageCdp, `JSON.stringify({
      videos: document.querySelectorAll("video").length,
      rect: (() => { const v = document.querySelector("video"); if (!v) return null; const r = v.getBoundingClientRect(); return [r.width, r.height]; })(),
      src: document.querySelector("video")?.currentSrc || null,
      bodyStart: document.body.innerHTML.slice(0, 200)
    })`);
    assert.fail('Balloon did not appear on the logged-in video page: ' + diag);
  }
  await evaluate(pageCdp, 'document.querySelector("[data-flugel-download-manager=balloon]").shadowRoot.querySelector(".btn").click()');
  await delay(400);
  const label = await evaluate(pageCdp, 'document.querySelector("[data-flugel-download-manager=balloon]").shadowRoot.querySelector(".item").innerText');
  assert.match(label, /Sample Episode - 01/);
  await evaluate(pageCdp, 'document.querySelector("[data-flugel-download-manager=balloon]").shadowRoot.querySelector(".item").click()');

  const ui = spawnSync('powershell.exe', ['-NoProfile', '-File', path.join(__dirname, 'Check-MediaDialog.ps1'),
    '-Destination', downloads, '-ExpectedUrlPattern', '^http://127\\.0\\.0\\.1:\\d+/api/download\\?path='],
    { timeout: 30000, encoding: 'utf8' });
  assert.equal(ui.status, 0, ui.stderr || ui.stdout);

  const file = path.join(downloads, expectedName);
  for (let i = 0; i < 200 && !fs.existsSync(file); i++) await delay(100);
  if (negative) {
    // Without the site permission the desktop request must be refused (401) and nothing may be written.
    assert.ok(!fs.existsSync(file), 'File appeared without site permission');
    assert.ok(seen.some(s => !s.authorized), 'Desktop request without permission was not refused');
    const negativeReport = { passed: true, mode: 'negative: no site permission', serverRequests: seen, fileWritten: false, evidence: output };
    fs.writeFileSync(path.join(output, 'evidence.json'), JSON.stringify(negativeReport, null, 2));
    console.log(JSON.stringify(negativeReport, null, 2));
    return;
  }
  assert.ok(fs.existsSync(file), 'Logged-in media did not finish downloading');
  const hash = bytes => createHash('sha256').update(bytes).digest('hex');
  assert.equal(hash(fs.readFileSync(file)), hash(payload));
  assert.ok(seen.some(s => s.authorized), 'Server never received the browser session cookie');

  const report = {
    passed: true, capturedAtUtc: new Date().toISOString(), browser: process.argv[2], label,
    scope: 'Real unpacked extension, browser session cookie for a local origin with explicit per-site permission, registered native host, WPF confirmation, exact bytes. External site and codec playback not tested.',
    serverRequests: seen, fileName: expectedName, bytes: payload.length, sha256: hash(payload), evidence: output
  };
  fs.writeFileSync(path.join(output, 'evidence.json'), JSON.stringify(report, null, 2));
  console.log(JSON.stringify(report, null, 2));
})().catch(error => { console.error(error); console.error('Evidence directory:', output); process.exitCode = 1; })
  .finally(async () => {
    try { if (workerSocket?.readyState === WebSocket.OPEN) workerSocket.send(JSON.stringify({ id: 999999, method: 'Browser.close' })); } catch {}
    for (const ws of sockets) try { ws.close(); } catch {}
    if (browser && browser.exitCode === null) browser.kill();
    server.closeAllConnections(); server.close();
  });
