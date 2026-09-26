// Formatting honours Settings → General (units, currency). All inputs are metric / EUR from the backend.

const prefs = { units: "metric", currency: "EUR", locale: "en-GB" };

// ETS2 pays in EUR; other currencies use fixed approximate rates (display only).
const CURRENCIES = {
  EUR: { symbol: "€", rate: 1 },
  GBP: { symbol: "£", rate: 0.86 },
  USD: { symbol: "$", rate: 1.08 },
  CHF: { symbol: "CHF ", rate: 0.95 },
  PLN: { symbol: "zł ", rate: 4.3 },
  CZK: { symbol: "Kč ", rate: 25 },
  SEK: { symbol: "kr ", rate: 11.4 },
  NOK: { symbol: "kr ", rate: 11.6 },
  DKK: { symbol: "kr ", rate: 7.46 },
  HUF: { symbol: "Ft ", rate: 395 },
};
export const currencyList = Object.keys(CURRENCIES);

export function setPrefs(p) {
  if (p?.locale) prefs.locale = p.locale;
  if (p?.units) prefs.units = p.units;
  if (p?.currency && CURRENCIES[p.currency]) prefs.currency = p.currency;
}

export const imperial = () => prefs.units === "imperial";

const nf = (d = 0) => new Intl.NumberFormat(prefs.locale, { minimumFractionDigits: d, maximumFractionDigits: d });

export function num(v, digits = 0) {
  if (v === null || v === undefined || Number.isNaN(+v)) return "—";
  return nf(digits).format(+v);
}

export function money(eur, { compact = false, sign = false } = {}) {
  if (eur === null || eur === undefined || Number.isNaN(+eur)) return "—";
  const c = CURRENCIES[prefs.currency];
  const v = +eur * c.rate;
  const abs = Math.abs(v);
  let s;
  if (compact && abs >= 1e6) s = `${nf(abs >= 1e7 ? 1 : 2).format(abs / 1e6)}M`;
  else if (compact && abs >= 1e4) s = `${nf(abs >= 1e5 ? 0 : 1).format(abs / 1e3)}k`;
  else s = nf(0).format(abs);
  const pre = v < 0 ? "−" : sign && v > 0 ? "+" : "";
  return `${pre}${c.symbol}${s}`;
}
export const currencySymbol = () => CURRENCIES[prefs.currency].symbol.trim();

export function dist(km, digits = 0) {
  if (km === null || km === undefined) return "—";
  return imperial() ? `${num(km * 0.621371, digits)} mi` : `${num(km, digits)} km`;
}
export const distUnit = () => (imperial() ? "mi" : "km");
export const distValue = (km) => (imperial() ? km * 0.621371 : km);

export function speed(kmh, digits = 0) {
  if (kmh === null || kmh === undefined) return "—";
  return imperial() ? `${num(kmh * 0.621371, digits)} mph` : `${num(kmh, digits)} km/h`;
}
export const speedUnit = () => (imperial() ? "mph" : "km/h");
export const speedValue = (kmh) => (imperial() ? kmh * 0.621371 : kmh);

export function volume(l, digits = 0) {
  if (l === null || l === undefined) return "—";
  return imperial() ? `${num(l * 0.264172, digits)} gal` : `${num(l, digits)} L`;
}
export const volumeValue = (l) => (imperial() ? l * 0.264172 : l);
export const volumeUnit = () => (imperial() ? "gal" : "L");

export function consumption(lPerKm) {
  if (!lPerKm) return "—";
  return imperial() ? `${num(235.215 / (lPerKm * 100), 1)} mpg` : `${num(lPerKm * 100, 1)} L/100km`;
}

export function mass(kg) {
  if (kg === null || kg === undefined) return "—";
  if (imperial()) return `${num(kg * 2.20462 / 1000, 1)}k lb`;
  return kg >= 1000 ? `${num(kg / 1000, 1)} t` : `${num(kg)} kg`;
}

export function pct(v, digits = 0) {
  if (v === null || v === undefined) return "—";
  return `${num(v * 100, digits)}%`;
}

export function temp(c) {
  if (c === null || c === undefined) return "—";
  return imperial() ? `${num(c * 9 / 5 + 32)} °F` : `${num(c)} °C`;
}

export function duration(seconds, { short = false } = {}) {
  if (seconds === null || seconds === undefined) return "—";
  const s = Math.max(0, Math.round(seconds));
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  if (short) return h > 0 ? `${h}h ${String(m).padStart(2, "0")}m` : `${m}m`;
  if (h > 0) return `${h}h ${String(m).padStart(2, "0")}m`;
  return m > 0 ? `${m}m ${String(s % 60).padStart(2, "0")}s` : `${s % 60}s`;
}

/** ETS2 in-game time: minutes since game start (day 1 = Monday). */
export function gameTime(minutes) {
  if (minutes === null || minutes === undefined) return "—";
  const days = prefs.locale.startsWith("de") ? ["Mo", "Di", "Mi", "Do", "Fr", "Sa", "So"] : ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
  const d = Math.floor(minutes / 1440);
  const h = Math.floor((minutes % 1440) / 60);
  const m = minutes % 60;
  return `${days[d % 7]} ${String(h).padStart(2, "0")}:${String(m).padStart(2, "0")}`;
}
export const gameDay = (minutes) => (minutes === null || minutes === undefined ? "—" : `Day ${Math.floor(minutes / 1440) + 1}`);

export function date(iso) {
  if (!iso) return "—";
  return new Date(iso).toLocaleDateString(prefs.locale, { year: "numeric", month: "short", day: "2-digit" });
}
export function dateTime(iso) {
  if (!iso) return "—";
  const d = new Date(iso);
  return `${d.toLocaleDateString(prefs.locale, { month: "short", day: "2-digit" })} ${d.toLocaleTimeString(prefs.locale, { hour: "2-digit", minute: "2-digit" })}`;
}
export function time(iso) {
  if (!iso) return "—";
  return new Date(iso).toLocaleTimeString(prefs.locale, { hour: "2-digit", minute: "2-digit" });
}

export function ago(iso) {
  if (!iso) return "never";
  const s = (Date.now() - new Date(iso).getTime()) / 1000;
  if (s < 5) return "just now";
  if (s < 60) return `${Math.floor(s)}s ago`;
  if (s < 3600) return `${Math.floor(s / 60)}m ago`;
  if (s < 86400) return `${Math.floor(s / 3600)}h ago`;
  return `${Math.floor(s / 86400)}d ago`;
}

export function bytes(n) {
  if (!n) return "0 B";
  const u = ["B", "KB", "MB", "GB"];
  const i = Math.min(u.length - 1, Math.floor(Math.log(n) / Math.log(1024)));
  return `${num(n / 1024 ** i, i === 0 ? 0 : 1)} ${u[i]}`;
}

export const cardinal = (deg) => ["N", "NE", "E", "SE", "S", "SW", "W", "NW"][Math.round(((deg % 360) + 360) % 360 / 45) % 8];

export const plural = (n, one, many = one + "s") => `${num(n)} ${n === 1 ? one : many}`;

// A fourth version part is a hotfix of the same release: "0.0.9.1-beta" is shown as "0.0.9-1" (and still sorts after 0.0.9).
const hotfix = (n) => n.replace(/^(\d+\.\d+\.\d+)\.(\d+)$/, "$1-$2");

/** "0.0.5-beta" → "0.0.5 BETA", "0.0.9.1-beta" → "0.0.9-1 BETA" (pre-release label shown in capitals). */
export function versionLabel(v) {
  const { number, channel } = versionParts(v);
  return channel ? `${number} ${channel}` : number;
}

/** "0.0.9-beta" → { number: "0.0.9", channel: "BETA" }; a release without a label is { channel: "" }. */
export function versionParts(v) {
  const m = String(v || "").trim().replace(/^v/i, "").match(/^([\d.]+(?:-\d+)?)(?:[-\s]+(.+))?$/);
  return m ? { number: hotfix(m[1]), channel: (m[2] || "").toUpperCase() } : { number: String(v || "—"), channel: "" };
}
