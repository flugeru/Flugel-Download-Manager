// Node built-ins only. Mock checks are not live browser integration tests.
"use strict";
const assert = require("node:assert/strict");
const fs = require("node:fs");
const vm = require("node:vm");
const path = require("node:path");
const { spawnSync } = require("node:child_process");

const manifest = JSON.parse(fs.readFileSync(path.join(__dirname, "extension/manifest.json"), "utf8"));
assert.equal(manifest.manifest_version, 3);
assert.deepEqual(manifest.permissions, ["contextMenus", "nativeMessaging"]);
assert.equal(manifest.host_permissions, undefined);
assert.equal(manifest.content_scripts, undefined);

let installed, clicked, menu, lastBadge, lastTitle;
let requests = [];
let response = { version: 1, status: "accepted-for-confirmation" };
let nativeError = false;
const chrome = {
  runtime: {
    onInstalled: { addListener(fn) { installed = fn; } },
    sendNativeMessage(host, request, callback) {
      requests.push({ host, request });
      if (nativeError) this.lastError = { message: "sensitive browser error" };
      callback(response);
      delete this.lastError;
    }
  },
  contextMenus: {
    removeAll(callback) { callback(); },
    create(options, callback) { menu = options; callback(); },
    onClicked: { addListener(fn) { clicked = fn; } }
  },
  action: {
    setBadgeText({ text }) { lastBadge = text; },
    setBadgeBackgroundColor() {},
    setTitle({ title }) { lastTitle = title; }
  }
};
vm.runInNewContext(fs.readFileSync(path.join(__dirname, "extension/service-worker.js"), "utf8"),
  { chrome, URL, TextEncoder });
installed();
assert.equal(menu.id, "flugel-download-manager-confirm-link");
assert.equal(JSON.stringify(menu.contexts), '["link"]');
assert.equal(JSON.stringify(menu.targetUrlPatterns), '["http://*/*","https://*/*"]');
const click = url => clicked({ menuItemId: menu.id, linkUrl: url });
click("https://example.test/file?private=secret&x=%22");
assert.equal(requests.length, 1);
assert.equal(requests[0].host, "com.flugel.downloadmanager");
assert.equal(JSON.stringify(requests[0].request), '{"version":1,"action":"enqueue","url":"https://example.test/file?private=secret&x=%22"}');
assert.equal(lastBadge, "ASK");
assert.ok(lastTitle.includes("not a completed download") && !lastTitle.includes("secret"));
for (const url of [undefined, "file:///x", "javascript:alert(1)", "https://a:b@example.test/", "https://@example.test/", "https://example.test/a b", "https://example.test/\\x", "https://example.test/\u0080", "https://example.test/" + "x".repeat(65536)]) {
  const before = requests.length;
  click(url);
  assert.equal(requests.length, before);
  assert.equal(lastBadge, "ERR");
}
clicked({ menuItemId: "other", linkUrl: "https://example.test/" });
assert.equal(requests.length, 1);
response = { version: 1, status: "completed" };
click("https://example.test/");
assert.equal(lastBadge, "ERR");
nativeError = true;
click("https://example.test/");
assert.equal(lastBadge, "ERR");
assert.ok(!lastTitle.includes("sensitive"));
console.log("PASS MV3 manifest, menu, exact request, URL/size rejection, confirmation-only status, native error (mocked APIs)");

const host = process.argv[2];
if (host) {
  function frame(body) {
    const bytes = Buffer.from(body);
    const header = Buffer.alloc(4);
    header.writeUInt32LE(bytes.length);
    return Buffer.concat([header, bytes]);
  }
  function check(input, expectedExit, expectedError) {
    const run = spawnSync(path.resolve(host), [], { input, timeout: 10000, windowsHide: true });
    assert.ifError(run.error);
    assert.equal(run.status, expectedExit);
    assert.equal(run.stderr.length, 0);
    if (!expectedError) { assert.equal(run.stdout.length, 0); return; }
    assert.ok(run.stdout.length >= 4);
    assert.equal(run.stdout.readUInt32LE(), run.stdout.length - 4);
    const reply = JSON.parse(run.stdout.subarray(4).toString("utf8"));
    assert.deepEqual(reply, { version: 1, status: "rejected", error: expectedError });
  }
  check(Buffer.alloc(0), 0);
  check(Buffer.from([1, 0]), 1, "invalid-frame");
  check(Buffer.from([2, 0, 0, 0, 123]), 1, "invalid-frame");
  check(Buffer.from([1, 0, 1, 0]), 1, "invalid-frame");
  check(frame('{"version":1,"action":"enqueue","url":"file:///x"}'), 1, "invalid-request");
  check(frame(Buffer.from([0xff])), 1, "invalid-request");
  console.log("PASS actual host process: EOF, partial header/body, oversized length, invalid URL/UTF-8; stdout contains framed protocol only; no desktop launched");
}
