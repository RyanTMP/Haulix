import { html, $, cx } from "../core/html.js";
import { icon } from "../core/icons.js";
import { store } from "../core/store.js";
import { card, empty, progress, kv, skeleton } from "../components/ui.js";
import { chart } from "../components/charts.js";
import { scoreColor } from "../components/sharecard.js";
import { teleValues, jobProgress } from "../core/teleview.js";
import { t } from "../core/i18n.js";
import * as f from "../core/format.js";

// Current job, like the tour page of VTC trackers: route and progress, real-time ETA, live driving score,
// costs (tolls, ferries, fines), a speed profile and a timeline of everything that happened on this job.
const EVENT = {
  start: ["flag", "Job accepted"], fine: ["receipt", "Fine"], toll: ["badge-euro", "Toll gate"], ferry: ["anchor", "Ferry"],
  train: ["train-front", "Train"], refuel: ["fuel", "Refuelled"],
};

const tile = (label, value, sub = "", cls = "") => html`<div class="${cx("job-kpi", cls)}"><div class="job-kpi__label">${label}</div><div class="job-kpi__value">${value}</div>${sub ? html`<div class="job-kpi__sub">${sub}</div>` : ""}</div>`;

export default {
  title: "Current job",
  crumb: () => "Live data of the job you are driving",

  render() {
    return html`<div class="page-max stack" id="jobRoot">${skeleton(4)}</div>`;
  },

  async mount(root, { call }) {
    const el = $("#jobRoot", root);
    let data = null;
    let speedChart = null;
    let lastKey = "";

    const drawEmpty = async () => {
      speedChart?.destroy(); speedChart = null; lastKey = "";
      const recent = await call("logbook.recent", { limit: 1 }).catch(() => []);
      const last = recent?.[0];
      el.innerHTML = html`${card({ body: empty({ iconName: "briefcase", title: "No active job",
        text: store.get("status")?.telemetry === "live" ? "Take a job from the job market, a cargo market or your company – HAULIX shows everything about it here while you drive." : "Start ETS2 and take a job – HAULIX shows its route, ETA, costs and your driving score here while you drive." }) })}
        ${last ? card({ title: "Last delivery", meta: html`<a class="link" href="#/logbook">${t("Open logbook")}</a>`, body: html`<div class="job-last">
          <div><div class="job-last__route">${last.originCity} → ${last.destCity}</div><div class="muted">${last.cargo} · ${f.dateTime(last.finishedUtc)}</div></div>
          <div class="job-last__stats"><span>${f.dist(last.distanceKm)}</span><span class="ok">${f.money(last.income)}</span>${last.score != null ? html`<span class="score-pill" style="--c:${scoreColor(last.score)}">${last.score}</span>` : ""}</div></div>` }) : ""}`.toString();
    };

    const drawJob = () => {
      const s = store.get("telemetry")?.snapshot;
      const d = data?.detail;
      if (!s?.onJob) { if (lastKey !== "none") { lastKey = "none"; drawEmpty(); } return; }
      const v = teleValues(s);
      const key = `${s.sourceCityId}|${s.destinationCityId}|${s.cargoId}`;
      if (key !== lastKey) { lastKey = key; speedChart?.destroy(); speedChart = null; el.innerHTML = layout(s); }
      const eta = s.eta;
      const driven = d?.distanceKm ?? 0;
      const planned = s.plannedDistanceKm || (eta ? driven + eta.remainingKm : 0);
      const prog = jobProgress(s);
      const margin = eta?.deadlineMarginGameMinutes;
      const costs = (d?.tollTotal ?? 0) + (d?.ferryTotal ?? 0) + (d?.fineTotal ?? 0);
      const score = d?.score;

      $("[data-job=progress]", el).innerHTML = html`<div class="job-progress__nums"><span><b>${f.dist(driven)}</b> ${t("driven")}</span><span class="accent"><b>${v.remaining}</b> ${t("remaining")}</span><span><b>${f.dist(planned)}</b> ${t("planned")}</span></div>
        ${progress(prog, { thick: true })}<div class="job-progress__pct">${f.num(prog * 100)} %</div>`.toString();
      $("[data-job=eta]", el).innerHTML = html`
        ${tile("Real-time ETA", v.etaReal, v.arrivalClock, "is-accent")}
        ${tile("Game ETA", v.eta, eta?.source === "haulix" ? t("HAULIX route") : t("In-game navigation"))}
        ${tile(margin != null && margin < 0 ? "Late by" : "Deadline buffer", margin == null ? "—" : f.duration(Math.abs(margin) * 60, { short: true }), v.deadline !== "—" ? `${t("Deadline in")} ${v.deadline}` : "", margin == null ? "" : margin < 0 ? "is-crit" : margin < 60 ? "is-warn" : "is-ok")}
        ${tile("Speed", f.speed(Math.abs(s.speedKmh)), v.speedLimit ? `${t("limit")} ${v.speedLimit}` : "", s.speedLimitKmh > 1 && s.speedKmh > s.speedLimitKmh + 5 ? "is-crit" : "")}`.toString();

      $("[data-job=score]", el).innerHTML = score
        ? html`<div class="job-score" style="--c:${scoreColor(score.score)};--p:${score.score}"><div class="job-score__ring"><b>${score.score}</b><small>/ 100</small></div>
            <div class="job-score__list">${[["Speeding", score.speedingPenalty, `${f.num(score.speedingPct, 1)} %`], ["Cargo damage", score.cargoPenalty, `${f.num(score.cargoDamagePct, 1)} %`],
              ["Truck damage", score.truckPenalty, `${f.num(score.truckDamagePct, 1)} %`], ["Fines", score.finePenalty, String(score.fines)], ["Late", score.latePenalty, score.late ? t("yes") : t("no")]]
              .map(([l, p, val]) => html`<div class="${cx("job-score__row", p > 0 && "is-bad")}"><span>${t(l)}</span><span class="faint">${val}</span><b>${p > 0 ? `−${p}` : "0"}</b></div>`)}</div></div>`.toString()
        : html`<div class="muted" style="padding:6px 0">${t("The live score appears after the first minute of driving.")}</div>`.toString();

      $("[data-job=stats]", el).innerHTML = html`
        ${tile("Average speed", d ? f.speed(d.avgSpeedKmh) : "—")}
        ${tile("Top speed", d ? f.speed(d.maxSpeedKmh) : "—")}
        ${tile("Driving time", d ? f.duration(d.driveSeconds, { short: true }) : "—")}
        ${tile("Fuel used", d ? f.volume(d.fuelUsedL) : "—", d?.consumptionL100 ? `${f.num(d.consumptionL100, 1)} l/100 km` : "")}
        ${tile("Speeding", d ? `${f.num(d.speedingPct, 1)} %` : "—", d ? f.duration(d.speedingSeconds, { short: true }) : "", d?.speedingPct > 5 ? "is-warn" : "")}
        ${tile("Cargo damage", v.cargoDamage, "", s.cargoDamage >= 0.05 ? "is-warn" : "")}
        ${tile("Truck wear", v.damage, d?.truckDamageDelta > 0.001 ? `+${f.pct(d.truckDamageDelta, 1)} ${t("on this job")}` : "")}
        ${tile("Trailer wear", v.trailerDamage, d?.trailerDamageDelta > 0.001 ? `+${f.pct(d.trailerDamageDelta, 1)} ${t("on this job")}` : "")}`.toString();

      $("[data-job=money]", el).innerHTML = kv([
        ["Income", html`<span class="ok">${f.money(s.jobIncome)}</span>`, { icon: "coins" }],
        ["Tolls", d?.tolls ? `${f.money(d.tollTotal)} · ${d.tolls}×` : f.money(0), { icon: "badge-euro" }],
        ["Ferries & trains", d?.ferries ? `${f.money(d.ferryTotal)} · ${d.ferries}×` : f.money(0), { icon: "anchor" }],
        ["Fines", d?.fines ? html`<span class="crit">${f.money(d.fineTotal)} · ${d.fines}×</span>` : f.money(0), { icon: "receipt" }],
        ["Refuelled", d?.refuels ? `${f.volume(d.refuelLitres)} · ${d.refuels}×` : "—", { icon: "fuel" }],
        ["Income after costs", html`<b>${f.money(s.jobIncome - costs)}</b>`, { icon: "wallet", tip: "Income minus tolls, ferries/trains and fines on this job (fuel is paid from your company account)." }],
        ["Per " + f.distUnit(), planned ? f.money((s.jobIncome - costs) / f.distValue(planned)) : "—", { icon: "route" }],
      ]).toString();

      const tl = d?.timeline || [];
      $("[data-job=timeline]", el).innerHTML = tl.length ? html`<ol class="job-timeline">${[...tl].reverse().map((e) => {
        const [ic, label] = EVENT[e.type] || ["circle-dot", e.type];
        return html`<li class="job-timeline__item is-${e.type}"><span class="job-timeline__icon">${icon(ic)}</span><div class="grow"><strong>${t(label)}</strong>
          <div class="faint">${e.detail || ""}</div></div><div class="job-timeline__meta">${e.amount && e.type !== "start" ? html`<b>${f.money(e.amount)}</b>` : ""}<span>${f.time(e.atUtc)} · ${f.dist(e.km, e.km < 10 ? 1 : 0)}</span></div></li>`;
      })}</ol>`.toString() : html`<div class="muted">${t("Tolls, ferries, fines and refuelling on this job appear here.")}</div>`.toString();

      // Speed profile (redrawn only when new points arrived)
      const prof = d?.profile || [];
      const box = $("[data-job=chart]", el);
      if (box && prof.length > 1) {
        const x = prof.map((p) => f.distValue(p[0]));
        const sp = prof.map((p) => f.speedValue(p[1]));
        const lim = prof.map((p) => (p[2] > 1 ? f.speedValue(p[2]) : null));
        if (!speedChart) {
          box.innerHTML = "";
          speedChart = chart(box, {
            x, height: 190,
            series: [{ label: t("Speed"), values: sp, fill: true }, { label: t("Limit"), values: lim, color: "comp" }],
            yFmt: (val, tip) => (tip ? `${f.num(val)} ${f.speedUnit()}` : f.num(val)),
            xFmt: (val) => `${f.num(val)} ${f.distUnit()}`,
          });
          speedChart.points = prof.length;
        } else if (speedChart.points !== prof.length) {
          speedChart.update(x, [sp, lim]);
          speedChart.points = prof.length;
        }
      } else if (box && !speedChart) box.innerHTML = html`<div class="muted" style="padding:40px 0;text-align:center">${t("The speed profile builds up while you drive.")}</div>`.toString();
    };

    const layout = (s) => {
      const v = teleValues(s);
      return html`
      <section class="card job-hero"><div class="card__body">
        <div class="job-hero__top">
          <div class="grow" style="min-width:0">
            <div class="job-hero__kicker"><i></i>${t("CURRENT JOB")}${s.specialJob ? html`<span class="badge badge--accent">${icon("star", "icon icon-sm")}${t("Special transport")}</span>` : ""}
              ${v.market !== "—" ? html`<span class="badge badge--outline">${t(v.market)}</span>` : ""}</div>
            <h2 class="job-hero__route">${s.sourceCity} <span>→</span> ${s.destinationCity}</h2>
            <div class="job-hero__companies">${s.sourceCompany || "—"} ${icon("arrow-right", "icon icon-sm")} ${s.destinationCompany || "—"}</div>
          </div>
          <div class="job-hero__income"><div class="label label--muted">${t("Income")}</div><div class="job-hero__money">${f.money(s.jobIncome)}</div></div>
        </div>
        <div class="job-hero__cargo">
          <span>${icon("package")}<b>${s.cargo}</b></span><span>${icon("weight")}${v.cargoMass}</span>
          <span>${icon("truck")}${v.truck}</span><span>${icon("container")}${v.trailer}</span>
          <a class="btn btn--sm btn--ghost" href="#/map" style="margin-left:auto">${icon("map")}${t("Show on map")}</a>
        </div>
        <div class="job-progress" data-job="progress"></div>
        <div class="job-kpis job-kpis--eta" data-job="eta"></div>
      </div></section>
      <div class="job-cols">
        ${card({ title: "Driving score", meta: html`<span class="faint">${t("live")}</span>`, body: html`<div data-job="score"></div>` })}
        ${card({ title: "Money", body: html`<div data-job="money"></div>` })}
      </div>
      ${card({ title: "This job so far", body: html`<div class="job-kpis" data-job="stats"></div>` })}
      <div class="job-cols job-cols--wide">
        ${card({ title: "Speed profile", meta: html`<span class="faint">${t("speed and limit over the distance")}</span>`, body: html`<div data-job="chart"></div>` })}
        ${card({ title: "Timeline", body: html`<div data-job="timeline"></div>` })}
      </div>`.toString();
    };

    const load = async () => {
      try { data = await call("job.current"); } catch { data = null; }
      drawJob();
    };
    await load();
    const poll = setInterval(load, 3000);
    let raf = 0;
    const offTele = store.on("telemetry", () => { if (!raf) raf = setTimeout(() => { raf = 0; drawJob(); }, 1000); });
    return () => { clearInterval(poll); clearTimeout(raf); offTele(); speedChart?.destroy(); };
  },
};
