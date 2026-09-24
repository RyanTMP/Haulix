// Static ETS2 city catalogue (copied from Haulix.Core at build time).
import { countryNameFor } from "./i18n.js";
let cache = null;

export async function loadCities() {
  if (cache) return cache;
  try {
    const res = await fetch("data/cities.json");
    const j = await res.json();
    cache = {
      countries: j.countries,
      list: j.cities.map(([id, name, country, lat, lon]) => ({ id, name, country, lat, lon })),
    };
  } catch {
    cache = { countries: {}, list: [] };
  }
  cache.byId = new Map(cache.list.map((c) => [c.id, c]));
  return cache;
}

export const countryName = (code) => countryNameFor(code, cache?.countries?.[code] || (code ? code.toUpperCase() : ""));
export const cityName = (id) => cache?.byId?.get(id)?.name || (id ? id.replace(/_/g, " ").replace(/\b\w/g, (m) => m.toUpperCase()) : "");
