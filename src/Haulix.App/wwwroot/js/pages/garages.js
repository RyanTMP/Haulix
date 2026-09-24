import { html, cx, $ } from "../core/html.js";
import { icon } from "../core/icons.js";
import { store } from "../core/store.js";
import { empty, slots, driverStatus, drawer, kv, progress } from "../components/ui.js";
import { noProfile, brandMark } from "./trucks.js";
import * as f from "../core/format.js";

export default {
  title: "Garages",
  crumb: () => {
    const p = store.get("profile");
    return p ? `${p.garages.length} garages · HQ ${p.hqCityName || "—"}` : "";
  },

  render() {
    const p = store.get("profile");
    if (!p) return noProfile();
    if (!p.garages.length) return empty({ brand: true, title: "No garages owned", text: "Buy a garage in ETS2 to start building your company. Garages, their trucks and drivers appear here." });
    const slotsTotal = p.garages.reduce((a, g) => a + g.slots, 0);
    const trucks = p.garages.reduce((a, g) => a + g.trucksAssigned, 0);
    const drivers = p.garages.reduce((a, g) => a + g.driversAssigned, 0);
    const util = slotsTotal ? p.garages.reduce((a, g) => a + Math.min(g.trucksAssigned, g.driversAssigned), 0) / slotsTotal : 0;
    return html`<div class="page-max">
      <div class="summary-strip">
        <div class="stat"><div class="stat__label">Garages</div><div class="stat__value">${p.garages.length}</div></div>
        <div class="stat"><div class="stat__label">Truck slots used</div><div class="stat__value">${trucks}<small>/ ${slotsTotal}</small></div></div>
        <div class="stat"><div class="stat__label">Driver slots used</div><div class="stat__value">${drivers}<small>/ ${slotsTotal}</small></div></div>
        <div class="stat" data-tip="Slots with both a truck and a driver"><div class="stat__label">Fleet utilisation</div><div class="stat__value">${f.pct(util)}</div></div>
        <div class="stat"><div class="stat__label">Garage revenue</div><div class="stat__value">${f.money(p.garages.reduce((a, g) => a + (g.profit?.revenue || 0), 0), { compact: true })}</div></div>
      </div>
      <div class="auto-grid" style="--min:380px">
        ${p.garages.map((g) => {
          const u = g.slots ? Math.min(g.trucksAssigned, g.driversAssigned) / g.slots : 0;
          return html`<section class="card card--hover garage-card ${g.isHq ? "card--accent" : ""}" data-id="${g.id}">
            <header class="card__head"><h2 class="label">${g.city} garage</h2>
              <div class="card__meta">${g.isHq ? html`<span class="badge badge--accent">Headquarters</span>` : ""}<span class="badge badge--outline">${g.size}</span></div></header>
            <div class="card__body">
              <div class="row" style="align-items:flex-end;justify-content:space-between;margin-bottom:16px">
                <div><div class="garage-card__util">${f.pct(u)}</div><div class="stat__label">Utilisation</div></div>
                <div class="muted" style="font-size:12px;text-align:right">${g.country || ""}<br><span class="faint">Level ${g.status} · ${g.slots} ${g.slots === 1 ? "slot" : "slots"}</span></div>
              </div>
              <div class="grid" style="gap:14px">
                <div class="span-6"><div class="row" style="justify-content:space-between;font-size:12px"><span class="muted">Trucks</span><span class="num">${g.trucksAssigned} / ${g.slots}</span></div>${slots(g.slots, g.trucksAssigned)}</div>
                <div class="span-6"><div class="row" style="justify-content:space-between;font-size:12px"><span class="muted">Drivers</span><span class="num">${g.driversAssigned} / ${g.slots}</span></div>${slots(g.slots, g.driversAssigned, { muted: true })}</div>
              </div>
              <div class="divider"></div>
              ${slotList(g, p, 3)}
            </div>
            <footer class="card__foot"><span class="muted" style="font-size:12px">${icon("container", "icon icon-sm")}</span><span class="muted" style="font-size:12px">${g.trailersAssigned} trailers</span><span class="spacer"></span><span class="num" style="font-size:12px">${f.money(g.profit?.revenue || 0)}</span><span class="faint" style="font-size:12px">revenue</span></footer>
          </section>`;
        })}
      </div>
    </div>`;
  },

  mount(root, { params }) {
    const p = store.get("profile");
    if (!p?.garages.length) return;
    root.addEventListener("click", (e) => {
      if (e.target.closest("a")) return;
      const c = e.target.closest("[data-id]");
      if (c) openGarage(p.garages.find((g) => g.id === c.dataset.id), p);
    });
    if (params[0]) openGarage(p.garages.find((g) => g.id === params[0]), p);
  },
};

function slotList(g, p, limit = 99) {
  const rows = [];
  for (let i = 0; i < g.slots; i++) {
    const t = p.trucks.find((x) => x.id === g.truckIds[i]);
    const d = t?.driverId ? p.drivers.find((x) => x.id === t.driverId) : p.drivers.filter((x) => x.garageId === g.id && !p.trucks.some((tt) => tt.driverId === x.id))[i - g.truckIds.length];
    rows.push({ t, d });
  }
  const shown = rows.slice(0, limit);
  return html`<div class="slot-list">${shown.map(({ t, d }, i) => html`<div class="${cx("slot", !t && !d && "slot--empty")}">
      <span class="slot__n">${String(i + 1).padStart(2, "0")}</span>
      <span class="ellipsis">${t ? html`<a class="link-muted" href="#/trucks/${encodeURIComponent(t.id)}">${icon("truck")}${t.name}</a>` : "Empty truck slot"}</span>
      <span class="ellipsis">${d ? (d.isPlayer ? "You" : html`<a class="link-muted" href="#/drivers/${encodeURIComponent(d.id)}">${d.name}</a>`) : html`<span class="faint">No driver</span>`}</span>
      ${d ? driverStatus(d.status) : html`<span></span>`}
    </div>`)}${rows.length > limit ? html`<div class="faint" style="font-size:12px;padding-top:8px">+ ${rows.length - limit} more slots</div>` : ""}</div>`;
}

function openGarage(g, p) {
  if (!g) return;
  const trailers = p.trailers.filter((t) => t.garageId === g.id);
  drawer({
    wide: true,
    title: `${g.city} garage`,
    sub: html`${g.isHq ? html`<span class="badge badge--accent">Headquarters</span>` : ""}<span class="badge badge--outline">${g.size}</span><span>${g.country || ""}</span>`,
    body: html`
      <div class="detail-hero">
        <div class="stat"><div class="stat__label">Trucks</div><div class="stat__value">${g.trucksAssigned}<small>/ ${g.slots}</small></div></div>
        <div class="stat"><div class="stat__label">Drivers</div><div class="stat__value">${g.driversAssigned}<small>/ ${g.slots}</small></div></div>
        <div class="stat"><div class="stat__label">Trailers</div><div class="stat__value">${g.trailersAssigned}</div></div>
        <div class="stat"><div class="stat__label">Utilisation</div><div class="stat__value">${f.pct(g.slots ? Math.min(g.trucksAssigned, g.driversAssigned) / g.slots : 0)}</div></div>
      </div>
      <div><div class="label label--muted" style="margin-bottom:6px">Slots</div>${slotList(g, p)}</div>
      ${trailers.length ? html`<div><div class="label label--muted" style="margin-bottom:6px">Trailers</div>
        <div class="slot-list">${trailers.map((t) => html`<div class="slot" style="grid-template-columns:22px minmax(0,1fr) auto auto"><span class="slot__n">${icon("container", "icon icon-sm")}</span><span>${t.name}</span><span class="muted">${t.bodyType || ""}</span><span class="num muted">${f.dist(t.odometerKm)}</span></div>`)}</div></div>` : ""}
      <div class="grid">
        <div class="span-6">${kv([
          ["Garage level", `${g.status} · ${g.size}`, { icon: "building-2" }],
          ["Productivity", f.pct(g.productivity), { icon: "activity", tip: "From the save file" }],
        ])}</div>
        <div class="span-6">${kv([
          ["Revenue", f.money(g.profit?.revenue || 0), { icon: "coins" }],
          ["Distance", f.dist(g.profit?.distanceKm || 0), { icon: "route" }],
        ])}</div>
      </div>
      <div class="callout">${icon("info")}<div>Garage upgrades are made in-game. HAULIX reads the garage level and slots from your latest save and refreshes automatically when ETS2 saves.</div></div>`,
  });
}
