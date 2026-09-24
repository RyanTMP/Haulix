// HAULIX map: Leaflet in ETS2 world coordinates (CRS.Simple). No external tiles.
// Game X grows east, Z grows south → Leaflet latlng = [-z, x].
// City positions come from (1) coordinates HAULIX learned from telemetry, else (2) an estimate projected
// from real-world lat/lon, fitted to the learned cities once three or more are known.

import { truckMarkerSvg } from "../core/icons.js";
import { esc } from "../core/html.js";

const L = () => window.L;

// Leaflet's canvas renderer can run a queued redraw after its map was removed (page change);
// make those late calls no-ops instead of throwing.
for (const fn of ["_redraw", "_clear", "_draw", "_updatePaths", "_update"]) {
  const orig = window.L.Canvas.prototype[fn];
  if (!orig) continue;
  window.L.Canvas.prototype[fn] = function (...args) {
    if (!this._ctx || !this._map) return undefined;
    return orig.apply(this, args);
  };
}

export const toLatLng = (x, z) => [-z, x];

// Default projection ≈ ETS2's 1:19 scale around 10°E / 51°N. Refined by least-squares fit when possible.
let projection = { ax: 3700, bx: 0, cx: -37000, az: 0, bz: -5850, cz: 298350 };

export function fitProjection(pairs) {
  // pairs: [{ lat, lon, x, z }]
  if (!pairs || pairs.length < 3) return false;
  const solve = (target) => {
    // Normal equations for target = a*lon + b*lat + c
    let s = [[0, 0, 0], [0, 0, 0], [0, 0, 0]], r = [0, 0, 0];
    for (const p of pairs) {
      const v = [p.lon, p.lat, 1];
      for (let i = 0; i < 3; i++) { r[i] += v[i] * p[target]; for (let j = 0; j < 3; j++) s[i][j] += v[i] * v[j]; }
    }
    const det = (m) => m[0][0] * (m[1][1] * m[2][2] - m[1][2] * m[2][1]) - m[0][1] * (m[1][0] * m[2][2] - m[1][2] * m[2][0]) + m[0][2] * (m[1][0] * m[2][1] - m[1][1] * m[2][0]);
    const d = det(s);
    if (Math.abs(d) < 1e-9) return null;
    return [0, 1, 2].map((k) => det(s.map((row, i) => row.map((v, j) => (j === k ? r[i] : v)))) / d);
  };
  const fx = solve("x"), fz = solve("z");
  if (!fx || !fz) return false;
  projection = { ax: fx[0], bx: fx[1], cx: fx[2], az: fz[0], bz: fz[1], cz: fz[2] };
  return true;
}

export function project(lat, lon) {
  const p = projection;
  return { x: p.ax * lon + p.bx * lat + p.cx, z: p.az * lon + p.bz * lat + p.cz };
}

function gridLayer() {
  // Subtle survey grid: major lines every 10 km, drawn on a canvas per tile.
  const G = L().GridLayer.extend({
    createTile(coords) {
      const tile = document.createElement("canvas");
      const size = this.getTileSize();
      tile.width = size.x; tile.height = size.y;
      const ctx = tile.getContext("2d");
      const scale = Math.pow(2, coords.z);
      const worldPerTile = size.x / scale;
      const x0 = coords.x * worldPerTile, y0 = coords.y * worldPerTile;
      const style = getComputedStyle(document.documentElement);
      const minor = style.getPropertyValue("--border-subtle").trim() || "#1a1d22";
      const major = style.getPropertyValue("--border").trim() || "#23272e";
      const step = worldPerTile > 20000 ? 50000 : worldPerTile > 4000 ? 10000 : worldPerTile > 800 ? 2000 : 500;
      for (let k = Math.ceil(x0 / step) * step; k < x0 + worldPerTile; k += step) {
        const px = Math.round((k - x0) * scale) + 0.5;
        ctx.strokeStyle = k % (step * 5) === 0 ? major : minor; ctx.lineWidth = 1;
        ctx.beginPath(); ctx.moveTo(px, 0); ctx.lineTo(px, size.y); ctx.stroke();
      }
      for (let k = Math.ceil(y0 / step) * step; k < y0 + worldPerTile; k += step) {
        const py = Math.round((k - y0) * scale) + 0.5;
        ctx.strokeStyle = k % (step * 5) === 0 ? major : minor; ctx.lineWidth = 1;
        ctx.beginPath(); ctx.moveTo(0, py); ctx.lineTo(size.x, py); ctx.stroke();
      }
      return tile;
    },
  });
  return new G({ tileSize: 256, minZoom: -9, maxZoom: 3, noWrap: true });
}

export function createMap(el, { zoom = -5, center = [0, 0], interactive = true, grid = true, tileConfig = null } = {}) {
  const map = L().map(el, {
    crs: L().CRS.Simple,
    minZoom: -9,
    maxZoom: 3,
    zoomSnap: 0.25,
    zoomDelta: 0.5,
    wheelPxPerZoomLevel: 90,
    attributionControl: false,
    zoomControl: interactive,
    dragging: interactive,
    scrollWheelZoom: interactive,
    doubleClickZoom: interactive,
    boxZoom: false,
    keyboard: interactive,
    preferCanvas: true,
    fadeAnimation: true,
  });
  if (interactive) map.zoomControl.setPosition("bottomright");
  if (tileConfig) {
    // User-supplied local tiles (Settings → Map → Tile folder), served by the host at https://tiles.haulix/.
    const t = tileConfig;
    const bounds = L().latLngBounds(toLatLng(t.minX, t.maxZ), toLatLng(t.maxX, t.minZ));
    L().tileLayer("https://tiles.haulix/{z}/{x}/{y}.png", {
      bounds, minNativeZoom: t.minZoom ?? 0, maxNativeZoom: t.maxZoom ?? 8, tileSize: t.tileSize ?? 256, noWrap: true,
    }).addTo(map);
  } else if (grid) {
    gridLayer().addTo(map);
  }
  map.setView(center, zoom);
  return map;
}

export function truckMarker(x, z, heading) {
  const iconEl = L().divIcon({ className: "", html: `<div class="truck-marker">${truckMarkerSvg(heading)}</div>`, iconSize: [34, 34], iconAnchor: [17, 17] });
  const m = L().marker(toLatLng(x, z), { icon: iconEl, zIndexOffset: 1000, interactive: true, keyboard: false });
  m.setHeading = (deg) => {
    const svg = m.getElement()?.querySelector("svg");
    if (svg) svg.style.transform = `rotate(${deg}deg)`;
  };
  return m;
}

/** Flat [x,z,x,z,…] with null gaps → array of latlng segments. */
export function segments(flat) {
  const segs = [];
  let cur = [];
  for (let i = 0; i < flat.length; i += 2) {
    const x = flat[i], z = flat[i + 1];
    if (x === null || z === null) { if (cur.length > 1) segs.push(cur); cur = []; continue; }
    cur.push(toLatLng(x, z));
  }
  if (cur.length > 1) segs.push(cur);
  return segs;
}

export function routeLine(flat, { current = false, highlighted = false, dashed = false, colorVar = "--chart-2" } = {}) {
  const css = getComputedStyle(document.documentElement);
  const color = current || highlighted ? css.getPropertyValue("--accent").trim() : css.getPropertyValue(colorVar).trim();
  const group = L().featureGroup();
  for (const seg of segments(flat)) {
    if (current) L().polyline(seg, { color, weight: 9, opacity: 0.14, interactive: false }).addTo(group);
    L().polyline(seg, {
      color, weight: current ? 3.5 : highlighted ? 3 : 2, opacity: current ? 1 : highlighted ? 0.95 : 0.45,
      lineJoin: "round", lineCap: "round", dashArray: dashed ? "5 7" : null,
    }).addTo(group);
  }
  return group;
}

export function cityMarker(x, z, { name, estimated = false, kind = "city", tip = true } = {}) {
  const css = getComputedStyle(document.documentElement);
  const colors = {
    city: css.getPropertyValue("--text-2").trim(),
    garage: css.getPropertyValue("--accent").trim(),
    hq: css.getPropertyValue("--accent").trim(),
    dealer: css.getPropertyValue("--info").trim(),
    recruit: css.getPropertyValue("--ok").trim(),
    service: css.getPropertyValue("--warn").trim(),
  };
  const c = colors[kind] || colors.city;
  const isGarage = kind === "garage" || kind === "hq";
  const m = isGarage
    ? L().marker(toLatLng(x, z), {
        icon: L().divIcon({
          className: "",
          html: `<div class="garage-marker ${kind === "hq" ? "is-hq" : ""} ${estimated ? "is-est" : ""}"><span>${kind === "hq" ? "HQ" : "G"}</span></div>`,
          iconSize: [22, 22], iconAnchor: [11, 11],
        }),
        zIndexOffset: 500,
      })
    : L().circleMarker(toLatLng(x, z), {
        radius: kind === "city" ? 3.5 : 4.5,
        color: c, weight: estimated ? 1.25 : 0, fillColor: c, fillOpacity: estimated ? 0 : 0.9, opacity: estimated ? 0.8 : 1,
        dashArray: null,
      });
  if (tip && name) m.bindTooltip(esc(name), { direction: "top", offset: [0, -6] });
  return m;
}

export function labelMarker(x, z, text) {
  return L().marker(toLatLng(x, z), {
    icon: L().divIcon({ className: "", html: "", iconSize: [0, 0] }),
    interactive: false,
  }).bindTooltip(esc(text), { permanent: true, direction: "right", offset: [6, 0], className: "city-label" });
}
