import { html, cx, $ } from "../core/html.js";
import { icon } from "../core/icons.js";
import { store } from "../core/store.js";
import { card, empty, progress, kv, drawer, segmented, damageTone, contextMenu } from "../components/ui.js";
import { chart } from "../components/charts.js";
import * as f from "../core/format.js";

export const brandMark = (brand = "") => brand.replace("Mercedes-Benz", "MB").replace("Renault", "RT").slice(0, 3).toUpperCase();

export function noProfile() {
  return empty({ brand: true, title: "No ETS2 profile loaded", text: "HAULIX reads trucks, trailers, garages and drivers from your save game. Choose a profile to get started.", action: html`<a class="btn btn--primary" href="#/setup">Run setup</a>` });
}

const WEAR_LABELS = { engine: "Engine", transmission: "Transmission", cabin: "Cabin", body: "Body", chassis: "Chassis", wheels: "Wheels" };

export function wearRows(w, keys = ["engine", "transmission", "cabin", "chassis", "wheels"]) {
  return keys.map((k) => [WEAR_LABELS[k], w[k]])
    .map(([l, v]) => html`<div class="wear-row"><span class="muted">${l}</span>${progress(Math.max(v || 0, 0.004), { tone: damageTone(v || 0), thin: true })}<span class="v">${f.pct(v || 0, 1)}</span></div>`);
}

export function profitChart(el, days, label = "Revenue") {
  if (!days?.length) {
    el.innerHTML = empty({ iconName: "chart-column", title: "No profit history yet", text: "ETS2 records daily revenue and costs once this vehicle completes jobs.", compact: true }).toString();
    return null;
  }
  return chart(el, {
    x: days.map((d) => d.day), height: 170,
    series: [{ label, values: days.map((d) => d.revenue), bars: true }],
    yFmt: (v, tip) => (tip ? f.money(v) : f.money(v, { compact: true })),
    xFmt: (v) => `D${v}`, tipX: (v) => `Game day ${v}`,
  });
}

const statusBadge = (t) => t.isPlayerTruck ? html`<span class="badge badge--accent">Your truck</span>`
  : t.driverId ? html`<span class="badge badge--ok">Assigned</span>` : html`<span class="badge">Idle</span>`;

let view = "cards";

export default {
  title: "Trucks",
  crumb: () => {
    const p = store.get("profile");
    return p ? `${p.trucks.length} trucks · ${p.companyName}` : "";
  },

  render() {
    const p = store.get("profile");
    if (!p) return noProfile();
    if (!p.trucks.length) return empty({ brand: true, title: "No trucks yet", text: "Buy a truck at a dealer in ETS2. HAULIX picks it up from the next save." });
    const totalKm = p.trucks.reduce((a, t) => a + t.odometerKm, 0);
    const value = p.trucks.reduce((a, t) => a + t.accessoryValue, 0);
    const avgDmg = p.trucks.reduce((a, t) => a + t.wear.overall, 0) / p.trucks.length;
    const assigned = p.trucks.filter((t) => t.driverId).length;
    return html`<div class="page-max">
      <div class="summary-strip">
        <div class="stat"><div class="stat__label">Trucks</div><div class="stat__value">${p.trucks.length}</div></div>
        <div class="stat"><div class="stat__label">Assigned to drivers</div><div class="stat__value">${assigned}<small>/ ${p.trucks.length}</small></div></div>
        <div class="stat"><div class="stat__label">Fleet mileage</div><div class="stat__value">${f.dist(totalKm)}</div></div>
        <div class="stat" data-tip="Sum of component refund values from the save"><div class="stat__label">Component value</div><div class="stat__value">${f.money(value, { compact: true })}</div></div>
        <div class="stat"><div class="stat__label">Average damage</div><div class="stat__value ${damageTone(avgDmg) === "ok" ? "" : damageTone(avgDmg)}">${f.pct(avgDmg, 1)}</div></div>
      </div>
      <div class="fleet-toolbar">
        <div class="search" style="width:280px">${icon("search")}<input class="input" id="truckSearch" placeholder="Filter by name, plate, garage…"></div>
        <select class="select" id="truckSort" style="width:auto"><option value="name">Sort: Name</option><option value="odo">Sort: Mileage</option><option value="damage">Sort: Damage</option><option value="profit">Sort: Profit</option><option value="garage">Sort: Garage</option></select>
        <span class="spacer"></span>
        ${segmented("view", [["cards", "Cards"], ["table", "Table"]], view)}
      </div>
      <div id="truckList"></div>
    </div>`;
  },

  mount(root, { params }) {
    const p = store.get("profile");
    if (!p?.trucks.length) return;
    let q = "", sort = "name";
    const draw = () => {
      let list = p.trucks.filter((t) => !q || [t.name, t.licensePlate, t.garageCity, t.driverName].some((v) => v?.toLowerCase().includes(q)));
      const sorters = {
        name: (a, b) => (b.isPlayerTruck - a.isPlayerTruck) || a.name.localeCompare(b.name), odo: (a, b) => b.odometerKm - a.odometerKm,
        damage: (a, b) => b.wear.overall - a.wear.overall, profit: (a, b) => b.profit.profit - a.profit.profit, garage: (a, b) => (a.garageCity || "").localeCompare(b.garageCity || ""),
      };
      list = list.sort(sorters[sort]);
      const el = $("#truckList", root);
      if (!list.length) { el.innerHTML = empty({ iconName: "search", title: "No trucks match", compact: true }).toString(); return; }
      el.innerHTML = view === "cards"
        ? html`<div class="auto-grid" style="--min:330px">${list.map((t) => html`
            <section class="card card--hover truck-card" data-id="${t.id}">
              <div class="card__body">
                <div class="truck-card__head">
                  <div class="brand-badge">${brandMark(t.brand)}</div>
                  <div class="grow"><div class="truck-card__name ellipsis">${t.name}</div><div class="truck-card__sub">${t.engine || "Engine unknown"}${t.horsePower ? ` · ${t.horsePower} hp` : ""}</div></div>
                  ${statusBadge(t)}
                </div>
                <div class="mini-stats">
                  <div class="stat"><div class="stat__value">${f.dist(t.odometerKm)}</div><div class="stat__label">Mileage</div></div>
                  <div class="stat"><div class="stat__value">${f.pct(t.fuelRelative)}</div><div class="stat__label">Fuel</div></div>
                  <div class="stat"><div class="stat__value ${damageTone(t.wear.overall) === "ok" ? "" : damageTone(t.wear.overall)}">${f.pct(t.wear.overall, 1)}</div><div class="stat__label">Damage</div></div>
                </div>
                ${progress(t.fuelRelative, { thin: true, tone: t.fuelRelative < 0.2 ? "warn" : "muted", tip: `Fuel ${f.pct(t.fuelRelative)}` })}
                <div class="row" style="justify-content:space-between;font-size:12px">
                  <span class="muted row" style="gap:6px">${icon("warehouse", "icon icon-sm")}${t.garageCity || "No garage"}</span>
                  <span class="muted row" style="gap:6px">${icon("user-round", "icon icon-sm")}${t.driverName || "No driver"}</span>
                  ${t.licensePlate ? html`<span class="plate">${t.licensePlate}</span>` : ""}
                </div>
              </div>
            </section>`)}</div>`.toString()
        : html`<section class="card"><div class="table-wrap"><table class="table">
            <thead><tr><th>Truck</th><th>Engine</th><th>Garage</th><th>Driver</th><th class="num">Mileage</th><th class="num">Fuel</th><th class="num">Damage</th><th class="num">Profit</th><th>Status</th></tr></thead>
            <tbody>${list.map((t) => html`<tr class="is-clickable" data-id="${t.id}">
              <td><strong>${t.name}</strong><span class="sub">${t.licensePlate || ""}</span></td><td class="muted">${t.engine || "—"}${t.horsePower ? html`<span class="sub">${t.horsePower} hp</span>` : ""}</td>
              <td>${t.garageCity || "—"}</td><td>${t.driverName || html`<span class="faint">—</span>`}</td>
              <td class="num">${f.dist(t.odometerKm)}</td><td class="num">${f.pct(t.fuelRelative)}</td>
              <td class="num ${damageTone(t.wear.overall) === "ok" ? "" : damageTone(t.wear.overall)}">${f.pct(t.wear.overall, 1)}</td>
              <td class="num">${f.money(t.profit.profit)}</td><td>${statusBadge(t)}</td></tr>`)}</tbody></table></div></section>`.toString();
    };
    draw();
    $("#truckSearch", root).addEventListener("input", (e) => { q = e.target.value.trim().toLowerCase(); draw(); });
    $("#truckSort", root).addEventListener("change", (e) => { sort = e.target.value; draw(); });
    root.addEventListener("click", (e) => {
      const seg = e.target.closest("[data-action=seg]");
      if (seg) { view = seg.dataset.value; root.querySelectorAll("[data-seg=view] button").forEach((b) => b.classList.toggle("is-active", b === seg)); draw(); return; }
      const c = e.target.closest("[data-id]");
      if (c) openTruck(p.trucks.find((t) => t.id === c.dataset.id));
    });
    root.addEventListener("contextmenu", (e) => {
      const c = e.target.closest("[data-id]");
      if (!c) return;
      const t = p.trucks.find((x) => x.id === c.dataset.id);
      contextMenu(e, [
        { label: "Open details", icon: "external-link", onClick: () => openTruck(t) },
        t.garageId && { label: `Go to ${t.garageCity} garage`, icon: "warehouse", onClick: () => (location.hash = `#/garages/${encodeURIComponent(t.garageId)}`) },
        t.driverId && !t.isPlayerTruck && { label: `Open ${t.driverName}`, icon: "user-round", onClick: () => (location.hash = `#/drivers/${encodeURIComponent(t.driverId)}`) },
        { label: "Deliveries with this truck", icon: "book-open", onClick: () => (location.hash = `#/logbook/q/${encodeURIComponent(t.name)}`) },
      ].filter(Boolean));
    });
    if (params[0]) openTruck(p.trucks.find((t) => t.id === params[0]));
  },
};

export function openTruck(t) {
  if (!t) return;
  drawer({
    wide: true,
    head: html`<div class="brand-badge" style="width:54px;height:54px;font-size:18px">${brandMark(t.brand)}</div>`,
    title: t.name,
    sub: html`${statusBadge(t)}${t.licensePlate ? html`<span class="plate">${t.licensePlate}</span>` : ""}<span>${t.plateCountry || ""}</span>`,
    body: html`
      <div class="detail-hero">
        <div class="stat"><div class="stat__label">Mileage</div><div class="stat__value">${f.dist(t.odometerKm)}</div></div>
        <div class="stat"><div class="stat__label">Engine</div><div class="stat__value">${t.horsePower ? html`${t.horsePower}<small>hp</small>` : "—"}</div></div>
        <div class="stat"><div class="stat__label">Fuel</div><div class="stat__value">${f.pct(t.fuelRelative)}</div></div>
        <div class="stat"><div class="stat__label">Damage</div><div class="stat__value ${damageTone(t.wear.overall) === "ok" ? "" : damageTone(t.wear.overall)}">${f.pct(t.wear.overall, 1)}</div></div>
      </div>
      <div class="grid">
        <div class="span-6">
          <div class="label label--muted" style="margin-bottom:8px">Specification</div>
          ${kv([
            ["Brand", t.brand, { icon: "truck", text: true }], ["Model", t.model, { icon: "badge-euro", text: true }],
            ["Engine", t.engine || "—", { icon: "zap", text: true }],
            ["Power", t.horsePower ? `${t.horsePower} hp` : "—", { icon: "gauge", tip: "Estimated from the engine code in the save" }],
            ["Transmission", t.transmission || "—", { icon: "cog", text: true }], ["Chassis", t.chassis || "—", { icon: "land-plot", text: true }],
            ["Cabin", t.cabin || "—", { icon: "car-front", text: true }], ["Parts fitted", f.num(t.accessoryCount), { icon: "wrench" }],
          ])}
        </div>
        <div class="span-6">
          <div class="label label--muted" style="margin-bottom:8px">Assignment & trip</div>
          ${kv([
            ["Garage", t.garageCity ? html`<a class="link" href="#/garages/${encodeURIComponent(t.garageId)}">${t.garageCity}</a>` : "—", { icon: "warehouse", text: true }],
            ["Driver", t.driverName ? (t.isPlayerTruck ? "You" : html`<a class="link" href="#/drivers/${encodeURIComponent(t.driverId)}">${t.driverName}</a>`) : "Unassigned", { icon: "user-round", text: true }],
            ["Trip distance", f.dist(t.tripDistanceKm), { icon: "route" }], ["Trip fuel", f.volume(t.tripFuelLitres), { icon: "fuel" }],
            ["Trip consumption", t.tripDistanceKm ? f.consumption(t.tripFuelLitres / t.tripDistanceKm) : "—", { icon: "activity" }],
            ["Component value", f.money(t.accessoryValue), { icon: "coins", tip: "Refund value of fitted parts as stored in the save" }],
          ])}
        </div>
      </div>
      <div><div class="label label--muted" style="margin-bottom:10px">Wear</div>${wearRows(t.wear)}</div>
      <div>
        <div class="row" style="margin-bottom:10px"><div class="label label--muted">Revenue by game day</div><span class="spacer"></span>
          <span class="muted" style="font-size:12px">Profit <span class="num ${t.profit.profit >= 0 ? "ok" : "crit"}">${f.money(t.profit.profit)}</span> · ${f.num(t.profit.jobs)} jobs</span></div>
        <div id="truckProfit"></div>
      </div>`,
    onMount: (body) => {
      const c = profitChart(body.querySelector("#truckProfit"), t.profit.days);
      return () => c?.destroy();
    },
  });
}
