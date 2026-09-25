import { html, cx, $, debounce } from "../core/html.js";
import { icon } from "../core/icons.js";
import { store } from "../core/store.js";
import { card, empty, skeleton, select, drawer, kv, toast, contextMenu, progress, damageTone } from "../components/ui.js";
import { chart } from "../components/charts.js";
import * as f from "../core/format.js";
import { companyLogo, cargoIcon, logoChip } from "../core/logos.js";
import { deliveryCard, scoreColor } from "../components/sharecard.js";

const SORTS = [["recent", "Most recent"], ["oldest", "Oldest"], ["longest", "Longest"], ["income", "Highest income"], ["xp", "Highest XP"], ["efficiency", "Most fuel efficient"]];
const PAGE = 50;

export default {
  title: "Logbook",
  crumb: () => "Every delivery, stored locally",

  render() {
    return html`<div class="page-max">
      <div class="filters" id="filters">
        <div class="search">${icon("search")}<input class="input" name="search" placeholder="Search cargo, city, company…"></div>
        <input class="input" type="date" name="from" data-tip="From date">
        <input class="input" type="date" name="to" data-tip="To date">
        <span id="facetSelects"></span>
        <input class="input" type="number" name="minDistance" placeholder="Min ${f.distUnit()}" style="width:110px;min-width:0">
        <input class="input" type="number" name="minIncome" placeholder="Min ${f.currencySymbol()}" style="width:110px;min-width:0">
        ${select("sort", SORTS, "recent")}
        <button class="btn btn--ghost btn--sm" id="resetFilters">${icon("rotate-ccw")}Reset</button>
        <span class="spacer"></span>
        <button class="btn btn--sm" id="exportCsv">${icon("download")}Export CSV</button>
      </div>
      ${card({ bodyCls: "card__body--flush", body: html`<div id="totals"></div><div id="table">${skeleton(6)}</div>`, foot: html`<div class="pager grow" id="pager"></div>` })}
    </div>`;
  },

  async mount(root, { call, params, setCrumb }) {
    const filter = { sort: "recent", offset: 0, limit: PAGE };
    let facetsRendered = false;
    const fEl = $("#filters", root);

    if (params[0] === "q" && params[1]) { filter.search = params[1]; fEl.querySelector("[name=search]").value = params[1]; }
    if (params[0] === "city" && params[1]) filter.city = params[1];

    const load = async () => {
      const t = $("#table", root);
      t.style.opacity = "0.6";
      let res;
      try {
        res = await call("logbook.query", { filter: { ...filter, minDistance: filter.minDistance ? filter.minDistance / (f.imperial() ? 0.621371 : 1) : null } });
      } catch (e) {
        t.innerHTML = empty({ error: true, title: "Logbook unavailable", text: e.message, compact: true }).toString();
        return;
      } finally {
        t.style.opacity = "";
      }
      if (!facetsRendered) renderFacets(res.facets);
      renderTotals(res.totals);
      renderTable(res.rows);
      renderPager(res.totals.count);
      setCrumb(`${f.num(res.totals.count)} ${res.totals.count === 1 ? "delivery" : "deliveries"} · stored locally`);
    };

    const renderFacets = (fc) => {
      facetsRendered = true;
      $("#facetSelects", root).innerHTML = html`
        ${select("truck", [["", "All trucks"], ...fc.trucks.map((x) => [x, x])], filter.truck)}
        ${select("cargo", [["", "All cargo"], ...fc.cargo.map((x) => [x, x])], filter.cargo)}
        ${select("country", [["", "All countries"], ...fc.countries.map((c) => [c.code, c.name])], filter.country)}
        ${select("city", [["", "All cities"], ...fc.cities.map((x) => [x, x])], filter.city)}
        ${select("status", [["", "Any status"], ["delivered", "Delivered"], ["cancelled", "Cancelled"]], filter.status)}
        ${select("source", [["", "All sources"], ["telemetry", "Recorded live"], ["save", "Imported from save"]], filter.source)}`.toString();
    };

    const renderTotals = (t) => {
      $("#totals", root).innerHTML = html`<div class="totals-bar">
        <span>Deliveries<b>${f.num(t.count)}</b></span><span>Distance<b>${f.dist(t.distanceKm)}</b></span>
        <span>Income<b>${f.money(t.income)}</b></span><span>XP<b>${f.num(t.xp)}</b></span>
        <span>Fuel used<b>${f.volume(t.fuelL)}</b></span>
        <span>Avg income<b>${t.distanceKm ? `${f.money(t.income / f.distValue(t.distanceKm))}/${f.distUnit()}` : "—"}</b></span></div>`.toString();
    };

    const renderTable = (rows) => {
      const el = $("#table", root);
      if (!rows.length) {
        const filtered = Object.entries(filter).some(([k, v]) => !["sort", "offset", "limit"].includes(k) && v);
        el.innerHTML = empty(filtered
          ? { iconName: "filter", title: "No matching deliveries", text: "Try widening the date range or clearing filters.", compact: true }
          : { brand: true, title: "Your logbook is empty", text: "Finish a job in ETS2 and it appears here with route, fuel, speed and damage. History from your save file is imported automatically." }).toString();
        return;
      }
      el.innerHTML = html`<div class="table-wrap" style="max-height:calc(100vh - 290px)"><table class="table">
        <thead><tr>
          <th>Date</th><th>Route</th><th>Cargo</th><th>Truck</th>
          <th class="num sortable ${filter.sort === "longest" ? "is-sorted" : ""}" data-sort="longest">Distance</th>
          <th class="num sortable ${filter.sort === "income" ? "is-sorted" : ""}" data-sort="income">Income</th>
          <th class="num sortable ${filter.sort === "xp" ? "is-sorted" : ""}" data-sort="xp">XP</th>
          <th class="num sortable ${filter.sort === "efficiency" ? "is-sorted" : ""}" data-sort="efficiency">Fuel</th>
          <th class="num">Damage</th><th class="num" data-tip="Driving score 0–100: speeding, damage, fines and punctuality">Score</th><th>Status</th>
        </tr></thead>
        <tbody>${rows.map((r) => html`<tr class="is-clickable" data-id="${r.id}">
          <td><span class="num">${r.finishedUtc ? f.dateTime(r.finishedUtc) : f.gameDay(r.gameEndMin)}</span>${r.source === "save" ? html`<span class="source-tag" data-tip="Imported from the in-game delivery log">save</span>` : ""}</td>
          <td><span class="route">${r.originCity}${icon("arrow-right")}${r.destCity}</span><span class="sub">${r.originCompany} → ${r.destCompany}</span></td>
          <td style="max-width:230px"><div class="with-logo">${cargoIcon(r.cargoId, r.cargo, "sm")}<div class="ellipsis">${r.cargo}<span class="sub">${r.cargoMassKg ? f.mass(r.cargoMassKg) : ""}</span></div></div></td>
          <td class="ellipsis muted" style="max-width:160px">${r.truck || "—"}</td>
          <td class="num">${f.dist(r.distanceKm)}</td>
          <td class="num ${r.status === "delivered" ? "ok" : ""}">${r.status === "delivered" ? f.money(r.income) : html`<span class="crit">−${f.money(r.penalty)}</span>`}</td>
          <td class="num">${f.num(r.xp)}</td>
          <td class="num muted">${r.fuelUsedL ? f.volume(r.fuelUsedL) : "—"}</td>
          <td class="num ${r.cargoDamage > 0.05 ? "warn" : "muted"}">${r.cargoDamage !== null ? f.pct(r.cargoDamage, 1) : "—"}</td>
          <td class="num">${r.score != null ? html`<span class="score-pill" style="--c:${scoreColor(r.score)}">${r.score}</span>` : html`<span class="faint">—</span>`}</td>
          <td>${r.status === "delivered" ? html`<span class="badge badge--ok">Delivered</span>` : html`<span class="badge badge--warn">Cancelled</span>`}</td>
        </tr>`)}</tbody></table></div>`.toString();
    };

    const renderPager = (count) => {
      const page = Math.floor(filter.offset / PAGE) + 1;
      const pages = Math.max(1, Math.ceil(count / PAGE));
      $("#pager", root).innerHTML = html`<span>${f.num(Math.min(count, filter.offset + 1))}–${f.num(Math.min(count, filter.offset + PAGE))} of ${f.num(count)}</span>
        <button class="btn btn--sm btn--icon" data-page="-1" ${page <= 1 ? "disabled" : ""}>${icon("chevron-left")}</button>
        <span class="num">${page} / ${pages}</span>
        <button class="btn btn--sm btn--icon" data-page="1" ${page >= pages ? "disabled" : ""}>${icon("chevron-right")}</button>`.toString();
    };

    const onChange = debounce((e) => {
      const el = e.target;
      if (!el.name) return;
      const v = el.value.trim();
      filter[el.name] = el.type === "number" ? (v ? +v : null) : v || null;
      if (el.name === "sort") filter.sort = v || "recent";
      filter.offset = 0;
      load();
    }, 220);
    fEl.addEventListener("input", onChange);
    fEl.addEventListener("change", onChange);
    $("#resetFilters", root).onclick = () => {
      for (const k of Object.keys(filter)) if (!["sort", "offset", "limit"].includes(k)) delete filter[k];
      filter.sort = "recent"; filter.offset = 0;
      fEl.querySelectorAll("input").forEach((i) => (i.value = ""));
      fEl.querySelectorAll("select").forEach((s) => (s.selectedIndex = 0));
      load();
    };
    $("#exportCsv", root).onclick = async () => {
      try {
        const path = await call("data.exportCsv");
        if (path) toast({ kind: "success", title: "Logbook exported", message: path });
      } catch (e) { toast({ kind: "error", title: "Export failed", message: e.message }); }
    };

    root.addEventListener("click", (e) => {
      const th = e.target.closest("[data-sort]");
      if (th) { filter.sort = filter.sort === th.dataset.sort ? "recent" : th.dataset.sort; fEl.querySelector("[name=sort]").value = filter.sort; filter.offset = 0; load(); return; }
      const pg = e.target.closest("[data-page]");
      if (pg) { filter.offset = Math.max(0, filter.offset + PAGE * +pg.dataset.page); load(); return; }
      const tr = e.target.closest("tr[data-id]");
      if (tr) openDelivery(+tr.dataset.id, call);
    });
    root.addEventListener("contextmenu", (e) => {
      const tr = e.target.closest("tr[data-id]");
      if (!tr) return;
      const id = +tr.dataset.id;
      const cells = tr.querySelectorAll("td");
      contextMenu(e, [
        { label: "Open details", icon: "external-link", onClick: () => openDelivery(id, call) },
        { label: "Filter by this cargo", icon: "filter", onClick: () => { filter.cargo = cells[2].childNodes[0].textContent.trim(); facetsRendered = false; filter.offset = 0; load(); } },
        "-",
        { label: "Copy route", icon: "copy", onClick: () => navigator.clipboard?.writeText(cells[1].querySelector(".route").textContent) },
      ]);
    });

    await load();
    if (params[0] && /^\d+$/.test(params[0])) openDelivery(+params[0], call);
    const off = store.on("dataChanged", () => load());
    return () => off();
  },
};

function scoreBlock(d) {
  if (d.score == null) return d.status === "delivered" && d.source === "telemetry" ? "" : "";
  let x = {};
  try { x = JSON.parse(d.scoreDetail || "{}"); } catch { /* old rows */ }
  const rows = [
    ["Speeding", x.speedingPenalty, x.speedingPct != null ? `${f.num(x.speedingPct, 1)} % of driving time` : ""],
    ["Cargo damage", x.cargoPenalty, x.cargoDamagePct != null ? `${f.num(x.cargoDamagePct, 1)} %` : ""],
    ["Truck damage", x.truckPenalty, x.truckDamagePct != null ? `+${f.num(x.truckDamagePct, 1)} %` : ""],
    ["Fines", x.finePenalty, x.fines != null ? f.num(x.fines) : ""],
    ["Late delivery", x.latePenalty, x.late ? "yes" : "no"],
  ];
  return html`<div class="score-card">
    <div class="score-ring" style="--c:${scoreColor(d.score)};--p:${d.score}"><span>${d.score}</span><small>/100</small></div>
    <div class="grow"><div class="label label--muted" style="margin-bottom:8px">Driving score</div>
      ${rows.map(([l, pen, info]) => html`<div class="score-row"><span class="muted">${l}</span><span class="faint">${info}</span><span class="num ${pen ? "crit" : "ok"}">${pen ? `−${pen}` : "0"}</span></div>`)}
    </div></div>`;
}

async function openDelivery(id, call) {
  let res;
  try {
    res = await call("logbook.get", { id });
  } catch (e) {
    toast({ kind: "error", title: "Could not open delivery", message: e.message });
    return;
  }
  if (!res) return;
  const d = res.delivery;
  const pts = res.points || [];
  drawer({
    wide: true,
    title: html`${d.originCity} <span class="accent">→</span> ${d.destCity}`,
    sub: html`${d.status === "delivered" ? html`<span class="badge badge--ok">Delivered</span>` : html`<span class="badge badge--warn">Cancelled</span>`}
      <span>${d.cargo}</span><span class="faint">${d.finishedUtc ? f.dateTime(d.finishedUtc) : `${f.gameDay(d.gameEndMin)} (in-game)`}</span>
      ${d.source === "save" ? html`<span class="source-tag">Imported from save</span>` : ""}`,
    body: html`
      <div class="row" style="gap:8px;justify-content:flex-end;margin-bottom:-6px">
        <button class="btn btn--sm" id="shareSave">${icon("download")}Save share card</button>
        <button class="btn btn--sm btn--ghost" id="shareCopy">${icon("copy")}Copy image</button>
      </div>
      <div class="detail-hero">
        <div class="stat"><div class="stat__label">Income</div><div class="stat__value ${d.status === "delivered" ? "ok" : "crit"}">${d.status === "delivered" ? f.money(d.income) : `−${f.money(d.penalty)}`}</div></div>
        <div class="stat"><div class="stat__label">Distance</div><div class="stat__value">${f.dist(d.distanceKm)}</div></div>
        <div class="stat"><div class="stat__label">XP</div><div class="stat__value">${f.num(d.xp)}</div></div>
        <div class="stat"><div class="stat__label">${f.currencySymbol()} / ${f.distUnit()}</div><div class="stat__value">${d.distanceKm ? f.num(d.income / f.distValue(d.distanceKm), 1) : "—"}</div></div>
      </div>
      ${pts.length > 5 ? html`<div><div class="label label--muted" style="margin-bottom:10px">Speed profile</div><div id="speedProfile"></div></div>` : ""}
      <div class="grid">
        <div class="span-6">${kv([
          ["Origin", html`<span class="with-logo with-logo--end">${logoChip(companyLogo(d.originCompany), d.originCompany || "", "sm")}<span>${d.originCity} <span class="faint">· ${d.originCompany || ""}</span></span></span>`, { icon: "map-pin", text: true }],
          ["Destination", html`<span class="with-logo with-logo--end">${logoChip(companyLogo(d.destCompany), d.destCompany || "", "sm")}<span>${d.destCity} <span class="faint">· ${d.destCompany || ""}</span></span></span>`, { icon: "flag", text: true }],
          ["Cargo", html`<span class="with-logo with-logo--end">${cargoIcon(d.cargoId, d.cargo, "xs")}<span>${d.cargo}</span></span>`, { icon: "package", text: true }],
          ["Cargo mass", d.cargoMassKg ? f.mass(d.cargoMassKg) : "—", { icon: "weight" }],
          ["Truck", d.truck || "—", { icon: "truck", text: true }],
          ["Trailer", d.trailer || "—", { icon: "container", text: true }],
          ["Driver", d.driver === "Player" ? "You" : d.driver || "—", { icon: "user-round", text: true }],
        ])}</div>
        <div class="span-6">${kv([
          ["Planned distance", d.plannedKm ? f.dist(d.plannedKm) : "—", { icon: "route" }],
          ["Fuel used", d.fuelUsedL ? f.volume(d.fuelUsedL, 1) : "—", { icon: "fuel" }],
          ["Consumption", d.fuelUsedL && d.distanceKm ? f.consumption(d.fuelUsedL / d.distanceKm) : "—", { icon: "activity" }],
          ["Average speed", d.avgSpeedKmh ? f.speed(d.avgSpeedKmh) : "—", { icon: "gauge" }],
          ["Top speed", d.maxSpeedKmh ? f.speed(d.maxSpeedKmh) : "—", { icon: "zap" }],
          ["Driving time", d.driveSeconds ? f.duration(d.driveSeconds) : "—", { icon: "timer", tip: "Real time spent driving" }],
          ["In-game duration", d.gameMinutes ? f.duration(d.gameMinutes * 60, { short: true }) : "—", { icon: "clock" }],
        ])}</div>
      </div>
      ${scoreBlock(d)}
      <div>
        <div class="label label--muted" style="margin-bottom:12px">Damage</div>
        <div class="wear-row"><span class="muted">Cargo</span>${progress(Math.max(d.cargoDamage || 0, 0.004), { tone: damageTone(d.cargoDamage || 0), thin: true })}<span class="v">${f.pct(d.cargoDamage || 0, 1)}</span></div>
        ${d.truckDamage !== null && d.truckDamage !== undefined ? html`<div class="wear-row"><span class="muted">Truck (at finish)</span>${progress(Math.max(d.truckDamage, 0.004), { tone: damageTone(d.truckDamage), thin: true })}<span class="v">${f.pct(d.truckDamage, 1)}</span></div>` : ""}
      </div>`,
    onMount: (body) => {
      const cleanup = [];
      const card = () => deliveryCard(d);
      body.querySelector("#shareSave")?.addEventListener("click", async () => {
        try {
          const path = await call("image.save", { dataUrl: await card(), name: `haulix-${(d.originCity || "").toLowerCase()}-${(d.destCity || "").toLowerCase()}.png`.replace(/\s+/g, "-") });
          if (path) toast({ kind: "success", title: "Share card saved", message: path });
        } catch (e) { toast({ kind: "error", title: "Could not save image", message: e.message }); }
      });
      body.querySelector("#shareCopy")?.addEventListener("click", async () => {
        try { await call("image.copy", { dataUrl: await card() }); toast({ kind: "success", title: "Image copied", message: "Paste it into Discord with Ctrl+V.", timeout: 3000 }); }
        catch (e) { toast({ kind: "error", title: "Could not copy image", message: e.message }); }
      });
      const sp = body.querySelector("#speedProfile");
      if (sp) {
        const xs = pts.map((_, i) => i);
        const c = chart(sp, {
          x: xs, height: 150,
          series: [{ label: "Speed", values: pts.map((p) => (p.speed === -1 ? null : f.speedValue(Math.max(0, p.speed)))), fill: true }],
          yFmt: (v, tip) => (tip ? `${f.num(v)} ${f.speedUnit()}` : f.num(v)),
          xFmt: () => "", tipX: (i) => (pts[i]?.t ? f.time(pts[i].t) : ""),
        });
        cleanup.push(() => c.destroy());
      }
      return () => cleanup.forEach((c) => c());
    },
  });
}
