import { html, raw, $ } from "../core/html.js";
import { icon, truckMarkerSvg } from "../core/icons.js";
import { card, kv, progress, slots, driverStatus, toggle, segmented, select, empty, skeleton, toast, modal, confirm, statTile } from "../components/ui.js";
import { chart, sparkline, gauge, barList } from "../components/charts.js";

const TOKENS = [
  ["--bg", "Background"], ["--surface-1", "Surface 1"], ["--surface-2", "Surface 2"], ["--surface-3", "Surface 3"], ["--border", "Border"],
  ["--text", "Primary"], ["--text-2", "Secondary"], ["--text-3", "Tertiary"],
  ["--accent", "Accent"], ["--ok", "Success"], ["--warn", "Warning"], ["--crit", "Critical"], ["--info", "Info"], ["--chart-2", "Comparison"],
];

export default {
  title: "Design system",
  crumb: () => "HAULIX UI reference",

  render() {
    return html`<div class="page-max stack">
      ${card({ title: "Colour", meta: "One accent for interaction · state colours carry meaning", body: html`<div class="ds-swatches">${TOKENS.map(([v, l]) => html`<div class="ds-swatch"><div style="background:var(${v})"></div><p>${l}<br>${v}</p></div>`)}</div>` })}
      <div class="grid">
        ${card({ cls: "span-6 md-12", title: "Typography", body: html`<div class="ds-type">
          <div class="display" style="font-size:44px;letter-spacing:.04em">Barlow Condensed</div>
          <div class="label">Section label with slanted tick</div>
          <div style="font-size:15px">Inter: interface text for dense, readable data layouts.</div>
          <div class="muted">Secondary text for supporting detail.</div>
          <div class="num" style="font-size:32px">€ 124,690 · 4,832 km</div>
          <div class="eyebrow">Eyebrow · 11px caps</div></div>` })}
        ${card({ cls: "span-6 md-12", title: "Buttons & inputs", body: html`
          <div class="row" style="flex-wrap:wrap;margin-bottom:14px"><button class="btn btn--primary">${icon("play")}Primary</button><button class="btn">${icon("download")}Secondary</button><button class="btn btn--ghost">Ghost</button><button class="btn btn--danger">Danger</button><button class="btn is-loading">Loading</button><button class="btn btn--icon">${icon("settings")}</button></div>
          <div class="row" style="flex-wrap:wrap;gap:12px;margin-bottom:14px"><div class="search" style="width:220px">${icon("search")}<input class="input" placeholder="Search…"></div>${select("x", [["a", "Select"], ["b", "Option B"]], "a", 'style="width:160px"')}${toggle("t", true)}${toggle("t2", false)}</div>
          ${segmented("demo", [["a", "Day"], ["b", "Week"], ["c", "Month"]], "b")}` })}
        ${card({ cls: "span-4 md-6 sm-12", title: "Status", body: html`
          <div class="row" style="flex-wrap:wrap;margin-bottom:14px"><span class="badge badge--ok">Delivered</span><span class="badge badge--warn">Late</span><span class="badge badge--crit">Damaged</span><span class="badge badge--info">Resting</span><span class="badge badge--accent">Your truck</span><span class="badge">Idle</span></div>
          <div class="row" style="flex-wrap:wrap;margin-bottom:14px">${["driving", "on_job", "resting", "available", "unknown"].map((s) => driverStatus(s))}</div>
          <div class="row" style="gap:14px"><span class="chip chip--live"><span class="dot dot--ok dot--live"></span><strong>LIVE</strong>telemetry</span><span class="chip"><span class="dot"></span><strong>OFFLINE</strong>last seen 2h ago</span></div>` })}
        ${card({ cls: "span-4 md-6 sm-12", title: "Meters", body: html`
          <div class="col" style="gap:14px">${progress(0.62)}${progress(0.18, { tone: "warn" })}${progress(0.04, { tone: "ok", thin: true })}${progress(0.4, { tone: "muted", thick: true })}${slots(5, 3)}${slots(5, 4, { muted: true })}</div>` })}
        ${card({ cls: "span-4 md-12", title: "Map markers", body: html`
          <div class="row" style="gap:28px;align-items:center;justify-content:center;padding:10px 0">
            <div class="col" style="align-items:center">${raw(truckMarkerSvg(35, 40))}<span class="faint" style="font-size:11px">Current truck</span></div>
            <div class="col" style="align-items:center"><div class="garage-marker is-hq"><span>HQ</span></div><span class="faint" style="font-size:11px">Headquarters</span></div>
            <div class="col" style="align-items:center"><div class="garage-marker"><span>G</span></div><span class="faint" style="font-size:11px">Garage</span></div>
            <div class="col" style="align-items:center"><div class="driver-marker"></div><span class="faint" style="font-size:11px">AI driver</span></div>
          </div>` })}
        ${card({ cls: "span-8 md-12", title: "Chart", meta: "single accent series · crosshair tooltip", body: html`<div id="dsChart"></div>` })}
        ${card({ cls: "span-4 md-12", title: "Gauge & sparkline", body: html`<div id="dsGauge" style="max-width:220px;margin:0 auto"></div><div style="margin-top:12px">${raw(sparkline([4, 6, 5, 8, 7, 9, 12, 10, 14], { h: 36 }))}</div>` })}
        ${card({ cls: "span-6 md-12", title: "Data table", bodyCls: "card__body--flush", body: html`<table class="table"><thead><tr><th>Route</th><th>Cargo</th><th class="num">Distance</th><th class="num">Income</th></tr></thead>
          <tbody><tr><td><span class="route">Hamburg${icon("arrow-right")}Bremen</span></td><td>Logs</td><td class="num">312 km</td><td class="num ok">€8,475</td></tr>
          <tr><td><span class="route">Kiel${icon("arrow-right")}Rostock</span></td><td>Machine Parts</td><td class="num">218 km</td><td class="num ok">€6,120</td></tr></tbody></table>` })}
        ${card({ cls: "span-6 md-12", title: "Key/value & ranked bars", body: html`${kv([["Engine", "DC16 770", { icon: "zap", text: true }], ["Mileage", "128,432 km", { icon: "gauge" }]], { compact: true })}<div class="divider"></div>${raw(barList([{ label: "Logs", value: 42000 }, { label: "Machine parts", value: 31000 }, { label: "Frozen food", value: 12000 }], { fmt: (v) => `€${v.toLocaleString()}` }))}` })}
        ${card({ cls: "span-4 md-6 sm-12", title: "Empty state", body: empty({ brand: true, title: "Nothing here yet", text: "Empty states explain what will appear and how to get there.", compact: true }) })}
        ${card({ cls: "span-4 md-6 sm-12", title: "Error state", body: empty({ error: true, title: "Save could not be read", text: "Errors say what failed and what to do next.", compact: true }) })}
        ${card({ cls: "span-4 md-12", title: "Loading", body: skeleton(3, { block: true }) })}
        ${card({ cls: "span-12", title: "Overlays", body: html`<div class="row" style="flex-wrap:wrap"><button class="btn" id="dsToast">${icon("bell")}Toast</button><button class="btn" id="dsModal">Modal</button><button class="btn" id="dsConfirm">Confirm</button>
          ${statTile({ iconName: "coins", value: "€124,690", label: "Stat tile" })}<div class="callout callout--accent" style="flex:1">${icon("info")}<div><strong>Callout.</strong> Contextual guidance inside a card.</div></div></div>` })}
      </div>
    </div>`;
  },

  mount(root) {
    const x = Array.from({ length: 30 }, (_, i) => Date.now() / 1000 - (29 - i) * 86400);
    const c = chart($("#dsChart", root), { x, series: [{ label: "Distance", values: x.map((_, i) => 300 + Math.sin(i / 3) * 120 + i * 8), bars: true }], xFmt: (v) => new Date(v * 1000).toLocaleDateString(undefined, { day: "2-digit", month: "short" }), yFmt: (v) => Math.round(v) });
    gauge($("#dsGauge", root), { max: 140, unit: "km/h", ticks: 7 }).set(86);
    $("#dsToast", root).onclick = () => toast({ kind: "success", title: "Delivered: Logs", message: "Hamburg → Bremen · €8,475 · 612 XP" });
    $("#dsModal", root).onclick = () => modal({ title: "Modal", body: html`<p class="muted">Modals focus a single decision.</p>`, actions: [{ label: "Close", kind: "primary" }] });
    $("#dsConfirm", root).onclick = () => confirm({ title: "Clear history?", text: "A safety backup is created first.", danger: true, confirmLabel: "Clear" });
    return () => c.destroy();
  },
};
