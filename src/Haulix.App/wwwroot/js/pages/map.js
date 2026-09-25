import { html, $, esc, debounce } from "../core/html.js";
import { icon } from "../core/icons.js";
import { store, isLive } from "../core/store.js";
import { toggle, driverStatus, toast, contextMenu, progress } from "../components/ui.js";
import { createMap, truckMarker, routeLine, cityMarker, toLatLng, fitProjection, labelMarker, segments } from "../components/map.js";
import { streetsLayer, markExplored, routeLayer, destinationMarker, progressIndex, remainingLatLngs, remainingKm, countriesLayer, loadCountries, landLayer } from "../components/streets.js";
import { cityPositions } from "../core/teleview.js";
import { loadCities, countryName } from "../core/cities.js";
import { loadMapData, mapReady } from "../core/mapdata.js";
import { saveSettings } from "../app.js";
import * as f from "../core/format.js";
import { t } from "../core/i18n.js";

// [key, label, swatch, default]
const LAYERS = [
  ["truck", "Current truck", "var(--accent)", true],
  ["navigation", "Navigation route", "var(--accent)", true],
  ["driven", "Driven this trip", "var(--map-explored)", true],
  ["previousRoutes", "Previous routes", "var(--map-explored)", true],
  ["garages", "Your garages", "var(--accent)", true],
  ["fleet", "Fleet trucks", "var(--text)", true],
  ["aiDrivers", "AI drivers", "var(--info)", true],
  ["cities", "Cities", "var(--text-2)", true],
  ["companies", "Companies", "var(--text-3)", false],
  ["fuel", "Fuel stations", "var(--warn)", false],
  ["service", "Service shops", "var(--text)", false],
  ["dealers", "Truck dealers", "var(--info)", false],
  ["recruitment", "Recruitment agencies", "var(--ok)", false],
  ["parking", "Parking", "var(--text-3)", false],
  ["ferries", "Ferries & trains", "var(--info)", false],
  ["events", "Tolls, fines & ferries paid", "var(--crit)", false],
];
const POI_ICONS = { fuel: "fuel", service: "wrench", dealer: "store", recruitment: "users", parking: "circle-parking", ferry: "anchor", train: "train-front" };
const POI_LAYER = { fuel: "fuel", service: "service", dealer: "dealers", recruitment: "recruitment", parking: "parking", ferry: "ferries", train: "ferries", company: "companies" };
const POI_MIN_ZOOM = -5.5;

const pretty = (id) => (id || "").replace(/_/g, " ").replace(/\b\w/g, (m) => m.toUpperCase());

export default {
  title: "Map",
  flush: true,
  crumb: () => (mapReady() ? "ETS2 road map · extracted from your game files" : "ETS2 world · schematic"),

  render() {
    return html`<div class="map-page">
      <div class="map-page__canvas" id="bigMap"></div>
      <div class="map-left"><div class="map-toolbar">
        <button class="btn btn--sm" data-act="follow" id="followBtn">${icon("locate-fixed")}Follow truck</button>
        <button class="btn btn--sm" data-act="route">${icon("route")}Show route</button>
        <button class="btn btn--sm" data-act="fit">${icon("maximize-2")}Fit all</button>
        <button class="btn btn--sm map-toolbar__layers" data-act="layers">${icon("layers")}Layers</button>
      </div>
      <aside class="nav-panel" id="navPanel"></aside></div>
      <aside class="map-panel" id="mapPanel"></aside>
      <div class="map-info hidden" id="mapInfo"></div>
      <div class="map-trip hidden" id="mapTrip"></div>
    </div>`;
  },

  async mount(root, { call, setCrumb }) {
    const settings = store.get("settings");
    const layersOn = Object.fromEntries(LAYERS.map(([k, , , d]) => [k, settings.map.layers?.[k] ?? d]));
    const cities = await loadCities();
    const tileConfig = settings.map.tileFolder ? await fetch("https://tiles.haulix/haulix-tiles.json").then((r) => r.json()).catch(() => null) : null;
    const map = createMap($("#bigMap", root), { zoom: settings.map.defaultZoom, tileConfig, grid: !mapReady() });
    const groups = Object.fromEntries(LAYERS.map(([k]) => [k, window.L.layerGroup()]));
    // Base map (always on, not switchable): streets and country names.
    groups.streets = window.L.layerGroup().addTo(map);
    groups.countries = window.L.layerGroup().addTo(map);
    const counts = {};
    let follow = settings.map.followTruck;
    let truck = null;
    let data = null;
    let mapData = null;
    let streets = null;
    let historyDays = settings.map.routeHistoryDays;
    let selectedRoute = null;
    let routeHint = 0;
    let destMarker = null;
    let cityPos = new Map();
    let recordedCurrent = null;

    const visible = (k) => layersOn[k] && (!POI_LAYER_KEYS.has(k) || map.getZoom() >= POI_MIN_ZOOM);
    const POI_LAYER_KEYS = new Set(Object.values(POI_LAYER));
    const syncLayers = () => { for (const [k] of LAYERS) (visible(k) ? groups[k].addTo(map) : groups[k].remove()); };
    map.on("zoomend", syncLayers);
    const zoomClass = () => {
      const el = $("#bigMap", root);
      el?.classList.toggle("map-zoom-close", map.getZoom() > -4);
      el?.classList.toggle("map-zoom-far", map.getZoom() < -6.25); // hide city names at overview zoom
      el?.classList.toggle("map-zoom-farthest", map.getZoom() < -7.75); // whole continent: country names would overlap
    };
    map.on("zoomend", zoomClass);
    zoomClass();

    const exploredColor = () => getComputedStyle(document.documentElement).getPropertyValue("--map-explored").trim();
    const trail = window.L.polyline([], { color: exploredColor(), weight: 2.5, opacity: 0.9, dashArray: "2 6", interactive: false }).addTo(groups.driven);
    const nav = routeLayer().addTo(groups.navigation);

    const setFollow = (v) => { follow = v; $("#followBtn", root).classList.toggle("is-active", v); };
    setFollow(follow);
    map.on("dragstart", () => setFollow(false));

    const info = $("#mapInfo", root);
    const showInfo = (content) => { info.innerHTML = content.toString(); info.classList.remove("hidden"); info.querySelector("[data-close]")?.addEventListener("click", () => info.classList.add("hidden")); };
    const infoHead = (label) => html`<div class="row" style="margin-bottom:8px"><span class="label">${label}</span><button class="btn btn--ghost btn--icon btn--sm" style="margin-left:auto" data-close>${icon("x")}</button></div>`;
    const bigName = (t) => html`<div style="font-family:var(--font-display);font-size:22px;font-weight:600;text-transform:uppercase;letter-spacing:.04em">${t}</div>`;

    /* ---------------- navigation ---------------- */

    const navigateTo = async (method, params, label) => {
      try {
        const r = await call(method, params);
        store.set("route", r);
        if (r?.error) toast({ kind: "warning", title: "No route", message: r.error });
        else if (r) toast({ kind: "success", title: `Navigating to ${label}`, message: `≈ ${f.dist(r.estimatedKm)} via the road network`, timeout: 3500 });
        else toast({ kind: "warning", title: "No route", message: "The destination could not be reached on the road map." });
      } catch (e) {
        toast({ kind: "error", title: "Routing failed", message: e.message });
      }
    };
    const navigateToCity = (id) => navigateTo("route.setCity", { cityId: id, name: cityName(id) }, cityName(id));
    const cityName = (id) => cities.byId.get(id)?.name || pretty(id);

    const drawRoute = () => {
      const r = store.get("route");
      const s = store.get("telemetry")?.snapshot;
      destMarker?.remove();
      destMarker = null;
      if (!r || !r.points?.length) { nav.update([]); renderNav(); return; }
      if (s) routeHint = progressIndex(r.points, s.x, s.z, routeHint);
      nav.update(remainingLatLngs(r.points, routeHint, s && (s.x || s.z) ? { x: s.x, z: s.z } : null));
      destMarker = destinationMarker(r.destination.x, r.destination.z, r.destination.name).addTo(groups.navigation);
      renderNav();
    };

    const renderNav = () => {
      const st = store.get("mapStatus");
      const r = store.get("route");
      const s = store.get("telemetry")?.snapshot;
      const el = $("#navPanel", root);
      if (st?.state !== "ready") {
        el.innerHTML = html`<section>
          <div class="label" style="margin-bottom:8px">Road map</div>
          ${st?.state === "building" ? html`<p class="muted" style="font-size:12px;margin-bottom:10px">${st.message}</p>${progress(st.progress)}<p class="faint" style="font-size:11px;margin-top:8px">Reading streets from your ETS2 files. This happens once per game update and takes about a minute. You can keep playing.</p>
              <button class="btn btn--sm btn--ghost" style="margin-top:8px" id="cancelBuild">Cancel</button>`
          : st?.state === "unavailable" ? html`<p class="muted" style="font-size:12px">ETS2 installation not found, so streets and navigation are unavailable. Set the game folder in Settings → ETS2.</p>`
          : html`<p class="muted" style="font-size:12px;margin-bottom:12px">${st?.state === "error" ? html`<span class="crit">Build failed: ${st.message}</span>` : "Streets and navigation need a one-time road map built from your ETS2 files (about a minute, fully offline)."}</p>
              <button class="btn btn--primary btn--sm" id="buildMap">${icon("map")}Build road map</button>`}
        </section>`.toString();
        $("#buildMap", root)?.addEventListener("click", () => call("map.build").then((m) => store.set("mapStatus", m)));
        $("#cancelBuild", root)?.addEventListener("click", () => call("map.cancelBuild"));
        return;
      }
      const hasRoute = r && r.points?.length;
      const eta = s?.eta;
      const km = hasRoute ? (eta ? eta.remainingKm : remainingKm(r.points, routeHint)) : 0;
      el.innerHTML = html`
        <section>
          <div class="row" style="margin-bottom:10px"><span class="label">Navigation</span><span class="spacer"></span>
            ${r ? html`<span class="badge ${r.mode === "job" ? "badge--accent" : "badge--info"}">${r.mode === "job" ? "Job" : "Manual"}</span>` : html`<span class="badge">Idle</span>`}</div>
          ${r ? html`
            <div class="nav-dest">${r.destination.name}</div>
            ${r.gameRoute === "matched" ? html`<div class="nav-sync nav-sync--ok" data-tip="HAULIX compares its route with the remaining distance of the in-game GPS and follows your route changes">${icon("circle-check")}Matches the in-game GPS</div>` : r.gameRoute === "differs" ? html`<div class="nav-sync nav-sync--warn" data-tip="The in-game GPS reports a distance that no road-map route matches, e.g. a waypoint or a road HAULIX does not know">${icon("triangle-alert")}In-game GPS takes another way</div>` : ""}
            ${r.error ? html`<p class="warn" style="font-size:12px;margin-top:6px">${r.error}</p>` : html`
              <div class="row" style="gap:18px;margin-top:10px">
                <div class="stat"><div class="stat__value" style="font-size:18px">${f.dist(km)}</div><div class="stat__label">${eta?.source === "game" ? "Remaining (game GPS)" : "Remaining (≈)"}</div></div>
                ${eta ? html`<div class="stat" data-tip="${eta.source === "game" ? "From the in-game navigation, converted to real time" : "Estimated from the HAULIX route and your average speed"}"><div class="stat__value" style="font-size:18px">${f.duration(Math.max(60, eta.realSeconds), { short: true })}</div><div class="stat__label">Real time · ≈ ${f.time(eta.arrivalUtc)}</div></div>
                  <div class="stat"><div class="stat__value" style="font-size:18px">${f.duration(eta.gameSeconds, { short: true })}</div><div class="stat__label">Game ETA</div></div>` : ""}
              </div>`}
            <div class="row" style="margin-top:12px;flex-wrap:wrap">
              ${r.mode === "manual" ? html`<button class="btn btn--sm" id="navClear">${icon("x")}${s?.onJob ? "Back to job route" : "Clear"}</button>` : ""}
              <button class="btn btn--sm btn--ghost" id="navReroute">${icon("refresh-cw")}Reroute</button>
            </div>`
          : html`<p class="muted" style="font-size:12px">${s?.onJob ? "Planning route to your job…" : "No active job. Search a destination or right-click the map to navigate."}</p>`}
        </section>
        <section>
          <div class="search">${icon("search")}<input class="input" id="navSearch" placeholder="Navigate to city or company…" autocomplete="off"></div>
          <div class="nav-results" id="navResults"></div>
          <p class="faint" style="font-size:11px;margin-top:8px;line-height:1.5">Tip: right-click anywhere on the map to navigate there. HAULIX plans its own route over the road network, so it may occasionally differ from the in-game GPS.</p>
        </section>`.toString();
      $("#navClear", root)?.addEventListener("click", async () => store.set("route", await call("route.clear")));
      $("#navReroute", root)?.addEventListener("click", async () => store.set("route", await call("route.reroute")));
      const input = $("#navSearch", root);
      input.addEventListener("input", debounce(() => searchDest(input.value.trim().toLowerCase()), 120));
    };

    const searchDest = (q) => {
      const out = $("#navResults", root);
      if (!q || !mapData) { out.innerHTML = ""; return; }
      const results = [];
      for (const c of mapData.pois.byKind.get("city") || []) {
        const name = cityName(c.id);
        if (name.toLowerCase().includes(q) || c.id.includes(q)) results.push({ kind: "city", label: name, sub: countryName(cities.byId.get(c.id)?.country) || "City", p: c });
      }
      for (const c of mapData.pois.byKind.get("company") || []) {
        const name = pretty(c.id);
        if (name.toLowerCase().includes(q) || cityName(c.c).toLowerCase().includes(q)) results.push({ kind: "company", label: `${name}`, sub: cityName(c.c), p: c });
      }
      results.sort((a, b) => (a.kind === b.kind ? a.label.localeCompare(b.label) : a.kind === "city" ? -1 : 1));
      out.innerHTML = results.slice(0, 40).map((r, i) => html`<button data-i="${i}">${icon(r.kind === "city" ? "building-2" : "factory")}<span class="grow ellipsis">${r.label}</span><span class="faint">${r.sub}</span></button>`.toString()).join("")
        || `<div class="faint" style="font-size:12px;padding:6px">No matches</div>`;
      out.querySelectorAll("[data-i]").forEach((b) => b.onclick = () => {
        const r = results[+b.dataset.i];
        if (r.kind === "city") navigateToCity(r.p.id);
        else navigateTo("route.setCompany", { companyId: r.p.id, cityId: r.p.c, name: `${r.label}, ${r.sub}`, x: r.p.x, z: r.p.z }, `${r.label}, ${r.sub}`);
        $("#navSearch", root).value = "";
        out.innerHTML = "";
        map.setView(toLatLng(r.p.x, r.p.z), Math.max(map.getZoom(), -5.5));
      });
    };

    map.on("contextmenu", (e) => {
      if (!mapReady()) return;
      const x = e.latlng.lng, z = -e.latlng.lat;
      let near = null, bd = Infinity;
      for (const c of cityPos.values()) { const d = Math.hypot(c.x - x, c.z - z); if (d < bd) { bd = d; near = c; } }
      contextMenu(e.originalEvent, [
        { label: "Navigate here", icon: "navigation", onClick: () => navigateTo("route.setPoint", { x, z, name: near ? `Near ${near.name}` : "Map point" }, near ? `near ${near.name}` : "map point") },
        near && { label: `Navigate to ${near.name}`, icon: "building-2", onClick: () => navigateToCity(near.id) },
        "-",
        { label: "Copy coordinates", icon: "copy", onClick: () => navigator.clipboard?.writeText(`${x.toFixed(1)}, ${z.toFixed(1)}`) },
      ].filter(Boolean));
    });

    /* ---------------- data layers ---------------- */

    const poiMarker = (p, kind) => {
      if (kind === "company" || kind === "parking") {
        return window.L.circleMarker(toLatLng(p.x, p.z), {
          radius: kind === "company" ? 3 : 2.5, weight: 0, fillOpacity: 0.8,
          fillColor: getComputedStyle(document.documentElement).getPropertyValue(kind === "company" ? "--text-3" : "--text-3").trim(),
        }).bindTooltip(esc(kind === "company" ? `${pretty(p.id)} · ${cityName(p.c)}` : "Parking"));
      }
      return window.L.marker(toLatLng(p.x, p.z), {
        icon: window.L.divIcon({ className: "", html: `<div class="poi-marker poi-marker--${kind}">${icon(POI_ICONS[kind] || "circle-dot").toString()}</div>`, iconSize: [18, 18], iconAnchor: [9, 9] }),
      }).bindTooltip(esc({ fuel: "Fuel station", service: "Service shop", dealer: "Truck dealer", recruitment: "Recruitment agency", ferry: `Ferry port · ${pretty(p.id)}`, train: `Train terminal · ${pretty(p.id)}` }[kind] || kind));
    };

    const draw = () => {
      if (!data) return;
      const p = store.get("profile");
      const learned = data.cities.map((c) => ({ ...c, cat: cities.byId.get(c.id) })).filter((c) => c.cat);
      fitProjection(mapData
        ? [...mapData.pois.cities.values()].filter((c) => cities.byId.has(c.id)).map((c) => ({ lat: cities.byId.get(c.id).lat, lon: cities.byId.get(c.id).lon, x: c.x, z: c.z }))
        : learned.map((c) => ({ lat: c.cat.lat, lon: c.cat.lon, x: c.x, z: c.z })));
      cityPos = cityPositions(data.cities, cities.list, mapData?.pois.cities);
      if (mapData) for (const c of cities.list) if (!cityPos.has(c.id)) { /* city not in this map (DLC not owned) */ }
      for (const [k, g] of Object.entries(groups)) if (!["navigation", "driven", "streets", "countries"].includes(k)) g.clearLayers();
      Object.keys(counts).forEach((k) => (counts[k] = 0));
      const bump = (k, n = 1) => (counts[k] = (counts[k] || 0) + n);

      // Cities
      const visited = new Set(p?.visitedCities || []);
      for (const c of cityPos.values()) {
        if (c.estimated && !settings.map.showEstimatedCities) continue;
        const m = cityMarker(c.x, c.z, { name: `${c.name}${c.estimated ? " · estimated position" : ""}${visited.has(c.id) ? " · visited" : ""}`, estimated: c.estimated });
        m.on("click", () => showCity(c));
        m.addTo(groups.cities); bump("cities");
        labelMarker(c.x, c.z, c.name).addTo(groups.cities);
      }
      // POIs from the game map
      if (mapData) {
        for (const [kind, list] of mapData.pois.byKind) {
          const layer = POI_LAYER[kind];
          if (!layer) continue;
          for (const poi of list) poiMarker(poi, kind).addTo(groups[layer]);
          bump(layer, list.length);
        }
      } else {
        const place = (ids, layer, kind) => {
          for (const id of ids || []) {
            const c = cityPos.get(id);
            if (!c) continue;
            cityMarker(c.x + 250, c.z + 250, { name: `${kind === "dealer" ? "Truck dealer" : "Recruitment agency"} · ${c.name}`, kind: kind === "dealer" ? "dealer" : "recruit", estimated: c.estimated }).addTo(groups[layer]);
            bump(layer);
          }
        };
        place(p?.unlockedDealers, "dealers", "dealer");
        place(p?.unlockedRecruitments, "recruitment", "recruit");
      }
      // Garages
      for (const g of p?.garages || []) {
        const exact = mapData?.pois.byKind.get("garage")?.find((x) => x.id === g.cityId);
        const c = exact ? { x: exact.x, z: exact.z, estimated: false } : cityPos.get(g.cityId);
        if (!c) continue;
        const m = cityMarker(c.x, c.z, { name: `${g.city} ${g.isHq ? "headquarters" : "garage"}`, kind: g.isHq ? "hq" : "garage", estimated: c.estimated });
        m.on("click", () => showGarage(g, c));
        m.addTo(groups.garages); bump("garages");
      }
      // Fleet trucks
      for (const t of p?.trucks || []) {
        if (t.isPlayerTruck) continue;
        const at = t.position || cityPos.get(p.garages.find((g) => g.id === t.garageId)?.cityId);
        if (!at) continue;
        window.L.circleMarker(toLatLng(at.x - 300, at.z + 300), { radius: 5, color: "#0b0c0e", weight: 2, fillColor: getComputedStyle(document.documentElement).getPropertyValue("--text").trim(), fillOpacity: 1 })
          .bindTooltip(esc(`${t.name} · ${t.driverName || "no driver"}${t.position ? "" : " · at garage"}`)).addTo(groups.fleet);
        bump("fleet");
      }
      // AI drivers
      for (const d of p?.drivers || []) {
        if (d.isPlayer) continue;
        const c = cityPos.get(d.currentCity) || cityPos.get(p.garages.find((g) => g.id === d.garageId)?.cityId);
        if (!c) continue;
        window.L.marker(toLatLng(c.x + 400, c.z - 200), { icon: window.L.divIcon({ className: "", html: `<div class="driver-marker"></div>`, iconSize: [14, 14], iconAnchor: [7, 7] }) })
          .bindTooltip(esc(`${d.name} · ${d.job ? `${d.job.cargo} → ${d.job.targetCity}` : d.status}`))
          .on("click", () => (location.hash = `#/drivers/${encodeURIComponent(d.id)}`))
          .addTo(groups.aiDrivers);
        bump("aiDrivers");
      }
      // Recorded routes
      for (const r of data.routes) {
        if (r.current) continue;
        const line = routeLine(r.points, { highlighted: selectedRoute === r.id, dashed: r.kind === "reconstructed", colorVar: r.kind === "reconstructed" ? "--chart-2" : "--map-explored" });
        line.on("click", () => { selectedRoute = r.id; draw(); showRoute(r); });
        line.addTo(groups.previousRoutes);
        bump("previousRoutes");
      }
      const cur = data.routes.find((r) => r.current);
      recordedCurrent?.remove();
      recordedCurrent = cur?.points?.length
        ? window.L.polyline(segments(cur.points), { color: exploredColor(), weight: 2.5, opacity: 0.9, dashArray: "2 6", interactive: false }).addTo(groups.driven)
        : null;
      if (cur?.points?.length) trail.setLatLngs([]);
      // Events
      for (const e of data.events || []) {
        window.L.circleMarker(toLatLng(e.x, e.z), { radius: 4, color: getComputedStyle(document.documentElement).getPropertyValue("--crit").trim(), weight: 1.5, fillOpacity: 0.25 })
          .bindTooltip(esc(`${e.type} · ${e.detail || ""} ${e.amount ? f.money(e.amount) : ""} · ${f.dateTime(e.at)}`)).addTo(groups.events);
        bump("events");
      }
      counts.truck = store.get("telemetry")?.snapshot ? 1 : 0;
      counts.navigation = store.get("route")?.points?.length ? 1 : 0;
      counts.streets = streets?.lines || 0;
      setCrumb(mapData ? `ETS2 road map · ${f.num(counts.streets)} street segments · ${cityPos.size} cities` : `${data.cities.length} learned cities · schematic map (build the road map for streets)`);
      renderPanel();
      syncLayers();
    };

    const renderPanel = () => {
      const totalKm = (data?.routes || []).reduce((a, r) => a + (r.distanceKm || 0), 0);
      const legend = [
        ["var(--accent)", "Job / navigation route", ""],
        ["var(--map-explored)", "Streets you have driven", exploredCount ? f.num(exploredCount) : ""],
        ["var(--road-motorway)", "Other streets", ""],
        ["var(--map-city)", "City names", ""],
        ["var(--map-country)", "Country names", ""],
      ];
      $("#mapPanel", root).innerHTML = html`
        ${mapReady() ? html`<section><div class="label" style="margin-bottom:10px">Legend</div>
          ${legend.map(([color, label, cnt]) => html`<div class="layer-row legend-row"><span class="sw sw--line" style="background:${color}"></span>${label}<span class="cnt">${cnt}</span></div>`)}
        </section>` : ""}
        <section><div class="label" style="margin-bottom:10px">Layers</div>
          ${LAYERS.map(([k, label, color]) => html`<label class="layer-row"><span class="sw" style="background:${color}"></span>${label}<span class="cnt">${counts[k] ? f.num(counts[k]) : ""}</span>${toggle(k, layersOn[k])}</label>`)}
          <p class="faint" style="font-size:11px;margin-top:6px">Points of interest appear when zoomed in.</p>
        </section>
        <section>
          <div class="label" style="margin-bottom:10px">Route history</div>
          <select class="select" id="historyDays">${[[7, "Last 7 days"], [30, "Last 30 days"], [90, "Last 90 days"], [365, "Last year"]].map(([v, l]) => html`<option value="${v}" ${v === historyDays ? "selected" : ""}>${l}</option>`)}</select>
          <div class="row" style="justify-content:space-between;margin-top:10px;font-size:12px"><span class="muted">Distance recorded</span><span class="num">${f.dist(totalKm)}</span></div>
        </section>`.toString();
      $("#mapPanel", root).querySelectorAll("input[type=checkbox]").forEach((cb) => cb.addEventListener("change", () => {
        layersOn[cb.name] = cb.checked;
        syncLayers();
        saveSettings((s) => (s.map.layers[cb.name] = cb.checked)).catch(() => {});
      }));
      $("#historyDays", root).onchange = async (e) => {
        historyDays = +e.target.value;
        await saveSettings((s) => (s.map.routeHistoryDays = historyDays));
        load();
      };
    };

    const showRoute = (r) => showInfo(html`${infoHead(r.kind === "reconstructed" ? "Delivery route (reconstructed)" : "Recorded route")}
      ${bigName(html`${r.originCity || "Free roam"} ${r.destCity ? html`<span class="accent">→</span> ${r.destCity}` : ""}`)}
      <div class="muted" style="margin:4px 0 12px">${r.cargo || (r.kind === "freeroam" ? "Free roam" : "—")} · ${r.startedUtc ? f.dateTime(r.startedUtc) : f.gameDay(r.gameEndMin)}</div>
      ${r.kind === "reconstructed" ? html`<p class="faint" style="font-size:11px;margin:-6px 0 10px">No GPS trace was recorded for this delivery; the line shows the likely road route.</p>` : ""}
      <div class="row" style="gap:24px"><div class="stat"><div class="stat__value" style="font-size:17px">${f.dist(r.distanceKm || 0)}</div><div class="stat__label">Distance</div></div>
      ${r.income ? html`<div class="stat"><div class="stat__value" style="font-size:17px">${f.money(r.income)}</div><div class="stat__label">Income</div></div>` : ""}
      ${r.deliveryId ? html`<a class="btn btn--sm" style="margin-left:auto" href="#/logbook/${r.deliveryId}">Open delivery</a>` : ""}</div>`);

    const showCity = (c) => {
      const p = store.get("profile");
      const g = p?.garages?.find((x) => x.cityId === c.id);
      showInfo(html`${infoHead("City")}${bigName(c.name)}
        <div class="muted" style="margin-bottom:10px">${countryName(c.country) || ""}</div>
        <div class="chips">${c.estimated ? html`<span class="badge badge--outline">Estimated position</span>` : c.fromMap ? html`<span class="badge badge--ok">From game map</span>` : html`<span class="badge badge--ok">Exact · ${c.samples} samples</span>`}
          ${p?.visitedCities?.includes(c.id) ? html`<span class="badge">Visited</span>` : ""}${g ? html`<span class="badge badge--accent">Your garage</span>` : ""}
          ${p?.unlockedDealers?.includes(c.id) ? html`<span class="badge badge--info">Dealer</span>` : ""}${p?.unlockedRecruitments?.includes(c.id) ? html`<span class="badge badge--ok">Recruitment</span>` : ""}</div>
        <div class="row" style="margin-top:12px">${mapReady() ? html`<button class="btn btn--sm btn--primary" id="navCity">${icon("navigation")}Navigate here</button>` : ""}<a class="btn btn--sm" href="#/logbook/city/${encodeURIComponent(c.name)}">${icon("book-open")}Deliveries</a></div>`);
      $("#navCity", root)?.addEventListener("click", () => navigateToCity(c.id));
    };

    const showGarage = (g, c) => {
      const p = store.get("profile");
      const drivers = p.drivers.filter((d) => d.garageId === g.id);
      showInfo(html`${infoHead(g.isHq ? "Headquarters" : "Garage")}${bigName(g.city)}
        <div class="muted" style="margin-bottom:12px">${g.size} · ${g.trucksAssigned}/${g.slots} trucks · ${g.driversAssigned}/${g.slots} drivers</div>
        ${drivers.map((d) => html`<div class="row" style="justify-content:space-between;padding:4px 0;font-size:12px"><span>${d.name}</span>${driverStatus(d.status)}</div>`)}
        <div class="row" style="margin-top:12px">${mapReady() ? html`<button class="btn btn--sm btn--primary" id="navGarage">${icon("navigation")}Navigate here</button>` : ""}<a class="btn btn--sm" href="#/garages/${encodeURIComponent(g.id)}">Open garage</a></div>`);
      $("#navGarage", root)?.addEventListener("click", () => navigateTo("route.setPoint", { x: c.x, z: c.z, name: `${g.city} garage` }, `${g.city} garage`));
    };

    const loadStreets = async () => {
      if (!mapReady()) { renderNav(); return; }
      try {
        mapData = await loadMapData();
        if (mapData && !streets) {
          streets = mapData.streets;
          await updateExplored(false);
          streetsTiles = streetsLayer(streets).addTo(groups.streets);
          const cj = await loadCountries(store.get("mapStatus")?.countriesUrl);
          if (cj) countriesLayer(cj, (code) => countryName(code)).addTo(groups.countries);
          landLayer(map, cj, store.get("mapStatus")?.countriesUrl)?.addTo(groups.streets);
        }
      } catch (e) {
        toast({ kind: "error", title: "Street map could not be loaded", message: e.message });
      }
      renderNav();
      draw();
    };

    // Streets already driven (all recorded traces, not only the history window) get their own colour.
    let streetsTiles = null;
    let exploredCount = 0;
    const updateExplored = async (redraw = true) => {
      if (!streets) return;
      const driven = await call("map.driven").catch(() => null);
      if (!driven) return;
      const r = markExplored(streets, driven);
      streets.explored = r.explored;
      exploredCount = r.count;
      if (redraw) streetsTiles?.redraw();
    };

    const load = async () => {
      try {
        data = await call("map.get");
        if (streetsTiles) await updateExplored();
        draw();
      } catch (e) {
        toast({ kind: "error", title: "Map data unavailable", message: e.message });
      }
    };
    await load();
    await loadStreets();
    drawRoute();
    renderTrip();

    // Initial view: truck, or everything we know
    const s0 = store.get("telemetry")?.snapshot;
    if (s0 && (s0.x || s0.z)) map.setView(toLatLng(s0.x, s0.z), settings.map.defaultZoom);
    else fitAll();

    function fitAll() {
      const b = window.L.latLngBounds([]);
      for (const k of ["navigation", "previousRoutes", "garages"]) groups[k].eachLayer((l) => (l.getBounds ? (l.getBounds().isValid() && b.extend(l.getBounds())) : l.getLatLng && b.extend(l.getLatLng())));
      if (b.isValid()) map.fitBounds(b, { padding: [60, 60], maxZoom: -3, animate: false });
    }

    root.querySelector(".map-toolbar").addEventListener("click", (e) => {
      const a = e.target.closest("[data-act]")?.dataset.act;
      if (a === "follow") setFollow(!follow);
      if (a === "layers") root.querySelector(".map-page").classList.toggle("layers-open");
      if (a === "fit") { setFollow(false); fitAll(); }
      if (a === "route") {
        const r = store.get("route");
        if (r?.points?.length) { setFollow(false); map.fitBounds(window.L.latLngBounds(remainingLatLngs(r.points, routeHint)), { padding: [80, 80], animate: false }); }
        else toast({ kind: "info", title: "No active route", message: "Take a job or pick a destination." });
      }
    });

    // Trip strip: destination, remaining distance, real-time ETA and arrival while a route is active.
    const renderTrip = () => {
      const el = $("#mapTrip", root);
      const r = store.get("route");
      const eta = store.get("telemetry")?.snapshot?.eta;
      if (!el) return;
      if (!r?.points?.length || r.error) { el.classList.add("hidden"); return; }
      el.classList.remove("hidden");
      el.innerHTML = html`<div class="map-trip__dest">${icon("flag")}${r.destination.name}</div>
        <div class="map-trip__stat"><b>${f.dist(eta ? eta.remainingKm : remainingKm(r.points, routeHint))}</b><small>${t("Remaining")}</small></div>
        ${eta ? html`<div class="map-trip__stat is-accent"><b>${f.duration(Math.max(60, eta.realSeconds), { short: true })}</b><small>${t("Real-time ETA")}</small></div>
          <div class="map-trip__stat"><b>${f.time(eta.arrivalUtc)}</b><small>${t("Arrival")}</small></div>` : ""}`.toString();
    };

    let lastNavRender = 0;
    const update = () => {
      const s = store.get("telemetry")?.snapshot;
      if (!s || (!s.x && !s.z)) return;
      const ll = toLatLng(s.x, s.z);
      if (!truck) {
        truck = truckMarker(s.x, s.z, s.headingDeg).addTo(groups.truck);
        truck.on("click", () => showInfo(html`${infoHead("Your truck")}${bigName(`${s.truckBrand} ${s.truckName}`)}
          <div class="muted">${s.onJob ? `${s.cargo} · ${s.sourceCity} → ${s.destinationCity}` : "Free roam"}</div>`));
      }
      const pts = trail.getLatLngs();
      const last = pts[pts.length - 1];
      if (!last || Math.hypot(last.lat - ll[0], last.lng - ll[1]) > 60) trail.addLatLng(ll);
      truck.setLatLng(ll);
      truck.setHeading(s.headingDeg);
      if (follow && isLive()) map.panTo(ll, { animate: false });
      const r = store.get("route");
      if (r?.points?.length) {
        routeHint = progressIndex(r.points, s.x, s.z, routeHint);
        nav.update(remainingLatLngs(r.points, routeHint, { x: s.x, z: s.z }));
      }
      if (Date.now() - lastNavRender > 2000 && document.activeElement?.id !== "navSearch") { lastNavRender = Date.now(); renderNav(); renderTrip(); }
    };
    update();
    const offs = [
      store.on("telemetry", update),
      store.on("profile", draw),
      store.on("route", () => { routeHint = 0; drawRoute(); renderTrip(); }),
      store.on("mapStatus", (m) => { if (m.state === "ready" && !streets) loadStreets(); else if (document.activeElement?.id !== "navSearch") renderNav(); }),
    ];
    const refresh = setInterval(() => { if (isLive()) load(); }, 45000);
    return () => { offs.forEach((o) => o()); clearInterval(refresh); map.remove(); };
  },
};
