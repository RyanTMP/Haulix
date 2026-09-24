import { html, raw, $ } from "../core/html.js";
import { icon } from "../core/icons.js";
import { store } from "../core/store.js";
import { card, empty, segmented, skeleton } from "../components/ui.js";
import { chart, barList } from "../components/charts.js";
import * as f from "../core/format.js";

const RANGES = [["7", "7 days"], ["30", "30 days"], ["90", "90 days"], ["365", "1 year"]];
const TABS = [["driving", "Driving"], ["economy", "Economy"], ["fleet", "Fleet"], ["drivers", "Drivers"]];
let state = { tab: "driving", range: "30" };

const iso = (d) => d.toISOString().slice(0, 10);
const dayTs = (day) => Date.parse(`${day}T12:00:00Z`) / 1000;

function daysBetween(from, to) {
  const out = [];
  for (let d = new Date(`${from}T00:00:00Z`); iso(d) <= to; d.setUTCDate(d.getUTCDate() + 1)) out.push(iso(d));
  return out;
}

export default {
  title: "Statistics",
  crumb: () => "Analytics from your local history",

  render() {
    return html`<div class="page-max">
      <div class="row" style="margin-bottom:var(--gutter);gap:16px;flex-wrap:wrap">
        <div class="tabs" style="margin:0;flex:1;border-bottom:0" id="statTabs">${TABS.map(([k, l]) => html`<button data-tab="${k}" class="${state.tab === k ? "is-active" : ""}">${l}</button>`)}</div>
        ${segmented("range", RANGES, state.range)}
      </div>
      <div id="statBody">${skeleton(4, { block: true })}</div>
    </div>`;
  },

  async mount(root, { call }) {
    let charts = [];
    const destroy = () => { charts.forEach((c) => c?.destroy()); charts = []; };

    const load = async () => {
      destroy();
      const body = $("#statBody", root);
      body.style.opacity = "0.6";
      const to = iso(new Date());
      const from = iso(new Date(Date.now() - (+state.range - 1) * 86400e3));
      let stats, snaps;
      try {
        [stats, snaps] = await Promise.all([call("stats.get", { from, to }), call("stats.snapshots").catch(() => [])]);
      } catch (e) {
        body.innerHTML = empty({ error: true, title: "Statistics unavailable", text: e.message }).toString();
        return;
      } finally {
        body.style.opacity = "";
      }
      const days = daysBetween(from, to);
      const byDay = (rows, key) => { const m = new Map(rows.map((r) => [r.day, r])); return days.map((d) => m.get(d)?.[key] ?? 0); };
      const ctx = { stats, snaps, days, byDay, x: days.map(dayTs), profile: store.get("profile") };
      ({ driving, economy, fleet, drivers })[state.tab](body, ctx, charts);
    };

    root.addEventListener("click", (e) => {
      const t = e.target.closest("[data-tab]");
      if (t) { state.tab = t.dataset.tab; root.querySelectorAll("[data-tab]").forEach((b) => b.classList.toggle("is-active", b === t)); load(); }
      const s = e.target.closest("[data-action=seg]");
      if (s) { state.range = s.dataset.value; root.querySelectorAll("[data-seg=range] button").forEach((b) => b.classList.toggle("is-active", b === s)); load(); }
    });
    await load();
    const off = store.on("dataChanged", load);
    return () => { off(); destroy(); };
  },
};

const dateFmt = (v) => new Date(v * 1000).toLocaleDateString(undefined, { day: "2-digit", month: "short" });
const kpis = (items) => html`<div class="summary-strip">${items.map(([l, v, sub, tip]) => html`<div class="stat" ${raw(tip ? `data-tip="${tip}"` : "")}><div class="stat__label">${l}</div><div class="stat__value">${v}</div>${sub ? html`<div class="faint" style="font-size:11px">${sub}</div>` : ""}</div>`)}</div>`;
const chartCard = (id, title, meta = "", span = "span-6 md-12") => card({ cls: span, title, meta, body: html`<div id="${id}"></div>` });

function noHistory(body, what) {
  body.innerHTML = empty({ brand: true, title: `No ${what} recorded in this period`, text: "HAULIX builds these charts from telemetry it records while you drive. Drive with ETS2 running, or pick a longer range." }).toString();
}

function driving(body, { stats, byDay, x }, charts) {
  const st = stats.sessionTotals;
  const dist = byDay(stats.sessions, "distanceKm");
  const drive = byDay(stats.sessions, "driveSeconds");
  const fuel = byDay(stats.sessions, "fuelL");
  const maxs = byDay(stats.sessions, "maxSpeed");
  const totalDist = dist.reduce((a, b) => a + b, 0);
  const totalDrive = drive.reduce((a, b) => a + b, 0);
  const totalFuel = fuel.reduce((a, b) => a + b, 0);
  if (!totalDist && !totalDrive) return noHistory(body, "driving");
  const avg = drive.map((s, i) => (s > 60 ? dist[i] / (s / 3600) : null));
  body.innerHTML = html`
    ${kpis([
      ["Distance", f.dist(totalDist), `${f.dist(st.distanceKm)} all time`],
      ["Driving time", f.duration(totalDrive, { short: true }), `${f.duration(st.driveSeconds, { short: true })} all time`, "Real time spent moving"],
      ["Average speed", totalDrive ? f.speed(totalDist / (totalDrive / 3600)) : "—", "in-game km per real hour", "Based on odometer distance and real driving time"],
      ["Top speed", f.speed(Math.max(0, ...maxs)), `${f.speed(st.maxSpeed)} all time`],
      ["Fuel used", f.volume(totalFuel), totalDist ? f.consumption(totalFuel / totalDist) : ""],
    ])}
    <div class="grid">
      ${chartCard("cDist", "Distance per day", f.distUnit())}
      ${chartCard("cHours", "Driving time per day", "hours")}
      ${chartCard("cAvg", "Average speed", f.speedUnit())}
      ${chartCard("cFuel", "Fuel consumption", f.volumeUnit())}
    </div>`.toString();
  charts.push(chart($("#cDist", body), { x, series: [{ label: "Distance", values: dist.map(f.distValue), bars: true }], yFmt: (v, t) => (t ? f.dist(v / (f.imperial() ? 0.621371 : 1)) : f.num(v)), xFmt: dateFmt }));
  charts.push(chart($("#cHours", body), { x, series: [{ label: "Driving", values: drive.map((s) => s / 3600), bars: true }], yFmt: (v, t) => (t ? f.duration(v * 3600, { short: true }) : f.num(v, 1)), xFmt: dateFmt }));
  charts.push(chart($("#cAvg", body), { x, series: [{ label: "Average speed", values: avg.map((v) => (v === null ? null : f.speedValue(v))) }], yFmt: (v, t) => (t ? `${f.num(v)} ${f.speedUnit()}` : f.num(v)), xFmt: dateFmt }));
  charts.push(chart($("#cFuel", body), { x, series: [{ label: "Fuel", values: fuel.map(f.volumeValue), bars: true }], yFmt: (v, t) => (t ? `${f.num(v)} ${f.volumeUnit()}` : f.num(v)), xFmt: dateFmt }));
}

function economy(body, { stats, byDay, x, snaps }, charts) {
  const income = byDay(stats.deliveries, "income");
  const penalties = byDay(stats.deliveries, "penalties");
  const distance = byDay(stats.deliveries, "distanceKm");
  const drive = byDay(stats.deliveries, "driveSeconds");
  const expByDay = new Map();
  const expByType = {};
  for (const e of stats.expenses) {
    expByDay.set(e.day, (expByDay.get(e.day) || 0) + e.amount);
    expByType[e.type] = (expByType[e.type] || 0) + e.amount;
  }
  const expenses = x.map((_, i) => (expByDay.get(new Date(x[i] * 1000).toISOString().slice(0, 10)) || 0) + penalties[i]);
  const totalIncome = income.reduce((a, b) => a + b, 0);
  const totalExp = expenses.reduce((a, b) => a + b, 0);
  const totalDist = distance.reduce((a, b) => a + b, 0);
  const totalDrive = drive.reduce((a, b) => a + b, 0);
  const pen = penalties.reduce((a, b) => a + b, 0);
  if (pen) expByType.cancellations = pen;
  if (!totalIncome && !totalExp && snaps.length < 2) return noHistory(body, "income");
  const labels = { toll: "Tolls", fine: "Fines", ferry: "Ferries", train: "Trains", cancellations: "Cancellation penalties" };
  body.innerHTML = html`
    ${kpis([
      ["Income", f.money(totalIncome), `${f.num(stats.deliveries.reduce((a, d) => a + d.delivered, 0))} deliveries`],
      ["Expenses", f.money(totalExp), "tolls, fines, ferries, penalties", "Costs recorded by telemetry. Fuel and repairs are shown per truck from the save."],
      ["Profit", f.money(totalIncome - totalExp)],
      [`Per ${f.distUnit()}`, totalDist ? f.money(totalIncome / f.distValue(totalDist)) : "—", "income per distance"],
      ["Per hour", totalDrive ? f.money(totalIncome / (totalDrive / 3600)) : "—", "income per real driving hour"],
    ])}
    <div class="grid">
      ${chartCard("cIncome", "Income per day", f.currencySymbol())}
      ${chartCard("cProfit", "Profit per day", "income − expenses")}
      ${chartCard("cBalance", "Company balance", "from saved games", "span-8 md-12")}
      ${card({ cls: "span-4 md-12", title: "Expenses by type", body: Object.keys(expByType).length ? raw(barList(Object.entries(expByType).filter(([, v]) => v > 0).map(([k, v]) => ({ label: labels[k] || k, value: v })), { fmt: (v) => f.money(v), muted: true })) : empty({ iconName: "receipt", title: "No expenses recorded", compact: true }) })}
      ${card({ cls: "span-12", title: "Top cargo by income", body: stats.byCargo.length ? raw(barList(stats.byCargo.map((c) => ({ label: c.cargo, value: c.income, tip: `${c.jobs} jobs · ${f.dist(c.distanceKm)}` })), { fmt: (v) => f.money(v) })) : empty({ iconName: "package", title: "No deliveries", compact: true }) })}
    </div>`.toString();
  charts.push(chart($("#cIncome", body), { x, series: [{ label: "Income", values: income, bars: true }], yFmt: (v, t) => (t ? f.money(v) : f.money(v, { compact: true })), xFmt: dateFmt }));
  charts.push(chart($("#cProfit", body), { x, series: [{ label: "Profit", values: income.map((v, i) => v - expenses[i]), fill: true }], yFmt: (v, t) => (t ? f.money(v) : f.money(v, { compact: true })), xFmt: dateFmt, yMin: Math.min(0, ...income.map((v, i) => v - expenses[i])) }));
  const bx = snaps.map((s) => Date.parse(s.t) / 1000);
  if (snaps.length > 1) charts.push(chart($("#cBalance", body), { x: bx, series: [{ label: "Balance", values: snaps.map((s) => s.money), fill: true }], yFmt: (v, t) => (t ? f.money(v) : f.money(v, { compact: true })), xFmt: dateFmt, yMin: Math.min(...snaps.map((s) => s.money)) * 0.95 }));
  else $("#cBalance", body).innerHTML = empty({ iconName: "wallet", title: "Balance history builds up over time", text: "HAULIX stores a snapshot each time ETS2 saves.", compact: true }).toString();
}

function fleet(body, { stats, profile }, charts) {
  if (!profile) { body.innerHTML = empty({ brand: true, title: "No profile loaded", text: "Fleet statistics come from your ETS2 save." }).toString(); return; }
  const trucks = [...profile.trucks].sort((a, b) => b.odometerKm - a.odometerKm);
  const usedTrucks = profile.trucks.filter((t) => t.driverId).length;
  const usedTrailers = profile.trailers.filter((t) => t.assignedTruckId || t.isPlayerTrailer).length;
  const slots = profile.garages.reduce((a, g) => a + g.slots, 0);
  body.innerHTML = html`
    ${kpis([
      ["Truck utilisation", f.pct(profile.trucks.length ? usedTrucks / profile.trucks.length : 0), `${usedTrucks} of ${profile.trucks.length} trucks have a driver`],
      ["Trailer utilisation", f.pct(profile.trailers.length ? usedTrailers / profile.trailers.length : 0), `${usedTrailers} of ${profile.trailers.length} in use`],
      ["Garage capacity", f.pct(slots ? profile.trucks.length / slots : 0), `${profile.trucks.length} trucks in ${slots} slots`],
      ["Fleet mileage", f.dist(profile.trucks.reduce((a, t) => a + t.odometerKm, 0))],
      ["Fleet revenue", f.money(profile.trucks.reduce((a, t) => a + t.profit.revenue, 0), { compact: true }), "from truck profit logs"],
    ])}
    <div class="grid">
      ${card({ cls: "span-6 md-12", title: "Truck mileage", body: raw(barList(trucks.map((t) => ({ label: t.name + (t.licensePlate ? ` · ${t.licensePlate}` : ""), value: t.odometerKm, tip: t.garageCity })), { fmt: (v) => f.dist(v) })) })}
      ${card({ cls: "span-6 md-12", title: "Truck profit", meta: "save profit logs", body: raw(barList([...profile.trucks].sort((a, b) => b.profit.profit - a.profit.profit).map((t) => ({ label: t.name, value: Math.max(0, t.profit.profit), tip: `Revenue ${f.money(t.profit.revenue)}` })), { fmt: (v) => f.money(v) })) })}
      ${card({ cls: "span-6 md-12", title: "Garage utilisation", body: raw(barList(profile.garages.map((g) => ({ label: `${g.city}${g.isHq ? " (HQ)" : ""}`, value: g.slots ? Math.min(g.trucksAssigned, g.driversAssigned) / g.slots : 0, tip: `${g.trucksAssigned}/${g.slots} trucks · ${g.driversAssigned}/${g.slots} drivers` })), { fmt: (v) => f.pct(v), muted: true })) })}
      ${card({ cls: "span-6 md-12", title: "Distance by truck", meta: "recorded deliveries", body: stats.byTruck.length ? raw(barList(stats.byTruck.map((t) => ({ label: t.truck, value: t.distanceKm, tip: `${t.jobs} jobs · ${f.money(t.income)}` })), { fmt: (v) => f.dist(v) })) : empty({ iconName: "truck", title: "No recorded deliveries", compact: true }) })}
    </div>`.toString();
}

function drivers(body, { profile, x }, charts) {
  const ai = profile?.drivers.filter((d) => !d.isPlayer) || [];
  if (!ai.length) { body.innerHTML = empty({ brand: true, title: "No AI drivers", text: "Hire drivers in ETS2 to see their income, distance and profitability here." }).toString(); return; }
  const dayMap = new Map();
  for (const d of ai) for (const day of d.profit.days || []) {
    const e = dayMap.get(day.day) || { revenue: 0, costs: 0 };
    e.revenue += day.revenue; e.costs += day.costs; dayMap.set(day.day, e);
  }
  const gameDays = [...dayMap.keys()].sort((a, b) => a - b);
  body.innerHTML = html`
    ${kpis([
      ["AI revenue", f.money(ai.reduce((a, d) => a + d.profit.revenue, 0), { compact: true })],
      ["AI profit", f.money(ai.reduce((a, d) => a + d.profit.profit, 0), { compact: true })],
      ["AI distance", f.dist(ai.reduce((a, d) => a + d.profit.distanceKm, 0))],
      ["Driving now", String(ai.filter((d) => d.status === "driving" || d.status === "on_job").length), `of ${ai.length} drivers`],
      ["Average XP", f.num(ai.reduce((a, d) => a + d.xp, 0) / ai.length)],
    ])}
    <div class="grid">
      ${chartCard("cAiDays", "AI revenue by game day", f.currencySymbol(), "span-12")}
      ${card({ cls: "span-6 md-12", title: "Driver profit", body: raw(barList([...ai].sort((a, b) => b.profit.profit - a.profit.profit).map((d) => ({ label: d.name, value: Math.max(0, d.profit.profit), tip: `Revenue ${f.money(d.profit.revenue)} · margin ${d.profit.revenue ? f.pct(d.profit.profit / d.profit.revenue) : "—"}` })), { fmt: (v) => f.money(v) })) })}
      ${card({ cls: "span-6 md-12", title: "Driver distance", body: raw(barList([...ai].sort((a, b) => b.profit.distanceKm - a.profit.distanceKm).map((d) => ({ label: d.name, value: d.profit.distanceKm, tip: `${d.profit.jobs} jobs` })), { fmt: (v) => f.dist(v), muted: true })) })}
      ${card({ cls: "span-6 md-12", title: "Driver experience", body: raw(barList([...ai].sort((a, b) => b.xp - a.xp).map((d) => ({ label: d.name, value: d.xp })), { fmt: (v) => `${f.num(v)} XP`, muted: true })) })}
      ${card({ cls: "span-6 md-12", title: "Profitability", meta: "profit per km", body: raw(barList([...ai].filter((d) => d.profit.distanceKm).sort((a, b) => b.profit.profit / b.profit.distanceKm - a.profit.profit / a.profit.distanceKm).map((d) => ({ label: d.name, value: Math.max(0, d.profit.profit / f.distValue(d.profit.distanceKm)) })), { fmt: (v) => `${f.money(v)}/${f.distUnit()}` })) })}
    </div>`.toString();
  if (gameDays.length) charts.push(chart($("#cAiDays", body), {
    x: gameDays, series: [{ label: "Revenue", values: gameDays.map((d) => dayMap.get(d).revenue), bars: true }],
    yFmt: (v, t) => (t ? f.money(v) : f.money(v, { compact: true })), xFmt: (v) => `D${v}`, tipX: (v) => `Game day ${v}`,
  }));
  else $("#cAiDays", body).innerHTML = empty({ iconName: "chart-column", title: "No driver history in the save yet", compact: true }).toString();
}
