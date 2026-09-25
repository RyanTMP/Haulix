import { html, $ } from "../core/html.js";
import { icon } from "../core/icons.js";
import { store } from "../core/store.js";
import { empty, progress, kv, drawer, damageTone } from "../components/ui.js";
import { noProfile, wearRows } from "./trucks.js";
import * as f from "../core/format.js";
import { trailerLogo, logoChip } from "../core/logos.js";

export default {
  title: "Trailers",
  crumb: () => {
    const p = store.get("profile");
    return p ? `${p.trailers.length} owned trailers` : "";
  },

  render() {
    const p = store.get("profile");
    if (!p) return noProfile();
    if (!p.trailers.length) return empty({ brand: true, title: "No owned trailers", text: "Owned trailers appear here once you buy one in ETS2 (Trailer Ownership). Quick jobs and freight-market trailers are not part of your fleet." });
    const types = [...new Set(p.trailers.map((t) => t.bodyType).filter(Boolean))].sort();
    const loaded = p.trailers.filter((t) => t.cargoMassKg > 0).length;
    const assigned = p.trailers.filter((t) => t.assignedTruckId).length;
    return html`<div class="page-max">
      <div class="summary-strip">
        <div class="stat"><div class="stat__label">Trailers</div><div class="stat__value">${p.trailers.length}</div></div>
        <div class="stat"><div class="stat__label">Assigned</div><div class="stat__value">${assigned}<small>/ ${p.trailers.length}</small></div></div>
        <div class="stat"><div class="stat__label">Loaded now</div><div class="stat__value">${loaded}</div></div>
        <div class="stat"><div class="stat__label">Body types</div><div class="stat__value">${types.length}</div></div>
        <div class="stat"><div class="stat__label">Total mileage</div><div class="stat__value">${f.dist(p.trailers.reduce((a, t) => a + t.odometerKm, 0))}</div></div>
      </div>
      <div class="fleet-toolbar">
        <div class="search" style="width:280px">${icon("search")}<input class="input" id="trSearch" placeholder="Filter trailers…"></div>
        <select class="select" id="trType" style="width:auto"><option value="">All body types</option>${types.map((t) => html`<option>${t}</option>`)}</select>
      </div>
      <section class="card"><div class="table-wrap" id="trTable"></div></section>
    </div>`;
  },

  mount(root, { params }) {
    const p = store.get("profile");
    if (!p?.trailers.length) return;
    let q = "", type = "";
    const truckName = (id) => p.trucks.find((t) => t.id === id)?.name;
    const driverName = (id) => p.drivers.find((d) => d.id === id)?.name;
    const draw = () => {
      const list = p.trailers.filter((t) => (!type || t.bodyType === type) && (!q || [t.name, t.licensePlate, t.garageCity, t.bodyType].some((v) => v?.toLowerCase().includes(q))));
      $("#trTable", root).innerHTML = list.length ? html`<table class="table">
        <thead><tr><th>Trailer</th><th>Type</th><th>Cargo</th><th>Garage</th><th>Assigned to</th><th class="num">Axles</th><th class="num">Mileage</th><th class="num">Damage</th><th>Status</th></tr></thead>
        <tbody>${list.map((t) => html`<tr class="is-clickable" data-id="${t.id}">
          <td><div class="with-logo">${logoChip(trailerLogo(t.typeId) || trailerLogo(t.name), "", "sm")}<div><strong>${t.name}</strong><span class="sub">${t.licensePlate || ""}</span></div></div></td>
          <td>${t.bodyType || "—"}<span class="sub">${t.chainType || ""}</span></td>
          <td>${t.cargoMassKg > 0 ? f.mass(t.cargoMassKg) : html`<span class="faint">Empty</span>`}</td>
          <td>${t.garageCity || "—"}</td>
          <td>${truckName(t.assignedTruckId) || html`<span class="faint">—</span>`}<span class="sub">${driverName(t.assignedDriverId) || ""}</span></td>
          <td class="num">${t.axles ?? "—"}</td><td class="num">${f.dist(t.odometerKm)}</td>
          <td class="num ${damageTone(t.wear.overall) === "ok" ? "" : damageTone(t.wear.overall)}">${f.pct(t.wear.overall, 1)}</td>
          <td>${t.isPlayerTrailer ? html`<span class="badge badge--accent">Attached</span>` : t.assignedTruckId ? html`<span class="badge badge--ok">In use</span>` : html`<span class="badge">Parked</span>`}</td>
        </tr>`)}</tbody></table>`.toString() : empty({ iconName: "search", title: "No trailers match", compact: true }).toString();
    };
    draw();
    $("#trSearch", root).addEventListener("input", (e) => { q = e.target.value.trim().toLowerCase(); draw(); });
    $("#trType", root).addEventListener("change", (e) => { type = e.target.value; draw(); });
    const open = (t) => {
      if (!t) return;
      drawer({
        title: t.name,
        sub: html`<span class="badge">${t.bodyType || "Trailer"}</span>${t.licensePlate ? html`<span class="plate">${t.licensePlate}</span>` : ""}`,
        body: html`
          <div class="detail-hero" style="grid-template-columns:repeat(3,1fr)">
            <div class="stat"><div class="stat__label">Mileage</div><div class="stat__value">${f.dist(t.odometerKm)}</div></div>
            <div class="stat"><div class="stat__label">Cargo</div><div class="stat__value">${t.cargoMassKg > 0 ? f.mass(t.cargoMassKg) : "Empty"}</div></div>
            <div class="stat"><div class="stat__label">Damage</div><div class="stat__value">${f.pct(t.wear.overall, 1)}</div></div>
          </div>
          ${kv([
            ["Type", t.bodyType || "—", { icon: "container", text: true }], ["Configuration", `${t.chainType || "Single"} · ${t.axles ?? "?"} axles`, { icon: "list", text: true }],
            ["Gross weight limit", t.grossWeightLimitKg ? f.mass(t.grossWeightLimitKg) : "—", { icon: "weight" }],
            ["Chassis mass", t.chassisMassKg ? f.mass(t.chassisMassKg) : "—", { icon: "weight" }],
            ["Volume", t.volumeM3 ? `${f.num(t.volumeM3)} m³` : "—", { icon: "package" }],
            ["Length", t.lengthM ? `${f.num(t.lengthM, 1)} m` : "—", { icon: "arrow-up-down" }],
            ["Garage", t.garageCity || "—", { icon: "warehouse", text: true }],
            ["Assigned truck", truckName(t.assignedTruckId) || "—", { icon: "truck", text: true }],
            ["Assigned driver", driverName(t.assignedDriverId) || "—", { icon: "user-round", text: true }],
            ["Parts value", f.money(t.accessoryValue), { icon: "coins" }],
          ])}
          <div><div class="label label--muted" style="margin-bottom:10px">Wear</div>${wearRows(t.wear, ["body", "chassis", "wheels"])}</div>`,
      });
    };
    root.addEventListener("click", (e) => { const r = e.target.closest("[data-id]"); if (r) open(p.trailers.find((t) => t.id === r.dataset.id)); });
    if (params[0]) open(p.trailers.find((t) => t.id === params[0]));
  },
};
