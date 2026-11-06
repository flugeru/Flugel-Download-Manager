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
let nativeReplies = []; // FIFO of statuses consumed one per sendNative call; empty falls back to nativeStatus.
let listener;
let nativeError;
let storedFlags = {};
let cookieJar = [];
let uuidSeq = 0;
let alarmHandler;
const alarmsCreated = [];
const session = {};
const activeTab = { id: 7, url: 'https://nuvem.anitsu.moe/', title: 'Test' };
const event = { addListener() {} };
let determiningFilename;
const chrome = {
  action: { setBadgeText() {}, setBadgeBackgroundColor() {}, setTitle() {} },
  contextMenus: { onClicked: event },
tabs: { onRemoved: event, onUpdated: event, async query() { return [activeTab]; }, async get() { return activeTab; } },
webRequest: { onBeforeRequest: event, onHeadersReceived: event, onBeforeSendHeaders: event },
  alarms: { create: (name, info) => alarmsCreated.push({ name, info }), onAlarm: { addListener(fn) { alarmHandler = fn; } } },
  downloads: {
onCreated: { addListener() { assert.fail('Do not capture before the browser determines the filename'); } },
onDeterminingFilename: { addListener(fn) { determiningFilename = fn; } },
    async pause(id) { downloadCalls.push(['pause', id]); if (pauseFails) throw Error('already complete'); },
    async cancel(id) { downloadCalls.push(['cancel', id]); if (cancelFails) throw Error('cannot cancel'); },
    async resume(id) { downloadCalls.push(['resume', id]); }
  },
  storage: {
    local: { async get(keys) { const out = {}; for (const k of keys) if (k in storedFlags) out[k] = storedFlags[k]; return out; } },
    session: {
      async get(key) {
        if (key === null || key === undefined) return { ...session };
        const keys = Array.isArray(key) ? key : [key];
        const out = {}; for (const k of keys) if (k in session) out[k] = session[k]; return out;
      },
      async set(values) { Object.assign(session, values); }, async remove(key) { delete session[key]; }
    }
  },
  cookies: { async getAll({ url }) { requests.push({ cookieLookup: url }); return cookieJar; } },
  runtime: {
    onInstalled: event,
    onStartup: { addListener() {} },
    onMessage: { addListener(fn) { listener = fn; } },
    sendNativeMessage(host, request, callback) {
      assert.equal(host, 'com.flugel.downloadmanager');
      requests.push(request);
      chrome.runtime.lastError = nativeError ? { message: nativeError } : undefined;
      const status = nativeReplies.length ? nativeReplies.shift() : nativeStatus;
      callback({ version: 1, status });
      chrome.runtime.lastError = undefined;
    }
  }
};
const context = vm.createContext({
  chrome, TextEncoder, URL, decodeURIComponent, Number, Object, Map, Array, String, Date,
  setTimeout, clearTimeout, crypto: { randomUUID: () => 'req-' + (++uuidSeq).toString(36) }
});
vm.runInContext(fs.readFileSync(path.join(__dirname, 'extension/service-worker.js'), 'utf8'), context);

const nuvem = 'https://nuvem.anitsu.moe/api/download?path=Animes%2FLetra%20Y%2FYoujo%20Senki%2F%5B2026%5D%20Youjo%20Senki%20II%2F%5BAnitsu%5D%20Youjo%20Senki%20S2%20-%20%5BWEB%20DUAL%201080p%20HEVC%5D%2F%5BAnitsu%5D%20Youjo%20Senki%20S2%20-%2008%20%5BWEB%20DUAL%201080p%20HEVC%5D.mkv';

(async () => {
  // Name detection, including the query-parameter path that failed before.
  assert.equal(context.mediaNameFromUrl(nuvem), '[Anitsu] Youjo Senki S2 - 08 [WEB DUAL 1080p HEVC].mkv');
  assert.equal(context.mediaNameFromUrl('https://cdn.example.test/588340.mp4?token=x'), '588340.mp4');
  assert.equal(context.mediaNameFromUrl('https://example.test/watch?id=7'), null);
  assert.equal(context.mediaNameFromUrl('https://example.test/page.html'), null);
assert.equal(context.typedName('https://example.test/api/download', 'video/x-matroska'), 'download.mkv');
const correctName = '[Flugel] Tensei Shitara Ken Deshita Ii - 04.mp4';
assert.equal(context.nameFromDisposition("attachment; filename=download; filename*=UTF-8'pt'%5BFlugel%5D%20Epis%C3%B3dio.mp4"), '[Flugel] Episódio.mp4');
assert.equal(context.nameFromDisposition('attachment; filename="part; 100%20.mp4"'), 'part; 100%20.mp4');
assert.equal(context.nameFromDisposition("attachment; filename=backup.mp4; filename*=UTF-8''bad%ZZ"), 'backup.mp4');
const drive = { url: 'https://drive.usercontent.google.com/download?id=fixture', mime: 'video/mp4', filename: '' };
assert.equal(context.fileNameForDownload({ ...drive, filename: 'C:\\Downloads\\' + correctName }, {}, 'Wrong title'), correctName);
assert.equal(context.fileNameForDownload({ ...drive, filename: 'download' }, { fileName: correctName }, 'Wrong title'), correctName);
assert.equal(context.fileNameForDownload(drive, {}, 'Episode 04'), 'Episode 04.mp4');
assert.equal(context.fileNameForDownload(drive, {}, 'Series / Episode 04'), 'Series _ Episode 04.mp4');
assert.equal(context.fileNameForDownload({ ...drive, filename: 'download.bin' }, {}, 'Episode 04.mp4'), 'Episode 04.mp4');
assert.equal(context.fileNameForDownload({ ...drive, filename: 'download.mp4' }, { fileName: 'download' }, 'Episode 04'), 'Episode 04.mp4');
assert.equal(context.fileNameForDownload({ ...drive, url: 'https://example.test/get?filename=real.zip' }, {}, 'Wrong title'), 'real.zip');
assert.equal(context.fileNameForDownload({ ...drive, filename: 'chosen.name' }, { fileName: 'server.mp4' }, 'Wrong title'), 'chosen.name');
let suggestions = 0;
determiningFilename({ url: 'blob:https://example.test/local', state: 'in_progress' }, (...args) => {
assert.equal(args.length, 0, 'Browser filename must not be overridden'); suggestions++;
});
assert.equal(suggestions, 1);
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

  // Automatic takeover: pause immediately, cancel ONLY after the desktop durably confirms.
  const item = { id: 99, url: nuvem, referrer: activeTab.url, state: 'in_progress', filename: 'C:\\Downloads\\episode.mkv' };
  nativeError = undefined;
  storedFlags = { autoIntercept: true, 'cookies:anitsu.moe': true };
  vm.runInContext('HANDOFF_POLL_DELAY_MS = 1; HANDOFF_DEADLINE_MS = 60', context);
  context.observeRequest({ url: nuvem, method: 'GET' });
  requests.length = 0;
  downloadCalls.length = 0;
  nativeReplies = ['waiting-user-confirmation', 'confirmed'];
  await context.interceptDownload(item);
  assert.deepEqual(downloadCalls, [['pause', 99], ['cancel', 99]]);
  const takeoverRequest = requests.find(r => r.action);
  assert.equal(takeoverRequest.url, nuvem);
  assert.equal(takeoverRequest.cookie, 'session=abc; lang=pt');
  assert.equal(typeof takeoverRequest.requestId, 'string');
  assert.equal(requests.filter(r => r.action === 'job-status').length, 1);
  assert.equal(requests.find(r => r.action === 'job-status').requestId, takeoverRequest.requestId);
  assert.equal(Object.keys(session).filter(key => key.startsWith('pending:')).length, 0, 'confirmed handoff must clear its ledger');

  // Final redirect URL and browser filename win; User-Agent comes only from observed headers.
  const finalUrl = 'https://cdn.example.test/download';
  context.observeRequest({ url: finalUrl, method: 'GET', requestHeaders: [{ name: 'User-Agent', value: 'BrowserTest/1.0' }] });
  requests.length = 0;
  nativeReplies = ['waiting-user-confirmation', 'confirmed'];
  downloadCalls.length = 0;
  await context.interceptDownload({ ...item, finalUrl, filename: 'C:\\Downloads\\server-name.zip' });
  assert.equal(requests.find(r => r.action === 'enqueue-media').url, finalUrl);
  assert.equal(requests.find(r => r.action === 'enqueue-media').fileName, 'server-name.zip');
  assert.equal(requests.find(r => r.action === 'enqueue-media').userAgent, 'BrowserTest/1.0');
  assert.deepEqual(downloadCalls, [['pause', 99], ['cancel', 99]]);
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

  // Closing/canceling the desktop dialog restores the browser copy instead of losing it.
  requests.length = 0;
  downloadCalls.length = 0;
  nativeReplies = ['waiting-user-confirmation', 'rejected'];
  await context.interceptDownload(item);
  assert.deepEqual(downloadCalls, [['pause', 99], ['resume', 99]]);
  assert.equal(requests.filter(r => r.action === 'job-status').length, 1);
  assert.equal(Object.keys(session).filter(key => key.startsWith('pending:')).length, 0, 'rejected handoff must clear its ledger');

  // A busy desktop is a rejection: resume without polling.
  nativeReplies = ['desktop-busy'];
  downloadCalls.length = 0;
  requests.length = 0;
  await context.interceptDownload(item);
  assert.deepEqual(downloadCalls, [['pause', 99], ['resume', 99]]);
  assert.equal(requests.filter(r => r.action === 'job-status').length, 0);

  // An unreachable host also restores the copy.
  nativeReplies = [];
  nativeError = 'Specified native messaging host not found.';
  downloadCalls.length = 0;
  await context.interceptDownload(item);
  assert.deepEqual(downloadCalls, [['pause', 99], ['resume', 99]]);
  nativeError = undefined;

  // Desktop restarted without any record of the request: unknown-request restores the copy.
  nativeReplies = ['waiting-user-confirmation', 'unknown-request'];
  downloadCalls.length = 0;
  requests.length = 0;
  await context.interceptDownload(item);
  assert.deepEqual(downloadCalls, [['pause', 99], ['resume', 99]]);

  // No durable outcome before the deadline: restore the browser copy.
  nativeReplies = [];
  nativeStatus = 'waiting-user-confirmation';
  downloadCalls.length = 0;
  await context.interceptDownload(item);
  assert.deepEqual(downloadCalls, [['pause', 99], ['resume', 99]]);
  nativeStatus = 'accepted-for-confirmation';

  // Reconciliation after worker death: aged ledger entries restore their own copies only.
  downloadCalls.length = 0;
  session['pending:aged'] = { downloadId: 1234, startedAt: Date.now() - 300000 };
  session['pending:fresh'] = { downloadId: 5555, startedAt: Date.now() };
  await vm.runInContext('reconcileHandoffs()', context);
  assert.deepEqual(downloadCalls, [['resume', 1234]], 'only the aged entry restores its copy');
  assert.equal('pending:aged' in session, false, 'aged entry must be cleared');
  assert.equal('pending:fresh' in session, true, 'fresh in-flight handoff must be preserved');
  assert.ok(alarmsCreated.some(a => a.name === 'fdm-reconcile'), 'periodic reconciliation alarm');
  setTimeout(() => { if (alarmHandler) void alarmHandler({ name: 'fdm-reconcile' }); }, 0);
  await new Promise(resolve => setTimeout(resolve, 10));
  assert.deepEqual(downloadCalls, [['resume', 1234]]);
  delete session['pending:fresh'];

  // Cancel failure during takeover still restores the copy.
  nativeReplies = ['waiting-user-confirmation', 'confirmed'];
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
  nativeReplies = [];

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
    // Real host must accept a correlated job-status request and answer without parse errors.
    const body = Buffer.from(JSON.stringify({ version: 1, action: 'job-status', requestId: 'fixture-status-0001' }));
    const header = Buffer.alloc(4);
    header.writeUInt32LE(body.length);
    const run = spawnSync(process.argv[2], [], { input: Buffer.concat([header, body]), timeout: 8000 });
    assert.ifError(run.error);
    const reply = JSON.parse(run.stdout.subarray(4, 4 + run.stdout.readUInt32LE(0)));
    assert.notEqual(reply.error, 'invalid-request');
  }
  console.log('PASS media naming, per-site cookies, popup submission, MV3 suspension, strict schema, durable-acceptance handoff and browser fallback');
})().catch(error => { console.error(error); process.exitCode = 1; });
