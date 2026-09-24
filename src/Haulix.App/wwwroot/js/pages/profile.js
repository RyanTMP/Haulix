import { html, raw, $ } from "../core/html.js";
import { icon } from "../core/icons.js";
import { store } from "../core/store.js";
import { card, kv, slots, toast } from "../components/ui.js";
import { sparkline } from "../components/charts.js";
import { noProfile } from "./trucks.js";
import { loadCities, cityName, countryName } from "../core/cities.js";
import * as f from "../core/format.js";

const SKILLS = [["adr", "ADR"], ["longDistance", "Long distance"], ["highValue", "High value cargo"], ["fragile", "Fragile cargo"], ["justInTime", "Just-in-time delivery"], ["ecoDriving", "Eco driving"]];

export default {
  title: "Profile",
  crumb: () => "Your ETS2 company, read from the local save",

  render() {
    const p = store.get("profile");
    if (!p) return noProfile();
    const ai = p.drivers.filter((d) => !d.isPlayer);
    const player = p.drivers.find((d) => d.isPlayer);
    return html`<div class="page-max stack">
      <section class="card profile-card">
        <div class="profile-hero">
          <span class="avatar avatar--lg"><img src="assets/brand/h-logo.png" alt=""></span>
          <div class="grow">
            <div class="profile-hero__name">${p.companyName || p.profileName}</div>
            <div class="profile-hero__meta">
              <span class="row" style="gap:6px">${icon("user-round", "icon icon-sm")}${p.profileName}</span>
              <span class="row" style="gap:6px">${icon("building-2", "icon icon-sm")}HQ ${p.hqCityName || "—"}</span>
              <span class="row" style="gap:6px">${icon("clock", "icon icon-sm")}${f.gameDay(p.gameTimeMinutes)} · ${f.gameTime(p.gameTimeMinutes)}</span>
              <span class="row faint" style="gap:6px">${icon("save", "icon icon-sm")}${p.saveName} · saved ${f.ago(p.saveTimeUtc)}</span>
            </div>
          </div>
          <div class="xp-ring">
            <div class="stat__value stat__value--lg">${f.num(p.xp)}<small>XP</small></div>
            <div class="stat__label" style="justify-content:flex-end">${p.skills.total} skill points invested</div>
          </div>
          <button class="btn" id="reloadProfile">${icon("refresh-cw")}Reload save</button>
        </div>
        <div class="stat-row" style="border-top:1px solid var(--border-subtle)">
          <div class="stat"><div class="stat__label">Money</div><div class="stat__value">${f.money(p.money)}</div><div id="moneySpark" style="margin-top:6px"></div></div>
          <div class="stat"><div class="stat__label">Total distance</div><div class="stat__value">${f.dist(p.totalDistanceKm)}</div></div>
          <div class="stat"><div class="stat__label">Deliveries</div><div class="stat__value">${f.num(store.get("counts")?.deliveries ?? p.deliveries.length)}</div><div class="faint" style="font-size:11px">${p.cancelledJobs} cancelled</div></div>
          <div class="stat"><div class="stat__label">Total income</div><div class="stat__value">${f.money(p.drivers.reduce((a, d) => a + (d.profit?.revenue || 0), 0), { compact: true })}</div><div class="faint" style="font-size:11px">driver profit logs</div></div>
          <div class="stat"><div class="stat__label">Loans</div><div class="stat__value">${f.money(p.loans)}</div><div class="faint" style="font-size:11px">limit ${f.money(p.loanLimit, { compact: true })}</div></div>
        </div>
      </section>

      <div class="grid">
        ${card({ cls: "span-4 md-6 sm-12", title: "Company", body: kv([
          ["Trucks", html`<a class="link" href="#/trucks">${p.trucks.length}</a>`, { icon: "truck" }],
          ["Trailers", html`<a class="link" href="#/trailers">${p.trailers.length}</a>`, { icon: "container" }],
          ["Garages", html`<a class="link" href="#/garages">${p.garages.length}</a>`, { icon: "warehouse" }],
          ["AI drivers", html`<a class="link" href="#/drivers">${ai.length}</a>`, { icon: "users" }],
          ["Preferred brand", p.preferredBrand ? p.preferredBrand.split("_")[0].replace(/^\w/, (m) => m.toUpperCase()) : "—", { icon: "badge-euro", text: true }],
          ["Active mods", f.num(p.activeModCount), { icon: "layers" }],
        ]) })}
        ${card({ cls: "span-4 md-6 sm-12", title: "Driver", body: kv([
          ["Your truck", player?.truckId ? html`<a class="link" href="#/trucks/${encodeURIComponent(player.truckId)}">${p.trucks.find((t) => t.id === player.truckId)?.name || "—"}</a>` : "—", { icon: "truck", text: true }],
          ["Real time played", f.duration(p.totalRealTimeMinutes * 60, { short: true }), { icon: "timer" }],
          ["Fuel bought", `${f.volume(p.totalFuelLitres)} · ${f.money(p.totalFuelPrice)}`, { icon: "fuel" }],
          ["Fuel stops", f.num(p.gasStationVisits), { icon: "map-pin" }],
          ["Service visits", f.num(p.serviceVisits), { icon: "wrench" }],
          ["Last city", p.lastVisitedCity ? cityName(p.lastVisitedCity) : "—", { icon: "navigation", text: true }],
        ]) })}
        ${card({ cls: "span-4 md-12", title: "Skills", meta: `${p.skills.total} / 36`, body: html`<div class="skill-bars" style="grid-template-columns:1fr">${SKILLS.map(([k, l]) => html`<div class="skill"><div class="row" style="justify-content:space-between"><span class="muted">${l}</span><span class="num">${p.skills[k]}/6</span></div>${slots(6, p.skills[k])}</div>`)}</div>` })}
        ${card({ cls: "span-8 md-12", title: "Exploration", meta: `${p.visitedCities.length} cities visited`, body: html`<div id="countries"></div>` })}
        ${card({ cls: "span-4 md-12", title: "Unlocked", body: kv([
          ["Cities visited", f.num(p.visitedCities.length), { icon: "map" }],
          ["Truck dealers", f.num(p.unlockedDealers.length), { icon: "store" }],
          ["Recruitment agencies", f.num(p.unlockedRecruitments.length), { icon: "users" }],
          ["Cargo types hauled", f.num(p.transportedCargoTypes.length), { icon: "package" }],
        ]) })}
        ${p.warnings?.length ? card({ cls: "span-12", title: "Warnings", body: html`${p.warnings.map((w) => html`<div class="callout callout--warn">${icon("triangle-alert")}<div>${w}</div></div>`)}` }) : ""}
      </div>
    </div>`;
  },

  async mount(root, { call }) {
    const p = store.get("profile");
    if (!p) return;
    $("#reloadProfile", root).onclick = async (e) => {
      e.currentTarget.classList.add("is-loading");
      await call("profile.reload").catch((err) => toast({ kind: "error", title: "Reload failed", message: err.message }));
      setTimeout(() => e.target.closest("button")?.classList.remove("is-loading"), 1500);
      toast({ kind: "info", title: "Re-reading save", message: "Your profile refreshes when parsing completes." });
    };
    call("stats.snapshots").then((s) => { if (s?.length > 1) $("#moneySpark", root).innerHTML = sparkline(s.map((x) => x.money), { h: 24 }); }).catch(() => {});
    await loadCities();
    const byCountry = new Map();
    for (const id of p.visitedCities) {
      const code = (await loadCities()).byId.get(id)?.country || "??";
      byCountry.set(code, [...(byCountry.get(code) || []), id]);
    }
    const rows = [...byCountry.entries()].sort((a, b) => b[1].length - a[1].length);
    $("#countries", root).innerHTML = html`<div class="bars">${rows.map(([code, ids]) => html`<div class="bar-row" data-tip="${ids.slice(0, 18).map(cityName).join(", ")}${ids.length > 18 ? "…" : ""}">
      <span class="ellipsis">${code === "??" ? "Other / DLC" : countryName(code)}</span><span class="bar"><i style="width:${(ids.length / rows[0][1].length) * 100}%"></i></span><span class="v">${ids.length}</span></div>`)}</div>`.toString();
  },
};
