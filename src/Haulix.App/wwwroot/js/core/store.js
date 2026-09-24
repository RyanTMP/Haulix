// Minimal observable app state. Pages subscribe to slices they care about.

const state = {
  version: "",
  settings: null,
  detection: null,
  status: null,
  profile: null,
  telemetry: null,     // { snapshot, job, routeId }
  counts: null,
  dataFolder: "",
  backupFolder: "",
  history: [],         // rolling telemetry history for strip charts: [t, speed, rpm, fuel]
};

const subs = new Map();

export const store = {
  get: (k) => state[k],
  all: () => state,
  set(k, v) {
    state[k] = v;
    for (const fn of subs.get(k) || []) fn(v);
    for (const fn of subs.get("*") || []) fn(k, v);
  },
  on(k, fn) {
    if (!subs.has(k)) subs.set(k, new Set());
    subs.get(k).add(fn);
    return () => subs.get(k).delete(fn);
  },
};

const HISTORY_SECONDS = 300;

export function pushHistory(s) {
  const t = Date.now() / 1000;
  const h = state.history;
  const last = h[h.length - 1];
  if (last && t - last[0] < 0.45) return; // ~2 samples/second is plenty for 5-minute strips
  h.push([t, s.speedKmh, s.engineRpm, s.fuelLitres, s.throttle, s.brake]);
  while (h.length && t - h[0][0] > HISTORY_SECONDS) h.shift();
}

/** Is telemetry flowing right now? */
export function isLive() {
  const t = state.status?.telemetry;
  return t === "live" || t === "demo" || t === "paused";
}
