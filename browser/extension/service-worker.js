"use strict";

const host = "org.streamharbor.bridge";
const menu = "streamharbor-confirm-link";

function status(ok, title) {
  chrome.action.setBadgeText({ text: ok ? "ASK" : "ERR" });
  chrome.action.setBadgeBackgroundColor({ color: ok ? "#24613b" : "#a32929" });
  chrome.action.setTitle({ title });
}

chrome.runtime.onInstalled.addListener(() => {
  chrome.contextMenus.removeAll(() => {
    if (chrome.runtime.lastError) {
      status(false, "StreamHarbor: could not create link menu");
      return;
    }
    chrome.contextMenus.create({
      id: menu,
      title: "Send link to StreamHarbor for confirmation",
      contexts: ["link"],
      targetUrlPatterns: ["http://*/*", "https://*/*"]
    }, () => {
      if (chrome.runtime.lastError)
        status(false, "StreamHarbor: could not create link menu");
    });
  });
});

chrome.contextMenus.onClicked.addListener(info => {
  if (info.menuItemId !== menu) return;
  try {
    const url = info.linkUrl;
    if (typeof url !== "string" || !/^https?:\/\//i.test(url) || /[\s\\\u0000-\u001f\u007f-\u009f]/u.test(url))
      throw new Error("Invalid URL");
    const parsed = new URL(url);
    const authority = url.slice(url.indexOf("://") + 3).split(/[/?#]/, 1)[0];
    if (!["http:", "https:"].includes(parsed.protocol) || !parsed.hostname || parsed.username || parsed.password || authority.includes("@"))
      throw new Error("Invalid URL");
    const request = { version: 1, action: "enqueue", url };
    if (new TextEncoder().encode(JSON.stringify(request)).length > 65536)
      throw new Error("Oversized request");
    chrome.runtime.sendNativeMessage(host, request, response => {
      if (chrome.runtime.lastError) {
        status(false, "StreamHarbor: host unavailable; check opt-in registration");
        return;
      }
      if (response?.version === 1 && response.status === "accepted-for-confirmation")
        status(true, "StreamHarbor: accepted for desktop confirmation, not a completed download");
      else
        status(false, "StreamHarbor: request rejected; check desktop installation");
    });
  } catch {
    status(false, "StreamHarbor: invalid or oversized HTTP(S) link");
  }
});
