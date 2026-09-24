// Browser-only mock of the HAULIX backend: deterministic sample company + a simulated live drive.
// Used for design preview and UI development when the page is not hosted inside Haulix.exe.

let seed = 7;
const rnd = () => ((seed = (seed * 16807) % 2147483647) / 2147483647);
const pick = (a) => a[Math.floor(rnd() * a.length)];
const int = (a, b) => Math.floor(a + rnd() * (b - a + 1));

// Same default projection as components/map.js
const proj = (lat, lon) => ({ x: 3700 * (lon - 10), z: -5850 * (lat - 51) });

const CITIES = {
  hamburg: ["Hamburg", "de", 53.55, 9.99], bremen: ["Bremen", "de", 53.08, 8.8], osnabruck: ["Osnabrück", "de", 52.28, 8.05],
  berlin: ["Berlin", "de", 52.52, 13.4], hannover: ["Hanover", "de", 52.38, 9.73], kiel: ["Kiel", "de", 54.32, 10.12],
  rostock: ["Rostock", "de", 54.09, 12.1], dortmund: ["Dortmund", "de", 51.51, 7.47], frankfurt: ["Frankfurt", "de", 50.11, 8.68],
  leipzig: ["Leipzig", "de", 51.34, 12.37], munchen: ["Munich", "de", 48.14, 11.58], koln: ["Cologne", "de", 50.94, 6.96],
  amsterdam: ["Amsterdam", "nl", 52.37, 4.9], rotterdam: ["Rotterdam", "nl", 51.92, 4.48], groningen: ["Groningen", "nl", 53.22, 6.57],
  aarhus: ["Aarhus", "dk", 56.16, 10.2], kobenhavn: ["Copenhagen", "dk", 55.68, 12.57], malmo: ["Malmö", "se", 55.61, 13.0],
  goteborg: ["Gothenburg", "se", 57.71, 11.97], prague: ["Prague", "cz", 50.08, 14.44], szczecin: ["Szczecin", "pl", 53.43, 14.55],
  poznan: ["Poznań", "pl", 52.41, 16.93], wien: ["Vienna", "at", 48.21, 16.37], linz: ["Linz", "at", 48.31, 14.29],
  bruxelles: ["Brussels", "be", 50.85, 4.35], luxembourg: ["Luxembourg", "lu", 49.61, 6.13],
};
const CARGO = ["Logs", "Machine Parts", "Canned Food", "Frozen Food", "Steel Coils", "Medical Vaccines", "Furniture", "Electronics",
  "Plastic Film", "Large Tubes", "Tyres", "Beverages", "Cement", "Lumber", "Chemicals", "Fresh Vegetables"];
const COMPANIES = ["Tradeaux", "Posped", "Wilnet Transport", "Stokes", "LKW Log", "Trameri", "Norrsken", "Kaarfor", "Nordic Stenbrott", "Eurogoodies", "Sanbuilders"];

const cityIds = Object.keys(CITIES);
const cityName = (id) => CITIES[id][0];
const cityPos = (id) => proj(CITIES[id][2], CITIES[id][3]);

function roadPath(a, b) {
  const p = cityPos(a), q = cityPos(b);
  const n = 14, pts = [];
  const nx = -(q.z - p.z), nz = q.x - p.x;
  const len = Math.hypot(nx, nz) || 1;
  const bend = (rnd() - 0.5) * 0.25;
  for (let i = 0; i <= n; i++) {
    const t = i / n;
    const wob = Math.sin(t * Math.PI) * bend + Math.sin(t * Math.PI * 3) * 0.02;
    pts.push(p.x + (q.x - p.x) * t + (nx / len) * wob * len, p.z + (q.z - p.z) * t + (nz / len) * wob * len);
  }
  return pts;
}

/* ---------------- Company fixture ---------------- */

const TRUCK_MODELS = [
  ["scania", "Scania", "S", "DC16 770", 770, "GRSO926R"], ["volvo", "Volvo", "FH 2024", "D17A780", 780, "I-Shift Dual Clutch"],
  ["daf", "DAF", "XG+", "MX-13 530", 530, "TraXon 12"], ["man", "MAN", "TGX 2020", "D3876 471", 640, "TipMatic 12"],
  ["mercedes", "Mercedes-Benz", "Actros 2019", "OM 473 625", 625, "PowerShift 3"], ["renault", "Renault", "T Evolution", "DTI 13 520", 520, "Optidriver"],
  ["iveco", "IVECO", "S-Way", "Cursor 13 570", 570, "Hi-Tronix"],
];
const GARAGES = [["hamburg", 5, true], ["bremen", 3], ["aarhus", 3], ["prague", 1]];
const DRIVER_NAMES = ["Lukas Brenner", "Sofia Lindqvist", "Marek Novák", "Jonas Petersen", "Elin Dahl", "Tomasz Wójcik", "Hanna Keller", "Pieter de Vries", "Mikkel Holm"];

function buildProfile() {
  seed = 11;
  const garages = GARAGES.map(([city, slots, hq]) => ({
    id: `garage.${city}`, cityId: city, city: cityName(city), country: { de: "Germany", dk: "Denmark", cz: "Czechia" }[CITIES[city][1]],
    status: slots === 5 ? 3 : slots === 3 ? 2 : 1, size: slots === 5 ? "Large" : slots === 3 ? "Medium" : "Small", slots,
    truckIds: [], driverIds: [], trailerIds: [], trailerSlots: slots, trucksAssigned: 0, driversAssigned: 0, trailersAssigned: 0,
    productivity: 0.6 + rnd() * 0.35, isHq: !!hq, profit: { revenue: 0, wage: 0, maintenance: 0, fuel: 0, distanceKm: 0, jobs: 0, profit: 0, days: [] },
  }));
  const trucks = [];
  const drivers = [{ id: "driver.105", name: "You", isPlayer: true, status: "player", xp: 0, skills: {}, profit: { revenue: 412_300, profit: 351_900, distanceKm: 38_420, jobs: 61, days: [] } }];
  let di = 0;
  garages.forEach((g) => {
    const n = g.slots === 5 ? 4 : g.slots === 3 ? (g.cityId === "bremen" ? 3 : 2) : 1;
    for (let i = 0; i < n; i++) {
      const [brandId, brand, model, engine, hp, gearbox] = TRUCK_MODELS[(trucks.length * 3) % TRUCK_MODELS.length];
      const isPlayer = trucks.length === 0;
      const t = {
        id: `_nameless.236.${(4000 + trucks.length).toString(16)}.${(9000 + trucks.length * 17).toString(16)}`,
        brandId, brand, modelId: `${brandId}.${model.toLowerCase().replace(/\W+/g, "_")}`, model, name: `${brand} ${model}`,
        engine, horsePower: hp, transmission: gearbox, chassis: pick(["4x2", "6x2 Midlift", "6x4", "6x2 Taglift"]), cabin: pick(["Highline", "Topline", "Globetrotter XL", "Space Cab"]),
        odometerKm: int(18_000, 420_000), fuelRelative: 0.25 + rnd() * 0.7, tripFuelLitres: int(80, 900), tripDistanceKm: int(200, 3000), tripTimeMinutes: int(200, 3000),
        wear: { engine: rnd() * 0.06, transmission: rnd() * 0.05, cabin: rnd() * 0.04, chassis: rnd() * 0.08, wheels: rnd() * 0.12, body: 0 },
        licensePlate: `${pick(["HH", "HB", "AR", "PR"])} ${int(1000, 9999)}`, plateCountry: g.country, accessoryValue: int(80_000, 190_000), accessoryCount: int(40, 80),
        garageId: g.id, garageCity: g.city, isPlayerTruck: isPlayer, profit: { revenue: 0, wage: 0, maintenance: 0, fuel: 0, distanceKm: 0, jobs: 0, profit: 0, days: [] },
      };
      t.wear.overall = Math.max(...Object.values(t.wear));
      if (isPlayer) {
        t.driverId = "driver.105"; t.driverName = "You"; drivers[0].truckId = t.id; drivers[0].truckName = t.name; drivers[0].garageId = g.id; drivers[0].garageCity = g.city;
        Object.assign(t, { brand: "Scania", model: "S", name: "Scania S", engine: "DC16 770", horsePower: 770, transmission: "GRSO926R", brandId: "scania" });
      } else if (di < DRIVER_NAMES.length && !(g.cityId === "aarhus" && i === 1)) {
        const days = Array.from({ length: 14 }, (_, k) => ({ day: 60 + k, revenue: rnd() > 0.2 ? int(4000, 16000) : 0, costs: int(900, 3500), distanceKm: int(200, 900) }));
        const revenue = days.reduce((a, d) => a + d.revenue, 0), costs = days.reduce((a, d) => a + d.costs, 0);
        const statuses = ["driving", "driving", "on_job", "resting", "driving", "available", "on_job", "resting", "driving"];
        const d = {
          id: `driver.${210 + di}`, name: DRIVER_NAMES[di], isPlayer: false, garageId: g.id, garageCity: g.city, truckId: t.id, truckName: t.name,
          hometown: pick(cityIds), currentCity: pick(cityIds), status: statuses[di], xp: int(4000, 90000),
          skills: { adr: int(0, 6), longDistance: int(0, 6), highValue: int(0, 6), fragile: int(0, 6), justInTime: int(0, 6), ecoDriving: int(0, 6) },
          trainingPolicy: pick(["Balanced", "Long distance", "ADR", "Fragile"]),
          job: statuses[di] === "driving" || statuses[di] === "on_job" ? { cargo: pick(CARGO), sourceCity: cityName(pick(cityIds)), targetCity: cityName(pick(cityIds)), plannedDistanceKm: int(180, 1200) } : null,
          profit: { revenue, wage: Math.round(costs * 0.45), maintenance: Math.round(costs * 0.2), fuel: Math.round(costs * 0.35), distanceKm: days.reduce((a, x) => a + x.distanceKm, 0), jobs: int(12, 60), days, cargoCounts: {} },
        };
        d.profit.profit = d.profit.revenue - d.profit.wage - d.profit.maintenance - d.profit.fuel;
        d.specialization = pick(CARGO);
        drivers.push(d);
        t.driverId = d.id; t.driverName = d.name;
        di++;
      }
      trucks.push(t);
      g.truckIds.push(t.id);
      if (t.driverId) g.driverIds.push(t.driverId);
    }
    g.trucksAssigned = g.truckIds.length;
    g.driversAssigned = g.driverIds.length;
  });
  // A hired driver waiting for a truck
  drivers.push({ id: "driver.300", name: "Mikkel Holm", isPlayer: false, garageId: "garage.prague", garageCity: "Prague", status: "available", xp: 2100,
    skills: { adr: 1, longDistance: 0, highValue: 1, fragile: 0, justInTime: 0, ecoDriving: 1 }, trainingPolicy: "Balanced", job: null,
    profit: { revenue: 0, wage: 0, maintenance: 0, fuel: 0, distanceKm: 0, jobs: 0, profit: 0, days: [] } });
  garages[3].driverIds.push("driver.300"); garages[3].driversAssigned = 1;

  const trailerTypes = [["Krone Profi Liner", "Curtainside", 40], ["Schmitz Cargobull S.KO", "Refrigerated", 38], ["Kögel Cargo", "Dry van", 40],
    ["Feldbinder Tank", "Fuel tank", 36], ["Krone Box Liner", "Container carrier", 34], ["SCS Log Trailer", "Log", 30], ["Wielton Tipper", "Tipper", 32]];
  const trailers = Array.from({ length: 11 }, (_, i) => {
    const [name, body, vol] = trailerTypes[i % trailerTypes.length];
    const g = garages[i % garages.length];
    const t = trucks[i % trucks.length];
    const tr = {
      id: `_nameless.236.6bdb.${(0xb000 + i * 97).toString(16)}`, typeId: name.toLowerCase().replace(/\W+/g, "."), name, bodyType: body, chainType: i % 5 === 3 ? "Double" : "Single",
      axles: pick([2, 3, 3, 3, 4]), grossWeightLimitKg: 40000, chassisMassKg: int(6500, 9000), volumeM3: vol * 2, lengthM: 13.6,
      cargoMassKg: i % 3 === 0 ? int(8000, 24000) : 0, cargoDamage: 0, odometerKm: int(2000, 180000),
      wear: { body: rnd() * 0.05, chassis: rnd() * 0.07, wheels: rnd() * 0.1 }, licensePlate: `${pick(["HH", "HB", "AR"])} ${int(100, 999)} T`,
      plateCountry: g.country, accessoryValue: int(30000, 90000), garageId: g.id, garageCity: g.city, isPlayerTrailer: i === 0,
      assignedTruckId: i < 7 ? t.id : null, assignedDriverId: i < 7 ? t.driverId : null,
    };
    tr.wear.overall = Math.max(tr.wear.body, tr.wear.chassis, tr.wear.wheels);
    g.trailerIds.push(tr.id); g.trailersAssigned++;
    return tr;
  });
  for (const t of trucks) {
    const days = Array.from({ length: 14 }, (_, k) => ({ day: 60 + k, revenue: rnd() > 0.25 ? int(3000, 15000) : 0, costs: int(600, 3000), distanceKm: int(150, 800) }));
    t.profit = { revenue: days.reduce((a, d) => a + d.revenue, 0), wage: 0, maintenance: int(2000, 9000), fuel: int(8000, 24000), distanceKm: days.reduce((a, d) => a + d.distanceKm, 0), jobs: int(8, 40), days };
    t.profit.profit = t.profit.revenue - t.profit.maintenance - t.profit.fuel;
  }
  for (const g of garages) {
    const gt = trucks.filter((t) => t.garageId === g.id);
    g.profit = { revenue: gt.reduce((a, t) => a + t.profit.revenue, 0), profit: gt.reduce((a, t) => a + t.profit.profit, 0), distanceKm: gt.reduce((a, t) => a + t.profit.distanceKm, 0), jobs: gt.reduce((a, t) => a + t.profit.jobs, 0), days: [] };
  }

  return {
    profileId: "4E6F72646861766E", profileName: "Nordhavn", companyName: "Nordhavn Haulage", preferredBrand: "scania_s_2016",
    saveName: "autosave", savePath: "…\\profiles\\4E6F72646861766E\\save\\autosave\\game.sii",
    saveTimeUtc: new Date(Date.now() - 42 * 60000).toISOString(), parsedAtUtc: new Date(Date.now() - 40 * 60000).toISOString(),
    money: 1_284_530, loans: 0, loanLimit: 500000, xp: 412_880,
    skills: { adrMask: 31, adr: 5, longDistance: 6, highValue: 4, fragile: 5, justInTime: 3, ecoDriving: 6, total: 29 },
    hqCity: "hamburg", hqCityName: "Hamburg", gameTimeMinutes: 94_512, totalDistanceKm: 146_820, totalRealTimeMinutes: 7_940,
    cancelledJobs: 4, totalFuelLitres: 48_210, totalFuelPrice: 71_880, serviceVisits: 38, gasStationVisits: 142,
    lastVisitedCity: "hamburg", visitedCities: cityIds, unlockedDealers: ["hamburg", "berlin", "aarhus", "prague", "wien", "amsterdam"],
    unlockedRecruitments: ["hamburg", "bremen", "aarhus", "prague", "rotterdam"], transportedCargoTypes: CARGO, activeModCount: 6,
    trucks, trailers, garages, drivers, deliveries: [], warnings: [],
  };
}

function buildDeliveries(profile) {
  seed = 23;
  const rows = [];
  const now = Date.now();
  let id = 1;
  for (let i = 0; i < 64; i++) {
    const a = pick(cityIds);
    let b = pick(cityIds);
    if (b === a) b = cityIds[(cityIds.indexOf(a) + 3) % cityIds.length];
    const pa = cityPos(a), pb = cityPos(b);
    const km = Math.round((Math.hypot(pa.x - pb.x, pa.z - pb.z) / 1000) * 19 * (1.15 + rnd() * 0.15));
    const hours = km / int(62, 74);
    const finished = new Date(now - i * 11.3 * 3600e3 - rnd() * 3 * 3600e3);
    const cancelled = i === 9 || i === 37;
    const fuel = km * (0.27 + rnd() * 0.08);
    rows.push({
      id: id++, source: i < 48 ? "telemetry" : "save", status: cancelled ? "cancelled" : "delivered",
      startedUtc: new Date(finished - hours * 3600e3).toISOString(), finishedUtc: i < 48 ? finished.toISOString() : null,
      gameStartMin: 90_000 - i * 700, gameEndMin: 90_000 - i * 700 + Math.round(hours * 60 * 1.3),
      truck: i % 4 === 0 ? pick(profile.trucks).name : "Scania S", truckBrand: "Scania", truckPlate: "HH 2026",
      trailer: pick(profile.trailers).name, driver: "Player", cargo: pick(CARGO), cargoId: "", cargoMassKg: int(6000, 26000),
      originCity: cityName(a), originCityId: a, originCompany: pick(COMPANIES), originCountry: CITIES[a][1],
      destCity: cityName(b), destCityId: b, destCompany: pick(COMPANIES), destCountry: CITIES[b][1],
      plannedKm: km, distanceKm: cancelled ? Math.round(km * 0.3) : km, income: cancelled ? 0 : Math.round(km * (19 + rnd() * 14)),
      xp: cancelled ? 0 : Math.round(km * 1.4 + 150), penalty: cancelled ? int(2000, 6000) : 0,
      fuelUsedL: i < 48 ? fuel : null, avgSpeedKmh: i < 48 ? 68 + rnd() * 12 : null, maxSpeedKmh: i < 48 ? 88 + rnd() * 6 : null,
      driveSeconds: i < 48 ? Math.round(hours * 3600 / 19) : null, gameMinutes: Math.round(hours * 60 * 1.3),
      cargoDamage: rnd() < 0.8 ? rnd() * 0.01 : rnd() * 0.06, truckDamage: rnd() * 0.03, autopark: 0, autoload: 0, market: "cargo_market", special: 0, demo: 0,
    score: int(62, 100), scoreDetail: JSON.stringify({ speedingPct: 6.5, cargoDamagePct: 0.8, truckDamagePct: 1.2, fines: 0, late: false, speedingPenalty: 4, cargoPenalty: 2, truckPenalty: 2, finePenalty: 0, latePenalty: 0 }),
      _route: i < 48 && !cancelled ? roadPath(a, b) : null,
    });
  }
  return rows;
}

const profile = buildProfile();
const deliveries = buildDeliveries(profile);
let settings = {
  setupComplete: true,
  general: { language: new URLSearchParams(location.search).get("lang") || "auto", languageChosen: new URLSearchParams(location.search).has("lang"), units: "metric", currency: "EUR", theme: "dark", startPage: "dashboard", startMinimized: false, launchWithWindows: false, minimizeToTray: false },
  ets2: { autoDetect: true, gamePath: null, documentsPath: null, profilePath: "C:\\Users\\you\\Documents\\Euro Truck Simulator 2\\profiles\\4E6F72646861766E", saveSelection: "latest", watchSaves: true, importSaveHistory: true },
  telemetry: { updateHz: 10, recordRoutes: true, recordFreeRoam: true, routePointSpacingM: 150, fields: { vehicle: true, drivetrain: true, fluids: true, damage: true, lights: true, navigation: true, job: true, trailer: true } },
  map: { defaultZoom: -5, followTruck: true, routeHistoryDays: 90, tileFolder: null, showEstimatedCities: true, layers: { truck: true, currentRoute: true, previousRoutes: true, garages: true, cities: true, services: false, dealers: false, recruitment: false, aiDrivers: true, fleet: true, events: false } },
  data: { autoBackup: true, backupIntervalHours: 24, backupKeep: 10, backupFolder: null },
  appearance: { accent: "amber", compact: false, animations: true, transparency: true, sidebarCollapsed: false },
  hud: { position: "topCenter", x: 50, y: 5, opacity: 90, size: "medium", onlyOnJob: false, fields: ["speedLimit", "remaining", "etaReal", "arrival"] },
  truckersMp: { antiAfk: false, message: "AFK - back soon", intervalMinutes: 8, chatKey: "Y", riskAccepted: false },
  notifications: { enabled: true, progress: true, warnings: true, overlay: true, voice: false, afkWarning: true, position: "topRight" },
};
const params = new URLSearchParams(location.search);
if (params.has("setup")) settings.setupComplete = false;
if (params.has("light")) settings.general.theme = "light";

const detection = {
  gamePath: "D:\\STEAM\\steamapps\\common\\Euro Truck Simulator 2", gameExe: "D:\\STEAM\\steamapps\\common\\Euro Truck Simulator 2\\bin\\win_x64\\eurotrucks2.exe",
  documentsPath: "C:\\Users\\you\\Documents\\Euro Truck Simulator 2",
  profiles: [
    { id: "4E6F72646861766E", name: "Nordhavn", companyName: "Nordhavn Haulage", path: settings.ets2.profilePath, kind: "local", lastSaveUtc: profile.saveTimeUtc, saveCount: 14, xp: profile.xp, distanceKm: profile.totalDistanceKm },
    { id: "54657374", name: "Test", companyName: "Sandbox Logistics", path: "C:\\Users\\you\\Documents\\Euro Truck Simulator 2\\profiles\\54657374", kind: "steam", lastSaveUtc: new Date(Date.now() - 9 * 86400e3).toISOString(), saveCount: 3, xp: 1200, distanceKm: 900 },
  ],
  pluginInstalled: true, pluginPath: "…\\bin\\win_x64\\plugins\\scs-telemetry.dll", otherTelemetryPlugins: [], saveFormat: "text", notes: [],
};

/* ---------------- Live drive simulation ---------------- */

const leg = { from: "hamburg", to: "bremen", cargo: "Logs", income: 8475, path: roadPath("hamburg", "bremen"), progress: 0.38 };
const segLens = [];
let totalLen = 0;
for (let i = 2; i < leg.path.length; i += 2) {
  const l = Math.hypot(leg.path[i] - leg.path[i - 2], leg.path[i + 1] - leg.path[i - 1]);
  segLens.push(l); totalLen += l;
}
function pointAt(f) {
  let d = f * totalLen;
  for (let k = 0; k < segLens.length; k++) {
    if (d <= segLens[k] || k === segLens.length - 1) {
      const t = Math.min(1, d / segLens[k]);
      const x0 = leg.path[k * 2], z0 = leg.path[k * 2 + 1], x1 = leg.path[k * 2 + 2], z1 = leg.path[k * 2 + 3];
      return { x: x0 + (x1 - x0) * t, z: z0 + (z1 - z0) * t, heading: (Math.atan2(x1 - x0, -(z1 - z0)) * 180 / Math.PI + 360) % 360 };
    }
    d -= segLens[k];
  }
  return { x: leg.path[0], z: leg.path[1], heading: 0 };
}

let tStart = performance.now();
let speed = 78, fuel = 412, odo = 128_432;
let live = !params.has("offline");
function snapshot() {
  const t = (performance.now() - tStart) / 1000;
  const target = 84 + Math.sin(t / 9) * 6 + Math.sin(t / 2.3) * 1.5;
  speed += (target - speed) * 0.08;
  leg.progress = Math.min(0.97, leg.progress + speed / 3.6 * 0.1 * 30 / (totalLen * 19));
  fuel -= 0.002; odo += speed / 36000 * 30;
  const p = pointAt(leg.progress);
  const plannedKm = (totalLen / 1000) * 19;
  const remaining = plannedKm * (1 - leg.progress);
  const gear = Math.min(12, Math.max(1, Math.floor(speed / 7.4) + 1));
  return {
    capturedUtc: new Date().toISOString(), sdkActive: true, paused: false, pluginRevision: 12, game: "ETS2", gameVersion: "1.18", gameTimeMinutes: 94_512 + Math.floor(t * 3),
    restStopMinutes: 382, demo: false, truckBrandId: "scania", truckBrand: "Scania", truckId: "vehicle.scania.s_2016", truckName: "S", licensePlate: "HH 2026",
    licensePlateCountry: "Germany", shifterType: "arcade", speedKmh: speed, cruiseControlKmh: 85, cruiseControl: true, speedLimitKmh: 90,
    engineRpm: 1150 + (speed % 7.4) * 55 + Math.sin(t * 3) * 10, engineRpmMax: 2500, gear, gearDashboard: gear, forwardGears: 12, reverseGears: 4,
    retarderLevel: 0, retarderSteps: 5, parkingBrake: false, engineBrake: false, engineOn: true, electricOn: true,
    throttle: Math.max(0, Math.min(1, 0.45 + Math.sin(t / 2) * 0.25)), brake: 0, clutch: 0, steering: Math.sin(t / 5) * 0.05,
    fuelLitres: fuel, fuelCapacity: 800, fuelAvgConsumption: 0.312, fuelRangeKm: fuel / 0.312, fuelWarning: false,
    adBlueLitres: 61, adBlueCapacity: 80, airPressure: 124 + Math.sin(t) * 2, oilPressure: 62, oilTemperature: 94, waterTemperature: 88,
    batteryVoltage: 27.6, brakeTemperature: 64, odometerKm: odo, wearEngine: 0.012, wearTransmission: 0.006, wearCabin: 0.008, wearChassis: 0.011, wearWheels: 0.019,
    truckDamage: 0.019, lightsLowBeam: true, lightsHighBeam: false, lightsBeacon: false, lightsHazard: false, blinkerLeft: false, blinkerRight: false,
    wipers: false, differentialLock: false, liftAxle: false, x: p.x, y: 18, z: p.z, headingDeg: p.heading, pitch: 0, roll: 0,
    routeDistanceKm: remaining, routeTimeSeconds: (remaining / 80) * 3600, timeScale: 19,
    eta: { source: "game", remainingKm: remaining, gameSeconds: (remaining / 80) * 3600, realSeconds: (remaining / 80) * 3600 / 19, timeScale: 19, arrivalUtc: new Date(Date.now() + ((remaining / 80) * 3600 / 19) * 1000).toISOString(), deadlineMarginGameMinutes: 95, targetsJob: true },
    onJob: true, cargoLoaded: true, specialJob: false,
    cargoId: "logs", cargo: leg.cargo, cargoMassKg: 22_400, cargoDamage: 0.003, cargoUnits: 1, sourceCityId: leg.from, sourceCity: cityName(leg.from),
    sourceCompanyId: "tradeaux", sourceCompany: "Tradeaux", destinationCityId: leg.to, destinationCity: cityName(leg.to), destinationCompanyId: "posped",
    destinationCompany: "Posped", jobIncome: leg.income, jobDeadlineGameMinutes: 94_512 + 520, plannedDistanceKm: Math.round(plannedKm), jobMarket: "cargo_market",
    trailerAttached: true, trailerName: "Log Trailer", trailerBrand: "SCS", trailerBodyType: "Log", trailerPlate: "HH 552 T",
    trailerWearChassis: 0.004, trailerWearWheels: 0.006, trailerWearBody: 0.002, trailerDamage: 0.006,
    gameplay: {}, flags: {},
  };
}

function statusPayload() {
  return {
    game: live ? "running" : "notRunning", telemetry: live ? "live" : "unavailable", pluginRevision: 12, gameVersion: "1.18",
    lastSampleUtc: live ? new Date().toISOString() : new Date(Date.now() - 3 * 3600e3).toISOString(), demo: false, pluginInstalled: true, gameDetected: true,
    profile: { state: "loaded", id: profile.profileId, name: profile.profileName, company: profile.companyName, parsedUtc: profile.parsedAtUtc, saveUtc: profile.saveTimeUtc, saveName: "autosave", error: null },
    db: { ok: true, error: null, path: "C:\\Users\\you\\AppData\\Local\\Haulix\\haulix.db", sizeBytes: 3_481_600 }, nowUtc: new Date().toISOString(),
  };
}

/* ---------------- Request handlers ---------------- */

const strip = ({ _route, ...r }) => r;

function logbook(f = {}) {
  let rows = deliveries.slice();
  const q = (f.search || "").toLowerCase();
  if (q) rows = rows.filter((r) => [r.cargo, r.originCity, r.destCity, r.originCompany, r.destCompany, r.truck].some((v) => v?.toLowerCase().includes(q)));
  if (f.truck) rows = rows.filter((r) => r.truck === f.truck);
  if (f.cargo) rows = rows.filter((r) => r.cargo === f.cargo);
  if (f.country) rows = rows.filter((r) => r.originCountry === f.country || r.destCountry === f.country);
  if (f.city) rows = rows.filter((r) => r.originCity === f.city || r.destCity === f.city);
  if (f.status) rows = rows.filter((r) => r.status === f.status);
  if (f.source) rows = rows.filter((r) => r.source === f.source);
  if (f.minDistance) rows = rows.filter((r) => r.distanceKm >= f.minDistance);
  if (f.maxDistance) rows = rows.filter((r) => r.distanceKm <= f.maxDistance);
  if (f.minIncome) rows = rows.filter((r) => r.income >= f.minIncome);
  if (f.from) rows = rows.filter((r) => r.finishedUtc && r.finishedUtc >= f.from);
  if (f.to) rows = rows.filter((r) => r.finishedUtc && r.finishedUtc.slice(0, 10) <= f.to);
  const sorters = {
    longest: (a, b) => b.distanceKm - a.distanceKm, income: (a, b) => b.income - a.income, xp: (a, b) => b.xp - a.xp,
    efficiency: (a, b) => (a.fuelUsedL ? a.fuelUsedL / a.distanceKm : 9) - (b.fuelUsedL ? b.fuelUsedL / b.distanceKm : 9),
    oldest: (a, b) => a.id < b.id ? 1 : -1, recent: (a, b) => a.id - b.id,
  };
  rows.sort(sorters[f.sort] || sorters.recent);
  const totals = { count: rows.length, distanceKm: rows.reduce((a, r) => a + r.distanceKm, 0), income: rows.reduce((a, r) => a + r.income, 0), xp: rows.reduce((a, r) => a + r.xp, 0), fuelL: rows.reduce((a, r) => a + (r.fuelUsedL || 0), 0) };
  const uniq = (k) => [...new Set(deliveries.map((r) => r[k]).filter(Boolean))].sort();
  return {
    rows: rows.slice(f.offset || 0, (f.offset || 0) + (f.limit || 50)).map(strip), totals,
    facets: { trucks: uniq("truck"), drivers: ["Player"], cargo: uniq("cargo"), cities: [...new Set([...uniq("originCity"), ...uniq("destCity")])].sort(),
      countries: [...new Set([...uniq("originCountry"), ...uniq("destCountry")])].map((c) => ({ code: c, name: { de: "Germany", nl: "Netherlands", dk: "Denmark", se: "Sweden", cz: "Czechia", pl: "Poland", at: "Austria", be: "Belgium", lu: "Luxembourg" }[c] || c })) },
  };
}

function stats(from, to) {
  const byDay = new Map();
  for (const r of deliveries) {
    if (!r.finishedUtc) continue;
    const day = r.finishedUtc.slice(0, 10);
    if (day < from || day > to) continue;
    const d = byDay.get(day) || { day, jobs: 0, delivered: 0, distanceKm: 0, income: 0, xp: 0, penalties: 0, fuelL: 0, driveSeconds: 0, maxSpeed: 0 };
    d.jobs++; if (r.status === "delivered") d.delivered++;
    d.distanceKm += r.distanceKm; d.income += r.income; d.xp += r.xp; d.penalties += r.penalty; d.fuelL += r.fuelUsedL || 0; d.driveSeconds += r.driveSeconds || 0;
    d.maxSpeed = Math.max(d.maxSpeed, r.maxSpeedKmh || 0);
    byDay.set(day, d);
  }
  const days = [...byDay.values()].sort((a, b) => a.day.localeCompare(b.day));
  seed = 5;
  const sessions = days.map((d) => ({ day: d.day, distanceKm: d.distanceKm * 1.08, driveSeconds: d.driveSeconds * 1.1, idleSeconds: d.driveSeconds * 0.12, maxSpeed: d.maxSpeed, fuelL: d.fuelL * 1.07, sessions: int(1, 3) }));
  const expenses = days.flatMap((d) => [
    { day: d.day, type: "toll", amount: int(0, 3) * 120, count: 1 },
    { day: d.day, type: "fine", amount: rnd() < 0.2 ? int(200, 1500) : 0, count: 1 },
    { day: d.day, type: "ferry", amount: rnd() < 0.15 ? int(600, 1800) : 0, count: 1 },
  ]);
  const group = (key) => {
    const m = new Map();
    for (const r of deliveries) if (r[key] && r.status === "delivered") {
      const g = m.get(r[key]) || { [key]: r[key], jobs: 0, income: 0, distanceKm: 0, fuelL: 0 };
      g.jobs++; g.income += r.income; g.distanceKm += r.distanceKm; g.fuelL += r.fuelUsedL || 0; m.set(r[key], g);
    }
    return [...m.values()].sort((a, b) => b.income - a.income).slice(0, 12);
  };
  const del = deliveries.filter((r) => r.status === "delivered");
  return {
    deliveries: days, sessions, expenses, byCargo: group("cargo"), byTruck: group("truck"),
    allTime: { jobs: del.length, distanceKm: del.reduce((a, r) => a + r.distanceKm, 0), income: del.reduce((a, r) => a + r.income, 0), xp: del.reduce((a, r) => a + r.xp, 0),
      longestKm: Math.max(...del.map((r) => r.distanceKm)), bestIncome: Math.max(...del.map((r) => r.income)), fuelL: del.reduce((a, r) => a + (r.fuelUsedL || 0), 0),
      fuelDistanceKm: del.filter((r) => r.fuelUsedL).reduce((a, r) => a + r.distanceKm, 0) },
    sessionTotals: { distanceKm: 31_240, driveSeconds: 1_530_000 / 19, maxSpeed: 94.2, fuelL: 9_880, sessions: 88 },
  };
}

function mapPayload() {
  const learned = ["hamburg", "bremen", "osnabruck", "berlin", "kiel", "aarhus", "prague", "rotterdam"].map((id) => {
    const p = cityPos(id);
    return { id, name: cityName(id), country: CITIES[id][1], x: p.x + (rnd() - 0.5) * 300, z: p.z + (rnd() - 0.5) * 300, samples: int(1, 8) };
  });
  const routes = deliveries.filter((r) => r._route).slice(0, 30).map((r) => ({
    id: r.id, kind: "job", startedUtc: r.startedUtc, endedUtc: r.finishedUtc, distanceKm: r.distanceKm, deliveryId: r.id,
    originCity: r.originCity, destCity: r.destCity, cargo: r.cargo, income: r.income, points: r._route, current: false,
  }));
  const k = Math.floor(leg.progress * 14);
  routes.push({ id: 999, kind: "job", startedUtc: new Date(Date.now() - 3600e3).toISOString(), endedUtc: null, distanceKm: 0, deliveryId: null,
    originCity: "Hamburg", destCity: "Bremen", cargo: "Logs", income: null, points: leg.path.slice(0, (k + 1) * 2), current: true });
  return { cities: learned, routes, events: [] };
}

let mapStatus = { state: "missing", progress: 0, message: "Road map not built yet", key: null, streetsUrl: null, poisUrl: null, builtUtc: null, gameVersion: null, segments: 0 };
let mockRoute = null;
function jobRoute() {
  const end = cityPos(leg.to);
  return { mode: "job", destination: { kind: "company", name: `Posped, ${cityName(leg.to)}`, companyId: "posped", cityId: leg.to, x: end.x, z: end.z },
    points: leg.path, lengthMeters: totalLen, estimatedKm: Math.round(totalLen * 19 / 1000), computedUtc: new Date().toISOString(), error: null };
}

function handle(method, p) {
  switch (method) {
    case "map.status": return mapStatus;
    case "map.build": {
      mapStatus = { ...mapStatus, state: "building", progress: 0, message: "Reading map sectors (preview)" };
      let n = 0;
      const t = setInterval(() => {
        n++;
        mapStatus = n < 10 ? { ...mapStatus, progress: n / 10, message: `Reading map sectors (${n * 4}/40)` } : { ...mapStatus, state: "ready", progress: 1, message: "Preview: streets are only available in the desktop app", segments: 596954, builtUtc: new Date().toISOString(), gameVersion: "1.61" };
        mockDispatch?.({ event: "mapStatus", data: mapStatus });
        if (n >= 10) { clearInterval(t); mockRoute = jobRoute(); mockDispatch?.({ event: "route", data: mockRoute }); }
      }, 400);
      return mapStatus;
    }
    case "map.cancelBuild": return true;
    case "route.get": return mockRoute;
    case "route.clear": mockRoute = jobRoute(); return mockRoute;
    case "route.reroute": return mockRoute;
    case "route.setCity": case "route.setPoint": case "route.setCompany": {
      const dest = (p.cityId && cityPosOrNull(p.cityId)) || { x: p.x ?? 0, z: p.z ?? 0 };
      const s = snapshot();
      mockRoute = { mode: "manual", destination: { kind: p.cityId ? "city" : "point", name: p.name, x: dest.x, z: dest.z }, points: [s.x, s.z, (s.x + dest.x) / 2 + 800, (s.z + dest.z) / 2, dest.x, dest.z],
        lengthMeters: Math.hypot(dest.x - s.x, dest.z - s.z), estimatedKm: Math.round(Math.hypot(dest.x - s.x, dest.z - s.z) * 19 / 1000), computedUtc: new Date().toISOString(), error: null };
      return mockRoute;
    }
    case "app.init":
      return { version: "1.0.0", settings, detection, status: statusPayload(), profile, telemetry: live ? { snapshot: snapshot(), job: null, routeId: 999 } : null,
        counts: { deliveries: deliveries.length, recorded: 48, imported: 16, routes: 46, routePoints: 18_220, sessions: 88, events: 214, learnedCities: 8, snapshots: 31 },
        dataFolder: "C:\\Users\\you\\AppData\\Local\\Haulix", backupFolder: "C:\\Users\\you\\AppData\\Local\\Haulix\\backups", mapStatus, route: mockRoute, systemLanguage: navigator.language.slice(0, 2) };
    case "settings.get": return settings;
    case "ets2.display": return { mode: "exclusive", gameRunning: false };
    case "ets2.setBorderless": return { ok: true, display: { mode: "borderless", gameRunning: false } };
    case "achievements.get": return [
      ["first_delivery", "package", "bronze", "First delivery", "Deliver your first job.", 1, 1],
      ["deliveries_50", "package", "silver", "Regular", "Deliver 50 jobs.", 50, 50],
      ["deliveries_250", "package", "gold", "Road veteran", "Deliver 250 jobs.", 64, 250],
      ["km_10000", "route", "bronze", "10,000 km", "Drive 10,000 km in total.", 10000, 10000],
      ["km_100000", "route", "silver", "100,000 km", "Drive 100,000 km in total.", 38420, 100000],
      ["perfect_10", "award", "silver", "Clean driver", "10 deliveries with a driving score of 95 or more.", 4, 10],
      ["night_10", "moon", "bronze", "Night owl", "Finish 10 deliveries between 22:00 and 05:00 (game time).", 10, 10],
      ["millionaire", "badge-euro", "gold", "Millionaire", "Have €1,000,000 in the bank.", 958481, 1000000],
    ].map(([id, icon, tier, title, description, progress, target]) => ({ id, icon, tier, title, description, progress, target, unlocked: progress >= target, unlockedUtc: progress >= target ? new Date(Date.now() - 86400e3 * 3).toISOString() : null }));
    case "update.check": return { enabled: !!settings.general.updateFeedUrl, available: false, current: "0.0.4-beta" };
    case "image.save": return "C:\\Users\\you\\Pictures\\haulix-card.png";
    case "image.copy": return true;
    case "shell.openUrl": return true;
    case "hud.preview": return true;
    case "notify.test": setTimeout(() => mockDispatch?.({ event: "notify", data: { kind: "info", category: "test", title: "Job updates appear here", message: "While you drive, HAULIX shows milestones, deadline and fuel warnings over the game." } }), 50); return true;
    case "settings.save": settings = p.settings; return settings;
    case "ets2.detect": return detection;
    case "ets2.validatePaths": return { game: p.gamePath || detection.gamePath, documents: p.documentsPath || detection.documentsPath, profiles: detection.profiles };
    case "ets2.saves": return [{ name: "autosave", path: "…", savedUtc: profile.saveTimeUtc, sizeBytes: 3_866_088 }, { name: "quicksave", path: "…", savedUtc: new Date(Date.now() - 86400e3).toISOString(), sizeBytes: 3_812_000 }];
    case "ets2.selectProfile": settings.ets2.profilePath = p.path; return settings;
    case "profile.get": return profile;
    case "profile.reload": return true;
    case "logbook.query": return logbook(p.filter);
    case "logbook.get": {
      const r = deliveries.find((d) => d.id === p.id);
      if (!r) return null;
      const pts = [];
      if (r._route) for (let i = 0; i < r._route.length; i += 2) pts.push({ x: r._route[i], z: r._route[i + 1], speed: 60 + Math.sin(i) * 20, t: r.startedUtc });
      return { delivery: strip(r), points: pts };
    }
    case "logbook.recent": return deliveries.slice(0, p.limit || 8).map(strip);
    case "events.recent": return [
      { id: 1, at: new Date(Date.now() - 18 * 60000).toISOString(), type: "toll", amount: 120, detail: "Toll gate" },
      { id: 2, at: new Date(Date.now() - 64 * 60000).toISOString(), type: "refuel", amount: null, detail: "388 L" },
      { id: 3, at: new Date(Date.now() - 3.2 * 3600e3).toISOString(), type: "fine", amount: 400, detail: "Speeding" },
      { id: 4, at: new Date(Date.now() - 6 * 3600e3).toISOString(), type: "ferry", amount: 1240, detail: "Kiel → Göteborg" },
    ];
    case "stats.get": return stats(p.from, p.to);
    case "stats.snapshots": {
      seed = 3;
      let money = 820_000;
      return Array.from({ length: 30 }, (_, i) => ({ t: new Date(Date.now() - (29 - i) * 86400e3).toISOString(), money: (money += int(-8000, 30000)), xp: 300_000 + i * 3800, distanceKm: 120_000 + i * 900, trucks: 7, trailers: 11, garages: 4, drivers: 8, aiRevenue: i * 9000, aiProfit: i * 5200 }));
    }
    case "map.get": return mapPayload();
    case "map.driven": return mapPayload().routes.flatMap((r) => [...r.points, null, null]);
    case "data.counts": return { deliveries: deliveries.length, recorded: 48, imported: 16, routes: 46, routePoints: 18_220, sessions: 88, events: 214, learnedCities: 8, snapshots: 31 };
    case "data.backups": return [
      { name: "auto-20260923-080102.haulix", path: "…", createdUtc: new Date(Date.now() - 5 * 3600e3).toISOString(), sizeBytes: 1_210_220, kind: "auto" },
      { name: "auto-20260922-080044.haulix", path: "…", createdUtc: new Date(Date.now() - 29 * 3600e3).toISOString(), sizeBytes: 1_190_004, kind: "auto" },
      { name: "manual-20260920-212210.haulix", path: "…", createdUtc: new Date(Date.now() - 3.2 * 86400e3).toISOString(), sizeBytes: 1_120_900, kind: "manual" },
    ];
    case "data.backupNow": return { name: "manual-now.haulix", path: "…", createdUtc: new Date().toISOString(), sizeBytes: 1_220_000, kind: "manual" };
    case "data.export": return { name: "haulix-export.haulix", path: "C:\\Users\\you\\Documents\\haulix-export.haulix", createdUtc: new Date().toISOString(), sizeBytes: 1_220_000, kind: "manual" };
    case "data.exportCsv": return "C:\\Users\\you\\Documents\\haulix-logbook.csv";
    case "data.import": case "data.restore": case "data.clear": return true;
    case "data.check": return { ok: true };
    case "demo.set": live = !!p.on || live; return p.on;
    case "dialog.pickFolder": return null;
    case "shell.openFolder": case "window.setTitle": return true;
    default: throw new Error(`Mock: unknown method ${method}`);
  }
}

let mockDispatch = null;
const cityPosOrNull = (id) => (CITIES[id] ? cityPos(id) : null);
export function createMockTransport(dispatch) {
  mockDispatch = dispatch;
  setInterval(() => dispatch({ event: "status", data: statusPayload() }), 1000);
  setInterval(() => { if (live) dispatch({ event: "telemetry", data: { snapshot: snapshot(), job: { distanceKm: 118, fuelUsedL: 36, maxSpeedKmh: 89, avgSpeedKmh: 76, driveSeconds: 5400 }, routeId: 999 } }); }, 100);
  return (msg) => {
    setTimeout(() => {
      try {
        dispatch({ id: msg.id, ok: true, result: structuredClone(handle(msg.method, msg.params || {})) });
      } catch (e) {
        dispatch({ id: msg.id, ok: false, error: e.message });
      }
    }, 40 + Math.random() * 60);
  };
}
