import { html, raw, bindText, cx, $ } from "../core/html.js";
import { icon } from "../core/icons.js";
import { store, isLive } from "../core/store.js";
import { card, empty, progress, skeleton, damageTone } from "../components/ui.js";
import { gauge, sparkline } from "../components/charts.js";
import { teleValues, jobProgress, cityPositions, nearestCity, locationLabel } from "../core/teleview.js";
import { loadCities } from "../core/cities.js";
import * as f from "../core/format.js";
import { rankOf, families } from "./achievements.js";
import { t } from "../core/i18n.js";

export default {
  // Premium look: the dashboard greets the driver instead of repeating its name.
  title: () => {
    const h = new Date().getHours();
    const part = h < 5 ? "evening" : h < 12 ? "morning" : h < 18 ? "afternoon" : "evening";
    const p = store.get("profile");
    const name = p?.profileName || p?.companyName;
    return name ? `Good ${part}, ${name}` : `Good ${part}`;
  },
  crumb: () => {
    const p = store.get("profile");
    return p ? `${p.companyName || p.profileName} · ${p.hqCityName || ""} HQ` : "";
  },

  render() {
    return html`<div class="dash page-max" id="dash">
      <section class="card dash__drive" id="driveCard">${skeleton(3, { block: true })}</section>

      ${card({
        title: "Live telemetry", cls: "dash__live", bodyCls: "card__body--flush", id: "liveCard",
        meta: html`<span data-bind="liveMeta">—</span>`,
        body: html`<div class="live-grid live-only" style="padding:16px var(--card-pad) 6px">
            <div id="gSpeed"></div><div id="gRpm"></div>
          </div>
          <div class="live-mini live-only">
            <div><div class="eyebrow">Gear</div><div class="gear" data-bind="gear">—</div><div class="faint" style="font-size:11px" data-bind="cruise"></div></div>
            <div><div class="eyebrow">Fuel</div><div class="stat__value" style="font-size:17px;margin:4px 0 6px" data-bind="fuel">—</div><div id="fuelBar"></div></div>
            <div><div class="eyebrow">Damage</div><div class="stat__value" style="font-size:17px;margin:4px 0 6px" data-bind="damage">—</div><div id="dmgBar"></div></div>
          </div>`,
      })}

      ${card({
        title: "Your progress", cls: "dash__route", id: "progressCard",
        meta: html`<a class="link-muted" href="#/achievements">Achievements ${icon("arrow-right")}</a>`,
        body: skeleton(3),
      })}

      ${card({ title: "Finances", cls: "dash__finance", id: "financeCard", meta: html`<span>Last 7 days</span>`, body: skeleton(2) })}

      ${card({
        title: "Recent deliveries", cls: "dash__recent", bodyCls: "card__body--flush", id: "recentCard",
        meta: html`<a class="link-muted" href="#/logbook">View logbook ${icon("arrow-right")}</a>`,
        body: skeleton(4),
      })}

      <div class="dash__side">
        ${card({ title: "Fleet", id: "fleetCard", bodyCls: "card__body--flush", meta: html`<a class="link-muted" href="#/garages">Garages ${icon("arrow-right")}</a>`, body: skeleton(2) })}
        ${card({ title: "Recent activity", id: "feedCard", bodyCls: "card__body--flush", body: skeleton(3) })}
      </div>
    </div>`;
  },

  async mount(root, { call }) {
    const cleanups = [];
    const cities = await loadCities();

    // Gauges
    const gSpeed = gauge($("#gSpeed", root), { max: f.imperial() ? 90 : 140, unit: f.speedUnit(), ticks: 7, fmt: (v) => Math.round(f.speedValue(v)) });
    const gRpm = gauge($("#gRpm", root), { max: 2500, unit: "rpm", ticks: 5, redline: 2100, fmt: (v) => Math.round(v) });

    // Where the truck is: cities learned while driving + estimated positions from the city catalogue.
    let cityPos = new Map();
    const loadCityPositions = async () => {
      const learned = await call("cities.learned").catch(() => []);
      cityPos = cityPositions(learned, cities.list);
    };
    loadCityPositions();

    // Your progress: driver rank and the achievements closest to being unlocked.
    const loadProgress = async () => {
      const list = await call("achievements.get").catch(() => null);
      renderProgress($("#progressCard .card__body", root), list);
    };
    loadProgress();
    cleanups.push(store.on("achievements", (l) => renderProgress($("#progressCard .card__body", root), l)));

    // Static sections
    const loadRecent = async () => {
      const rows = await call("logbook.recent", { limit: 8 }).catch(() => []);
      renderRecent($("#recentCard .card__body", root), rows);
    };
    const loadFeed = async () => {
      const [events, rows] = await Promise.all([call("events.recent", { limit: 8 }).catch(() => []), call("logbook.recent", { limit: 5 }).catch(() => [])]);
      renderFeed($("#feedCard .card__body", root), events, rows);
    };
    const loadFinance = async () => {
      const to = new Date().toISOString().slice(0, 10);
      const from = new Date(Date.now() - 6 * 86400e3).toISOString().slice(0, 10);
      const [stats, snaps] = await Promise.all([call("stats.get", { from, to }).catch(() => null), call("stats.snapshots").catch(() => [])]);
      renderFinance($("#financeCard .card__body", root), stats, snaps);
    };
    loadRecent(); loadFeed(); loadFinance();
    renderFleet($("#fleetCard .card__body", root));
    cleanups.push(store.on("profile", () => { renderFleet($("#fleetCard .card__body", root)); loadFinance(); loadCityPositions(); loadProgress(); }));
    cleanups.push(store.on("dataChanged", () => { loadRecent(); loadFeed(); }));

    // Live updates
    const driveCard = $("#driveCard", root);
    let driveMode = null;
    const update = () => {
      const t = store.get("telemetry");
      const s = t?.snapshot;
      const live = isLive();
      root.querySelector("#dash").classList.toggle("disconnected", !live);
      const mode = !s ? "none" : s.onJob ? "job" : "free";
      if (mode !== driveMode) {
        driveMode = mode;
        renderDrive(driveCard, mode);
      }
      if (!s) {
        bindText(root, { liveMeta: "No data yet" });
        return;
      }
      const v = teleValues(s);
      const near = nearestCity(s, [...cityPos.values()]);
      v.location = locationLabel(near);
      v.cruise = s.cruiseControl ? `Cruise ${f.speed(s.cruiseControlKmh)}` : "Cruise off";
      v.liveMeta = live ? `${s.demo ? "Demo · " : ""}updated ${f.time(s.capturedUtc)}` : `Last seen ${f.ago(store.get("status")?.lastSampleUtc)}`;
      v.progressPct = f.pct(jobProgress(s));
      v.driven = t.job ? f.dist(t.job.distanceKm) : "—";
      bindText(root, v);
      gSpeed.set(Math.abs(s.speedKmh), { unitLabel: f.speedUnit() });
      gRpm.set(s.engineRpm);
      const fb = $("#fuelBar", root), db = $("#dmgBar", root);
      const fuelPct = s.fuelCapacity ? s.fuelLitres / s.fuelCapacity : 0;
      fb.innerHTML = progress(fuelPct, { tone: fuelPct < 0.15 ? "crit" : fuelPct < 0.25 ? "warn" : "muted", thin: true }).toString();
      db.innerHTML = progress(Math.max(s.truckDamage, 0.004), { tone: damageTone(s.truckDamage), thin: true }).toString();
      const pf = driveCard.querySelector(".progress__fill");
      if (pf) pf.style.width = `${(jobProgress(s) * 100).toFixed(1)}%`;
      const lim = driveCard.querySelector(".limit-sign");
      if (lim) lim.style.visibility = s.speedLimitKmh > 1 ? "visible" : "hidden";
    };
    update();
    cleanups.push(store.on("telemetry", update));
    cleanups.push(store.on("status", update));

    return () => {
      cleanups.forEach((c) => c());
    };
  },
};

function renderDrive(el, mode) {
  const s = store.get("telemetry")?.snapshot;
  const status = store.get("status");
  if (mode === "none") {
    el.innerHTML = html`<header class="card__head"><h2 class="label">Current drive</h2></header>
      ${empty({
        brand: true,
        title: status?.game === "running" ? "Waiting for telemetry" : "ETS2 is not running",
        text: status?.game === "running"
          ? (status?.pluginInstalled ? "HAULIX is connected to the game and waiting for the first telemetry frame." : "Install the scs-telemetry plugin to see live data. Setup explains how.")
          : "Start Euro Truck Simulator 2 and HAULIX will switch to LIVE automatically. Your history, fleet and statistics stay available offline.",
        action: html`<div class="row"><a class="btn" href="#/logbook">${icon("book-open")}Open logbook</a><a class="btn btn--ghost" href="#/settings/ets2">ETS2 settings</a></div>`,
      })}`.toString();
    return;
  }
  const job = mode === "job";
  el.innerHTML = html`
    <header class="card__head">
      <h2 class="label">${job ? "Current drive" : "Free roam"}</h2>
      <div class="card__meta"><span class="drive__truck">${icon("truck", "icon icon-sm")}<span data-bind="truck">—</span><span class="plate" data-bind="plateShort">${s?.licensePlate || ""}</span></span></div>
    </header>
    <div class="card__body">
      <div class="drive">
        <div style="min-width:0">
          ${job ? html`<div class="drive__route">
              <div class="drive__city"><span data-bind="from">—</span><small data-bind="fromCo"></small></div>
              <div class="drive__arrow"><i></i>${icon("chevron-right")}</div>
              <div class="drive__city"><span data-bind="to">—</span><small data-bind="toCo"></small></div>
            </div>
            <div class="drive__progress">${progress(jobProgress(s), { thick: true })}</div>
            <div class="drive__progress-meta"><span><span data-bind="progressPct">0%</span> complete</span><span><span data-bind="remaining">—</span> remaining</span></div>`
          : html`<div class="drive__route"><div class="drive__city"><span data-bind="location">—</span><small>No active job · roaming</small></div></div>
            <p class="muted" style="margin-top:16px;max-width:520px">Take a job from the freight market or your company to see route progress, ETA and income here. HAULIX keeps recording your free-roam distance.</p>`}
        </div>
        <div class="drive__speed live-only">
          <div class="row" style="gap:14px;align-items:flex-start">
            <div class="limit-sign" data-tip="Speed limit" data-bind="speedLimit"></div>
            <div><div class="v" data-bind="speed">0</div><div class="u" data-bind="speedUnit">km/h</div></div>
          </div>
          <span class="faint" style="font-size:12px" data-bind="cruise">—</span>
        </div>
      </div>
    </div>
    <div class="drive__facts">
      ${job ? [
        ["Cargo", html`<span data-bind="cargo">—</span> <span class="faint" data-bind="cargoMass"></span>`, "package"],
        ["Arrival (real time)", html`<span class="num eta-real" data-bind="etaReal">—</span> <span class="faint" data-bind="arrivalClock"></span>`, "timer", "etaSource"],
        ["Game ETA", html`<span data-bind="eta">—</span> <span class="faint" data-bind="onTime"></span>`, "clock"],
        ["Income", html`<span class="num" data-bind="income">—</span>`, "coins"],
      ].map(([l, v, i, tip]) => html`<div ${raw(tip ? `data-bind-tip="${tip}"` : "")}><div class="stat__label" style="margin-bottom:4px">${icon(i, "icon icon-sm")}${l}</div><div class="ellipsis" style="font-weight:500">${v}</div></div>`)
      : [
        ["Odometer", html`<span class="num" data-bind="odometer">—</span>`, "gauge"],
        ["Fuel range", html`<span class="num" data-bind="fuelRange">—</span>`, "fuel"],
        ["Heading", html`<span class="num" data-bind="heading">—</span>`, "compass"],
        ["Game time", html`<span class="num" data-bind="gameTime">—</span>`, "clock"],
      ].map(([l, v, i]) => html`<div><div class="stat__label" style="margin-bottom:4px">${icon(i, "icon icon-sm")}${l}</div><div class="ellipsis" style="font-weight:500">${v}</div></div>`)}
    </div>`.toString();
}

function renderRecent(el, rows) {
  if (!rows?.length) {
    el.innerHTML = empty({ iconName: "book-open", title: "No deliveries yet", text: "Completed jobs appear here automatically. Your in-game delivery history is imported from the save.", compact: true }).toString();
    return;
  }
  el.innerHTML = html`<div class="table-wrap"><table class="table">
    <thead><tr><th>Date</th><th>Route</th><th>Cargo</th><th class="num">Distance</th><th class="num">Income</th><th class="num">XP</th></tr></thead>
    <tbody>${rows.map((r) => html`<tr class="is-clickable" onclick="location.hash='#/logbook/${r.id}'">
      <td><span class="${cx("dot", r.status === "delivered" ? "dot--ok" : "dot--warn")}" style="display:inline-block;margin-right:10px"></span><span class="num">${r.finishedUtc ? f.dateTime(r.finishedUtc) : f.gameDay(r.gameEndMin)}</span></td>
      <td><span class="route">${r.originCity}${icon("arrow-right")}${r.destCity}</span></td>
      <td class="ellipsis" style="max-width:200px">${r.cargo}</td>
      <td class="num">${f.dist(r.distanceKm)}</td>
      <td class="num ${r.status === "delivered" ? "ok" : "warn"}">${r.status === "delivered" ? f.money(r.income) : "Cancelled"}</td>
      <td class="num muted">${f.num(r.xp)}</td>
    </tr>`)}</tbody></table></div>`.toString();
}

function renderFinance(el, stats, snaps) {
  const p = store.get("profile");
  const income = (stats?.deliveries || []).reduce((a, d) => a + d.income, 0);
  const expenses = (stats?.expenses || []).reduce((a, e) => a + (e.amount || 0), 0) + (stats?.deliveries || []).reduce((a, d) => a + d.penalties, 0);
  const moneySeries = (snaps || []).map((s) => s.money);
  const delta = moneySeries.length > 1 ? moneySeries[moneySeries.length - 1] - moneySeries[0] : null;
  el.innerHTML = html`
    <div class="finance-hero">
      <div class="stat">
        <div class="stat__label">${icon("wallet", "icon icon-sm")}Company balance</div>
        <div class="stat__value stat__value--lg">${p ? f.money(p.money) : "—"}</div>
        ${delta !== null ? html`<div class="stat__delta ${delta >= 0 ? "ok" : "crit"}">${f.money(delta, { sign: true })} since ${f.date(snaps[0].t)}</div>` : html`<div class="stat__delta faint">From your latest save</div>`}
      </div>
      <div style="width:44%;min-width:120px" data-tip="Balance history from saved games">${raw(sparkline(moneySeries.length > 1 ? moneySeries : [0, 0], { h: 46 }))}</div>
    </div>
    <div class="finance-split">
      <div class="stat"><div class="stat__label">Earnings</div><div class="stat__value" style="font-size:18px">${f.money(income)}</div><div class="faint" style="font-size:11px;padding-bottom:14px">Recorded deliveries</div></div>
      <div class="stat" data-tip="Tolls, fines, ferries, trains and cancellation penalties recorded by telemetry"><div class="stat__label">Expenses</div><div class="stat__value" style="font-size:18px">${f.money(expenses)}</div><div class="faint" style="font-size:11px;padding-bottom:14px">Tolls, fines, ferries</div></div>
      <div class="stat"><div class="stat__label">Profit</div><div class="stat__value ${income - expenses >= 0 ? "" : "crit"}" style="font-size:18px">${f.money(income - expenses)}</div><div class="faint" style="font-size:11px;padding-bottom:14px">${p?.loans ? `Loans ${f.money(p.loans)}` : "No outstanding loans"}</div></div>
    </div>`.toString();
}

function renderFleet(el) {
  const p = store.get("profile");
  if (!p) {
    el.innerHTML = empty({ iconName: "truck", title: "No profile loaded", text: "Select your ETS2 profile to see your fleet.", compact: true, action: html`<a class="btn btn--sm" href="#/setup">Run setup</a>` }).toString();
    return;
  }
  const ai = p.drivers.filter((d) => !d.isPlayer);
  const slots = p.garages.reduce((a, g) => a + g.slots, 0);
  const used = p.garages.reduce((a, g) => a + Math.min(g.trucksAssigned, g.driversAssigned), 0);
  el.innerHTML = html`
    <div class="fleet-counts">
      <a href="#/trucks"><span class="n">${p.trucks.length}</span><span class="l">${icon("truck")}Trucks</span></a>
      <a href="#/trailers"><span class="n">${p.trailers.length}</span><span class="l">${icon("container")}Trailers</span></a>
      <a href="#/garages"><span class="n">${p.garages.length}</span><span class="l">${icon("warehouse")}Garages</span></a>
      <a href="#/drivers"><span class="n">${ai.length}</span><span class="l">${icon("users")}AI drivers</span></a>
    </div>
    <div style="padding:14px var(--card-pad) 16px;border-top:1px solid var(--border-subtle)">
      <div class="row" style="justify-content:space-between;margin-bottom:8px;font-size:12px"><span class="muted">Fleet utilisation</span><span class="num">${used} / ${slots} slots · ${f.pct(slots ? used / slots : 0)}</span></div>
      ${progress(slots ? used / slots : 0)}
    </div>`.toString();
}

function renderFeed(el, events, rows) {
  const items = [
    ...(events || []).map((e) => ({ at: e.at, tone: e.type === "fine" ? "warn" : "info", text: { toll: "Toll paid", fine: `Fined · ${e.detail}`, ferry: `Ferry ${e.detail}`, train: `Train ${e.detail}`, refuel: `Refuelled ${e.detail}` }[e.type] || e.type, amount: e.amount ? -e.amount : null })),
    ...(rows || []).filter((r) => r.finishedUtc).map((r) => ({ at: r.finishedUtc, tone: r.status === "delivered" ? "ok" : "warn", text: r.status === "delivered" ? `Delivered ${r.cargo} to ${r.destCity}` : `Cancelled ${r.cargo}`, amount: r.status === "delivered" ? r.income : -r.penalty })),
  ].sort((a, b) => (a.at < b.at ? 1 : -1)).slice(0, 7);
  if (!items.length) {
    el.innerHTML = empty({ iconName: "activity", title: "Quiet so far", text: "Deliveries, tolls, fines, ferries and refuels appear here as they happen.", compact: true }).toString();
    return;
  }
  el.innerHTML = html`<div class="feed">${items.map((i) => html`<div class="feed__item">
      <span class="feed__time">${f.time(i.at)}</span><span class="${cx("dot", `dot--${i.tone}`)}"></span>
      <span class="ellipsis">${i.text}</span>
      <span class="${cx("feed__amt", i.amount > 0 ? "ok" : "muted")}">${i.amount ? f.money(i.amount, { sign: true }) : ""}</span></div>`)}</div>`.toString();
}

function renderProgress(el, list) {
  if (!el) return;
  if (!list?.length) {
    el.innerHTML = empty({ iconName: "award", title: "No achievements yet", text: "Deliver your first job to start your driver rank.", compact: true }).toString();
    return;
  }
  const points = list.filter((a) => a.unlocked).reduce((s, a) => s + (a.points || 0), 0);
  const rank = rankOf(points);
  const near = families(list).filter((x) => x.next).sort((a, b) => b.share - a.share).slice(0, 4);
  el.innerHTML = html`<div class="dash-rank">
      <div class="dash-rank__badge">${icon("trophy")}<b>${rank.level}</b></div>
      <div class="grow"><div class="eyebrow">${t("Driver rank")}</div><div class="dash-rank__name">${t(rank.name)}</div>
        ${progress(rank.to ? (points - rank.from) / (rank.to - rank.from) : 1, { thin: true })}
        <div class="faint" style="font-size:12px;margin-top:4px">${f.num(points)} ${t("Points")}${rank.next ? ` · ${f.num(rank.to - points)} ${t("points to")} ${t(rank.next)}` : ""}</div></div>
    </div>
    <div class="eyebrow" style="margin:16px 0 8px">${t("Almost there")}</div>
    <div class="dash-near">${near.map((x) => html`<div class="dash-near__row tier-${x.next.tier}">${icon(x.icon)}<div class="grow" style="min-width:0">
      <div class="dash-near__title">${x.next.title}</div>${progress(x.share, { thin: true })}</div><b class="num">${Math.floor(x.share * 100)} %</b></div>`)}</div>`.toString();
}
