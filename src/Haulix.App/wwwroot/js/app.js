import { html, raw, render, $, cx, esc, debounce } from "./core/html.js";
import { icon, loadIcons } from "./core/icons.js";
import { connect, call, on, isNative } from "./core/bridge.js";
import { store, pushHistory, isLive } from "./core/store.js";
import * as fmt from "./core/format.js";
import { initTooltips, toast, closeDrawer, modal } from "./components/ui.js";
import { loadCities } from "./core/cities.js";
import { setLanguage, resolveLanguage, startAutoTranslate, locale } from "./core/i18n.js";

const PAGES = {
  dashboard: () => import("./pages/dashboard.js"),
  telemetry: () => import("./pages/telemetry.js"),
  job: () => import("./pages/job.js"),
  logbook: () => import("./pages/logbook.js"),
  trucks: () => import("./pages/trucks.js"),
  trailers: () => import("./pages/trailers.js"),
  garages: () => import("./pages/garages.js"),
  drivers: () => import("./pages/drivers.js"),
  statistics: () => import("./pages/statistics.js"),
  profile: () => import("./pages/profile.js"),
  achievements: () => import("./pages/achievements.js"),
  vtc: () => import("./pages/soon.js"),
  "vtc-my": () => import("./pages/soon.js"),
  "vtc-events": () => import("./pages/soon.js"),
  "vtc-jobs": () => import("./pages/soon.js"),
  "vtc-leaderboards": () => import("./pages/soon.js"),
  "cloud-sync": () => import("./pages/soon.js"),
  settings: () => import("./pages/settings.js"),
  setup: () => import("./pages/setup.js"),
  design: () => import("./pages/design.js"),
};

const NAV = [
  { section: "Operate" },
  { id: "dashboard", label: "Dashboard", icon: "layout-dashboard", key: "1" },
  { id: "telemetry", label: "Telemetry", icon: "gauge", key: "2" },
  { id: "job", label: "Current job", icon: "briefcase", key: "3", dot: () => !!store.get("telemetry")?.snapshot?.onJob },
  { id: "logbook", label: "Logbook", icon: "book-open", key: "4", badge: () => store.get("counts")?.deliveries },
  { section: "Fleet" },
  { id: "trucks", label: "Trucks", icon: "truck", key: "5", badge: () => store.get("profile")?.trucks?.length },
  { id: "trailers", label: "Trailers", icon: "container", key: "6", badge: () => store.get("profile")?.trailers?.length },
  { id: "garages", label: "Garages", icon: "warehouse", key: "7", badge: () => store.get("profile")?.garages?.length },
  { id: "drivers", label: "Drivers", icon: "users", key: "8", badge: () => store.get("profile")?.drivers?.filter((d) => !d.isPlayer).length },
  { section: "Insight" },
  { id: "statistics", label: "Statistics", icon: "chart-column", key: "9" },
  { id: "achievements", label: "Achievements", icon: "award", badge: () => store.get("achievementCount") },
  { id: "profile", label: "Profile", icon: "id-card" },
  { section: "VTC" },
  { id: "vtc", label: "Find a VTC", icon: "handshake", soon: true },
  { id: "vtc-my", label: "My VTC", icon: "building-2", soon: true },
  { id: "vtc-events", label: "Events & convoys", icon: "calendar", soon: true },
  { id: "vtc-jobs", label: "Job board", icon: "briefcase", soon: true },
  { id: "vtc-leaderboards", label: "Leaderboards", icon: "award", soon: true },
  { section: "Online" },
  { id: "cloud-sync", label: "Cloud sync", icon: "cloud-off", soon: true },
  { section: "App" },
  { id: "settings", label: "Settings", icon: "settings" },
];

let current = { id: null, cleanup: null, token: 0 };

/* ------------------------------------------------------------------ boot */

async function boot() {
  initTooltips();
  await Promise.all([loadIcons(), connect(), loadCities()]);

  on("status", (s) => store.set("status", s));
  let onJob = null;
  on("telemetry", (t) => {
    pushHistory(t.snapshot);
    store.set("telemetry", t);
    if (!!t.snapshot?.onJob !== onJob) { onJob = !!t.snapshot?.onJob; updateNavBadges(); }
  });
  on("profile", (p) => store.set("profile", p));
  on("settings", (s) => applySettings(s));
  on("toast", (t) => toast(t));
  on("delivery", (d) => onDelivery(d));
  on("gameEvent", (e) => onGameEvent(e));
  on("job", (j) => toast({ kind: "info", title: "New job started", message: `${j.cargo} · ${j.from} → ${j.to} · ${fmt.money(j.income)}` }));
  on("backup", (b) => console.info("[haulix] automatic backup", b));
  // Job notifications (already in the UI language); the host also shows them over the game.
  // Job start / delivery / fines already have their own in-app toasts; progress and warnings are new.
  on("notify", (n) => n.category !== "job" && toast({ kind: n.kind === "critical" ? "error" : n.kind, title: n.title, message: n.message, timeout: n.kind === "critical" ? 9000 : 6000 }));
  on("achievements", (list) => { store.set("achievements", list); store.set("achievementCount", list.filter((a) => a.unlocked).length); });
  on("antiAfkSent", (a) => toast({ kind: "info", title: "Anti-AFK message sent", message: a.message, timeout: 4000 }));
  on("dataChanged", (d) => { refreshCounts(); store.set("dataChanged", d); });
  on("delivery", (d) => store.set("dataChanged", { scope: "logbook", id: d.id }));

  let init;
  try {
    init = await call("app.init");
  } catch (e) {
    showFatal(e);
    return;
  }
  // Language: explicit choice (settings/installer) → otherwise the Windows display language.
  setLanguage(resolveLanguage(init.settings.general.language, init.settings.general.languageChosen, init.systemLanguage));
  fmt.setPrefs({ locale: locale() });
  store.set("systemLanguage", init.systemLanguage);
  startAutoTranslate();
  // "0.0.3-devkit" → "0.0.3 DEVKIT" for display (the pre-release label marks developer builds).
  store.set("version", fmt.versionLabel(init.version));
  store.set("detection", init.detection);
  store.set("status", init.status);
  store.set("profile", init.profile);
  store.set("counts", init.counts);
  store.set("dataFolder", init.dataFolder);
  store.set("backupFolder", init.backupFolder);
  if (init.telemetry) store.set("telemetry", init.telemetry);
  applySettings(init.settings);

  renderShell();
  store.on("status", updateStatus);
  store.on("profile", () => { updateNavBadges(); updateProfileChip(); });
  store.on("counts", updateNavBadges);
  setInterval(updateFreshness, 1000);

  window.addEventListener("hashchange", route);
  window.addEventListener("resize", debounce(applyRail, 80));
  document.addEventListener("keydown", onShortcut);

  if (!init.settings.setupComplete && !location.hash.startsWith("#/setup")) location.hash = "#/setup";
  else if (!location.hash || location.hash === "#/") location.hash = `#/${init.settings.general.startPage === "last" ? (localGet("haulix.lastPage") || "dashboard") : "dashboard"}`;
  await route();

  call("achievements.get").then((list) => { store.set("achievements", list); store.set("achievementCount", list.filter((a) => a.unlocked).length); }).catch(() => {});
  checkForUpdate(init.settings);
  // "What's new" once per version (after setup; the first start after an update shows the changelog).
  if (init.settings.setupComplete && init.settings.general.lastSeenVersion !== init.version) {
    setTimeout(() => import("./core/changelog.js").then((m) => m.showChangelog()), 900);
    saveSettings((s) => (s.general.lastSeenVersion = init.version)).catch(() => {});
  }

  const splash = document.getElementById("splash");
  splash.classList.add("is-done");
  setTimeout(() => splash.remove(), 400);
  if (!isNative) toast({ kind: "info", title: "Preview mode", message: "Running in a browser with sample data. Launch Haulix.exe to read your ETS2 data.", timeout: 7000 });
}

/** Update check against the HAULIX releases on GitHub (no own server): on start and every 6 hours. */
let updateTimer = null, updateShownFor = null;
async function checkForUpdate(settings) {
  if (!isNative || settings.general.updateCheck === false) return;
  const run = async () => {
    const u = await call("update.check").catch(() => null);
    if (u?.available && updateShownFor !== u.latest) { updateShownFor = u.latest; showUpdate(u); }
  };
  await run();
  clearInterval(updateTimer);
  updateTimer = setInterval(run, 6 * 3600 * 1000);
}

/** "Update available" dialog: release notes, then download + start the new setup. */
export function showUpdate(u) {
  modal({
    title: `Update available: ${fmt.versionLabel(u.latest)}`,
    body: html`<p class="muted" style="margin:0 0 10px">You are using ${fmt.versionLabel(u.current)}. The new setup is downloaded from the HAULIX releases on GitHub and updates HAULIX in place – your logbook and settings are kept.</p>
      ${u.notes ? html`<pre class="release-notes">${u.notes}</pre>` : ""}
      <div class="update-progress hidden" id="updateProgress">${raw('<div class="progress progress--thick"><div class="progress__fill" style="width:0%"></div></div>')}<span class="faint" id="updatePct">0 %</span></div>`,
    actions: [
      { label: "Later", kind: "ghost", value: false },
      ...(u.url ? [{ label: "Release page", kind: "", onClick: () => { call("shell.openUrl", { url: u.url }).catch(() => {}); return false; } }] : []),
      ...(u.setupUrl ? [{ label: "Install now", kind: "primary", onClick: async (overlay) => {
        overlay.querySelector("#updateProgress")?.classList.remove("hidden");
        const offP = on("updateProgress", (p) => {
          const fill = overlay.querySelector(".progress__fill");
          if (fill) fill.style.width = `${p.pct}%`;
          const t = overlay.querySelector("#updatePct");
          if (t) t.textContent = p.starting ? "Starting setup…" : `${p.pct} %`;
        });
        const offE = on("updateError", (e) => { toast({ kind: "error", title: "Update failed", message: e.message }); offP?.(); offE?.(); });
        await call("update.install", { url: u.setupUrl });
        return false; // keep the dialog open while downloading; HAULIX closes when the setup starts
      } }] : []),
    ],
  });
}

function showFatal(e) {
  const splash = document.getElementById("splash");
  splash.innerHTML = html`<div class="splash__error">
    <img src="assets/brand/h-logo.png" alt="" width="54">
    <h1>HAULIX could not start</h1><p>${e.message}</p>
    <button class="btn" onclick="location.reload()">Retry</button></div>`.toString();
}

function localGet(k) { try { return localStorage.getItem(k); } catch { return null; } }
function localSet(k, v) { try { localStorage.setItem(k, v); } catch { /* storage unavailable */ } }
function sessionStorageGet(k) { try { return sessionStorage.getItem(k); } catch { return null; } }
function sessionStorageSet(k, v) { try { sessionStorage.setItem(k, v); } catch { /* storage unavailable */ } }

/* ------------------------------------------------------------------ settings */

export function applySettings(s) {
  store.set("settings", s);
  fmt.setPrefs({ units: s.general.units, currency: s.general.currency });
  const root = document.documentElement;
  root.dataset.theme = s.general.theme;
  root.dataset.accent = s.appearance.accent;
  root.dataset.compact = String(!!s.appearance.compact);
  root.dataset.animations = String(s.appearance.animations !== false);
  root.dataset.transparency = String(!!s.appearance.transparency);
  applyRail();
}

function applyRail() {
  const s = store.get("settings");
  // Below 1440px the rail is automatic unless the user expanded it for this session.
  const auto = window.innerWidth < 1440 && sessionStorageGet("haulix.expand") !== "1";
  const rail = !!s?.appearance.sidebarCollapsed || auto;
  document.documentElement.dataset.rail = String(rail);
  const btn = document.getElementById("railToggle");
  if (btn) {
    btn.dataset.tip = rail ? "Expand sidebar" : "Collapse sidebar";
    btn.innerHTML = icon(rail ? "panel-left-open" : "panel-left-close").toString();
  }
}

// Saves run one after another: each starts from the result of the previous one, so a slower
// earlier save can never overwrite a newer change (the host handles messages in parallel).
let saveQueue = Promise.resolve();
export function saveSettings(mutate) {
  const run = saveQueue.catch(() => {}).then(async () => {
    const s = structuredClone(store.get("settings"));
    mutate(s);
    const saved = await call("settings.save", { settings: s });
    applySettings(saved);
    return saved;
  });
  saveQueue = run;
  return run;
}

/** Switch the UI language immediately: saves the choice, then rebuilds the shell and the current page. */
export async function changeLanguage(value) {
  const saved = await saveSettings((s) => { s.general.language = value; s.general.languageChosen = value !== "auto"; });
  setLanguage(resolveLanguage(saved.general.language, saved.general.languageChosen, store.get("systemLanguage")));
  fmt.setPrefs({ locale: locale() });
  startAutoTranslate();
  current.cleanup?.();
  current.cleanup = null;
  renderShell();
  await route();
  return saved;
}

/* ------------------------------------------------------------------ shell */

function renderShell() {
  render(document.getElementById("app"), html`
    <div class="app">
      <aside class="sidebar">
        <a class="sidebar__brand" href="#/dashboard" aria-label="HAULIX dashboard">
          <img class="banner" src="assets/brand/banner.png" alt="HAULIX ETS2 Logger">
          <img class="mark" src="assets/brand/h-logo.png" alt="HAULIX">
        </a>
        <button class="sidebar__collapse" data-tip="Collapse sidebar" id="railToggle">${icon("panel-left-close")}</button>
        <nav class="nav" id="nav">
          ${NAV.map((n) => n.section
            ? html`<div class="nav__section">${n.section}</div>`
            : html`<a class="nav__item" href="#/${n.id}" data-nav="${n.id}" ${raw(`data-tip-rail="${esc(n.label)}"`)}>${icon(n.icon)}<span>${n.label}</span>${n.soon ? html`<em class="nav__soon">Soon</em>` : html`<em class="nav__badge" data-badge="${n.id}"></em>`}</a>`)}
        </nav>
        <div class="sidebar__status" id="sidebarStatus"></div>
      </aside>
      <div class="main">
        <div class="online-notice" id="onlineNotice" role="status">
          <span class="online-notice__icon">${icon("triangle-alert")}</span>
          <span class="online-notice__text"><b>HAULIX is preparing to go ONLINE.</b> Updates now come more frequently and may add features that are not available yet and do not represent the final version.</span>
        </div>
        <header class="topbar">
          <div class="topbar__title"><h1 id="pageTitle">Dashboard</h1><span class="topbar__crumb" id="pageCrumb"></span></div>
          <div class="topbar__actions">
            <div class="search" style="width:260px">
              ${icon("search")}<input class="input" id="globalSearch" placeholder="Search trucks, drivers, cities…" autocomplete="off"><kbd>Ctrl K</kbd>
            </div>
            <div id="freshChip"></div>
            <a class="profile-chip" href="#/profile" id="profileChip"></a>
          </div>
        </header>
        <main class="page" id="page"></main>
      </div>
    </div>`);

  $("#railToggle").onclick = () => {
    const rail = document.documentElement.dataset.rail === "true";
    if (window.innerWidth < 1440) {
      sessionStorageSet("haulix.expand", rail ? "1" : "0");
      if (rail && store.get("settings").appearance.sidebarCollapsed) saveSettings((s) => (s.appearance.sidebarCollapsed = false));
      else applyRail();
    } else {
      saveSettings((s) => (s.appearance.sidebarCollapsed = !rail));
    }
  };
  applyRail();
  // In rail mode the nav labels are hidden: show them as tooltips instead.
  document.querySelectorAll("[data-tip-rail]").forEach((a) => {
    a.addEventListener("mouseenter", () => { a.dataset.tip = document.documentElement.dataset.rail === "true" ? a.dataset.tipRail : ""; });
  });
  setupSearch();
  updateStatus(store.get("status"));
  updateNavBadges();
  updateProfileChip();
}

function statusRows(s) {
  if (!s) return [];
  const game = { running: ["ok", "Running"], notRunning: ["", "Not running"], notDetected: ["crit", "Not found"] }[s.game] || ["", "—"];
  const tel = {
    live: ["ok", "Live"], demo: ["accent", "Demo"], paused: ["warn", "Paused"], waiting: ["warn", "Waiting"],
    stale: ["warn", "Stale"], unsupported: ["crit", "Unsupported"], unavailable: ["", s.pluginInstalled ? "Offline" : "No plugin"],
  }[s.telemetry] || ["", "—"];
  const prof = { loaded: ["ok", "Loaded"], cached: ["warn", "Cached"], loading: ["warn", "Loading"], error: ["crit", "Error"], none: ["", "None"] }[s.profile?.state] || ["", "—"];
  const db = s.db?.ok ? ["ok", "OK"] : ["crit", "Error"];
  return [
    ["ETS2", game, s.game === "notDetected" ? "ETS2 installation not found — set the path in Settings → ETS2" : s.gameVersion ? `Game running` : ""],
    ["Telemetry", tel, s.telemetry === "unsupported" ? `Plugin revision ${s.pluginRevision} is not supported` : s.pluginInstalled ? "scs-telemetry plugin" : "scs-telemetry.dll not installed"],
    ["Profile", prof, s.profile?.error || (s.profile?.name ? `${s.profile.name} · ${s.profile.saveName || ""}` : "")],
    ["Database", db, s.db?.error || `${fmt.bytes(s.db?.sizeBytes)} · local SQLite`],
  ];
}

function updateStatus(s) {
  const el = $("#sidebarStatus");
  if (!el || !s) return;
  render(el, html`
    <dl class="status-grid">
      ${statusRows(s).map(([k, [tone, label], tip]) => html`<dt>${k}</dt><dd data-tip="${tip}"><span class="${cx("dot", tone && `dot--${tone}`, k === "Telemetry" && (s.telemetry === "live" || s.telemetry === "demo") && "dot--live")}"></span><span>${label}</span></dd>`)}
    </dl>
    <div class="sidebar__meta"><span>v${store.get("version")}</span><span id="lastUpdate"></span></div>`);
  updateFreshness();
}

function updateFreshness() {
  const s = store.get("status");
  const chip = $("#freshChip");
  if (!chip || !s) return;
  const last = s.lastSampleUtc;
  let cls = "chip", dot = "dot", label, detail;
  if (s.telemetry === "demo") { cls += " chip--demo"; dot += " dot--accent dot--live"; label = "DEMO"; detail = "simulated drive"; }
  else if (s.telemetry === "live") { cls += " chip--live"; dot += " dot--ok dot--live"; label = "LIVE"; detail = "telemetry"; }
  else if (s.telemetry === "paused") { dot += " dot--warn"; label = "PAUSED"; detail = "game paused"; }
  else if (s.game === "running") { dot += " dot--warn"; label = "WAITING"; detail = s.pluginInstalled ? "for telemetry" : "no plugin"; }
  else { label = "OFFLINE"; detail = last ? `last seen ${fmt.ago(last)}` : "ETS2 not running"; }
  const htmlStr = html`<span class="${cls}" data-tip="${last ? `Last telemetry update ${fmt.dateTime(last)}` : "No telemetry received yet"}"><span class="${dot}"></span><strong>${label}</strong>${detail}</span>`.toString();
  if (chip.innerHTML !== htmlStr) chip.innerHTML = htmlStr;
  const lu = $("#lastUpdate");
  if (lu) lu.textContent = s.profile?.parsedUtc ? `Synced ${fmt.ago(s.profile.parsedUtc)}` : "";
}

function updateNavBadges() {
  for (const n of NAV) {
    if (n.dot) { document.querySelector(`[data-badge="${n.id}"]`)?.classList.toggle("nav__badge--live", !!n.dot()); continue; }
    if (!n.badge) continue;
    const el = document.querySelector(`[data-badge="${n.id}"]`);
    if (!el) continue;
    const v = n.badge();
    el.textContent = v ? fmt.num(v) : "";
  }
}

function updateProfileChip() {
  const el = $("#profileChip");
  if (!el) return;
  const p = store.get("profile");
  render(el, p
    ? html`<span class="avatar"><img src="assets/brand/h-logo.png" alt=""></span>
        <span class="profile-chip__text"><strong class="ellipsis">${p.companyName || p.profileName}</strong><span>${fmt.num(p.xp)} XP · ${fmt.money(p.money, { compact: true })}</span></span>`
    : html`<span class="avatar"><img src="assets/brand/h-logo.png" alt=""></span><span class="profile-chip__text"><strong>No profile</strong><span>Select in setup</span></span>`);
}

async function refreshCounts() {
  try { store.set("counts", await call("data.counts")); } catch { /* ignore */ }
}

/* ------------------------------------------------------------------ routing */

async function route() {
  const hash = location.hash.replace(/^#\/?/, "");
  const [id, ...rest] = hash.split("/");
  const pageId = PAGES[id] ? id : "dashboard";
  const token = ++current.token;
  closeDrawer();
  current.cleanup?.();
  current.cleanup = null;

  document.querySelectorAll("[data-nav]").forEach((a) => a.classList.toggle("is-active", a.dataset.nav === pageId));
  const mod = (await PAGES[pageId]()).default;
  if (token !== current.token) return;
  // Fresh container per page: listeners a page attached to its root must not leak into the next page.
  const old = $("#page");
  const page = old.cloneNode(false);
  old.replaceWith(page);

  const title = typeof mod.title === "function" ? mod.title() : mod.title;
  $("#pageTitle").textContent = title;
  $("#pageCrumb").textContent = typeof mod.crumb === "function" ? mod.crumb() : mod.crumb || "";
  page.className = cx("page", mod.flush && "page--flush");
  page.scrollTop = 0;
  const ctx = { pageId, params: rest.map(decodeURIComponent), call, store, fmt, setCrumb: (t) => ($("#pageCrumb").textContent = t) };
  render(page, mod.render(ctx));
  page.firstElementChild?.classList.add("page-enter");
  try {
    current.cleanup = (await mod.mount?.(page, ctx)) || null;
  } catch (e) {
    console.error(e);
    toast({ kind: "error", title: `${title} failed to load`, message: e.message });
  }
  if (pageId !== "setup") localSet("haulix.lastPage", pageId);
  call("window.setTitle", { title: `HAULIX — ${title}` }).catch(() => {});
}

/* ------------------------------------------------------------------ events */

function onDelivery(d) {
  const r = d.row?.delivery;
  if (!r) return;
  if (d.status === "delivered") {
    toast({ kind: "success", title: `Delivered: ${r.cargo}`, message: `${r.originCity} → ${r.destCity} · ${fmt.money(r.income)} · ${fmt.num(r.xp)} XP`, timeout: 8000 });
  } else {
    toast({ kind: "warning", title: "Job cancelled", message: `${r.cargo} · penalty ${fmt.money(r.penalty)}` });
  }
}

function onGameEvent(e) {
  const titles = { fine: "Fined", toll: "Toll paid", ferry: "Ferry", train: "Train", refuel: "Refuelled" };
  toast({ kind: e.type === "fine" ? "warning" : "info", title: titles[e.type] || e.type, message: [e.detail, e.amount ? fmt.money(e.amount) : ""].filter(Boolean).join(" · "), timeout: 4000 });
}

function onShortcut(e) {
  if (e.ctrlKey && e.key.toLowerCase() === "k") { e.preventDefault(); $("#globalSearch")?.focus(); return; }
  if (e.ctrlKey && e.key === ",") { e.preventDefault(); location.hash = "#/settings"; return; }
  if (e.altKey && /^[1-9]$/.test(e.key)) {
    const n = NAV.find((x) => x.key === e.key);
    if (n) { e.preventDefault(); location.hash = `#/${n.id}`; }
  }
}

/* ------------------------------------------------------------------ global search */

function setupSearch() {
  const input = $("#globalSearch");
  let box = null;
  const close = () => { box?.remove(); box = null; };
  const results = (q) => {
    const p = store.get("profile");
    const out = [];
    const m = (s) => s && s.toLowerCase().includes(q);
    for (const t of p?.trucks || []) if (m(t.name) || m(t.licensePlate) || m(t.garageCity)) out.push({ icon: "truck", label: t.name, sub: `${t.licensePlate || ""} · ${t.garageCity || "No garage"}`, href: `#/trucks/${encodeURIComponent(t.id)}` });
    for (const d of p?.drivers || []) if (!d.isPlayer && (m(d.name) || m(d.garageCity))) out.push({ icon: "user-round", label: d.name, sub: d.garageCity || "", href: `#/drivers/${encodeURIComponent(d.id)}` });
    for (const g of p?.garages || []) if (m(g.city) || m(g.country)) out.push({ icon: "warehouse", label: `${g.city} garage`, sub: g.country || "", href: `#/garages/${encodeURIComponent(g.id)}` });
    for (const r of p?.trailers || []) if (m(r.name) || m(r.licensePlate)) out.push({ icon: "container", label: r.name, sub: r.licensePlate || "", href: `#/trailers/${encodeURIComponent(r.id)}` });
    out.push({ icon: "book-open", label: `Search logbook for “${q}”`, sub: "Deliveries, cargo and cities", href: `#/logbook/q/${encodeURIComponent(q)}` });
    return out.slice(0, 9);
  };
  const show = () => {
    const q = input.value.trim().toLowerCase();
    if (!q) return close();
    const items = results(q);
    if (!box) {
      box = document.createElement("div");
      box.className = "menu search-results";
      document.body.append(box);
    }
    const r = input.getBoundingClientRect();
    box.style.left = `${r.left}px`;
    box.style.top = `${r.bottom + 6}px`;
    box.style.width = `${Math.max(r.width, 340)}px`;
    box.innerHTML = items.map((it, i) => html`<button data-href="${it.href}" class="${i === 0 ? "is-hl" : ""}">${icon(it.icon)}<span class="grow ellipsis">${it.label}<span class="faint" style="margin-left:8px">${it.sub}</span></span></button>`.toString()).join("");
    box.querySelectorAll("button").forEach((b) => b.onmousedown = (ev) => { ev.preventDefault(); location.hash = b.dataset.href; input.value = ""; close(); input.blur(); });
  };
  input.addEventListener("input", show);
  input.addEventListener("focus", show);
  input.addEventListener("blur", () => setTimeout(close, 120));
  input.addEventListener("keydown", (e) => {
    if (e.key === "Escape") { input.value = ""; close(); input.blur(); }
    if (!box) return;
    const btns = [...box.querySelectorAll("button")];
    let i = btns.findIndex((b) => b.classList.contains("is-hl"));
    if (e.key === "ArrowDown" || e.key === "ArrowUp") {
      e.preventDefault();
      btns[i]?.classList.remove("is-hl");
      i = (i + (e.key === "ArrowDown" ? 1 : -1) + btns.length) % btns.length;
      btns[i].classList.add("is-hl");
    }
    if (e.key === "Enter" && btns[i]) { location.hash = btns[i].dataset.href; input.value = ""; close(); input.blur(); }
  });
}

export { isLive };

boot();
