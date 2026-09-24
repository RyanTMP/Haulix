// Road map data extracted from the local game files (served by the host at https://map.haulix/).
import { store } from "./store.js";
import { loadStreets } from "../components/streets.js";

let poiCache = null;

export function mapReady() {
  return store.get("mapStatus")?.state === "ready";
}

/** Streets (parsed binary) and POIs; resolves to null when the road map is not built. */
export async function loadMapData() {
  const s = store.get("mapStatus");
  if (s?.state !== "ready" || !s.streetsUrl) return null;
  const [streets, pois] = await Promise.all([loadStreets(s.streetsUrl), loadPois(s.poisUrl)]);
  // City area rectangles (built-up areas) are drawn by the street layer underneath the roads.
  streets.areas ??= pois.byKind.get("area") || [];
  return { streets, pois };
}

async function loadPois(url) {
  if (poiCache?.url === url) return poiCache.data;
  const raw = await fetch(url).then((r) => r.json());
  const data = { all: raw, byKind: new Map(), cities: new Map() };
  for (const p of raw) {
    if (!data.byKind.has(p.k)) data.byKind.set(p.k, []);
    data.byKind.get(p.k).push(p);
    if (p.k === "city") data.cities.set(p.id, { id: p.id, x: p.x, z: p.z });
  }
  poiCache = { url, data };
  return data;
}
