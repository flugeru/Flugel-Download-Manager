// Run: node browser/check-media-bridge.cjs [published Flugel.NativeHost.exe]
// Exercises the real service-worker source with mocked chrome.* APIs (no browser).
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { spawnSync } = require('node:child_process');

const requests = [];
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
  webRequest: { onBeforeRequest: event, onHeadersReceived: event },
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
      callback({ version: 1, status: 'accepted-for-confirmation' });
      chrome.runtime.lastError = undefined;
    }
  }
};
const context = vm.createContext({ chrome, TextEncoder, URL, decodeURIComponent, Number, Object, Map, Array, String });
vm.runInContext(fs.readFileSync(path.join(__dirname, 'extension/service-worker.js'), 'utf8'), context);

const nuvem = 'https://nuvem.anitsu.moe/api/download?path=Animes%2FLetra%20Y%2FYoujo%20Senki%2F%5B2026%5D%20Youjo%20Senki%20II%2F%5BAnitsu%5D%20Youjo%20Senki%20S2%20-%20%5BWEB%20DUAL%201080p%20HEVC%5D%2F%5BAnitsu%5D%20Youjo%20Senki%20S2%20-%2008%20%5BWEB%20DUAL%201080p%20HEVC%5D.mkv';

(async () => {
  // Name detection, including the query-parameter path that failed before.
  assert.equal(context.mediaNameFromUrl(nuvem), '[Anitsu] Youjo Senki S2 - 08 [WEB DUAL 1080p HEVC].mkv');
  assert.equal(context.mediaNameFromUrl('https://cdn.example.test/588340.mp4?token=x'), '588340.mp4');
  assert.equal(context.mediaNameFromUrl('https://example.test/watch?id=7'), null);
  assert.equal(context.mediaNameFromUrl('https://example.test/page.html'), null);
  assert.equal(context.typedName('https://example.test/api/download', 'video/x-matroska'), 'download.mkv');
  context.recordMedia(7, { url: nuvem, fileName: 'episode.mkv' });
  const message = (msg, sender = {}) => new Promise(resolve => listener(msg, sender, resolve));
  assert.equal((await message({ type: 'get-page-media' })).media.length, 1);
  vm.runInContext('tabMedia.clear()', context);
  assert.equal((await message({ type: 'get-page-media' })).media.length, 1, 'worker suspension lost detected media');
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
  console.log('PASS media naming (path parameter), cookie permission per site, strict link schema, native error propagation, unsafe URL rejection');
})().catch(error => { console.error(error); process.exitCode = 1; });
