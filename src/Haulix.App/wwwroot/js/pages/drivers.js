import { html, $ } from "../core/html.js";
import { icon } from "../core/icons.js";
import { store } from "../core/store.js";
import { empty, slots, driverStatus, drawer, kv, card } from "../components/ui.js";
import { noProfile, profitChart } from "./trucks.js";
import { cityName } from "../core/cities.js";
import * as f from "../core/format.js";

const SKILLS = [["adr", "ADR"], ["longDistance", "Long distance"], ["highValue", "High value cargo"], ["fragile", "Fragile cargo"], ["justInTime", "Just-in-time"], ["ecoDriving", "Eco driving"]];

export default {
  title: "Drivers",
  crumb: () => "AI drivers from your ETS2 company",

  render() {
    const p = store.get("profile");
    if (!p) return noProfile();
    const ai = p.drivers.filter((d) => !d.isPlayer);
    if (!ai.length) return empty({ brand: true, title: "No AI drivers hired", text: "Hire drivers at a recruitment agency in ETS2. HAULIX detects them from your save and tracks their earnings, distance and skills." });
    const byProfit = [...ai].sort((a, b) => b.profit.profit - a.profit.profit)[0];
    const byDist = [...ai].sort((a, b) => b.profit.distanceKm - a.profit.distanceKm)[0];
    const total = ai.reduce((a, d) => a + d.profit.profit, 0);
    const dist = ai.reduce((a, d) => a + d.profit.distanceKm, 0);
    const counts = ai.reduce((m, d) => ((m[d.status] = (m[d.status] || 0) + 1), m), {});
    return html`<div class="page-max stack">
      <div class="grid">
        ${card({ cls: "span-3 md-6", title: "Most profitable", muted: true, bare: true, body: html`<div class="leader"><a class="who link-muted" href="#/drivers/${encodeURIComponent(byProfit.id)}">${byProfit.name}</a><span class="num ok">${f.money(byProfit.profit.profit)}</span><span class="faint" style="font-size:12px">${byProfit.garageCity || ""}</span></div>` })}
        ${card({ cls: "span-3 md-6", title: "Most active", muted: true, bare: true, body: html`<div class="leader"><a class="who link-muted" href="#/drivers/${encodeURIComponent(byDist.id)}">${byDist.name}</a><span class="num">${f.dist(byDist.profit.distanceKm)}</span><span class="faint" style="font-size:12px">${f.num(byDist.profit.jobs)} jobs</span></div>` })}
        ${card({ cls: "span-3 md-6", title: "Total AI profit", muted: true, bare: true, body: html`<div class="stat"><div class="stat__value stat__value--lg ${total >= 0 ? "" : "crit"}">${f.money(total, { compact: true })}</div><div class="stat__label">${ai.length} drivers · revenue minus wages, fuel & maintenance</div></div>` })}
        ${card({ cls: "span-3 md-6", title: "Total AI distance", muted: true, bare: true, body: html`<div class="stat"><div class="stat__value stat__value--lg">${f.dist(dist)}</div><div class="stat__label">${f.num(ai.reduce((a, d) => a + d.profit.jobs, 0))} jobs completed</div></div>` })}
      </div>
      <div class="fleet-toolbar" style="margin:0">
        <div class="search" style="width:280px">${icon("search")}<input class="input" id="drSearch" placeholder="Filter drivers…"></div>
        <select class="select" id="drStatus" style="width:auto"><option value="">All statuses</option>
          ${[["driving", "Driving"], ["on_job", "On job"], ["resting", "Resting"], ["available", "Available"], ["unknown", "Unknown"]].map(([v, l]) => html`<option value="${v}">${l} (${counts[v] || 0})</option>`)}</select>
        <select class="select" id="drSort" style="width:auto"><option value="profit">Sort: Profit</option><option value="distance">Sort: Distance</option><option value="xp">Sort: Experience</option><option value="name">Sort: Name</option></select>
      </div>
      <section class="card"><div class="table-wrap" id="drTable"></div></section>
    </div>`;
  },

  mount(root, { params }) {
    const p = store.get("profile");
    const ai = p?.drivers.filter((d) => !d.isPlayer) || [];
    if (!ai.length) return;
    let q = "", status = "", sort = "profit";
    const draw = () => {
      const sorters = { profit: (a, b) => b.profit.profit - a.profit.profit, distance: (a, b) => b.profit.distanceKm - a.profit.distanceKm, xp: (a, b) => b.xp - a.xp, name: (a, b) => a.name.localeCompare(b.name) };
      const list = ai.filter((d) => (!status || d.status === status) && (!q || [d.name, d.garageCity, d.truckName].some((v) => v?.toLowerCase().includes(q)))).sort(sorters[sort]);
      $("#drTable", root).innerHTML = list.length ? html`<table class="table">
        <thead><tr><th>Driver</th><th>Status</th><th>Current job</th><th>Garage</th><th>Truck</th><th class="num">Skill</th><th class="num">Distance</th><th class="num">Revenue</th><th class="num">Profit</th></tr></thead>
        <tbody>${list.map((d) => html`<tr class="is-clickable" data-id="${d.id}">
          <td><strong>${d.name}</strong><span class="sub">${f.num(d.xp)} XP${d.specialization ? ` · ${d.specialization}` : ""}</span></td>
          <td>${driverStatus(d.status)}</td>
          <td class="ellipsis" style="max-width:240px">${d.job ? html`${d.job.cargo}<span class="sub">${d.job.sourceCity} → ${d.job.targetCity}</span>` : html`<span class="faint">—</span>`}</td>
          <td>${d.garageCity || "—"}</td>
          <td class="muted">${d.truckName || html`<span class="faint">No truck</span>`}</td>
          <td class="num">${totalSkill(d)}<span class="faint">/36</span></td>
          <td class="num">${f.dist(d.profit.distanceKm)}</td>
          <td class="num">${f.money(d.profit.revenue)}</td>
          <td class="num ${d.profit.profit >= 0 ? "ok" : "crit"}">${f.money(d.profit.profit)}</td></tr>`)}</tbody></table>`.toString()
        : empty({ iconName: "search", title: "No drivers match", compact: true }).toString();
    };
    draw();
    $("#drSearch", root).addEventListener("input", (e) => { q = e.target.value.trim().toLowerCase(); draw(); });
    $("#drStatus", root).addEventListener("change", (e) => { status = e.target.value; draw(); });
    $("#drSort", root).addEventListener("change", (e) => { sort = e.target.value; draw(); });
    root.addEventListener("click", (e) => { const r = e.target.closest("tr[data-id]"); if (r) openDriver(ai.find((d) => d.id === r.dataset.id), p); });
    if (params[0]) openDriver(ai.find((d) => d.id === params[0]), p);
  },
};

const totalSkill = (d) => SKILLS.reduce((a, [k]) => a + (d.skills?.[k] || 0), 0);

function openDriver(d, p) {
  if (!d) return;
  const margin = d.profit.revenue ? d.profit.profit / d.profit.revenue : null;
  drawer({
    wide: true,
    head: html`<span class="avatar avatar--lg"><img src="assets/brand/h-logo.png" alt=""></span>`,
    title: d.name,
    sub: html`${driverStatus(d.status)}<span>${d.garageCity ? `${d.garageCity} garage` : "No garage"}</span><span class="faint">${f.num(d.xp)} XP</span>`,
    body: html`
      <div class="detail-hero">
        <div class="stat"><div class="stat__label">Profit</div><div class="stat__value ${d.profit.profit >= 0 ? "ok" : "crit"}">${f.money(d.profit.profit)}</div></div>
        <div class="stat"><div class="stat__label">Revenue</div><div class="stat__value">${f.money(d.profit.revenue)}</div></div>
        <div class="stat"><div class="stat__label">Distance</div><div class="stat__value">${f.dist(d.profit.distanceKm)}</div></div>
        <div class="stat" data-tip="Profit as a share of revenue"><div class="stat__label">Margin</div><div class="stat__value">${margin === null ? "—" : f.pct(margin)}</div></div>
      </div>
      ${d.job ? html`<div class="callout callout--accent">${icon("package")}<div><strong>${d.job.cargo}</strong> · ${d.job.sourceCity} → ${d.job.targetCity}${d.job.plannedDistanceKm ? ` · ${f.dist(d.job.plannedDistanceKm)}` : ""}</div></div>` : ""}
      <div class="grid">
        <div class="span-6"><div class="label label--muted" style="margin-bottom:12px">Skills</div>
          <div class="skill-bars" style="grid-template-columns:1fr">${SKILLS.map(([k, l]) => html`<div class="skill"><div class="row" style="justify-content:space-between"><span class="muted">${l}</span><span class="num">${d.skills?.[k] || 0}/6</span></div>${slots(6, d.skills?.[k] || 0)}</div>`)}</div>
        </div>
        <div class="span-6"><div class="label label--muted" style="margin-bottom:8px">Details</div>${kv([
          ["Truck", d.truckName ? html`<a class="link" href="#/trucks/${encodeURIComponent(d.truckId)}">${d.truckName}</a>` : "No truck assigned", { icon: "truck", text: true }],
          ["Hometown", d.hometown ? cityName(d.hometown) : "—", { icon: "map-pin", text: true }],
          ["Current city", d.currentCity ? cityName(d.currentCity) : "—", { icon: "navigation", text: true }],
          ["Training focus", d.trainingPolicy || "—", { icon: "award", text: true }],
          ["Specialisation", d.specialization || "—", { icon: "star", text: true, tip: "Most frequently hauled cargo" }],
          ["Jobs", f.num(d.profit.jobs), { icon: "briefcase" }],
          ["Wages", f.money(d.profit.wage), { icon: "wallet" }],
          ["Fuel & maintenance", f.money((d.profit.fuel || 0) + (d.profit.maintenance || 0)), { icon: "fuel" }],
        ])}</div>
      </div>
      <div><div class="label label--muted" style="margin-bottom:10px">Revenue by game day</div><div id="drvChart"></div></div>`,
    onMount: (body) => {
      const c = profitChart(body.querySelector("#drvChart"), d.profit.days);
      return () => c?.destroy();
    },
  });
}
