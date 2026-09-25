// Converts a telemetry snapshot into display strings for data-bind targets (shared by Dashboard & Telemetry).
import * as f from "./format.js";
// Rough projection of real-world lat/lon to ETS2 world coordinates (≈ 1:19 around 10°E / 51°N), used to
// estimate positions of cities HAULIX has not driven through yet (for the "near …" location label).
const project = (lat, lon) => ({ x: 3700 * lon - 37000, z: -5850 * lat + 298350 });

export function gearLabel(s) {
  if (!s) return "—";
  const g = s.gearDashboard ?? s.gear;
  if (g === 0) return "N";
  if (g < 0) return `R${Math.abs(g)}`;
  return String(g);
}

/** Nearest known city for "current location" (learned coordinates preferred, catalogue estimate otherwise). */
export function nearestCity(s, cities) {
  if (!s || !cities?.length) return null;
  let best = null, bd = Infinity;
  for (const c of cities) {
    const d = Math.hypot(c.x - s.x, c.z - s.z);
    if (d < bd) { bd = d; best = c; }
  }
  if (!best) return null;
  return { ...best, worldDistance: bd };
}

/** "In Hamburg" / "Near Hamburg". Estimated city positions are too coarse to quote a distance. */
export function locationLabel(near) {
  if (!near) return "Unknown location";
  if (!near.estimated && near.worldDistance < 1200) return `In ${near.name}`;
  return near.worldDistance < 9000 ? `Near ${near.name}` : `Between cities · nearest ${near.name}`;
}

export function teleValues(s) {
  if (!s) return {};
  const fuelPct = s.fuelCapacity ? s.fuelLitres / s.fuelCapacity : 0;
  const deadlineLeft = s.jobDeadlineGameMinutes && s.gameTimeMinutes ? s.jobDeadlineGameMinutes - s.gameTimeMinutes : null;
  const eta = s.eta; // { source, remainingKm, gameSeconds, realSeconds, arrivalUtc, deadlineMarginGameMinutes }
  const margin = eta?.deadlineMarginGameMinutes;
  return {
    speed: f.num(f.speedValue(Math.abs(s.speedKmh))),
    speedUnit: f.speedUnit(),
    speedSub: [f.speedUnit(), s.speedLimitKmh > 1 ? `limit ${f.num(f.speedValue(s.speedLimitKmh))}` : "", s.cruiseControl ? `cruise ${f.num(f.speedValue(s.cruiseControlKmh))}` : ""].filter(Boolean).join(" · "),
    speedLimit: s.speedLimitKmh > 1 ? f.num(f.speedValue(s.speedLimitKmh)) : "",
    cruise: s.cruiseControl ? f.speed(s.cruiseControlKmh) : "Off",
    rpm: f.num(s.engineRpm),
    gear: gearLabel(s),
    gearCount: `${s.forwardGears}F · ${s.reverseGears}R`,
    fuel: f.volume(s.fuelLitres),
    fuelCap: f.volume(s.fuelCapacity),
    fuelPct: f.pct(fuelPct),
    fuelRange: f.dist(s.fuelRangeKm),
    fuelAvg: f.consumption(s.fuelAvgConsumption),
    adblue: s.adBlueCapacity ? `${f.volume(s.adBlueLitres)} / ${f.volume(s.adBlueCapacity)}` : "—",
    damage: f.pct(s.truckDamage, 1),
    trailerDamage: s.trailerAttached ? f.pct(s.trailerDamage, 1) : "—",
    cargoDamage: s.onJob ? f.pct(s.cargoDamage, 1) : "—",
    odometer: f.dist(s.odometerKm),
    air: `${f.num(s.airPressure)} psi`,
    oilP: `${f.num(s.oilPressure)} psi`,
    oilT: f.temp(s.oilTemperature),
    waterT: f.temp(s.waterTemperature),
    battery: `${f.num(s.batteryVoltage, 1)} V`,
    brakeT: f.temp(s.brakeTemperature),
    retarder: s.retarderSteps ? `${s.retarderLevel} / ${s.retarderSteps}` : "—",
    truck: `${s.truckBrand} ${s.truckName}`.trim() || "—",
    plate: s.licensePlate ? `${s.licensePlate}${s.licensePlateCountry ? ` · ${s.licensePlateCountry}` : ""}` : "—",
    shifter: s.shifterType ? s.shifterType.replace(/_/g, " ") : "—",
    heading: `${f.num(s.headingDeg)}° ${f.cardinal(s.headingDeg)}`,
    coords: `X ${f.num(s.x)} · Z ${f.num(s.z)}`,
    altitude: `${f.num(s.y)} m`,
    gameTime: f.gameTime(s.gameTimeMinutes),
    restStop: s.restStopMinutes > 0 ? f.duration(s.restStopMinutes * 60, { short: true }) : "—",
    cargo: s.onJob ? s.cargo : "—",
    cargoMass: s.onJob ? f.mass(s.cargoMassKg) : "—",
    from: s.onJob ? s.sourceCity : "—",
    fromCo: s.onJob ? s.sourceCompany : "",
    to: s.onJob ? s.destinationCity : "—",
    toCo: s.onJob ? s.destinationCompany : "",
    income: s.onJob ? f.money(s.jobIncome) : "—",
    remaining: eta ? f.dist(eta.remainingKm) : s.routeDistanceKm > 0 ? f.dist(s.routeDistanceKm) : "—",
    eta: eta ? f.duration(eta.gameSeconds, { short: true }) : "—",
    etaReal: eta ? f.duration(Math.max(60, eta.realSeconds), { short: true }) : "—",
    arrivalClock: eta ? `≈ ${f.time(eta.arrivalUtc)}` : "",
    etaSource: eta?.source === "haulix" ? "Estimated from the HAULIX route and your average speed" : eta ? "From the in-game navigation, converted to real time" : "No route to estimate yet",
    onTime: margin == null ? "" : margin >= 0 ? `on time · ${f.duration(margin * 60, { short: true })} spare` : `${f.duration(-margin * 60, { short: true })} late`,
    planned: s.plannedDistanceKm ? f.dist(s.plannedDistanceKm) : "—",
    deadline: deadlineLeft === null ? "—" : deadlineLeft < 0 ? `${f.duration(-deadlineLeft * 60, { short: true })} late` : f.duration(deadlineLeft * 60, { short: true }),
    market: s.jobMarket ? s.jobMarket.replace(/_/g, " ") : "—",
    trailer: s.trailerAttached ? `${s.trailerBrand} ${s.trailerName}`.trim() : "Not attached",
    trailerBody: s.trailerAttached ? s.trailerBodyType || "—" : "—",
    trailerPlate: s.trailerAttached ? s.trailerPlate || "—" : "—",
    game: `${s.game} · telemetry ${s.gameVersion}`,
    plugin: `Revision ${s.pluginRevision}`,
    throttle: f.pct(s.throttle),
  };
}

export function jobProgress(s) {
  if (!s?.onJob || !s.plannedDistanceKm) return 0;
  const left = s.eta?.remainingKm ?? s.routeDistanceKm;
  return Math.max(0, Math.min(1, 1 - left / s.plannedDistanceKm));
}

/** City positions: learned while driving (exact) + catalogue (estimated). */
export function cityPositions(learned, catalogue) {
  const out = new Map();
  for (const c of learned || []) out.set(c.id, { ...c, estimated: false });
  for (const c of catalogue || []) {
    if (out.has(c.id)) continue;
    const p = project(c.lat, c.lon);
    out.set(c.id, { id: c.id, name: c.name, country: c.country, x: p.x, z: p.z, estimated: true });
  }
  return out;
}
