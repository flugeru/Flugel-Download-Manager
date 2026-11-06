// Run: node browser/check-media-bridge.cjs [published Flugel.NativeHost.exe]
// Exercises the real service-worker source with mocked chrome.* APIs (no browser).
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { spawnSync } = require('node:child_process');

const requests = [];
const downloadCalls = [];
let pauseFails = false;
let cancelFails = false;
let nativeStatus = 'accepted-for-confirmation';
let listener;
let nativeError;
let storedFlags = {};
let cookieJar = [];
const session = {};
const activeTab = { id: 7, url: 'https://nuvem.anitsu.moe/', title: 'Test' };
const event = { addListener() {} };
const chrome = {
  action: { setBadgeText() {}, setBadgeBackgroundColor() {}, setTitle() {} },
  contextMenus: { onClicked: event },
  tabs: { onRemoved: event, onUpdated: event, async query() { return [activeTab]; } },
webRequest: { onBeforeRequest: event, onHeadersReceived: event, onBeforeSendHeaders: event },
  downloads: {
    onCreated: event,
    async pause(id) { downloadCalls.push(['pause', id]); if (pauseFails) throw Error('already complete'); },
    async cancel(id) { downloadCalls.push(['cancel', id]); if (cancelFails) throw Error('cannot cancel'); },
    async resume(id) { downloadCalls.push(['resume', id]); }
  },
  storage: {
    local: { async get(keys) { const out = {}; for (const k of keys) if (k in storedFlags) out[k] = storedFlags[k]; return out; } },
    session: { async get(key) { return { [key]: session[key] }; }, async set(values) { Object.assign(session, values); }, async remove(key) { delete session[key]; } }
  },
  cookies: { async getAll({ url }) { requests.push({ cookieLookup: url }); return cookieJar; } },
  runtime: {
    onInstalled: event,
    onMessage: { addListener(fn) { listener = fn; } },
    sendNativeMessage(host, request, callback) {
      assert.equal(host, 'com.flugel.downloadmanager');
      requests.push(request);
      chrome.runtime.lastError = nativeError ? { message: nativeError } : undefined;
      callback({ version: 1, status: nativeStatus });
      chrome.runtime.lastError = undefined;
    }
  }
};
const context = vm.createContext({ chrome, TextEncoder, URL, decodeURIComponent, Number, Object, Map, Array, String, Date });
vm.runInContext(fs.readFileSync(path.join(__dirname, 'extension/service-worker.js'), 'utf8'), context);

const nuvem = 'https://nuvem.anitsu.moe/api/download?path=Animes%2FLetra%20Y%2FYoujo%20Senki%2F%5B2026%5D%20Youjo%20Senki%20II%2F%5BAnitsu%5D%20Youjo%20Senki%20S2%20-%20%5BWEB%20DUAL%201080p%20HEVC%5D%2F%5BAnitsu%5D%20Youjo%20Senki%20S2%20-%2008%20%5BWEB%20DUAL%201080p%20HEVC%5D.mkv';

(async () => {
  // Name detection, including the query-parameter path that failed before.
  assert.equal(context.mediaNameFromUrl(nuvem), '[Anitsu] Youjo Senki S2 - 08 [WEB DUAL 1080p HEVC].mkv');
  assert.equal(context.mediaNameFromUrl('https://cdn.example.test/588340.mp4?token=x'), '588340.mp4');
  assert.equal(context.mediaNameFromUrl('https://example.test/watch?id=7'), null);
  assert.equal(context.mediaNameFromUrl('https://example.test/page.html'), null);
  assert.equal(context.typedName('https://example.test/api/download', 'video/x-matroska'), 'download.mkv');
await context.recordMedia(7, { url: nuvem, fileName: 'episode.mkv' });
  const message = (msg, sender = {}) => new Promise(resolve => listener(msg, sender, resolve));
  assert.equal((await message({ type: 'get-page-media' })).media.length, 1);
vm.runInContext('tabMedia.clear(); mediaReady.clear()', context);
assert.equal((await message({ type: 'get-page-media' })).media.length, 1, 'worker suspension lost detected media');
await context.recordMedia(7, { url: 'https://example.test/new.mp4', fileName: 'new.mp4' });
assert.equal(session['media:7'].length, 2, 'first write after suspension discarded existing metadata');
assert.equal(session['media:7'].some(m => m.url === nuvem), true);
  assert.equal((await message({ type: 'get-page-media' }, { tab: { id: 8 } })).media.length, 0);
  assert.equal((await message({ type: 'enqueue-detected-media', url: 'https://unknown.test/' })).error, 'media-not-found');

  const send = payload => new Promise(resolve => listener({ type: 'enqueue-media', payload }, {}, resolve));

  // Cookies are sent only for an explicitly enabled site, and only as the browser would attach them.
  cookieJar = [{ name: 'session', value: 'abc' }, { name: 'lang', value: 'pt' }];
  storedFlags = {};
  requests.length = 0;
  assert.equal((await send({ url: nuvem, pageUrl: 'https://anitsu.moe/anime/x', fileName: 'x.mkv' })).ok, true);
  const noSite = requests.find(r => r.action);
  assert.equal(Object.hasOwn(noSite, 'cookie'), false, 'cookie leaked without site permission');

  storedFlags = { 'cookies:anitsu.moe': true };
  requests.length = 0;
  assert.equal((await send({ url: nuvem, pageUrl: 'https://anitsu.moe/anime/x', fileName: 'x.mkv', mediaQuality: '1080p' })).ok, true);
  const withSite = requests.find(r => r.action);
  assert.equal(withSite.action, 'enqueue-media');
  assert.equal(withSite.cookie, 'session=abc; lang=pt');
  assert.equal(requests.find(r => r.cookieLookup)?.cookieLookup, nuvem);
  requests.length = 0;
  assert.equal((await message({ type: 'enqueue-detected-media', url: nuvem })).ok, true);
  assert.equal(requests.find(r => r.action).fileName, 'episode.mkv');
  assert.equal(requests.find(r => r.action).cookie, 'session=abc; lang=pt');
  assert.equal((await message({ type: 'enqueue-detected-media', url: nuvem }, { tab: activeTab })).error, 'unknown-message');

  // Parent-domain permission covers the file host; unrelated sites never receive cookies.
  requests.length = 0;
  await send({ url: 'https://other.example.test/video.mp4', pageUrl: 'https://other.example.test/' });
  assert.equal(requests.find(r => r.action)?.cookie, undefined);

  // Plain links keep the strict three-field contract.
  storedFlags = {};
  requests.length = 0;
  assert.equal((await send({ url: 'https://example.test/file.zip' })).ok, true);
  assert.deepEqual(JSON.parse(JSON.stringify(requests.find(r => r.action))), { version: 1, action: 'enqueue', url: 'https://example.test/file.zip' });

  // Native error reasons survive the bridge.
  nativeError = 'Specified native messaging host not found.';
  assert.equal((await send({ url: 'https://example.test/video.mp4' })).error, 'host-not-found');
  nativeError = 'Access to the specified native messaging host is forbidden.';
  assert.equal((await send({ url: 'https://example.test/video.mp4' })).error, 'host-forbidden');
  assert.equal((await send({ url: 'file:///C:/secret' })).error, 'invalid-url');

  // Automatic takeover cancels only after acceptance and restores on host failure.
  const item = { id: 99, url: nuvem, referrer: activeTab.url, state: 'in_progress', filename: 'C:\\Downloads\\episode.mkv' };
  nativeError = undefined;
storedFlags = { autoIntercept: true, 'cookies:anitsu.moe': true };
context.observeRequest({ url: nuvem, method: 'GET' });
  requests.length = 0;
  downloadCalls.length = 0;
  await context.interceptDownload(item);
  assert.deepEqual(downloadCalls, [['pause', 99], ['cancel', 99]]);
  assert.equal(requests.find(r => r.action).url, nuvem);
assert.equal(requests.find(r => r.action).cookie, 'session=abc; lang=pt');
// Final redirect URL and browser filename win; User-Agent comes only from observed headers.
const finalUrl = 'https://cdn.example.test/download';
context.observeRequest({ url: finalUrl, method: 'GET', requestHeaders: [{ name: 'User-Agent', value: 'BrowserTest/1.0' }] });
requests.length = 0;
await context.interceptDownload({ ...item, finalUrl, filename: 'C:\\Downloads\\server-name.zip' });
assert.equal(requests.find(r => r.action).url, finalUrl);
assert.equal(requests.find(r => r.action).fileName, 'server-name.zip');
assert.equal(requests.find(r => r.action).userAgent, 'BrowserTest/1.0');
// POST is not silently replayed as GET. Do not pause/cancel its original browser copy.
context.observeRequest({ url: finalUrl, method: 'POST' });
downloadCalls.length = 0;
requests.length = 0;
await context.interceptDownload({ ...item, finalUrl });
assert.equal(downloadCalls.length, 0);
assert.equal(requests.length, 0);
context.observeRequest({ url: finalUrl, method: 'GET' });
await context.interceptDownload({ ...item, finalUrl });
assert.equal(downloadCalls.length, 0, 'mixed methods at one URL must not replay as GET');
assert.equal((await send({ url: finalUrl })).error, 'unsupported-request-method');
vm.runInContext('requestContext.clear()', context);
downloadCalls.length = 0;
await context.interceptDownload(item);
assert.equal(downloadCalls.length, 0, 'unknown method after worker restart must remain in browser');
context.observeRequest({ url: nuvem, method: 'GET' });
nativeError = 'Specified native messaging host not found.';
  downloadCalls.length = 0;
  await context.interceptDownload(item);
  assert.deepEqual(downloadCalls, [['pause', 99], ['resume', 99]]);
  nativeError = undefined;
  nativeStatus = 'desktop-busy';
  downloadCalls.length = 0;
  await context.interceptDownload(item);
  assert.deepEqual(downloadCalls, [['pause', 99], ['resume', 99]]);
  nativeStatus = 'accepted-for-confirmation';
  cancelFails = true;
  downloadCalls.length = 0;
  await context.interceptDownload(item);
  assert.deepEqual(downloadCalls, [['pause', 99], ['cancel', 99], ['resume', 99]]);
  cancelFails = false;
  pauseFails = true;
  requests.length = 0;
  downloadCalls.length = 0;
  await context.interceptDownload(item);
  assert.deepEqual(downloadCalls, [['pause', 99]]);
  assert.equal(requests.length, 0);
  pauseFails = false;
  for (const flags of [{}, { autoIntercept: false }, { autoIntercept: true, globalEnabled: false }, { autoIntercept: true, 'site:nuvem.anitsu.moe': false }]) {
    storedFlags = flags;
    downloadCalls.length = 0;
    await context.interceptDownload(item);
    assert.equal(downloadCalls.length, 0);
  }
  storedFlags = { autoIntercept: true };
  for (const extra of [{ url: 'blob:https://example.test/x' }, { byExtensionId: 'other' }, { incognito: true }, { state: 'complete' }]) {
    await context.interceptDownload({ ...item, ...extra });
    assert.equal(downloadCalls.length, 0);
  }

  if (process.argv[2]) {
    for (const request of requests.filter(r => r.action).slice(0, 2)) {
      const body = Buffer.from(JSON.stringify(request));
      const header = Buffer.alloc(4);
      header.writeUInt32LE(body.length);
      const run = spawnSync(process.argv[2], [], { input: Buffer.concat([header, body]), timeout: 5000 });
      assert.ifError(run.error);
      const reply = JSON.parse(run.stdout.subarray(4, 4 + run.stdout.readUInt32LE(0)));
      assert.notEqual(reply.error, 'invalid-request');
    }
  }
console.log('PASS media naming, per-site cookies, popup submission, MV3 suspension, strict schema, automatic takeover and browser fallback');
})().catch(error => { console.error(error); process.exitCode = 1; });
