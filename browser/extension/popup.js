// Flugel Download Manager popup — global and per-site settings.
"use strict";
const $ = (id) => document.getElementById(id);

async function activeHost() {
  try {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    if (!tab?.url) return "";
    const u = new URL(tab.url);
    if (u.protocol !== "http:" && u.protocol !== "https:") return "";
    return u.hostname.toLowerCase();
  } catch { return ""; }
}

async function load() {
  const host = await activeHost();
  $("host").textContent = host || "not a web page";
  const keys = ["globalEnabled", "balloonEnabled", "autoIntercept"];
  if (host) keys.push("site:" + host, "cookies:" + host);
  const d = await chrome.storage.local.get(keys);
  $("globalEnabled").checked = d.globalEnabled !== false;
  $("balloonEnabled").checked = d.balloonEnabled !== false;
  $("autoIntercept").checked = d.autoIntercept === true;
  $("siteEnabled").checked = host ? d["site:" + host] !== false : true;
  $("siteEnabled").disabled = !host;
  $("siteCookies").checked = host ? d["cookies:" + host] === true : false;
  $("siteCookies").disabled = !host;
}

async function save(partial) {
  await chrome.storage.local.set(partial);
  $("status").textContent = "Saved.";
  setTimeout(() => $("status").textContent = "", 1200);
}

$("globalEnabled").addEventListener("change", () => save({ globalEnabled: $("globalEnabled").checked }));
$("balloonEnabled").addEventListener("change", () => save({ balloonEnabled: $("balloonEnabled").checked }));
$("autoIntercept").addEventListener("change", () => save({ autoIntercept: $("autoIntercept").checked }));
$("siteEnabled").addEventListener("change", async () => {
  const host = await activeHost();
  if (host) await save({ ["site:" + host]: $("siteEnabled").checked });
});
$("siteCookies").addEventListener("change", async () => {
  const host = await activeHost();
  if (host) await save({ ["cookies:" + host]: $("siteCookies").checked });
});

function checkBridge() {
  $("extensionId").textContent = chrome.runtime.id;
  chrome.runtime.sendNativeMessage("com.flugel.downloadmanager", { version: 1, action: "ping" }, (response) => {
    const error = chrome.runtime.lastError?.message || "";
    $("bridgeStatus").textContent = error
      ? /not found/i.test(error) ? "Ponte nativa não registrada neste navegador."
        : /forbidden/i.test(error) ? "Este ID de extensão não está autorizado no registro da ponte."
        : "Não foi possível iniciar a ponte nativa. Verifique a instalação."
      : response?.version === 1 && response?.status === "ready" ? "Ponte nativa conectada."
        : "Ponte nativa incompatível. Atualize o aplicativo.";
  });
}
$("checkBridge").addEventListener("click", checkBridge);
load();
checkBridge();
