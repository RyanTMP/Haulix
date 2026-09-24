// Street layer: renders the road network extracted from the local ETS2 files (streets.bin) on canvas tiles.
// Format: "HXST" u32 version, u32 lines, u32 points, u8 class[lines] (padded to 4), u32 offsets[lines+1], f32 x/z[points].

const CELL = 2000; // world metres per index cell
let loaded = null;  // { url, promise }

export function loadStreets(url) {
  if (loaded?.url === url) return loaded.promise;
  const promise = fetch(url)
    .then((r) => { if (!r.ok) throw new Error(`streets ${r.status}`); return r.arrayBuffer(); })
    .then(parse);
  loaded = { url, promise };
  promise.catch(() => { if (loaded?.url === url) loaded = null; });
  return promise;
}

function parse(buf) {
  const dv = new DataView(buf);
  const magic = String.fromCharCode(dv.getUint8(0), dv.getUint8(1), dv.getUint8(2), dv.getUint8(3));
  if (magic !== "HXST") throw new Error("Invalid street file");
  const lines = dv.getUint32(8, true);
  const points = dv.getUint32(12, true);
  let off = 16;
  const cls = new Uint8Array(buf, off, lines);
  off += lines + ((4 - (lines % 4)) % 4);
  const offsets = new Uint32Array(buf, off, lines + 1);
  off += (lines + 1) * 4;
  const xy = new Float32Array(buf, off, points * 2);

  // Spatial index: cell → line ids; plus per-line bbox for quick rejects.
  const index = new Map();
  const bbox = new Float32Array(lines * 4);
  for (let l = 0; l < lines; l++) {
    let minX = Infinity, minZ = Infinity, maxX = -Infinity, maxZ = -Infinity;
    for (let p = offsets[l]; p < offsets[l + 1]; p++) {
      const x = xy[p * 2], z = xy[p * 2 + 1];
      if (x < minX) minX = x; if (x > maxX) maxX = x; if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
    }
    bbox.set([minX, minZ, maxX, maxZ], l * 4);
    for (let cx = Math.floor(minX / CELL); cx <= Math.floor(maxX / CELL); cx++)
      for (let cz = Math.floor(minZ / CELL); cz <= Math.floor(maxZ / CELL); cz++) {
        const k = cx * 100000 + cz;
        let a = index.get(k);
        if (!a) index.set(k, (a = []));
        a.push(l);
      }
  }
  return { lines, cls, offsets, xy, index, bbox };
}

/**
 * Flags the street lines the player has driven: a line counts as explored when most of its vertices lie
 * within ~30 m of a recorded driving trace. `driven` is flat [x, z, …] with null pairs separating traces.
 * Returns Uint8Array(lines) and the number of explored lines.
 */
export function markExplored(data, driven) {
  const R = 25;                                  // grid cell (m); vertex + 8 neighbours ≈ 25–50 m tolerance
  const key = (cx, cz) => (cx + 50000) * 100000 + (cz + 50000);
  const near = new Set();
  const coarse = new Set();                      // street index cells touched by the trace
  const put = (x, z) => { near.add(key(Math.floor(x / R), Math.floor(z / R))); coarse.add(Math.floor(x / CELL) * 100000 + Math.floor(z / CELL)); };
  for (let i = 0; i + 1 < driven.length; i += 2) {
    const x = driven[i], z = driven[i + 1];
    if (x == null) continue;
    put(x, z);
    const nx = driven[i + 2], nz = driven[i + 3];
    if (nx == null) continue;
    const d = Math.hypot(nx - x, nz - z);
    if (d > 1500) continue;                      // ferry, train or teleport: not a driven road
    for (let s = R; s < d; s += R) put(x + ((nx - x) * s) / d, z + ((nz - z) * s) / d);
  }
  const explored = new Uint8Array(data.lines);
  let count = 0;
  const seen = new Set();
  for (const cell of coarse) {
    for (const l of data.index.get(cell) || []) {
      if (seen.has(l)) continue;
      seen.add(l);
      const s = data.offsets[l], e = data.offsets[l + 1];
      let hit = 0;
      for (let p = s; p < e; p++) {
        const cx = Math.floor(data.xy[p * 2] / R), cz = Math.floor(data.xy[p * 2 + 1] / R);
        let ok = false;
        for (let dx = -1; dx <= 1 && !ok; dx++) for (let dz = -1; dz <= 1 && !ok; dz++) ok = near.has(key(cx + dx, cz + dz));
        if (ok) hit++;
      }
      if (hit > 0 && hit >= (e - s) * 0.6) { explored[l] = 1; count++; }
    }
  }
  return { explored, count };
}

/**
 * Leaflet GridLayer drawing streets. Leaflet CRS.Simple: latlng = [-z, x]; at zoom z, 1 world metre = 2^z px.
 * Streets flagged in `data.explored` are drawn on top in the explored-road colour.
 */
export function streetsLayer(data) {
  const L = window.L;
  const css = getComputedStyle(document.documentElement);
  const colors = [
    css.getPropertyValue("--road-motorway").trim() || "#4a505b",
    css.getPropertyValue("--road-main").trim() || "#383d46",
    css.getPropertyValue("--road-local").trim() || "#2a2e35",
    css.getPropertyValue("--road-local").trim() || "#2a2e35",
  ];
  const exploredColor = css.getPropertyValue("--map-explored").trim() || "#4fb3ff";
  const areaColor = css.getPropertyValue("--map-area").trim() || "#16191e";
  const Layer = L.GridLayer.extend({
    createTile(coords) {
      const tile = document.createElement("canvas");
      const size = this.getTileSize();
      const ratio = window.devicePixelRatio || 1;
      tile.width = size.x * ratio;
      tile.height = size.y * ratio;
      tile.style.width = `${size.x}px`;
      tile.style.height = `${size.y}px`;
      const ctx = tile.getContext("2d");
      ctx.scale(ratio, ratio);
      const scale = Math.pow(2, coords.z);
      const x0 = (coords.x * size.x) / scale;          // world X at tile left
      const z0 = (coords.y * size.y) / scale;          // world Z at tile top (lat = -z)
      const x1 = x0 + size.x / scale, z1 = z0 + size.y / scale;
      const pad = 40 / scale;
      const zoom = coords.z;
      // Level of detail: hide small roads when zoomed far out.
      const maxClass = zoom < -7.5 ? 0 : zoom < -6 ? 1 : 3;
      const widths = zoom >= -2 ? [5, 4, 3, 2.5] : zoom >= -4 ? [3.2, 2.4, 1.6, 1.4] : zoom >= -6 ? [2.2, 1.5, 1, 1] : [1.6, 1.1, 0.8, 0.8];
      // Built-up city areas first, as flat blocks under the roads.
      if (data.areas?.length && zoom >= -7.5) {
        ctx.fillStyle = areaColor;
        for (const a of data.areas) {
          if (a.x > x1 + pad || a.x + a.w < x0 - pad || a.z > z1 + pad || a.z + a.h < z0 - pad) continue;
          ctx.fillRect((a.x - x0) * scale, (a.z - z0) * scale, a.w * scale, a.h * scale);
        }
      }
      const drawn = new Set();
      const explored = data.explored;
      ctx.lineCap = "round";
      ctx.lineJoin = "round";
      const trace = (l) => {
        const s = data.offsets[l], e = data.offsets[l + 1];
        ctx.moveTo((data.xy[s * 2] - x0) * scale, (data.xy[s * 2 + 1] - z0) * scale);
        for (let p = s + 1; p < e; p++) ctx.lineTo((data.xy[p * 2] - x0) * scale, (data.xy[p * 2 + 1] - z0) * scale);
      };
      // Draw minor classes first so motorways sit on top; driven streets go last in their own colour.
      const own = [[], [], [], []];
      for (let c = Math.min(maxClass, 3); c >= 0; c--) {
        ctx.strokeStyle = colors[c];
        ctx.lineWidth = widths[c];
        ctx.beginPath();
        for (let cx = Math.floor((x0 - pad) / CELL); cx <= Math.floor((x1 + pad) / CELL); cx++)
          for (let cz = Math.floor((z0 - pad) / CELL); cz <= Math.floor((z1 + pad) / CELL); cz++) {
            const ids = data.index.get(cx * 100000 + cz);
            if (!ids) continue;
            for (const l of ids) {
              const lc = data.cls[l];
              if (lc !== c || drawn.has(l)) continue;
              const b = l * 4;
              if (data.bbox[b + 2] < x0 - pad || data.bbox[b] > x1 + pad || data.bbox[b + 3] < z0 - pad || data.bbox[b + 1] > z1 + pad) continue;
              drawn.add(l);
              if (explored?.[l]) own[c].push(l);
              else trace(l);
            }
          }
        ctx.stroke();
      }
      if (explored) {
        ctx.strokeStyle = exploredColor;
        for (let c = 3; c >= 0; c--) {
          if (!own[c].length) continue;
          ctx.lineWidth = Math.max(widths[c], 1.4);
          ctx.beginPath();
          own[c].forEach(trace);
          ctx.stroke();
        }
      }
      return tile;
    },
  });
  return new Layer({ tileSize: 512, minZoom: -9, maxZoom: 3, noWrap: true, updateWhenZooming: false, keepBuffer: 2, className: "streets-layer" });
}

/* ---------------- Countries ---------------- */

/** Soft landmass image (land-<key>.png) under the streets, so land and sea read at a glance. */
export function landLayer(map, data, countriesUrl) {
  const L = window.L;
  if (!data?.land?.file || !countriesUrl) return null;
  if (!map.getPane("land")) { const p = map.createPane("land"); p.style.zIndex = 150; p.style.pointerEvents = "none"; }
  const url = countriesUrl.slice(0, countriesUrl.lastIndexOf("/") + 1) + data.land.file;
  const b = data.land;
  return L.imageOverlay(url, [[-b.z1, b.x0], [-b.z0, b.x1]], { pane: "land", className: "land-layer", interactive: false });
}

/** Country name labels from countries-<key>.json. */
export function countriesLayer(data, nameOf, { labels = true } = {}) {
  const L = window.L;
  const group = L.layerGroup();
  // Border lines are intentionally not drawn (derived borders were too imprecise); names only.
  if (labels) {
    for (const c of data.countries) {
      L.marker([-c.z, c.x], {
        interactive: false, keyboard: false,
        icon: L.divIcon({ className: "", html: `<div class="country-label">${nameOf(c.code).replace(/[<>&]/g, "")}</div>`, iconSize: [0, 0] }),
      }).addTo(group);
    }
  }
  return group;
}

let countriesCache = null;
export function loadCountries(url) {
  if (!url) return Promise.resolve(null);
  if (countriesCache?.url === url) return countriesCache.promise;
  const promise = fetch(url).then((r) => (r.ok ? r.json() : null)).catch(() => null);
  countriesCache = { url, promise };
  return promise;
}

/* ---------------- Route drawing ---------------- */

/** Nearest route vertex index to a position, searching forward from a hint. */
export function progressIndex(points, x, z, hint = 0) {
  const n = points.length / 2;
  let best = hint, bd = Infinity;
  const from = Math.max(0, hint - 30), to = Math.min(n, hint + 800);
  for (let i = from; i < to; i++) {
    const d = (points[i * 2] - x) ** 2 + (points[i * 2 + 1] - z) ** 2;
    if (d < bd) { bd = d; best = i; }
  }
  return best;
}

/** Remaining route as latlngs from index i (prepending the truck position). */
export function remainingLatLngs(points, i, truck) {
  const out = truck ? [[-truck.z, truck.x]] : [];
  for (let k = i + 1; k < points.length / 2; k++) out.push([-points[k * 2 + 1], points[k * 2]]);
  return out;
}

/** Approximate remaining in-game km of a route from index i (world metres × 19). */
export function remainingKm(points, i) {
  let m = 0;
  for (let k = i; k < points.length / 2 - 1; k++) m += Math.hypot(points[k * 2 + 2] - points[k * 2], points[k * 2 + 3] - points[k * 2 + 1]);
  return (m * 19) / 1000;
}

/** Route polyline pair (casing + accent) as a Leaflet feature group with an update(latlngs) method. */
export function routeLayer() {
  const L = window.L;
  const css = getComputedStyle(document.documentElement);
  const accent = css.getPropertyValue("--accent").trim();
  const casing = L.polyline([], { color: css.getPropertyValue("--bg").trim() || "#0b0c0e", weight: 9, opacity: 0.9, interactive: false, lineJoin: "round", lineCap: "round" });
  const glow = L.polyline([], { color: accent, weight: 14, opacity: 0.14, interactive: false, lineJoin: "round", lineCap: "round" });
  const line = L.polyline([], { color: accent, weight: 4.5, opacity: 1, interactive: false, lineJoin: "round", lineCap: "round" });
  const group = L.featureGroup([glow, casing, line]);
  group.update = (latlngs) => { glow.setLatLngs(latlngs); casing.setLatLngs(latlngs); line.setLatLngs(latlngs); };
  return group;
}

export function destinationMarker(x, z, label) {
  const L = window.L;
  return L.marker([-z, x], {
    icon: L.divIcon({ className: "", html: `<div class="dest-marker"><svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M4 22V4"/><path d="M4 4h13l-2 4 2 4H4"/></svg></div>`, iconSize: [30, 30], iconAnchor: [15, 28] }),
    zIndexOffset: 900,
  }).bindTooltip(label.replace(/[<>&"]/g, ""), { direction: "top", offset: [0, -26] });
}
