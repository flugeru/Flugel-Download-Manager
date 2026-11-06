// Flugel Download Manager MV3 service worker — independent implementation.
// - Context-menu link sending (confirmation flow in the desktop app)
// - Media detection from URL names (including path/file query parameters) and response headers
// - Opt-in, per-site browser session cookies for the single transfer the user starts
// - Native messaging bridge to Flugel.NativeHost
"use strict";

const HOST = "com.flugel.downloadmanager";
const MENU_LINK = "flugel-confirm-link";
const MENU_PAGE = "flugel-confirm-page";
const MAX_PER_TAB = 60;
const MEDIA_EXT = /\.(mp4|m4v|mkv|webm|mov|avi|flv|f4v|wmv|mpg|mpeg|ts|m2ts|3gp|ogv|mka|m3u8|mpd|mp3|m4a|flac|aac|ogg|opus|wav)$/i;
const MEDIA_TYPE = /^(video|audio)\/|^application\/(x-matroska|vnd\.apple\.mpegurl|dash\+xml|x-mpegurl)/i;
const tabMedia = new Map(); // tabId -> Map(url -> meta)

function badge(ok, title) {
  try {
    chrome.action.setBadgeText({ text: ok ? "OK" : "ERR" });
    chrome.action.setBadgeBackgroundColor({ color: ok ? "#24613b" : "#a32929" });
    chrome.action.setTitle({ title });
  } catch {}
}

function cleanUrl(raw) {
  if (typeof raw !== "string" || !/^https?:\/\//i.test(raw) || /[\s\\\u0000-\u001f\u007f-\u009f]/u.test(raw)) return null;
  try {
    const u = new URL(raw);
    if (!["http:", "https:"].includes(u.protocol) || !u.hostname || u.username || u.password) return null;
    const authority = raw.slice(raw.indexOf("://") + 3).split(/[/?#]/, 1)[0];
    if (authority.includes("@")) return null;
    return u.href;
  } catch { return null; }
}

// Names are read from the path and from common download parameters (e.g. ?path=folder%2Ffile.mkv).
function mediaNameFromUrl(url) {
  let u;
  try { u = new URL(url); } catch { return null; }
  const candidates = [u.pathname];
  for (const key of ["path", "file", "filename", "name", "f"]) {
    const value = u.searchParams.get(key);
    if (value) candidates.push(value);
  }
  for (const candidate of candidates) {
    let last;
    try { last = decodeURIComponent(candidate.split(/[\\/]/).filter(Boolean).pop() || ""); } catch { continue; }
    last = last.replace(/[<>:"/\\|?*\x00-\x1f]/g, "_").slice(0, 200);
    if (MEDIA_EXT.test(last)) return last;
  }
  return null;
}

function nameFromDisposition(value) {
  if (!value) return null;
  const star = /filename\*=(?:UTF-8'')?([^;]+)/i.exec(value);
  const plain = /filename="?([^";]+)"?/i.exec(value);
  const raw = (star ? star[1] : plain ? plain[1] : "").trim();
  if (!raw) return null;
  let name;
  try { name = decodeURIComponent(raw); } catch { name = raw; }
  name = name.replace(/[<>:"/\\|?*\x00-\x1f]/g, "_").slice(0, 200);
  return name || null;
}

function formatOf(name) {
  if (/\.m3u8$/i.test(name)) return "hls";
  if (/\.mpd$/i.test(name)) return "dash";
  return "direct";
}

function hostOf(url) {
  try { return new URL(url).hostname.toLowerCase(); } catch { return null; }
}

function recordMedia(tabId, meta) {
  if (tabId < 0) return;
  let map = tabMedia.get(tabId);
  if (!map) { map = new Map(); tabMedia.set(tabId, map); }
  map.set(meta.url, meta);
while (map.size > MAX_PER_TAB) map.delete(map.keys().next().value);
// MV3 workers suspend between popup actions; keep metadata in browser-session memory only.
chrome.storage.session.set({ ["media:" + tabId]: [...map.values()] }).catch(() => {});
}

async function mediaForTab(tabId) {
const map = tabMedia.get(tabId);
if (map) return [...map.values()];
const stored = await chrome.storage.session.get("media:" + tabId);
return stored["media:" + tabId] || [];
}

function clearTabMedia(tabId) {
tabMedia.delete(tabId);
chrome.storage.session.remove("media:" + tabId).catch(() => {});
}

// Session cookies are sent only when the user has enabled them for this site (or a parent domain).
async function siteAllowed(host) {
  if (!host) return false;
  const keys = [];
  for (let h = host; h.includes("."); h = h.slice(h.indexOf(".") + 1)) keys.push("cookies:" + h);
  if (keys.length === 0) return false;
  const stored = await chrome.storage.local.get(keys);
  return keys.some(k => stored[k] === true);
}

async function sessionCookieHeader(mediaUrl, pageUrl) {
  const allowed = (await siteAllowed(hostOf(pageUrl))) || (await siteAllowed(hostOf(mediaUrl)));
  if (!allowed) return null;
  // Same cookies the browser would attach to this exact URL.
  const cookies = await chrome.cookies.getAll({ url: mediaUrl });
  if (!cookies.length) return null;
  return cookies.map(c => `${c.name}=${c.value}`).join("; ");
}

function sendNative(request) {
  return new Promise((resolve) => {
    try {
      if (new TextEncoder().encode(JSON.stringify(request)).length > 65536) return resolve({ ok: false, error: "oversized" });
      chrome.runtime.sendNativeMessage(HOST, request, (response) => {
        if (chrome.runtime.lastError) {
          const message = chrome.runtime.lastError.message || "";
          const error = /not found/i.test(message) ? "host-not-found" : /forbidden/i.test(message) ? "host-forbidden" : "host-unavailable";
          return resolve({ ok: false, error });
        }
        const ok = response?.version === 1 && response?.status === "accepted-for-confirmation";
        resolve({ ok, error: ok ? undefined : response?.error || "invalid-host-response" });
      });
    } catch {
      resolve({ ok: false, error: "host-unavailable" });
    }
  });
}

async function enqueueLink(url, extra = {}) {
  const clean = cleanUrl(url);
  if (!clean) { badge(false, "Flugel Download Manager: invalid or unsafe URL"); return { ok: false, error: "invalid-url" }; }
  const request = { version: 1, action: "enqueue", url: clean };
  for (const key of ["fileName", "referrer", "pageTitle", "mediaQuality"]) {
    if (typeof extra[key] === "string" && extra[key].length > 0) request[key] = extra[key];
  }
  if (Array.isArray(extra.segmentUrls) && extra.segmentUrls.length > 0) request.segmentUrls = extra.segmentUrls;
  const cookie = await sessionCookieHeader(clean, extra.pageUrl || extra.referrer || "").catch(() => null);
  if (cookie) request.cookie = cookie;
  if (Object.keys(request).length > 3) request.action = "enqueue-media";
  const result = await sendNative(request);
  badge(result.ok, result.ok ? "Flugel Download Manager: accepted for desktop confirmation" : "Flugel Download Manager: host unavailable or request rejected");
  return result;
}

chrome.runtime.onInstalled.addListener(() => {
  chrome.contextMenus.removeAll(() => {
    chrome.contextMenus.create({ id: MENU_LINK, title: "Send link to Flugel Download Manager", contexts: ["link"], targetUrlPatterns: ["http://*/*", "https://*/*"] });
    chrome.contextMenus.create({ id: MENU_PAGE, title: "Send page address to Flugel Download Manager", contexts: ["page"], documentUrlPatterns: ["http://*/*", "https://*/*"] });
  });
  chrome.storage.local.get(["globalEnabled", "balloonEnabled"], (d) => {
    const patch = {};
    if (d.globalEnabled === undefined) patch.globalEnabled = true;
    if (d.balloonEnabled === undefined) patch.balloonEnabled = true;
    if (Object.keys(patch).length) chrome.storage.local.set(patch);
  });
});

chrome.contextMenus.onClicked.addListener(async (info, tab) => {
  if (info.menuItemId === MENU_LINK) await enqueueLink(info.linkUrl, { pageTitle: tab?.title || "", pageUrl: tab?.url || "" });
  if (info.menuItemId === MENU_PAGE) await enqueueLink(info.pageUrl, { pageTitle: tab?.title || "", pageUrl: tab?.url || "" });
});

// Metadata-only observation: URLs and response headers; response bodies are never read.
try {
  chrome.webRequest.onBeforeRequest.addListener((details) => {
    const url = cleanUrl(details.url);
    if (!url) return;
    const name = mediaNameFromUrl(url);
    if (!name) return;
    recordMedia(details.tabId, { url, label: name, format: formatOf(name), fileName: name });
  }, { urls: ["http://*/*", "https://*/*"] });
} catch { /* webRequest unavailable; content-script detection still works. */ }

try {
  chrome.webRequest.onHeadersReceived.addListener((details) => {
    const url = cleanUrl(details.url);
    if (!url || details.statusCode >= 400) return;
    const headers = Object.fromEntries((details.responseHeaders || []).map(h => [h.name.toLowerCase(), h.value || ""]));
    const type = (headers["content-type"] || "").split(";")[0].trim();
    const named = nameFromDisposition(headers["content-disposition"]);
    const name = named && MEDIA_EXT.test(named) ? named : mediaNameFromUrl(url) || (MEDIA_TYPE.test(type) ? typedName(url, type) : null);
    if (!name) return;
    const size = Number(headers["content-length"]) || null;
    recordMedia(details.tabId, { url, label: name, format: formatOf(name), fileName: name, sizeBytes: size });
  }, { urls: ["http://*/*", "https://*/*"] }, ["responseHeaders"]);
} catch { /* optional */ }

const EXT_FOR_TYPE = {
  "video/mp4": "mp4", "video/x-m4v": "m4v", "video/webm": "webm", "video/quicktime": "mov", "video/x-msvideo": "avi",
  "video/x-matroska": "mkv", "video/mp2t": "ts", "video/x-flv": "flv", "video/3gpp": "3gp", "video/ogg": "ogv",
  "audio/mpeg": "mp3", "audio/mp4": "m4a", "audio/flac": "flac", "audio/ogg": "ogg", "audio/wav": "wav",
  "application/vnd.apple.mpegurl": "m3u8", "application/x-mpegurl": "m3u8", "application/dash+xml": "mpd"
};

// Used only when the server gives a media type but neither the URL nor the disposition names a file.
function typedName(url, type) {
  const ext = EXT_FOR_TYPE[type.toLowerCase()];
  if (!ext) return null;
  let base = "media";
  try {
    const last = decodeURIComponent(new URL(url).pathname.split("/").filter(Boolean).pop() || "");
    if (last) base = last.replace(/\.[^.]*$/, "").replace(/[<>:"/\\|?*\x00-\x1f]/g, "_").slice(0, 180) || "media";
  } catch {}
  return `${base}.${ext}`;
}

chrome.tabs.onRemoved.addListener(clearTabMedia);
chrome.tabs.onUpdated.addListener((tabId, info) => { if (info.status === "loading") clearTabMedia(tabId); });

// Pause first, cancel only after desktop acceptance; otherwise restore the browser.
async function interceptDownload(item) {
if (item.state !== "in_progress" || item.paused || item.byExtensionId || item.incognito) return;
const url = cleanUrl(item.url);
if (!url) return; // blob/data downloads cannot be fetched independently by the desktop.
const pageUrl = cleanUrl(item.referrer) || url;
let paused = false;
try {
const flags = await chrome.storage.local.get(["globalEnabled", "autoIntercept", "site:" + hostOf(pageUrl)]);
if (flags.globalEnabled === false || flags.autoIntercept !== true || flags["site:" + hostOf(pageUrl)] === false) return;
await chrome.downloads.pause(item.id);
paused = true;
const fileName = mediaNameFromUrl(url) || item.filename?.split(/[\\/]/).pop();
const result = await enqueueLink(url, { fileName, referrer: cleanUrl(item.referrer) || undefined });
if (result.ok) {
await chrome.downloads.cancel(item.id);
paused = false;
}
} catch {
badge(false, "Flugel Download Manager: interception failed; restoring browser download");
} finally {
if (paused) {
try { await chrome.downloads.resume(item.id); }
catch { badge(false, "Flugel Download Manager: resume the download from the browser download list"); }
}
}
}
chrome.downloads.onCreated.addListener(item => { void interceptDownload(item); });

chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  (async () => {
    if (msg?.type === "enqueue-media") {
      const r = await enqueueLink(msg.payload?.url, msg.payload || {});
      sendResponse(r);
      return;
    }
if (msg?.type === "get-page-media") {
const tabId = sender?.tab?.id ?? (await chrome.tabs.query({ active: true, currentWindow: true }))[0]?.id ?? -1;
sendResponse({ media: await mediaForTab(tabId) });
return;
}
if (msg?.type === "enqueue-detected-media" && !sender.tab) {
const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
const media = (await mediaForTab(tab?.id ?? -1)).find(m => m.url === msg.url);
if (!media) { sendResponse({ ok: false, error: "media-not-found" }); return; }
sendResponse(await enqueueLink(media.url, { fileName: media.fileName, referrer: tab.url, pageTitle: tab.title || "" }));
return;
}
    if (msg?.type === "get-config") {
      const d = await chrome.storage.local.get(["globalEnabled", "balloonEnabled", "autoIntercept"]);
      sendResponse({ ok: true, config: { globalEnabled: d.globalEnabled !== false, balloonEnabled: d.balloonEnabled !== false, autoIntercept: d.autoIntercept === true } });
      return;
    }
    sendResponse({ ok: false, error: "unknown-message" });
  })();
  return true; // async
});
