// Flugel Download Manager content script — independent implementation.
// Detects HTML5 media and shows a floating, non-obstructive "Download this video"
// balloon. Never modifies playback, never accesses DRM/protected streams.
"use strict";
(() => {
  if (globalThis.__flugelContentLoaded) return;
  globalThis.__flugelContentLoaded = true;

  const HOST = "com.flugel.downloadmanager";
  const VIDEO_EXT = /\.(mp4|m4v|mkv|webm|mov|avi|flv|wmv|ts|m2ts|3gp|ogv)$/i;
  const MEDIA_LINK_EXT = /\.(mp4|m4v|mkv|webm|mov|avi|flv|wmv|mpg|mpeg|ts|m2ts|3gp|ogv|m3u8|mpd|mp3|m4a|flac|aac|ogg|opus|wav)$/i;
  const HLS_HINT = /\.(m3u8)(?:[?#].*)?$/i;
  const DASH_HINT = /\.(mpd)(?:[?#].*)?$/i;

  let globalEnabled = true;
  let siteEnabled = true;
  let balloonEnabled = true;
  let autoIntercept = false;
  const balloons = new WeakMap(); // video -> {host, shadow, menu}
  const seenUrls = new Map(); // url -> meta

  function pageHost() { try { return location.hostname.toLowerCase(); } catch { return ""; } }
  function isTopFrame() { try { return window.top === window; } catch { return false; } }

  async function loadSettings() {
    try {
      const host = pageHost();
      const data = await chrome.storage.local.get(["globalEnabled", "balloonEnabled", "autoIntercept", "site:" + host]);
      globalEnabled = data.globalEnabled !== false;
      balloonEnabled = data.balloonEnabled !== false;
      autoIntercept = data.autoIntercept === true;
      siteEnabled = data["site:" + host] !== false;
    } catch { /* default on */ }
  }

  function absoluteUrl(value) {
    if (!value || typeof value !== "string") return null;
    try {
      const u = new URL(value, document.baseURI);
      if (u.protocol !== "http:" && u.protocol !== "https:") return null;
      if (u.username || u.password) return null;
      return u.href;
    } catch { return null; }
  }

  function fileNameFromUrl(url) {
    try {
      const u = new URL(url);
      const candidates = [u.pathname];
      for (const key of ["path", "file", "filename", "name", "f"]) {
        const value = u.searchParams.get(key);
        if (value) candidates.push(value);
      }
      for (const candidate of candidates) {
        const seg = decodeURIComponent(candidate.split(/[\\/]/).filter(Boolean).pop() || "");
        if (MEDIA_LINK_EXT.test(seg)) return seg.replace(/[<>:"/\\|?*\x00-\x1f]/g, "_").slice(0, 200);
      }
      const fallback = decodeURIComponent(u.pathname.split("/").filter(Boolean).pop() || "");
      return fallback.replace(/[<>:"/\\|?*\x00-\x1f]/g, "_").slice(0, 180) || "media";
    } catch { return "media"; }
  }

  function collectVariants(video) {
    const variants = [];
    const push = (url, label, format, extra = {}) => {
      const abs = absoluteUrl(url);
      if (!abs) return;
      if (variants.some(v => v.url === abs)) return;
      const meta = { url: abs, label, format, fileName: fileNameFromUrl(abs), ...extra };
      variants.push(meta);
      seenUrls.set(abs, meta);
    };

    // Direct sources on <video> and <source> children.
    const current = video.currentSrc || video.src;
    if (current) {
      if (HLS_HINT.test(current)) push(current, "HLS playlist", "hls");
      else if (DASH_HINT.test(current)) push(current, "DASH manifest", "dash");
      else if (VIDEO_EXT.test(current)) push(current, guessQualityLabel(current, video), "direct");
      else push(current, "Video source", "direct");
    }
    for (const src of video.querySelectorAll("source[src]")) {
      const u = src.getAttribute("src");
      const type = (src.getAttribute("type") || "").toLowerCase();
      if (HLS_HINT.test(u) || type.includes("mpegurl")) push(u, "HLS playlist", "hls");
      else if (DASH_HINT.test(u) || type.includes("dash")) push(u, "DASH manifest", "dash");
      else push(u, src.getAttribute("label") || type || guessQualityLabel(u, video), "direct");
    }
    // Media files linked from the page (for example download buttons that point to a file).
    for (const a of document.querySelectorAll("a[href]")) {
      if (variants.length >= 40) break;
      const abs = absoluteUrl(a.getAttribute("href"));
      if (!abs) continue;
      const name = fileNameFromUrl(abs);
      if (!MEDIA_LINK_EXT.test(name)) continue;
      const format = HLS_HINT.test(name) ? "hls" : DASH_HINT.test(name) ? "dash" : "direct";
      push(abs, "Link da página", format);
    }
    return variants;
  }

  function guessQualityLabel(url, video) {
    const m = url.match(/(\d{3,4})p\b/i) || url.match(/\b(2160|1440|1080|720|480|360)\b/);
    if (m) return m[1] + "p";
    const h = video.videoHeight || 0;
    if (h >= 2160) return "2160p";
    if (h >= 1440) return "1440p";
    if (h >= 1080) return "1080p";
    if (h >= 720) return "720p";
    if (h >= 480) return "480p";
    if (h > 0) return h + "p";
    const ext = (url.match(/\.([a-z0-9]{2,5})(?:[?#]|$)/i) || [,"file"])[1].toUpperCase();
    return ext;
  }

  function removeBalloon(video) {
    const entry = balloons.get(video);
    if (entry?.host?.isConnected) entry.host.remove();
    balloons.delete(video);
  }

  function positionBalloon(video, host) {
    const r = video.getBoundingClientRect();
    if (r.width < 120 || r.height < 80 || r.bottom < 0 || r.right < 0 || r.top > innerHeight || r.left > innerWidth) {
      host.style.display = "none";
      return;
    }
    host.style.display = "block";
    // Place near top-right of the player, inset so it doesn't cover controls.
    const top = Math.max(4, r.top + 8);
    const left = Math.min(innerWidth - 210, Math.max(4, r.right - 205));
    host.style.top = top + "px";
    host.style.left = left + "px";
  }

  function createBalloon(video) {
    if (balloons.has(video)) return balloons.get(video);
    const host = document.createElement("div");
    host.setAttribute("data-flugel-download-manager", "balloon");
    host.style.cssText = "position:fixed;z-index:2147483646;display:block;pointer-events:auto;font:13px/1.3 Segoe UI,system-ui,sans-serif;";
    const shadow = host.attachShadow({ mode: "open" });
    shadow.innerHTML = `
      <style>
        :host { all: initial; }
        .wrap { position: relative; }
        .btn {
          display: inline-flex; align-items: center; gap: 6px;
          padding: 7px 10px; border: 1px solid rgba(255,255,255,.22);
          border-radius: 8px; cursor: pointer; user-select: none;
          background: rgba(16,48,74,.92); color: #fff;
          box-shadow: 0 6px 18px rgba(0,0,0,.28);
          backdrop-filter: blur(2px);
        }
        .btn:hover,.btn:focus { background: rgba(8,126,133,.96); outline: none; }
        .btn svg { width: 14px; height: 14px; flex: 0 0 auto; }
        .menu {
          display: none; position: absolute; right: 0; top: calc(100% + 6px);
          min-width: 220px; max-width: 320px; max-height: 260px; overflow: auto;
          background: #fff; color: #172D46; border: 1px solid #cfd9e3;
          border-radius: 10px; box-shadow: 0 10px 28px rgba(0,0,0,.18);
        }
        .menu.open { display: block; }
        .item { padding: 9px 11px; cursor: pointer; border-bottom: 1px solid #eef3f7; }
        .item:last-child { border-bottom: 0; }
        .item:hover { background: #eef8f8; }
        .label { font-weight: 600; }
        .sub { font-size: 11px; color: #52647A; word-break: break-all; }
        .warn { padding: 9px 11px; font-size: 11px; color: #52647A; background: #f7f9fb; }
        .close { margin-left: 2px; opacity: .75; font-size: 15px; line-height: 1; padding: 0 2px; }
        .close:hover { opacity: 1; }
      </style>
      <div class="wrap">
        <button class="btn" type="button" aria-haspopup="menu" aria-expanded="false">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 3v12"/><path d="M7 10l5 5 5-5"/><path d="M5 21h14"/></svg>
          <span>Download this video</span>
          <span class="close" title="Hide on this page for now" aria-label="Hide">×</span>
        </button>
        <div class="menu" role="menu"></div>
      </div>`;
    const button = shadow.querySelector(".btn");
    const menu = shadow.querySelector(".menu");
    const close = shadow.querySelector(".close");

    close.addEventListener("click", (e) => {
      e.stopPropagation();
      host.style.display = "none";
      host.dataset.hiddenByUser = "1";
    });
    button.addEventListener("click", (e) => {
      if (e.target === close) return;
      e.preventDefault(); e.stopPropagation();
      const open = menu.classList.toggle("open");
      button.setAttribute("aria-expanded", open ? "true" : "false");
      if (open) renderMenu(video, menu);
    });
    shadow.addEventListener("click", e => e.stopPropagation());
    document.addEventListener("click", () => { menu.classList.remove("open"); button.setAttribute("aria-expanded","false"); }, { capture: true, passive: true });

    (document.body || document.documentElement).appendChild(host);
    const entry = { host, shadow, menu };
    balloons.set(video, entry);
    positionBalloon(video, host);
    return entry;
  }

  function renderMenu(video, menu) {
    let variants = [];
    try { variants = collectVariants(video); } catch (error) { console.warn("Flugel Download Manager: media scan failed", error?.name); }
    // Merge variants reported by the background network observer for this page.
    chrome.runtime.sendMessage({ type: "get-page-media" }, (resp) => {
      try {
        const extra = Array.isArray(resp?.media) ? resp.media : [];
        for (const m of extra) {
          if (!variants.some(v => v.url === m.url)) variants.push(m);
        }
        paint();
      } catch { paint(); }
    });
    function paint() {
      menu.textContent = "";
      if (variants.length === 0) {
        const warn = document.createElement("div");
        warn.className = "warn";
        warn.textContent = "No direct/unprotected media URL detected yet. Try playing the video, or use the browser context menu on a link. DRM-protected streams are not available.";
        menu.appendChild(warn);
        return;
      }
      for (const v of variants) {
        const item = document.createElement("div");
        item.className = "item";
        item.setAttribute("role", "menuitem");
        item.tabIndex = 0;
        item.innerHTML = `<div class="label">${escapeHtml(v.label || v.format || "Media")} · ${escapeHtml(v.format || "file")}</div>` +
          `<div class="sub">${escapeHtml(v.fileName || "")}</div>`;
        const choose = () => {
          menu.classList.remove("open");
          chrome.runtime.sendMessage({
            type: "enqueue-media",
            payload: {
              url: v.url,
              fileName: v.fileName,
              mediaQuality: v.label,
              pageUrl: location.href,
              pageTitle: document.title || "",
              referrer: location.href
            }
          }, (r) => {
            if (chrome.runtime.lastError || !r?.ok) {
              const messages = {
                "host-not-found": "A mídia foi detectada, mas a ponte com o aplicativo não está registrada neste navegador. Abra a extensão Flugel Download Manager para ver as instruções de conexão.",
                "host-forbidden": "A ponte nativa não autoriza esta extensão. Registre o ID mostrado no painel da extensão Flugel Download Manager.",
                "invalid-request": "A ponte nativa rejeitou o pedido. Atualize a extensão e o aplicativo Flugel Download Manager juntos.",
                "desktop-unavailable": "A ponte nativa foi encontrada, mas não conseguiu abrir o Flugel Download Manager. Verifique a instalação do aplicativo.",
                "oversized": "A lista de segmentos excedeu o limite da ponte nativa.",
                "desktop-busy": "O Flugel Download Manager já está confirmando outro download. Conclua ou cancele essa janela e tente novamente."
              };
              notifyUser(messages[r?.error] || "Falha na comunicação com o Flugel Download Manager. Recarregue a extensão e esta página; o download do navegador não foi cancelado.");
            }
          });
        };
        item.addEventListener("click", choose);
        item.addEventListener("keydown", e => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); choose(); } });
        menu.appendChild(item);
      }
      const note = document.createElement("div");
      note.className = "warn";
      note.textContent = "Only direct/unprotected media is offered. If the browser download fails, Flugel Download Manager does not cancel or discard it.";
      menu.appendChild(note);
    }
  }

  function notifyUser(text) {
    // Lightweight in-page notice; never uses alert() (breaks playback).
    try {
      const n = document.createElement("div");
      n.style.cssText = "position:fixed;right:14px;bottom:14px;z-index:2147483647;background:#9B283B;color:#fff;padding:10px 12px;border-radius:8px;font:13px Segoe UI,system-ui;max-width:320px;box-shadow:0 8px 22px rgba(0,0,0,.25)";
      n.textContent = text;
      document.body.appendChild(n);
      setTimeout(() => n.remove(), 4500);
    } catch {}
  }

  function escapeHtml(s) { return String(s).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c])); }

  function isUsableVideo(v) {
    if (!(v instanceof HTMLVideoElement)) return false;
    const r = v.getBoundingClientRect();
    // Ignore tiny/hidden elements (trackers, audio-only placeholders).
    return r.width >= 120 && r.height >= 80;
  }

  function scan() {
    if (!globalEnabled || !siteEnabled || !balloonEnabled) {
      for (const v of document.querySelectorAll("video")) removeBalloon(v);
      return;
    }
    for (const v of document.querySelectorAll("video")) {
      if (!isUsableVideo(v)) { removeBalloon(v); continue; }
      const entry = balloons.get(v);
      if (entry?.host?.dataset.hiddenByUser === "1") continue;
      if (entry) positionBalloon(v, entry.host);
      else createBalloon(v);
    }
    // Clean up balloons whose videos left the document.
    for (const host of document.querySelectorAll('[data-flugel-download-manager="balloon"]')) {
      // best-effort; weak map handles detached videos
      if (!host.isConnected) continue;
    }
  }

  function observe() {
    const mo = new MutationObserver(() => scheduleScan());
    mo.observe(document.documentElement || document, { childList: true, subtree: true, attributes: true, attributeFilter: ["src", "style", "class"] });
    const ro = typeof ResizeObserver !== "undefined" ? new ResizeObserver(() => scheduleScan()) : null;
    const io = typeof IntersectionObserver !== "undefined" ? new IntersectionObserver(() => scheduleScan(), { threshold: [0, 0.2, 0.6, 1] }) : null;
    const hook = (v) => { ro?.observe(v); io?.observe(v); v.addEventListener("loadedmetadata", scan, { passive: true }); v.addEventListener("play", scan, { passive: true }); };
    for (const v of document.querySelectorAll("video")) hook(v);
    mo.takeRecords(); // drain
    // Hook future videos via periodic lightweight scan (covers shadow roots added outside mutation attrs in some cases).
  }

  let scanTimer = 0;
  function scheduleScan() { if (!scanTimer) scanTimer = setTimeout(() => { scanTimer = 0; scan(); }, 180); }

  chrome.storage.onChanged.addListener((changes, area) => {
    if (area !== "local") return;
    if (changes.globalEnabled || changes.balloonEnabled || changes.autoIntercept || changes["site:" + pageHost()]) {
      loadSettings().then(scan);
    }
  });

  addEventListener("scroll", scheduleScan, { passive: true, capture: true });
  addEventListener("resize", scheduleScan, { passive: true });
  document.addEventListener("visibilitychange", scheduleScan, { passive: true });

  loadSettings().then(() => { scan(); observe(); setInterval(scan, 2500); });
})();
