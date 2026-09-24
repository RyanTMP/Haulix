import { esc } from "../core/html.js";

// Chart helpers on top of uPlot (vendored, offline). Thin 2px lines, recessive grid,
// single accent series by default, crosshair tooltip on every chart.

const css = (name) => getComputedStyle(document.documentElement).getPropertyValue(name).trim();

function palette() {
  return {
    accent: css("--accent"),
    comp: css("--chart-2"),
    grid: css("--chart-grid"),
    axis: css("--chart-axis"),
    text2: css("--text-2"),
    surface: css("--surface-1"),
    ok: css("--ok"),
    crit: css("--crit"),
  };
}

function withAlpha(hex, a) {
  const h = hex.replace("#", "");
  if (h.length !== 6) return hex;
  const n = parseInt(h, 16);
  return `rgba(${(n >> 16) & 255}, ${(n >> 8) & 255}, ${n & 255}, ${a})`;
}

/**
 * @param {HTMLElement} el
 * @param {{ x:number[], series:{label:string,values:number[],color?:string,fill?:boolean,bars?:boolean}[],
 *           yFmt?:(v)=>string, xFmt?:(v)=>string, height?:number, time?:boolean, yMin?:number, stacked?:boolean }} opts
 */
export function chart(el, opts) {
  const p = palette();
  const height = opts.height || 220;
  el.classList.add("chart");
  el.style.height = `${height}px`;
  const tip = document.createElement("div");
  tip.className = "chart-tip";
  el.append(tip);

  const colorOf = (s, i) => (s.color === "comp" ? p.comp : s.color === "ok" ? p.ok : s.color === "crit" ? p.crit : s.color || (i === 0 ? p.accent : p.comp));
  const barsPath = window.uPlot.paths.bars({ size: [0.62, 26], radius: 0.18, gap: 2 });

  const series = [
    {},
    ...opts.series.map((s, i) => {
      const c = colorOf(s, i);
      return {
        label: s.label,
        stroke: c,
        width: s.bars ? 0 : 2,
        fill: s.bars ? c : s.fill ? withAlpha(c.startsWith("#") ? c : "#ffb020", 0.1) : undefined,
        paths: s.bars ? barsPath : undefined,
        points: { show: false },
        spanGaps: false,
      };
    }),
  ];

  const axisFont = "11px JetBrains Mono, monospace";
  const uopts = {
    width: el.clientWidth || 600,
    height,
    padding: [12, 8, 0, 0],
    legend: { show: false },
    cursor: {
      y: false,
      points: { size: 8, width: 2, fill: p.surface },
      drag: { x: false, y: false },
    },
    scales: {
      x: { time: !!opts.time },
      y: { range: (u, min, max) => [opts.yMin ?? Math.min(0, min), max <= 0 ? 1 : max * 1.08] },
    },
    axes: [
      {
        stroke: p.axis, font: axisFont, size: 32,
        grid: { show: false }, ticks: { show: false },
        values: opts.xFmt ? (u, vals) => vals.map((v) => opts.xFmt(v)) : undefined,
      },
      {
        stroke: p.axis, font: axisFont, size: 56, gap: 6,
        grid: { stroke: p.grid, width: 1 }, ticks: { show: false },
        values: (u, vals) => vals.map((v) => (opts.yFmt ? opts.yFmt(v) : v)),
      },
    ],
    series,
    hooks: {
      setCursor: [
        (u) => {
          const i = u.cursor.idx;
          if (i === null || i === undefined || u.cursor.left < 0) { tip.style.display = "none"; return; }
          const xv = u.data[0][i];
          const rows = opts.series.map((s, k) => {
            const v = u.data[k + 1][i];
            return `<div><i style="display:inline-block;width:8px;height:8px;border-radius:2px;margin-right:6px;background:${colorOf(s, k)}"></i>${s.label} <b>${v === null || v === undefined ? "—" : opts.yFmt ? opts.yFmt(v, true) : v}</b></div>`;
          }).join("");
          tip.innerHTML = `<div class="t">${opts.tipX ? opts.tipX(xv) : opts.xFmt ? opts.xFmt(xv) : xv}</div>${rows}`;
          tip.style.display = "block";
          const left = u.valToPos(xv, "x") + u.over.offsetLeft;
          const top = Math.min(...opts.series.map((_, k) => {
            const v = u.data[k + 1][i];
            return v === null || v === undefined ? u.bbox.height : u.valToPos(v, "y");
          })) + u.over.offsetTop;
          tip.style.left = `${Math.max(60, Math.min(el.clientWidth - 60, left))}px`;
          tip.style.top = `${Math.max(40, top)}px`;
        },
      ],
    },
  };

  const data = [opts.x, ...opts.series.map((s) => s.values)];
  const u = new window.uPlot(uopts, data, el);
  u.over.addEventListener("mouseleave", () => (tip.style.display = "none"));

  const ro = new ResizeObserver(() => {
    const w = el.clientWidth;
    if (w > 0 && Math.abs(w - u.width) > 1) u.setSize({ width: w, height });
  });
  ro.observe(el);

  return {
    u,
    update(x, seriesValues) {
      u.setData([x, ...seriesValues], true);
    },
    destroy() {
      ro.disconnect();
      u.destroy();
      tip.remove();
    },
  };
}

/** Inline SVG sparkline (no axes) for stat tiles. */
export function sparkline(values, { w = 120, h = 32, color = "var(--accent)", fill = true } = {}) {
  const v = values.filter((x) => x !== null && x !== undefined);
  if (v.length < 2) return `<svg width="${w}" height="${h}"></svg>`;
  const min = Math.min(...v), max = Math.max(...v);
  const span = max - min || 1;
  const pts = v.map((y, i) => [(i / (v.length - 1)) * w, h - 2 - ((y - min) / span) * (h - 4)]);
  const d = pts.map(([x, y], i) => `${i ? "L" : "M"}${x.toFixed(1)} ${y.toFixed(1)}`).join(" ");
  const area = `${d} L${w} ${h} L0 ${h} Z`;
  return `<svg width="100%" height="${h}" viewBox="0 0 ${w} ${h}" preserveAspectRatio="none" style="overflow:visible">
    ${fill ? `<path d="${area}" fill="${color}" opacity="0.08"/>` : ""}
    <path d="${d}" fill="none" stroke="${color}" stroke-width="1.75" vector-effect="non-scaling-stroke" stroke-linejoin="round" stroke-linecap="round"/>
    <circle cx="${pts[pts.length - 1][0]}" cy="${pts[pts.length - 1][1]}" r="2.5" fill="${color}"/>
  </svg>`;
}

/** Arc gauge. Returns { set(value) }. Angles: 225° sweep. */
export function gauge(el, { max = 120, unit = "", ticks = 6, redline = null, fmt = (v) => Math.round(v) }) {
  const R = 80, cx = 100, cy = 96, sweep = 240, start = 150;
  const rad = (d) => (d * Math.PI) / 180;
  const pt = (deg, r = R) => [cx + r * Math.cos(rad(deg)), cy + r * Math.sin(rad(deg))];
  const arc = (a0, a1, r = R) => {
    const [x0, y0] = pt(a0, r), [x1, y1] = pt(a1, r);
    return `M${x0.toFixed(2)} ${y0.toFixed(2)} A${r} ${r} 0 ${a1 - a0 > 180 ? 1 : 0} 1 ${x1.toFixed(2)} ${y1.toFixed(2)}`;
  };
  const len = (Math.PI * 2 * R * sweep) / 360;
  let tickSvg = "";
  for (let i = 0; i <= ticks; i++) {
    const a = start + (sweep * i) / ticks;
    const [x0, y0] = pt(a, R + 9), [x1, y1] = pt(a, R + 14);
    tickSvg += `<line class="gauge__tick" x1="${x0}" y1="${y0}" x2="${x1}" y2="${y1}" stroke-width="1.5"/>`;
  }
  const red = redline ? `<path d="${arc(start + (sweep * redline) / max, start + sweep, R + 11)}" stroke="var(--crit)" stroke-width="2" fill="none" opacity="0.7"/>` : "";
  el.classList.add("gauge");
  el.innerHTML = `<svg viewBox="0 0 200 150">
      <path class="gauge__track" d="${arc(start, start + sweep)}" stroke-width="10" fill="none" stroke-linecap="butt"/>
      ${tickSvg}${red}
      <path class="gauge__value" d="${arc(start, start + sweep)}" stroke-width="10" fill="none" stroke-dasharray="${len}" stroke-dashoffset="${len}"/>
    </svg>
    <div class="gauge__readout"><div class="gauge__num">0</div><div class="gauge__unit">${unit}</div></div>`;
  const valueEl = el.querySelector(".gauge__value");
  const numEl = el.querySelector(".gauge__num");
  const unitEl = el.querySelector(".gauge__unit");
  let last = -1;
  return {
    set(v, { unitLabel } = {}) {
      const c = Math.max(0, Math.min(max, v || 0));
      if (Math.abs(c - last) < max / 1000 && unitLabel === undefined) return;
      last = c;
      valueEl.style.strokeDashoffset = String(len * (1 - c / max));
      valueEl.style.stroke = redline && c >= redline ? "var(--crit)" : "";
      const t = String(fmt(v || 0));
      if (numEl.textContent !== t) numEl.textContent = t;
      if (unitLabel !== undefined && unitEl.textContent !== unitLabel) unitEl.textContent = unitLabel;
    },
  };
}

/** Horizontal bar list (HTML, not canvas) for ranked categories. */
export function barList(rows, { fmt = (v) => v, muted = false } = {}) {
  const max = Math.max(1, ...rows.map((r) => r.value || 0));
  return `<div class="bars">${rows.map((r) => `
    <div class="bar-row" data-tip="${esc(r.tip ?? "")}">
      <span class="ellipsis">${esc(r.label)}</span>
      <span class="bar"><i class="${muted ? "muted" : ""}" style="width:${((r.value || 0) / max) * 100}%"></i></span>
      <span class="v">${fmt(r.value)}</span>
    </div>`).join("")}</div>`;
}
