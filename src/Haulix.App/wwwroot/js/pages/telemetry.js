import { html, bindText, cx, $ } from "../core/html.js";
import { icon } from "../core/icons.js";
import { store, isLive } from "../core/store.js";
import { card, kv, progress, empty, damageTone } from "../components/ui.js";
import { chart } from "../components/charts.js";
import { teleValues, jobProgress, nearestCity, cityPositions, locationLabel } from "../core/teleview.js";
import { loadCities } from "../core/cities.js";
import * as f from "../core/format.js";

const LAMPS = [
  ["engineOn", "Engine", "power", "ok"], ["electricOn", "Electrics", "zap", "ok"], ["parkingBrake", "Parking brake", "circle-parking", "warn"],
  ["engineBrake", "Engine brake", "octagon-alert", ""], ["cruiseControl", "Cruise control", "gauge", ""], ["retarder", "Retarder", "disc", ""],
  ["lightsLowBeam", "Low beam", "lightbulb", ""], ["lightsHighBeam", "High beam", "sun", ""], ["lightsBeacon", "Beacons", "siren", "warn"],
  ["lightsHazard", "Hazards", "triangle-alert", "warn"], ["blinkerLeft", "Left blinker", "chevron-left", "ok"], ["blinkerRight", "Right blinker", "chevron-right", "ok"],
  ["wipers", "Wipers", "droplets", ""], ["differentialLock", "Diff lock", "cog", ""], ["liftAxle", "Lift axle", "arrow-up", ""], ["fuelWarning", "Low fuel", "fuel", "warn"],
];

export default {
  title: "Telemetry",
  crumb: () => {
    const s = store.get("status");
    return s?.telemetry === "live" ? "Live from ETS2" : s?.telemetry === "demo" ? "Simulated drive (demo)" : "Last known values";
  },

  render() {
    const fields = store.get("settings")?.telemetry?.fields || {};
    const show = (k) => fields[k] !== false;
    return html`<div class="page-max" id="tele">
      <div id="teleNotice"></div>
      <div class="tele-hero live-only">
        ${[
          ["Speed", "speed", "speedUnit"], ["Engine", "rpm", null, "rpm"], ["Gear", "gear", "gearCount"],
          ["Fuel", "fuel", "fuelPct"], ["Range", "fuelRange"], ["Damage", "damage"],
        ].map(([l, k, sub, unit]) => html`<div><div class="eyebrow">${l}</div><div class="v" data-bind="${k}">—</div>
          <div class="faint" style="font-size:12px;margin-top:2px">${sub ? html`<span data-bind="${sub}"></span>` : unit || ""}</div></div>`)}
      </div>

      <div class="tele-layout">
        ${card({
          title: "Speed & engine", cls: "tl-8", meta: html`<span>Last 5 minutes</span>`,
          body: html`<div class="tele-charts"><div><div class="eyebrow" style="margin-bottom:6px">Speed · <span data-bind="speedUnit"></span></div><div id="speedChart"></div></div>
            <div><div class="eyebrow" style="margin-bottom:6px">Engine · rpm</div><div id="rpmChart"></div></div></div>`,
        })}
        ${show("job") ? card({ title: "Current job", cls: "tl-4", meta: html`<span data-bind="market"></span>`, body: html`<div id="jobBody"></div>` }) : ""}

        ${show("vehicle") ? card({ title: "Vehicle", cls: "tl-4", body: kv([
          ["Truck", html`<span data-bind="truck">—</span>`, { icon: "truck", text: true }],
          ["License plate", html`<span data-bind="plate">—</span>`, { icon: "id-card", text: true }],
          ["Odometer", html`<span data-bind="odometer">—</span>`, { icon: "gauge" }],
          ["Transmission", html`<span data-bind="shifter">—</span>`, { icon: "cog", text: true }],
          ["Gears", html`<span data-bind="gearCount">—</span>`, { icon: "list" }],
          ["Retarder", html`<span data-bind="retarder">—</span>`, { icon: "disc" }],
          ["Cruise control", html`<span data-bind="cruise">—</span>`, { icon: "gauge" }],
          ["Speed limit", html`<span data-bind="speedLimitFull">—</span>`, { icon: "octagon-alert" }],
        ]) }) : ""}
        ${show("fluids") ? card({ title: "Fluids & gauges", cls: "tl-4", body: kv([
          ["Fuel", html`<span data-bind="fuel">—</span> / <span data-bind="fuelCap">—</span>`, { icon: "fuel" }],
          ["Average consumption", html`<span data-bind="fuelAvg">—</span>`, { icon: "activity" }],
          ["AdBlue", html`<span data-bind="adblue">—</span>`, { icon: "droplet" }],
          ["Air pressure", html`<span data-bind="air">—</span>`, { icon: "gauge" }],
          ["Oil", html`<span data-bind="oilP">—</span> · <span data-bind="oilT">—</span>`, { icon: "droplets" }],
          ["Coolant", html`<span data-bind="waterT">—</span>`, { icon: "thermometer" }],
          ["Brakes", html`<span data-bind="brakeT">—</span>`, { icon: "disc" }],
          ["Battery", html`<span data-bind="battery">—</span>`, { icon: "zap" }],
        ]) }) : ""}
        ${show("damage") ? card({ title: "Damage", cls: "tl-4", meta: html`<span data-bind="damage">—</span>`, body: html`<div id="wear"></div>` }) : ""}

        ${show("navigation") ? card({ title: "Location & navigation", cls: "tl-4", body: html`
          <div class="row" style="gap:18px;margin-bottom:10px">
            <div class="compass"><span class="n">N</span><span class="e">E</span><span class="s">S</span><span class="w">W</span>
              <svg id="needle" width="40" height="40" viewBox="-20 -20 40 40"><path d="M0 -15 L6 8 L0 4 L-6 8 Z" fill="var(--accent)"/></svg></div>
            <div class="stat"><div class="stat__value" style="font-size:18px;white-space:normal" data-bind="location">—</div><div class="stat__label" data-bind="heading">—</div></div>
          </div>
          ${kv([
            ["Route remaining", html`<span data-bind="remaining">—</span>`, { icon: "route" }],
            ["Arrival (real time)", html`<span data-bind="etaReal">—</span> <span class="faint" data-bind="arrivalClock"></span>`, { icon: "timer", tip: "How long you still drive in real minutes (game time runs about 19× faster)" }],
            ["Game ETA", html`<span data-bind="eta">—</span> <span class="faint" data-bind="onTime"></span>`, { icon: "clock", tip: "Navigation estimate in in-game time" }],
            ["Game time", html`<span data-bind="gameTime">—</span>`, { icon: "calendar" }],
            ["Next rest stop", html`<span data-bind="restStop">—</span>`, { icon: "moon" }],
            ["World position", html`<span data-bind="coords">—</span>`, { icon: "crosshair" }],
          ], { compact: true })}` }) : ""}
        ${show("trailer") ? card({ title: "Trailer", cls: "tl-4", body: kv([
          ["Trailer", html`<span data-bind="trailer">—</span>`, { icon: "container", text: true }],
          ["Body type", html`<span data-bind="trailerBody">—</span>`, { icon: "package", text: true }],
          ["Plate", html`<span data-bind="trailerPlate">—</span>`, { icon: "id-card", text: true }],
          ["Trailer damage", html`<span data-bind="trailerDamage">—</span>`, { icon: "wrench" }],
          ["Cargo damage", html`<span data-bind="cargoDamage">—</span>`, { icon: "package" }],
        ]) }) : ""}
        ${card({ title: "Session", cls: "tl-4", body: kv([
          ["Source", html`<span data-bind="game">—</span>`, { icon: "monitor", text: true }],
          ["Plugin", html`<span data-bind="plugin">—</span>`, { icon: "plug" }],
          ["Distance this job", html`<span data-bind="driven">—</span>`, { icon: "route" }],
          ["Fuel used this job", html`<span data-bind="jobFuel">—</span>`, { icon: "fuel" }],
          ["Average / top speed", html`<span data-bind="jobAvg">—</span> · <span data-bind="jobMax">—</span>`, { icon: "activity" }],
          ["Last update", html`<span data-bind="lastUpdate">—</span>`, { icon: "clock" }],
        ]) })}

        ${show("lights") ? card({ title: "Controls & lights", cls: "tl-8", body: html`<div class="lamps">${LAMPS.map(([k, l, i, tone]) => html`<div class="${cx("lamp", tone && `lamp--${tone}`)}" data-lamp="${k}">${icon(i)}${l}</div>`)}</div>` }) : ""}
        ${show("drivetrain") ? card({ title: "Driver inputs", cls: "tl-4", body: html`
          <div class="pedals">
            <div class="pedal"><div class="track"><i data-pedal="throttle"></i></div>Throttle</div>
            <div class="pedal pedal--brake"><div class="track"><i data-pedal="brake"></i></div>Brake</div>
            <div class="pedal pedal--clutch"><div class="track"><i data-pedal="clutch"></i></div>Clutch</div>
          </div>` }) : ""}
      </div>
    </div>`;
  },

  async mount(root) {
    const cities = await loadCities();
    const known = [...cityPositions([], cities.list).values()];
    const h = store.get("history");
    const speedChart = chart($("#speedChart", root), {
      x: h.map((r) => r[0]), time: true, height: 200,
      series: [{ label: "Speed", values: h.map((r) => f.speedValue(r[1])), fill: true }],
      yFmt: (v, tip) => (tip ? f.speed(v / (f.imperial() ? 0.621371 : 1)) : f.num(v)),
      xFmt: (v) => new Date(v * 1000).toLocaleTimeString([], { minute: "2-digit", second: "2-digit" }),
      tipX: (v) => new Date(v * 1000).toLocaleTimeString(),
    });
    const rpmChart = chart($("#rpmChart", root), {
      x: h.map((r) => r[0]), time: true, height: 200,
      series: [{ label: "Engine", values: h.map((r) => r[2]), color: "comp" }],
      yFmt: (v, tip) => (tip ? `${f.num(v)} rpm` : f.num(v)),
      xFmt: (v) => new Date(v * 1000).toLocaleTimeString([], { minute: "2-digit", second: "2-digit" }),
      tipX: (v) => new Date(v * 1000).toLocaleTimeString(),
    });
    let lastChart = 0;
    let jobShown = null;

    const update = () => {
      const t = store.get("telemetry");
      const s = t?.snapshot;
      const live = isLive();
      root.querySelector("#tele").classList.toggle("disconnected", !live);
      const notice = $("#teleNotice", root);
      const status = store.get("status");
      const noticeHtml = live ? "" : html`<div class="callout ${status?.telemetry === "unsupported" ? "callout--crit" : ""}" style="margin-bottom:var(--gutter)">${icon(status?.telemetry === "unsupported" ? "triangle-alert" : "wifi-off")}
        <div><strong>${status?.telemetry === "unsupported" ? "Unsupported telemetry plugin" : status?.game === "running" ? "Waiting for telemetry" : "ETS2 is offline"}</strong> ·
        ${status?.telemetry === "unsupported" ? `scs-telemetry revision ${status.pluginRevision} is not supported. HAULIX expects revision 12; update the plugin.` : s ? `Showing the last values received ${f.ago(status?.lastSampleUtc)}.` : "Start the game to see live telemetry."}</div></div>`.toString();
      if (notice.innerHTML !== noticeHtml) notice.innerHTML = noticeHtml;
      if (!s) return;

      const v = teleValues(s);
      const near = nearestCity(s, known);
      v.location = locationLabel(near);
      v.speedLimitFull = s.speedLimitKmh > 1 ? f.speed(s.speedLimitKmh) : "None";
      v.driven = t.job ? f.dist(t.job.distanceKm, 1) : "—";
      v.jobFuel = t.job ? f.volume(t.job.fuelUsedL, 1) : "—";
      v.jobAvg = t.job?.avgSpeedKmh ? f.speed(t.job.avgSpeedKmh) : "—";
      v.jobMax = t.job?.maxSpeedKmh ? f.speed(t.job.maxSpeedKmh) : "—";
      v.lastUpdate = f.time(s.capturedUtc) + (live ? "" : ` (${f.ago(s.capturedUtc)})`);
      bindText(root, v);

      for (const el of root.querySelectorAll("[data-pedal]")) el.style.height = `${Math.round((s[el.dataset.pedal] || 0) * 100)}%`;
      for (const el of root.querySelectorAll("[data-lamp]")) {
        const k = el.dataset.lamp;
        const on = k === "retarder" ? s.retarderLevel > 0 : !!s[k];
        el.classList.toggle("is-on", on);
      }
      const needle = $("#needle", root);
      if (needle) needle.style.transform = `rotate(${s.headingDeg}deg)`;

      const wear = $("#wear", root);
      if (wear) {
        wear.innerHTML = [
          ["Engine", s.wearEngine], ["Transmission", s.wearTransmission], ["Cabin", s.wearCabin], ["Chassis", s.wearChassis], ["Wheels", s.wearWheels],
          ...(s.trailerAttached ? [["Trailer", s.trailerDamage]] : []), ...(s.onJob ? [["Cargo", s.cargoDamage]] : []),
        ].map(([l, x]) => html`<div class="wear-row"><span class="muted">${l}</span>${progress(Math.max(x, 0.004), { tone: damageTone(x), thin: true })}<span class="v">${f.pct(x, 1)}</span></div>`.toString()).join("");
      }

      const jb = $("#jobBody", root);
      if (jb) {
        const key = s.onJob ? `${s.sourceCityId}${s.destinationCityId}${s.cargoId}` : "none";
        if (key !== jobShown) {
          jobShown = key;
          jb.innerHTML = s.onJob
            ? html`<div class="row" style="justify-content:space-between;margin-bottom:10px"><strong data-bind="cargo">—</strong><span class="num ok" data-bind="income">—</span></div>
              <div class="muted" style="font-size:12px;margin-bottom:12px"><span data-bind="from"></span> <span class="faint" data-bind="fromCo"></span> → <span data-bind="to"></span> <span class="faint" data-bind="toCo"></span></div>
              <div id="jobProg" style="margin-bottom:12px"></div>
              ${kv([["Cargo mass", html`<span data-bind="cargoMass">—</span>`, { icon: "weight" }], ["Planned distance", html`<span data-bind="planned">—</span>`, { icon: "route" }],
                ["Remaining", html`<span data-bind="remaining">—</span>`, { icon: "milestone" }], ["Deadline", html`<span data-bind="deadline">—</span>`, { icon: "timer" }]], { compact: true })}`.toString()
            : empty({ iconName: "package", title: "No active job", text: "Accept a job in ETS2 to track cargo, route progress and deadline.", compact: true }).toString();
          bindText(jb, v);
        }
        const jp = $("#jobProg", root);
        if (jp) jp.innerHTML = progress(jobProgress(s)).toString();
      }

      const now = Date.now();
      if (now - lastChart > 1000) {
        lastChart = now;
        const hist = store.get("history");
        speedChart.update(hist.map((r) => r[0]), [hist.map((r) => f.speedValue(r[1]))]);
        rpmChart.update(hist.map((r) => r[0]), [hist.map((r) => r[2])]);
      }
    };
    update();
    const offs = [store.on("telemetry", update), store.on("status", update)];
    return () => { offs.forEach((o) => o()); speedChart.destroy(); rpmChart.destroy(); };
  },
};
