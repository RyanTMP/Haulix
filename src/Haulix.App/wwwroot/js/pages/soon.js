import { html } from "../core/html.js";
import { icon } from "../core/icons.js";
import { store } from "../core/store.js";
import * as f from "../core/format.js";

// Areas that need an online HAULIX service (accounts, VTCs, live positions). This offline version shows
// what is coming, and says plainly that the feature is not available yet.
const PAGES = {
  vtc: {
    title: "Find a VTC", crumb: "Virtual trucking companies", icon: "handshake",
    text: "Later you will be able to browse virtual trucking companies here, compare them and apply to join. HAULIX works fully offline today, so your logbook, map and statistics keep running as usual.",
    points: [["search", "Search VTCs by language, region and play style"], ["handshake", "Send join requests from HAULIX"], ["chart-column", "Share your deliveries with your company"]],
  },
  "vtc-my": {
    title: "My VTC", crumb: "Your virtual trucking company", icon: "building-2",
    text: "Your company's home in HAULIX: members and roles, company statistics built from everyone's logbooks, and a shared company bank.",
    points: [["users", "Members, roles and invitations"], ["chart-line", "Company kilometres, income and deliveries"], ["wallet", "Company bank and payouts"]],
  },
  "vtc-events": {
    title: "Events & convoys", crumb: "Drive together", icon: "calendar",
    text: "Plan and join convoys: date and time, meeting point, route on the HAULIX map and the server to meet on – with reminders shortly before the start.",
    points: [["calendar", "Event calendar for your VTC and public convoys"], ["route", "Meeting point and route on the map"], ["bell", "Reminders over the game before the start"]],
  },
  "vtc-jobs": {
    title: "Job board", crumb: "Jobs posted by your VTC", icon: "briefcase",
    text: "Your VTC posts jobs – cargo, route and reward – and members take them. HAULIX checks the delivery automatically from telemetry.",
    points: [["briefcase", "Company jobs with cargo, route and reward"], ["circle-check", "Automatic proof of delivery"], ["coins", "Rewards credited to the company bank"]],
  },
  "vtc-leaderboards": {
    title: "Leaderboards", crumb: "Rankings", icon: "award",
    text: "Rankings within your VTC and across all HAULIX drivers: kilometres, deliveries, income and driving score – weekly, monthly and all time.",
    points: [["award", "Weekly, monthly and all-time rankings"], ["gauge", "Driving score as a fair ranking"], ["users", "Your VTC against other companies"]],
  },
  "live-map": {
    title: "Live map", crumb: "Friends and colleagues on the map", icon: "globe",
    text: "See your friends and VTC colleagues live on the HAULIX map – where they drive, what they haul and when they arrive.",
    points: [["globe", "Live positions of friends and your VTC"], ["package", "Their current job and arrival time"], ["navigation", "Navigate to a friend with one click"]],
  },
  "cloud-sync": {
    title: "Cloud sync", crumb: "Your logbook on every PC", icon: "cloud-off",
    text: "Optionally back up your logbook, statistics and achievements to your HAULIX account and use them on several PCs. Offline stays the default.",
    points: [["history", "Automatic backup of your logbook"], ["monitor", "Same data on every PC"], ["shield-check", "Opt-in – nothing leaves your PC without your consent"]],
  },
};

const page = (id) => PAGES[id] || PAGES.vtc;

// Developer preview: the same screens against the local sample backend (Haulix.Core/Online/SampleOnlineApi).
const table = (head, rows) => html`<section class="card"><div class="card__body card__body--flush"><div class="table-wrap"><table class="table">
  <thead><tr>${head.map((h) => html`<th class="${h.startsWith("#") ? "num" : ""}">${h.replace(/^#/, "")}</th>`)}</tr></thead>
  <tbody>${rows}</tbody></table></div></div></section>`;
const PREVIEW = {
  vtc: async (call) => {
    const list = await call("online.vtcs", {});
    return html`<div class="auto-grid" style="--min:300px">${list.map((v) => html`<section class="card"><div class="card__body">
      <div class="row" style="justify-content:space-between;align-items:flex-start"><div><div class="vtc-card__tag">${v.tag}</div><h3 class="vtc-card__name">${v.name}</h3></div>
        ${v.recruiting ? html`<span class="badge badge--ok">Recruiting</span>` : html`<span class="badge">Closed</span>`}</div>
      <p class="muted" style="margin:8px 0 14px">${v.description}</p>
      <div class="row" style="gap:18px;font-size:12px"><span>${icon("users", "icon icon-sm")} ${v.members}</span><span>${icon("globe", "icon icon-sm")} ${v.language.toUpperCase()} · ${v.region}</span><span class="num">${f.dist(v.totalKm)}</span></div>
      <button class="btn btn--sm" style="margin-top:14px" disabled>${icon("handshake")}Request to join</button></div></section>`)}</div>`;
  },
  "vtc-my": async (call) => {
    const members = await call("online.members", { vtcId: "vtc-nordlicht" });
    return table(["Member", "Role", "#Distance", "#Deliveries", "#Avg. score", "Status"], members.map((m) => html`<tr><td>${m.name}</td><td>${["Owner", "Manager", "Driver", "Trainee"][m.role] ?? m.role}</td>
      <td class="num">${f.dist(m.km)}</td><td class="num">${f.num(m.deliveries)}</td><td class="num">${f.num(m.avgScore, 1)}</td><td>${m.online ? html`<span class="badge badge--ok">Online</span>` : html`<span class="faint">Offline</span>`}</td></tr>`));
  },
  "vtc-jobs": async (call) => {
    const jobs = await call("online.jobs", { vtcId: "vtc-nordlicht" });
    return table(["Cargo", "Route", "#Distance", "#Reward", "Status"], jobs.map((j) => html`<tr><td>${j.cargo}</td><td>${j.fromCity} → ${j.toCity}</td>
      <td class="num">${f.dist(j.distanceKm)}</td><td class="num">${f.money(j.reward)}</td><td>${j.status === "open" ? html`<span class="badge badge--accent">Open</span>` : html`<span class="badge">Taken by ${j.takenBy}</span>`}</td></tr>`));
  },
  "vtc-events": async (call) => {
    const evs = await call("online.events", {});
    return html`<div class="stack">${evs.map((e) => html`<section class="card"><div class="card__body row" style="gap:18px;align-items:flex-start">
      <div class="event-date"><b>${new Date(e.startUtc).getDate()}</b><small>${f.dateTime(e.startUtc).split(" ").slice(1, 2)}</small></div>
      <div class="grow"><h3 class="vtc-card__name">${e.title}</h3><div class="muted" style="margin:4px 0">${f.dateTime(e.startUtc)} · ${e.server}</div>
        <div style="font-size:12px">${icon("map-pin", "icon icon-sm")} ${e.meetingPoint} · ${icon("route", "icon icon-sm")} ${e.route}</div></div>
      <span class="badge">${e.attendees} ${e.public ? "attending" : "members"}</span></div></section>`)}</div>`;
  },
  "vtc-leaderboards": async (call) => {
    const rows = await call("online.leaderboard", { metric: "km", period: "week" });
    return table(["#Rank", "Driver", "VTC", "#This week"], rows.map((r) => html`<tr><td class="num">${r.rank}</td><td>${r.name}</td><td>${r.vtcTag || "—"}</td><td class="num">${f.dist(r.value)}</td></tr>`));
  },
  "live-map": async (call) => {
    const pos = await call("online.live", { scope: "vtc" });
    return table(["Driver", "Cargo", "Destination", "#Speed"], pos.map((p) => html`<tr><td>${p.name}</td><td>${p.cargo || "—"}</td><td>${p.destinationCity || "—"}</td><td class="num">${f.speed(p.speedKmh)}</td></tr>`));
  },
};

export default {
  title: () => page(location.hash.replace(/^#\/?/, "").split("/")[0]).title,
  crumb: () => page(location.hash.replace(/^#\/?/, "").split("/")[0]).crumb,

  render({ pageId }) {
    const p = page(pageId);
    if (store.get("onlineSample") && PREVIEW[pageId]) {
      return html`<div class="page-max stack">
        <div class="callout callout--warn">${icon("sparkles")}<div><strong>Developer preview with sample data.</strong> The HAULIX online service is not running yet – nothing here is real and nothing is sent. Turn it off in Settings → Online.</div></div>
        <div id="previewData">${icon("refresh-cw")}</div>
      </div>`;
    }
    return html`<div class="page-max stack">
      <section class="card vtc-soon">
        <div class="card__body vtc-soon__body">
          <div class="vtc-soon__icon">${icon(p.icon)}</div>
          <span class="badge badge--outline">${icon("clock", "icon icon-sm")}Coming later</span>
          <h2 class="vtc-soon__title">${p.title}</h2>
          <p class="vtc-soon__lead">This feature is not available in the current HAULIX version.</p>
          <p class="muted vtc-soon__text">${p.text}</p>
          <div class="vtc-soon__points">${p.points.map(([i, t]) => html`<div>${icon(i)}<span>${t}</span></div>`)}</div>
        </div>
      </section>
    </div>`;
  },

  async mount(root, { pageId, call }) {
    const el = root.querySelector("#previewData");
    if (!el || !PREVIEW[pageId]) return;
    try { el.innerHTML = (await PREVIEW[pageId](call)).toString(); }
    catch (e) { el.innerHTML = html`<div class="callout callout--crit">${icon("circle-alert")}<div>${e.message}</div></div>`.toString(); }
  },
};
