import { html, raw, $, cx } from "../core/html.js";
import { icon } from "../core/icons.js";
import { store } from "../core/store.js";
import { toggle, select, segmented, toast, confirm, skeleton, modal } from "../components/ui.js";
import { on } from "../core/bridge.js";
import { saveSettings, changeLanguage, showUpdate } from "../app.js";
import { t } from "../core/i18n.js";
import { currencyList } from "../core/format.js";
import * as f from "../core/format.js";

// Three categories: HAULIX itself, ETS2 and (later) ATS. Each lists its own sections in the side navigation.
const CATEGORIES = [
  ["haulix", "HAULIX", "sliders-horizontal", "App, look, data and about"],
  ["ets2", "Euro Truck Simulator 2", "truck", "Game, telemetry, notifications, HUD, TruckersMP"],
  ["ats", "American Truck Simulator", "flag", "Coming later"],
];
const SECTIONS = [
  ["general", "General", "sliders-horizontal", "haulix"], ["appearance", "Appearance", "palette", "haulix"], ["online", "Online", "globe", "haulix"],
  ["data", "Data", "database", "haulix"], ["about", "About", "info", "haulix"],
  ["ets2", "Game & profile", "truck", "ets2"], ["telemetry", "Telemetry", "activity", "ets2"], ["notifications", "Notifications & sounds", "bell", "ets2"],
  ["ats", "American Truck Simulator", "flag", "ats"],
];
const CAT_OF = Object.fromEntries(SECTIONS.map(([id, , , c]) => [id, c]));
const ACCENTS = [["amber", "#ffb020", "Electric amber"], ["copper", "#e07a3f", "Copper"], ["ice", "#7cc4ff", "Ice blue"], ["signal", "#e8e9eb", "Signal white"]];
const AFK_DEFAULT = "AFK - back soon! Logging my trips with HAULIX, free ETS2 tracker: www.haulix-logging.com";
// [key, label, preview label, preview value, preview tone]
const HUD_FIELDS = [["remaining", "Remaining distance", "Remaining", "214 km"], ["etaReal", "Real-time ETA", "Real-time ETA", "9 min", "is-accent"], ["arrival", "Arrival time", "Arrival", "14:42"],
  ["etaGame", "Game ETA", "Game ETA", "2 h 50 min"], ["deadline", "Deadline buffer", "Deadline buffer", "1 h 35 min", "is-ok"], ["income", "Income", "Income", "14,820 €", "is-ok"],
  ["company", "Destination company", "Destination", "Posped"], ["speed", "Speed", "Speed", "78 km/h · limit 80"], ["speedLimit", "Speed limit", "Speed limit", "80 km/h"],
  ["cruise", "Cruise control", "Cruise control", "80 km/h", "is-accent"], ["gear", "Gear", "Gear", "11"], ["fuel", "Fuel", "Fuel", "412 l · 51.5 %"], ["fuelRange", "Fuel range", "Fuel range", "640 km"],
  ["rest", "Next rest", "Next rest", "5 h 10 min"], ["damage", "Cargo damage", "Cargo damage", "1.2 %"], ["truckDamage", "Truck wear", "Truck wear", "5 %"],
  ["trailerDamage", "Trailer wear", "Trailer wear", "2 %"], ["gameTime", "Game time", "Game time", "14:35"], ["clock", "Time of day (PC)", "Time", "21:07"]];
const HUD_DEFAULT = ["remaining", "etaReal", "arrival", "deadline", "speed", "fuelRange"];
const HUD_POSITIONS = [["topRight", "Top right"], ["topCenter", "Top centre"], ["topLeft", "Top left"], ["middleRight", "Middle right"], ["middleLeft", "Middle left"],
  ["bottomRight", "Bottom right"], ["bottomCenter", "Bottom centre"], ["bottomLeft", "Bottom left"], ["custom", "Custom (dragged)"]];
const HUD_POS_OK = (p) => (HUD_POSITIONS.some(([k]) => k === p) ? p : null);
const HUD_ACCENTS = [["app", "", "Same as HAULIX"], ["amber", "#ffb020", "Amber"], ["copper", "#e07a3f", "Copper"], ["green", "#3dd68c", "Green"], ["blue", "#7cc4ff", "Blue"],
  ["red", "#f0474f", "Red"], ["purple", "#a78bfa", "Purple"], ["white", "#e8e9eb", "White"]];
const hudScale = (h) => (h?.scale >= 50 ? h.scale : { small: 82, large: 125 }[h?.size] || 100);
const slider = (name, min, max, step, value, unit) => html`<div class="range-row"><input type="range" name="${name}" min="${min}" max="${max}" step="${step}" value="${value}" data-unit="${unit}"><span class="num" data-range="${name}">${value}${unit}</span></div>`;
const FIELDS = [["vehicle", "Vehicle"], ["drivetrain", "Driver inputs"], ["fluids", "Fluids & gauges"], ["damage", "Damage"], ["navigation", "Location & navigation"], ["job", "Current job"], ["trailer", "Trailer"], ["lights", "Controls & lights"]];

const row = (title, desc, control, cls = "") => html`<div class="${cx("setting", cls)}"><div><div class="setting__title">${title}</div>${desc ? html`<div class="setting__desc">${desc}</div>` : ""}</div><div class="setting__control">${control}</div></div>`;
const section = (id, title, body) => html`<section class="card" id="sec-${id}" data-cat="${CAT_OF[id]}"><header class="card__head"><h2 class="label">${title}</h2></header><div class="card__body" style="padding-top:4px;padding-bottom:4px">${body}</div></section>`;

export default {
  title: "Settings",
  crumb: () => "Stored locally in your HAULIX database",

  render() {
    const s = store.get("settings");
    const det = store.get("detection");
    const st = store.get("status");
    return html`<div class="settings-cats" role="tablist">${CATEGORIES.map(([id, l, i, sub]) => html`<button class="settings-cat" role="tab" data-cat-tab="${id}">${icon(i)}<span><b>${l}</b><small>${sub}</small></span>${id === "ats" ? html`<em class="nav__soon">Soon</em>` : ""}</button>`)}</div>
    <div class="settings">
      <nav class="settings__nav">${SECTIONS.map(([id, l, i, c]) => html`<a href="#/settings/${id}" data-sec="${id}" data-cat="${c}">${icon(i)}${l}</a>`)}</nav>
      <div>
        ${section("general", "General", html`
          ${row("Language", "Uses your Windows display language unless you choose one.", select("general.language", [["auto", "Automatic (Windows)"], ["en", "English"], ["de", "Deutsch"]], s.general.languageChosen ? s.general.language : "auto"))}
          ${row("Units", "Distances, speeds, volumes and temperatures.", segmented("general.units", [["metric", "Metric"], ["imperial", "Imperial"]], s.general.units))}
          ${row("Currency", "ETS2 pays in euro; other currencies use fixed approximate rates.", select("general.currency", currencyList.map((c) => [c, c]), s.general.currency))}
          ${row("Theme", "", segmented("general.theme", [["dark", "Dark"], ["midnight", "Midnight"], ["light", "Light"]], s.general.theme))}
          ${row("Start page", "What HAULIX opens on launch.", select("general.startPage", [["dashboard", "Dashboard"], ["last", "Last visited page"]], s.general.startPage))}
          ${row("Launch with Windows", "Start HAULIX minimised when you sign in, so every drive is logged.", toggle("general.launchWithWindows", s.general.launchWithWindows))}
          ${row("Start minimised", "", toggle("general.startMinimized", s.general.startMinimized))}
          ${row("Keep running in the tray", "Closing the window keeps HAULIX logging in the background.", toggle("general.minimizeToTray", s.general.minimizeToTray))}
          ${row(html`Discord status <span class="badge badge--outline" style="margin-left:6px">${icon("clock", "icon icon-sm")}Coming later</span>`, "Shows your current drive on your Discord profile (Rich Presence), e.g. cargo, route and arrival. Not available in this HAULIX version yet.", html`<span class="locked" data-tip="Coming later">${icon("shield")}${toggle("general.discordPresence", false, { disabled: true })}</span>`, "setting--locked")}
          ${row("Discord application ID", html`Create an application named "HAULIX" in the <a class="link" href="#" data-url="https://discord.com/developers/applications">Discord Developer Portal</a> and paste its Application ID here. Optional: upload images named "haulix" and "truck" under Rich Presence → Art Assets.`, html`<input class="input" name="general.discordAppId" inputmode="numeric" placeholder="e.g. 1234567890123456789" style="width:230px" value="${s.general.discordAppId || ""}" disabled>`, "setting--locked")}
        `)}

        ${section("ets2", "Game & profile", html`
          <div class="callout ${det?.gamePath ? "" : "callout--warn"}" style="margin:14px 0 6px">${icon(det?.gamePath ? "circle-check" : "triangle-alert")}
            <div>${det?.gamePath ? html`<strong>ETS2 detected.</strong> ${det.pluginInstalled ? "Telemetry plugin installed." : "Telemetry plugin missing, so live data is unavailable."} Save format: ${det.saveFormat || "unknown"}.` : html`<strong>ETS2 not found automatically.</strong> Set the installation folder below.`}</div></div>
          ${row("Automatic detection", "Find the game through Steam and your Documents folder.", toggle("ets2.autoDetect", s.ets2.autoDetect))}
          ${row("Installation folder", html`<span class="path-value">${s.ets2.gamePath || det?.gamePath || "Not found"}</span>`, html`<button class="btn btn--sm" data-pick="ets2.gamePath">${icon("folder-open")}Browse</button>`)}
          ${row("Documents folder", html`<span class="path-value">${s.ets2.documentsPath || det?.documentsPath || "Not found"}</span>`, html`<button class="btn btn--sm" data-pick="ets2.documentsPath">${icon("folder-open")}Browse</button>`)}
          ${row("Profile", html`<span class="path-value">${s.ets2.profilePath || "None selected"}</span>`, select("ets2.profilePath", [["", "Select profile…"], ...(det?.profiles || []).map((p) => [p.path, `${p.name}${p.companyName ? ` · ${p.companyName}` : ""} (${p.kind})`])], s.ets2.profilePath))}
          ${row("Save to read", "“Latest” follows autosaves automatically.", html`<select class="select" name="ets2.saveSelection" id="saveSel"><option value="latest">Latest save</option></select>`)}
          ${row("Watch for new saves", "Re-read the profile whenever ETS2 writes a save.", toggle("ets2.watchSaves", s.ets2.watchSaves))}
          ${row("Import in-game delivery history", "Add jobs from the save's delivery log to the logbook.", toggle("ets2.importSaveHistory", s.ets2.importSaveHistory))}
          ${row("Re-run detection", "", html`<button class="btn btn--sm" id="redetect">${icon("refresh-cw")}Detect again</button><a class="btn btn--sm btn--ghost" href="#/setup">Setup wizard</a>`)}
        `)}

        ${section("telemetry", "Telemetry", html`
          ${row("Status", html`${st?.pluginInstalled ? "scs-telemetry.dll installed" : "Plugin not installed"}${st?.pluginRevision ? ` · revision ${st.pluginRevision}` : ""}`, html`<span class="badge ${st?.telemetry === "live" ? "badge--ok" : ""}">${st?.telemetry || "—"}</span>`)}
          ${row("Update frequency", "How often HAULIX reads telemetry. The UI refreshes at up to 10 Hz.", segmented("telemetry.updateHz", [["5", "5 Hz"], ["10", "10 Hz"], ["20", "20 Hz"]], String(s.telemetry.updateHz)))}
          ${row("Record routes", "Store GPS breadcrumbs for the speed profile in delivery details.", toggle("telemetry.recordRoutes", s.telemetry.recordRoutes))}
          ${row("Record free roam", "Also record routes driven without a job.", toggle("telemetry.recordFreeRoam", s.telemetry.recordFreeRoam))}
          ${row("Route point spacing", "Distance between stored points. Lower gives more detail but a larger database.", select("telemetry.routePointSpacingM", [[50, "50 m"], [100, "100 m"], [150, "150 m"], [300, "300 m"], [600, "600 m"]], s.telemetry.routePointSpacingM))}
          ${row("Telemetry panels", "Choose which panels the Telemetry page shows.", html`<div class="chips" style="justify-content:flex-end">${FIELDS.map(([k, l]) => html`<label class="pill" style="cursor:pointer"><input type="checkbox" name="telemetry.fields.${k}" ${raw(s.telemetry.fields[k] !== false ? "checked" : "")} style="accent-color:var(--accent)">${l}</label>`)}</div>`, "setting--stack")}
          ${row("Demo mode", "Simulate a drive to explore HAULIX without the game. Demo data is never saved permanently.", html`<button class="btn btn--sm" id="demoToggle">${icon(st?.demo ? "pause" : "play")}${st?.demo ? "Stop demo" : "Start demo"}</button>`)}
        `)}

        ${section("notifications", "Notifications", html`
          ${row("Job notifications", "Job accepted, delivered or cancelled, and fines.", toggle("notifications.enabled", s.notifications?.enabled !== false))}
          ${row("Progress", "Milestones (100, 50, 10 and 2 km left) and halfway, with the real-time arrival.", toggle("notifications.progress", s.notifications?.progress !== false))}
          ${row("Warnings", "Deadline at risk, fuel range too short, new cargo damage and rest needed.", toggle("notifications.warnings", s.notifications?.warnings !== false))}
          ${row("Show over the game", "Small HAULIX cards on top of ETS2 while HAULIX runs in the background. Needs ETS2 in windowed or borderless fullscreen mode; exclusive fullscreen hides every overlay.", toggle("notifications.overlay", s.notifications?.overlay !== false))}
          ${row("ETS2 display mode", html`<span id="displayMode">Checking…</span>`, html`<button class="btn btn--sm hidden" id="setBorderless">${icon("monitor")}Switch to borderless</button>`)}
          <div class="setting-group"><div class="setting-group__title">${icon("bell")}Sounds</div>
            ${row("Play sounds", "A short chime with notifications, so you notice them while driving.", toggle("notifications.sounds", s.notifications?.sounds !== false))}
            ${row("Sound style", "", segmented("notifications.soundStyle", [["soft", "Soft"], ["digital", "Digital"]], s.notifications?.soundStyle || "soft"))}
            ${row("Volume", "", slider("notifications.soundVolume", 0, 100, 5, s.notifications?.soundVolume ?? 70, " %"))}
            ${row("Job updates", "Job accepted, delivered, cancelled and fines.", html`<div class="row" style="gap:8px">${toggle("notifications.soundJob", s.notifications?.soundJob !== false)}<button class="btn btn--sm btn--ghost btn--icon" data-sound="job" data-tip="Play">${icon("play")}</button></div>`)}
            ${row("Milestones & achievements", "Distance milestones, route changes and new achievements.", html`<div class="row" style="gap:8px">${toggle("notifications.soundProgress", s.notifications?.soundProgress !== false)}<button class="btn btn--sm btn--ghost btn--icon" data-sound="success" data-tip="Play">${icon("play")}</button></div>`)}
            ${row("Warnings", "Deadline, fuel, damage and rest.", html`<div class="row" style="gap:8px">${toggle("notifications.soundWarnings", s.notifications?.soundWarnings !== false)}<button class="btn btn--sm btn--ghost btn--icon" data-sound="warning" data-tip="Play">${icon("play")}</button></div>`)}
            ${row("AFK warning alarm", "A louder alarm with the TruckersMP inactivity warning – plays even while HAULIX is open.", html`<div class="row" style="gap:8px">${toggle("notifications.soundAfk", s.notifications?.soundAfk !== false)}<button class="btn btn--sm btn--ghost btn--icon" data-sound="afk" data-tip="Play">${icon("play")}</button></div>`)}
          </div>
          <div class="setting-group"><div class="setting-group__title">${icon("bell")}Voice</div>
            ${row("Read aloud", "Speaks notifications – audible even in exclusive fullscreen and on a single monitor.", toggle("notifications.voice", !!s.notifications?.voice))}
            ${row("Voice engine", "Natural AI voices sound human and run offline on your PC (download once). Windows voices need no download.", segmented("notifications.voiceEngine", [["natural", "Natural AI voice"], ["windows", "Windows voice"]], s.notifications?.voiceEngine || "natural"))}
            <div id="voiceNatural" class="${(s.notifications?.voiceEngine || "natural") === "natural" ? "" : "hidden"}"><div class="voice-list" id="voiceList">${skeleton(1)}</div>
              <p class="faint" style="font-size:11px;margin:6px 0 10px">Voices from the open-source Piper project, stored in %LOCALAPPDATA%\Haulix\voices – not in the program folder.</p></div>
            <div id="voiceWindows" class="${s.notifications?.voiceEngine === "windows" ? "" : "hidden"}">${row("Windows voice", "", html`<select class="select" name="notifications.windowsVoice" id="winVoice" style="width:240px"><option value="">Automatic (UI language)</option></select>`)}</div>
            ${row("Speed", "", html`<div class="range-row"><input type="range" name="notifications.voiceRate" min="0.7" max="1.4" step="0.05" value="${s.notifications?.voiceRate ?? 1}"><span class="num" data-range-x="notifications.voiceRate">${(s.notifications?.voiceRate ?? 1).toFixed(2)}×</span></div>`)}
            ${row("Volume", "", html`<div class="range-row"><input type="range" name="notifications.voiceVolume" min="0" max="100" step="5" value="${s.notifications?.voiceVolume ?? 90}"><span class="num" data-range="notifications.voiceVolume">${s.notifications?.voiceVolume ?? 90} %</span></div>`)}
            ${row("Test", "Plays a sample announcement with the chosen voice.", html`<button class="btn btn--sm" id="voiceTest">${icon("play")}Play sample</button>`)}
          </div>
          ${row("TruckersMP inactivity warning", "Warns after 8 and 27 minutes without input on TruckersMP, before the server's automatic AFK kick (10 min on full servers, otherwise 30 min). HAULIX never sends input to the game – avoiding the kick automatically is against the TruckersMP rules.", toggle("notifications.afkWarning", s.notifications?.afkWarning !== false))}
          ${row("Position", "Screen corner of the monitor ETS2 runs on.", select("notifications.position", [["topRight", "Top right"], ["topLeft", "Top left"], ["bottomRight", "Bottom right"], ["bottomLeft", "Bottom left"]], s.notifications?.position || "topRight"))}
          ${row("Test", "Shows a sample notification over all windows.", html`<button class="btn btn--sm" id="notifyTest">${icon("bell")}Send test</button>`)}
        `)}

        ${section("hud", "In-game HUD", html`
          <div class="hud-preview-wrap" id="hudScene"><div class="hudp-card" id="hudCardPrev" data-tip="Drag to place"></div><span class="hud-preview-hint">${t("Drag the card to place it")}</span></div>
          ${row("Show the HUD", "A job card over ETS2 while you drive, like VTC trackers. Needs borderless fullscreen or window mode (see Notifications → ETS2 display mode).", toggle("general.hud", !!s.general.hud))}
          ${row("Only during a job", "Hide the HUD in free roam.", toggle("hud.onlyOnJob", !!s.hud?.onlyOnJob))}
          <div class="setting-group"><div class="setting-group__title">${icon("move")}Place</div>
            ${row("Place on screen", "Makes the card draggable over the game: drag it anywhere, double-click to save, right-click to cancel.", html`<div class="row" style="gap:8px"><button class="btn btn--sm btn--primary" id="hudPlace">${icon("move")}Place on screen</button><button class="btn btn--sm" id="hudTest">${icon("monitor")}Show for 10 s</button></div>`)}
            ${row("Position", "Or pick a fixed spot. Dragging switches to “Custom”.", select("hud.position", HUD_POSITIONS, HUD_POS_OK(s.hud?.position) || "topRight"))}
            ${row("Distance from the edge", "", slider("hud.margin", 0, 120, 2, s.hud?.margin ?? 16, " px"))}
          </div>
          <div class="setting-group"><div class="setting-group__title">${icon("palette")}Look</div>
            ${row("Style", "", segmented("hud.theme", [["dark", "Dark"], ["glass", "Glass"], ["light", "Light"], ["contrast", "High contrast"]], s.hud?.theme || "dark"))}
            ${row("Accent colour", "", html`<div class="hud-swatches" data-hud-accent>${HUD_ACCENTS.map(([k, c, l]) => html`<button type="button" data-value="${k}" style="--sw:${c}" data-tip="${l}" class="${(s.hud?.accent || "app") === k ? "is-active" : ""}"></button>`)}</div>`)}
            ${row("Size", "", slider("hud.scale", 60, 180, 5, hudScale(s.hud), " %"))}
            ${row("Width", "", slider("hud.width", 220, 420, 10, s.hud?.width ?? 290, " px"))}
            ${row("Row spacing", "", segmented("hud.density", [["compact", "Compact"], ["normal", "Normal"], ["roomy", "Roomy"]], s.hud?.density || "normal"))}
            ${row("Visibility", "How opaque the card is over the game.", slider("hud.opacity", 20, 100, 5, s.hud?.opacity ?? 90, " %"))}
            ${row("Rounded corners", "", toggle("hud.rounded", s.hud?.rounded !== false))}
          </div>
          <div class="setting-group"><div class="setting-group__title">${icon("package")}Content</div>
            ${row("Header", "“Current job” line with the HAULIX mark.", toggle("hud.showHeader", s.hud?.showHeader !== false))}
            ${row("Cargo", "Cargo name and weight.", toggle("hud.showCargo", s.hud?.showCargo !== false))}
            ${row("Route", "Start → destination city.", toggle("hud.showRoute", s.hud?.showRoute !== false))}
            ${row("Progress bar", "", toggle("hud.showProgress", s.hud?.showProgress !== false))}
            ${row("Rows", "What the card lists, in this order.", html`<div class="chips hud-fields" style="justify-content:flex-end">${HUD_FIELDS.map(([k, l]) => html`<label class="pill" style="cursor:pointer"><input type="checkbox" data-hud-field="${k}" ${raw((s.hud?.fields || HUD_DEFAULT).includes(k) ? "checked" : "")} style="accent-color:var(--accent)">${l}</label>`)}</div>`, "setting--stack")}
            ${row("Reset", "Back to the standard HUD.", html`<button class="btn btn--sm btn--ghost" id="hudReset">${icon("rotate-ccw")}Reset HUD</button>`)}
          </div>
        `)}

        ${section("online", "Online", html`
          <div class="callout" style="margin:14px 0 6px">${icon("globe")}<div><strong>The HAULIX online service is being prepared.</strong> Accounts, VTCs, job board, events, leaderboards and cloud sync are built into HAULIX already, but there is no server yet – nothing is sent from this PC.</div></div>
          ${row(html`Status`, "", html`<span class="badge badge--outline">${icon("clock", "icon icon-sm")}Not available yet</span>`)}
          <div class="setting-group"><div class="setting-group__title">${icon("shield-check")}Terms & privacy</div>
            ${row("Terms for online features", "Signing in will require accepting Part B of the HAULIX License Agreement (accounts, VTCs, your content, conduct) and the Privacy Policy. Nothing is sent before that.", html`<span id="termsState" class="badge badge--outline">${icon("clock", "icon icon-sm")}Not accepted yet</span>`)}
            ${row("Read the documents", "", html`<div class="row" style="gap:8px;flex-wrap:wrap;justify-content:flex-end"><button class="btn btn--sm" data-legal="license">${icon("file-text")}License agreement</button><button class="btn btn--sm" data-legal="privacy">${icon("shield")}Privacy policy</button></div>`)}
            ${row("Privacy by default", "When the online service starts, every kind of sharing stays off until you turn it on.", "")}
            ${row("Show me on leaderboards", "Your name and results on public leaderboards.", html`<span class="locked" data-tip="Coming later">${icon("lock")}${toggle("online.showOnLeaderboards", false, { disabled: true })}</span>`, "setting--locked")}
            ${row("Share deliveries with my VTC", "Your deliveries count for your company's statistics.", html`<span class="locked" data-tip="Coming later">${icon("lock")}${toggle("online.shareWithVtc", false, { disabled: true })}</span>`, "setting--locked")}
            ${row("Cloud sync", "Back up your logbook to your HAULIX account.", html`<span class="locked" data-tip="Coming later">${icon("lock")}${toggle("online.cloudSync", false, { disabled: true })}</span>`, "setting--locked")}
          </div>
          ${row("Developer preview", "Shows the VTC and Online pages with local sample data (no network), to try the screens before the service starts.", toggle("online.sample", !!store.get("onlineSample")))}
        `)}

        ${section("truckersmp", "TruckersMP", html`
          <div class="callout callout--crit" style="margin:14px 0 6px">${icon("octagon-alert")}
            <div><strong>Against the TruckersMP rules – use at your own risk.</strong> The TruckersMP rules list "bypassing the server-sided auto kick system" as a violation that can lead to bans up to a permanent ban. An automatic anti-AFK message does exactly that. HAULIX is not responsible for bans. The official way to stay longer is the TruckersMP Patreon tier "Master Trucker".</div></div>
          ${row("Anti-AFK message", "While you are inactive on TruckersMP, HAULIX opens the chat, types your message and sends it once per interval. Only works while ETS2 is the active window.", toggle("truckersMp.antiAfk", !!s.truckersMp?.antiAfk))}
          ${row("Message", "Sent in the TruckersMP chat (max. 120 characters).", html`<div class="row" style="gap:8px"><input class="input" name="truckersMp.message" maxlength="120" style="width:260px" value="${s.truckersMp?.message || AFK_DEFAULT}"><button class="btn btn--sm btn--ghost" id="resetAfkMsg" data-tip="Back to the default message">${icon("rotate-ccw")}Reset</button></div>`)}
          ${row("Interval", "Minutes of inactivity between messages. On full servers TruckersMP kicks after 10 minutes.", select("truckersMp.intervalMinutes", [[4, "4 min"], [6, "6 min"], [8, "8 min"], [9, "9 min"], [15, "15 min"], [25, "25 min"]], s.truckersMp?.intervalMinutes || 8))}
          ${row("Chat key", "The key that opens the TruckersMP chat in your controls (default Y).", html`<input class="input" name="truckersMp.chatKey" maxlength="1" style="width:60px;text-align:center;text-transform:uppercase" value="${s.truckersMp?.chatKey || "Y"}">`)}
        `)}

        ${section("data", "Data", html`
          ${row("Database", html`<span class="path-value">${st?.db?.path || "—"}</span>`, html`<span class="badge ${st?.db?.ok ? "badge--ok" : "badge--crit"}">${st?.db?.ok ? "Healthy" : "Error"}</span><span class="num muted" style="font-size:12px">${f.bytes(st?.db?.sizeBytes)}</span><button class="btn btn--sm btn--ghost" data-open="${store.get("dataFolder")}">${icon("folder-open")}</button>`)}
          <div id="counts"></div>
          ${row("Automatic backups", "Rolling copies of your database.", toggle("data.autoBackup", s.data.autoBackup))}
          ${row("Backup frequency", "", select("data.backupIntervalHours", [[6, "Every 6 hours"], [24, "Daily"], [168, "Weekly"]], s.data.backupIntervalHours))}
          ${row("Keep", "", select("data.backupKeep", [[5, "5 backups"], [10, "10 backups"], [30, "30 backups"]], s.data.backupKeep))}
          ${row("Backups", html`<span class="path-value">${store.get("backupFolder")}</span>`, html`<button class="btn btn--sm" id="backupNow">${icon("archive")}Back up now</button><button class="btn btn--sm btn--ghost" data-open="${store.get("backupFolder")}">${icon("folder-open")}</button>`)}
          <div class="backup-list" id="backups"></div>
          ${row("Export", "Everything (deliveries, routes, sessions, settings) in one .haulix file.", html`<button class="btn btn--sm" id="exportAll">${icon("download")}Export data</button><button class="btn btn--sm btn--ghost" id="exportCsv">CSV logbook</button>`)}
          ${row("Import backup", "Replaces current data. A safety backup is made first.", html`<button class="btn btn--sm" id="importAll">${icon("upload")}Import…</button>`)}
          ${row("Clear data", "A safety backup is always created before clearing.", html`<button class="btn btn--sm btn--danger" data-clear="history">Clear history</button><button class="btn btn--sm btn--danger" data-clear="cache">Clear caches</button>`)}
        `)}

        ${section("appearance", "Appearance", html`
          ${row("Accent colour", "HAULIX uses one accent for interaction. State colours stay fixed.", html`<div class="swatches">${ACCENTS.map(([k, c, l]) => html`<button class="${cx("swatch", s.appearance.accent === k && "is-active")}" style="background-color:${c}" data-accent="${k}" data-tip="${l}" aria-label="${l}"></button>`)}</div>`)}
          ${row("Compact mode", "Tighter spacing for smaller screens.", toggle("appearance.compact", s.appearance.compact))}
          ${row("Animations", "Transitions and live indicators.", toggle("appearance.animations", s.appearance.animations))}
          ${row("Transparency", "Slightly translucent panels.", toggle("appearance.transparency", s.appearance.transparency))}
          ${row("Collapse sidebar", "Icon-only navigation rail. Always on below 1440 px wide.", toggle("appearance.sidebarCollapsed", s.appearance.sidebarCollapsed))}
        `)}

        ${section("about", "About", html`
          <div class="about-hero">
            <img class="brand-img" src="assets/brand/wordmark-outline.png" alt="HAULIX" style="height:40px">
            <div class="grow"><div class="about-hero__lead">Your free logbook and co-driver for Euro Truck Simulator 2.</div>
              <div class="faint" style="font-size:12px">Version <span class="num">${f.versionLabel(store.get("version"))}</span> · made by RyanTMP</div></div>
            <button class="btn btn--sm" id="showChangelog">${icon("sparkles")}What's new</button>
          </div>
          <div class="about-points">
            <div>${icon("book-open")}<span><strong>Logs every delivery</strong> with route, income, fuel and a driving score – automatically.</span></div>
            <div>${icon("gauge")}<span><strong>Live dashboard</strong>, current job page, real-time ETA and your driving score.</span></div>
            <div>${icon("monitor")}<span><strong>In-game HUD and notifications</strong> over your game, optionally read out by a natural voice.</span></div>
            <div>${icon("shield-check")}<span><strong>Private:</strong> no account, no ads, no tracking. Your data stays on this PC.</span></div>
          </div>
          ${row("Updates", "HAULIX looks for new versions on start and every 6 hours and installs them with one click.", html`<div class="row" style="gap:8px">${toggle("general.updateCheck", s.general.updateCheck !== false)}<button class="btn btn--sm" id="checkUpdate">${icon("refresh-cw")}Check now</button></div>`)}
          <div id="updateResult"></div>
          ${row("Your data", "Logbook, settings and backups are stored only on this PC. The only thing HAULIX asks the internet is whether a new version exists.", html`<span class="badge badge--ok">${icon("cloud-off")}Stays on your PC</span>`)}
          ${row("Links", "", html`<div class="row" style="gap:8px;flex-wrap:wrap;justify-content:flex-end">
            <a class="btn btn--sm" href="#" data-url="https://www.haulix-logging.com">${icon("globe")}Website</a>
            <a class="btn btn--sm" href="#" data-url="https://github.com/RyanTMP/Haulix/releases">${icon("download")}Downloads</a>
            <a class="btn btn--sm btn--ghost" href="#" data-url="https://github.com/RyanTMP/Haulix/issues">${icon("circle-alert")}Report a problem</a></div>`)}
          ${row("Keyboard", "", html`<span class="muted" style="font-size:12px"><span class="kbd">Ctrl K</span> search · <span class="kbd">Alt 1–9</span> pages · <span class="kbd">Ctrl ,</span> settings</span>`)}
          ${row("License & privacy", "HAULIX is free for personal use under the HAULIX License Agreement. Your data stays on this PC – see the Privacy Policy.", html`<div class="row" style="gap:8px;flex-wrap:wrap;justify-content:flex-end"><button class="btn btn--sm" data-legal="license">${icon("file-text")}License agreement</button><button class="btn btn--sm" data-legal="privacy">${icon("shield")}Privacy policy</button><button class="btn btn--sm btn--ghost" data-legal="thirdparty">${icon("list")}Third-party notices</button></div>`)}
          ${row("Credits", "Live data comes from the free SCS telemetry plugin by RenCloud. Charts with uPlot, icons by Lucide, voices by Piper.", "")}
          <p class="faint about-legal">© 2026 RyanTMP. All rights reserved. HAULIX, its name, logo and artwork belong to RyanTMP.</p>
          <p class="faint about-legal">HAULIX is an independent fan project, not affiliated with or endorsed by SCS Software, TruckersMP or any other company. Euro Truck Simulator 2 and American Truck Simulator are trademarks of SCS Software; all other names and trademarks belong to their owners.</p>
          <details class="about-dev"><summary>${icon("wrench")}For developers</summary>
            ${row("Custom update source", "URL of your own update manifest (JSON with version, url, notes). Leave empty to use GitHub.", html`<input class="input" name="general.updateFeedUrl" placeholder="https://…/haulix-update.json" style="width:260px" value="${s.general.updateFeedUrl || ""}">`)}
            ${row("Telemetry", "Reads the SCS SDK shared memory written by scs-telemetry.dll (RenCloud scs-sdk-plugin, revision 12).", "")}
            ${row("Open-source components", "uPlot (MIT), Lucide icons (ISC), Piper (MIT), Inter / Barlow Condensed / JetBrains Mono (OFL).", "")}
            ${row("Design system", "Colours, type, components and markers used across HAULIX.", html`<a class="btn btn--sm" href="#/design">${icon("palette")}Open reference</a>`)}
          </details>
        `)}

        <section class="card vtc-soon" id="sec-ats" data-cat="ats">
          <div class="card__body vtc-soon__body">
            <div class="vtc-soon__icon">${icon("flag")}</div>
            <span class="badge badge--outline">${icon("clock", "icon icon-sm")}Coming later</span>
            <h2 class="vtc-soon__title">American Truck Simulator</h2>
            <p class="vtc-soon__lead">This feature is not available in the current HAULIX version.</p>
            <p class="muted vtc-soon__text">Later HAULIX will support American Truck Simulator too: the same logbook, live job page, HUD and notifications – with miles and dollars. HAULIX works fully with ETS2 today.</p>
            <div class="vtc-soon__points">
              <div>${icon("book-open")}<span>Logbook and statistics for your ATS company</span></div>
              <div>${icon("briefcase")}<span>Current job page with real-time ETA for ATS</span></div>
              <div>${icon("gauge")}<span>In-game HUD, notifications and sounds in ATS</span></div>
            </div>
          </div>
        </section>
      </div>
    </div>`;
  },

  async mount(root, { call, params }) {
    const setting = (path, value) => saveSettings((s) => {
      const keys = path.split(".");
      let o = s;
      for (const k of keys.slice(0, -1)) o = o[k];
      o[keys[keys.length - 1]] = value;
    }).then(() => toast({ kind: "success", title: "Saved", timeout: 1400 })).catch((e) => toast({ kind: "error", title: "Could not save", message: e.message }));

    const coerce = (name, v) => {
      if (["notifications.voiceRate", "notifications.voiceVolume", "notifications.soundVolume", "hud.x", "hud.y", "hud.margin", "hud.scale", "hud.width", "hud.opacity", "truckersMp.intervalMinutes", "telemetry.updateHz", "telemetry.routePointSpacingM", "data.backupIntervalHours", "data.backupKeep"].includes(name)) return Number(v);
      if (name === "ets2.profilePath" && !v) return null;
      return v;
    };

    root.addEventListener("change", (e) => {
      const el = e.target;
      if (!el.name || !el.name.includes(".")) return;
      const v = el.type === "checkbox" ? el.checked : coerce(el.name, el.value);
      if (el.name === "online.sample") {
        call("online.sample", { on: v }).then(() => { store.set("onlineSample", v); toast({ kind: "info", title: v ? "Developer preview on" : "Developer preview off", message: v ? "The VTC and Online pages now show sample data." : "", timeout: 3000 }); });
        return;
      }
      if (el.name === "truckersMp.antiAfk" && v && !store.get("settings").truckersMp?.riskAccepted) {
        // Explicit confirmation before the first activation.
        confirm({ title: "Enable anti-AFK message?", danger: true, confirmLabel: "I accept the risk",
          text: "Automatically avoiding the TruckersMP inactivity kick is against the TruckersMP rules and can get your account banned, up to permanently. You use this feature at your own risk." })
          .then((ok) => {
            if (!ok) { el.checked = false; return; }
            saveSettings((s) => { s.truckersMp.antiAfk = true; s.truckersMp.riskAccepted = true; })
              .then(() => toast({ kind: "warning", title: "Anti-AFK message on", message: "Only while ETS2 is the active window on TruckersMP.", timeout: 5000 }));
          });
        return;
      }
      if (el.name === "general.language") {
        // Applies at once: the shell and this page are rebuilt in the new language.
        changeLanguage(v).catch((err) => toast({ kind: "error", title: "Could not save", message: err.message }));
        return;
      }
      setting(el.name, v);
      if (el.name === "ets2.profilePath" && v) loadSaves(v);
    });

    // ETS2 display mode: overlays are invisible in exclusive fullscreen.
    const showDisplay = (d) => {
      const el = $("#displayMode", root);
      if (!el || !d) return;
      el.textContent = {
        exclusive: "Exclusive fullscreen – the overlay cannot appear over the game. Switch to borderless fullscreen (looks the same).",
        borderless: "Borderless fullscreen – notifications appear over the game.",
        windowed: "Window mode – notifications appear over the game.",
      }[d.mode] || "Unknown (config.cfg not found)";
      $("#setBorderless", root)?.classList.toggle("hidden", d.mode !== "exclusive");
    };
    call("ets2.display").then(showDisplay).catch(() => {});
    $("#setBorderless", root)?.addEventListener("click", async () => {
      const r = await call("ets2.setBorderless").catch((err) => ({ ok: false, error: err.message }));
      if (r.ok) toast({ kind: "success", title: "Borderless fullscreen set", message: "Start ETS2 – HAULIX notifications now appear over the game. A backup of config.cfg was saved." });
      else toast({ kind: "warning", title: "Not changed", message: r.error });
      showDisplay(r.display);
    });

    $("#resetAfkMsg", root)?.addEventListener("click", () => {
      const input = root.querySelector('input[name="truckersMp.message"]');
      input.value = AFK_DEFAULT;
      setting("truckersMp.message", AFK_DEFAULT);
    });

    // License agreement, privacy policy and third-party notices, read inside HAULIX.
    const LEGAL_TITLES = { license: "HAULIX License Agreement", privacy: "HAULIX Privacy Policy", thirdparty: "Third-party notices" };
    root.querySelectorAll("[data-legal]").forEach((b) => b.addEventListener("click", async () => {
      const text = await call("legal.get", { doc: b.dataset.legal }).catch((e) => e.message);
      modal({ title: LEGAL_TITLES[b.dataset.legal], wide: true, body: html`<pre class="legal-text">${text}</pre>` });
    }));
    call("online.status").then((o) => {
      const el = $("#termsState", root);
      if (!el || !o?.termsAccepted) return;
      el.className = "badge badge--ok";
      el.innerHTML = html`${icon("circle-check", "icon icon-sm")}Accepted · version ${o.termsVersion}`.toString();
    }).catch(() => {});

    root.querySelectorAll("[data-url]").forEach((a) => a.addEventListener("click", (e) => { e.preventDefault(); call("shell.openUrl", { url: a.dataset.url }).catch(() => {}); }));
    $("#checkUpdate", root)?.addEventListener("click", async () => {
      const el = $("#updateResult", root);
      const u = await call("update.check", { force: true }).catch((err) => ({ enabled: true, error: err.message }));
      el.innerHTML = (!u.enabled ? html`<div class="callout" style="margin:8px 0">${icon("info")}<div>No update source is configured in this build.</div></div>`
        : u.error ? html`<div class="callout callout--warn" style="margin:8px 0">${icon("triangle-alert")}<div>Update check failed: ${u.error}</div></div>`
        : u.available ? html`<div class="callout callout--ok" style="margin:8px 0">${icon("download")}<div><strong>Version ${f.versionLabel(u.latest)} is available.</strong> <a class="link" href="#" data-show-update>Show update</a></div></div>`
        : html`<div class="callout callout--ok" style="margin:8px 0">${icon("circle-check")}<div>HAULIX is up to date (${f.versionLabel(u.current)}).</div></div>`).toString();
      el.querySelector("[data-show-update]")?.addEventListener("click", (e) => { e.preventDefault(); showUpdate(u); });
      el.querySelector("[data-dl]")?.addEventListener("click", (e) => { e.preventDefault(); call("shell.openUrl", { url: e.target.dataset.dl }); });
    });

    // ---- Voice: natural AI voices (download on request) or Windows voices ----
    const drawVoices = async () => {
      const el = $("#voiceList", root);
      if (!el) return;
      const v = await call("voice.list").catch(() => null);
      if (!v) { el.innerHTML = ""; return; }
      const cur = store.get("settings").notifications || {};
      const lang = store.get("settings").general.language === "en" || document.documentElement.lang === "en" ? "en" : "de";
      const chosen = cur.voiceId || (v.natural.find((x) => x.lang === lang && x.installed) || v.natural.find((x) => x.lang === lang))?.id;
      el.innerHTML = v.natural.map((x) => html`<div class="voice ${x.id === chosen ? "is-chosen" : ""}" data-voice="${x.id}">
          <span class="voice__flag">${x.lang.toUpperCase()}</span>
          <div class="grow"><strong>${x.name}</strong><span class="faint">${t(x.gender === "male" ? "Male voice" : "Female voice")} · ${x.sizeMb} MB</span>
            <div class="progress progress--thin hidden" data-voice-progress="${x.id}"><div class="progress__fill" style="width:0%"></div></div></div>
          ${x.installed
            ? html`${x.id === chosen ? html`<span class="badge badge--ok">${icon("check", "icon icon-sm")}In use</span>` : html`<button class="btn btn--sm" data-use-voice="${x.id}">Use</button>`}<button class="btn btn--sm btn--ghost btn--icon" data-remove-voice="${x.id}" data-tip="Remove">${icon("trash-2")}</button>`
            : html`<button class="btn btn--sm" data-install-voice="${x.id}">${icon("download")}Download</button>`}
        </div>`.toString()).join("");
      const win = $("#winVoice", root);
      if (win && win.options.length <= 1) v.windows.forEach((n) => win.add(new Option(n, n, false, n === cur.windowsVoice)));
    };
    drawVoices();
    const offVoice = [
      on("voiceProgress", (p) => { const bar = root.querySelector(`[data-voice-progress="${p.id}"]`); if (bar) { bar.classList.remove("hidden"); bar.firstElementChild.style.width = `${p.pct}%`; } }),
      on("voiceInstalled", (p) => { setting("notifications.voiceId", p.id); drawVoices(); }),
      on("voiceError", (p) => { toast({ kind: "error", title: "Voice download failed", message: p.message }); drawVoices(); }),
    ];
    $("#voiceList", root)?.addEventListener("click", async (e) => {
      const ins = e.target.closest("[data-install-voice]"), use = e.target.closest("[data-use-voice]"), rem = e.target.closest("[data-remove-voice]");
      if (ins) { ins.disabled = true; ins.classList.add("is-loading"); await call("voice.install", { id: ins.dataset.installVoice }); }
      if (use) { await setting("notifications.voiceId", use.dataset.useVoice); drawVoices(); }
      if (rem) { await call("voice.remove", { id: rem.dataset.removeVoice }); drawVoices(); }
    });
    root.querySelectorAll('[data-seg="notifications.voiceEngine"] button').forEach((b) => b.addEventListener("click", () => {
      $("#voiceNatural", root)?.classList.toggle("hidden", b.dataset.value !== "natural");
      $("#voiceWindows", root)?.classList.toggle("hidden", b.dataset.value !== "windows");
    }));
    root.querySelector('input[name="notifications.voiceRate"]')?.addEventListener("input", (e) => { const o = root.querySelector('[data-range-x="notifications.voiceRate"]'); if (o) o.textContent = `${(+e.target.value).toFixed(2)}×`; });
    $("#voiceTest", root)?.addEventListener("click", () => call("voice.test").catch(() => {}));
    root.querySelectorAll("[data-sound]").forEach((b) => b.addEventListener("click", () => call("sound.test", { sound: b.dataset.sound }).catch(() => {})));

    // ---- In-game HUD: live preview (drag it to place it), rows, sliders, look ----
    const drawHudPreview = () => {
      const h = store.get("settings").hud || {};
      const fields = h.fields || HUD_DEFAULT;
      const scene = $("#hudScene", root), card = $("#hudCardPrev", root);
      if (!scene || !card || card.classList.contains("is-dragging")) return;
      scene.style.setProperty("--hud-opacity", String((h.opacity ?? 90) / 100));
      const accent = HUD_ACCENTS.find(([k]) => k === (h.accent || "app"))?.[1] || getComputedStyle(document.documentElement).getPropertyValue("--accent").trim() || "#ffb020";
      card.style.setProperty("--hc-accent", accent);
      card.style.setProperty("--hc-w", String(h.width ?? 290));
      card.style.setProperty("--hc-scale", String(hudScale(h) / 100));
      card.dataset.theme = h.theme || "dark";
      card.dataset.density = h.density || "normal";
      card.toggleAttribute("data-square", h.rounded === false);
      card.innerHTML = html`${h.showHeader !== false ? html`<div class="hudp-card__kicker"><i></i>${t("CURRENT JOB")}<span>HAULIX</span></div>` : ""}
        ${h.showCargo !== false ? html`<div class="hudp-card__cargo">Steel coils · 22.4 t</div>` : ""}
        ${h.showRoute !== false ? html`<div class="hudp-card__route">Hamburg → Prague</div>` : ""}
        ${h.showProgress !== false ? html`<div class="hudp-card__bar"><em><i style="width:67%"></i></em><span>67 %</span></div>` : ""}
        ${fields.map((k) => HUD_FIELDS.find(([x]) => x === k)).filter(Boolean).map(([, , l, v, tone]) => html`<div class="hudp-card__row ${tone || ""}"><span>${t(l)}</span><b>${v}</b></div>`)}`.toString();
      card.dataset.pos = HUD_POS_OK(h.position) || "topRight";
      card.style.left = card.dataset.pos === "custom" ? `${h.x ?? 85}%` : "";
      card.style.top = card.dataset.pos === "custom" ? `${h.y ?? 20}%` : "";
      const sel = root.querySelector('select[name="hud.position"]');
      if (sel && sel.value !== card.dataset.pos) sel.value = card.dataset.pos;
    };
    drawHudPreview();
    const offHud = store.on("settings", drawHudPreview);

    // Drag the preview card inside the mini screen: sets a custom position (percent of the screen).
    $("#hudCardPrev", root)?.addEventListener("pointerdown", (e) => {
      const card = e.currentTarget, scene = $("#hudScene", root);
      const box = scene.getBoundingClientRect(), r = card.getBoundingClientRect();
      const dx = e.clientX - (r.left + r.width / 2), dy = e.clientY - (r.top + r.height / 2);
      card.setPointerCapture(e.pointerId);
      card.classList.add("is-dragging");
      card.dataset.pos = "custom";
      let x = 0, y = 0;
      const move = (ev) => {
        x = Math.round(Math.min(100, Math.max(0, ((ev.clientX - dx - box.left) / box.width) * 100)) * 10) / 10;
        y = Math.round(Math.min(100, Math.max(0, ((ev.clientY - dy - box.top) / box.height) * 100)) * 10) / 10;
        card.style.left = `${x}%`; card.style.top = `${y}%`;
      };
      move(e);
      const up = () => {
        card.removeEventListener("pointermove", move);
        card.classList.remove("is-dragging");
        saveSettings((s) => { s.hud.position = "custom"; s.hud.x = x; s.hud.y = y; })
          .then(() => toast({ kind: "success", title: "HUD position saved", timeout: 1600 }));
      };
      card.addEventListener("pointermove", move);
      card.addEventListener("pointerup", up, { once: true });
    });
    root.querySelectorAll("[data-hud-field]").forEach((cb) => cb.addEventListener("change", (e) => {
      e.stopPropagation();
      // Newly ticked rows go to the end, so the order is the order of ticking.
      const cur = (store.get("settings").hud?.fields || HUD_DEFAULT).filter((k) => root.querySelector(`[data-hud-field="${k}"]`)?.checked);
      const added = [...root.querySelectorAll("[data-hud-field]")].filter((x) => x.checked && !cur.includes(x.dataset.hudField)).map((x) => x.dataset.hudField);
      setting("hud.fields", [...cur, ...added]);
    }));
    root.querySelectorAll('input[type=range]').forEach((r) => r.addEventListener("input", () => {
      const out = root.querySelector(`[data-range="${r.name}"]`);
      if (out) out.textContent = `${r.value}${r.dataset.unit ?? " %"}`;
      const h = store.get("settings").hud;
      if (h && r.name.startsWith("hud.")) { h[r.name.slice(4)] = +r.value; drawHudPreview(); }
    }));
    $("[data-hud-accent]", root)?.addEventListener("click", (e) => {
      const b = e.target.closest("button[data-value]");
      if (!b) return;
      root.querySelectorAll("[data-hud-accent] button").forEach((x) => x.classList.toggle("is-active", x === b));
      setting("hud.accent", b.dataset.value);
    });
    $("#hudReset", root)?.addEventListener("click", async () => {
      if (!(await confirm({ title: "Reset the HUD?", text: "Position, look and rows go back to the standard HUD.", confirmLabel: "Reset" }))) return;
      await saveSettings((s) => { s.hud = { cardEnabled: true, onlyOnJob: s.hud?.onlyOnJob ?? false }; });
      location.reload();
    });
    $("#hudPlace", root)?.addEventListener("click", () => call("hud.place")
      .then(() => toast({ kind: "info", title: "Place the HUD", message: "Switch to your game monitor: drag the card, double-click it to save, right-click to cancel.", timeout: 7000 }))
      .catch((err) => toast({ kind: "error", title: "Not available", message: err.message })));
    $("#showChangelog", root)?.addEventListener("click", () => import("../core/changelog.js").then((m) => m.showChangelog()));
    $("#hudTest", root)?.addEventListener("click", () => call("hud.preview").then(() => toast({ kind: "info", title: "HUD shown for 10 seconds", message: "Check the position on your game monitor.", timeout: 4000 })).catch(() => {}));

    $("#notifyTest", root)?.addEventListener("click", () => call("notify.test").catch((err) => toast({ kind: "error", title: "Could not show notification", message: err.message })));

    root.addEventListener("click", async (e) => {
      // Only match elements inside this page: <html> itself carries data-accent/data-theme for styling,
      // so an unscoped closest("[data-accent]") matched every click and saved the accent each time.
      const hit = (sel) => { const x = e.target.closest(sel); return x && root.contains(x) ? x : null; };
      const seg = hit("[data-action=seg]");
      if (seg) {
        root.querySelectorAll(`[data-seg="${seg.dataset.name}"] button`).forEach((b) => b.classList.toggle("is-active", b === seg));
        setting(seg.dataset.name, coerce(seg.dataset.name, seg.dataset.value));
        return;
      }
      const acc = hit("[data-accent]");
      if (acc) {
        root.querySelectorAll("[data-accent]").forEach((b) => b.classList.toggle("is-active", b === acc));
        setting("appearance.accent", acc.dataset.accent);
        return;
      }
      const pick = hit("[data-pick]");
      if (pick) {
        const path = await call("dialog.pickFolder", { title: "Select folder" }).catch(() => null);
        if (!path) return;
        if (pick.dataset.pick.startsWith("ets2.")) {
          const v = await call("ets2.validatePaths", pick.dataset.pick === "ets2.gamePath" ? { gamePath: path } : { documentsPath: path });
          const ok = pick.dataset.pick === "ets2.gamePath" ? v.game : v.documents;
          if (!ok) { toast({ kind: "warning", title: "That doesn't look right", message: pick.dataset.pick === "ets2.gamePath" ? "The folder should contain bin\\win_x64\\eurotrucks2.exe." : "The folder should contain a profiles or steam_profiles folder." }); return; }
        }
        await setting(pick.dataset.pick, path);
        if (pick.dataset.pick.startsWith("ets2.")) store.set("detection", await call("ets2.detect"));
        location.reload();
        return;
      }
      const open = hit("[data-open]");
      if (open) { call("shell.openFolder", { path: open.dataset.open }); return; }
      const clear = hit("[data-clear]");
      if (clear) {
        const scope = clear.dataset.clear;
        const ok = await confirm({ title: scope === "history" ? "Clear history?" : "Clear caches?", danger: true, confirmLabel: "Clear",
          text: scope === "history" ? "Deliveries, routes, sessions, events and snapshots will be deleted. A safety backup is created first, so you can restore from Data → Backups." : "The cached profile, learned city positions and cargo names will be rebuilt from your save and future drives." });
        if (!ok) return;
        try { await call("data.clear", { scope }); toast({ kind: "success", title: "Data cleared", message: "A safety backup was saved first." }); loadData(); }
        catch (err) { toast({ kind: "error", title: "Clear failed", message: err.message }); }
      }
    });

    $("#redetect", root).onclick =async () => { store.set("detection", await call("ets2.detect")); toast({ kind: "success", title: "Detection complete" }); location.reload(); };
    $("#demoToggle", root).onclick = async () => {
      const on = !store.get("status")?.demo;
      await call("demo.set", { on });
      toast({ kind: "info", title: on ? "Demo drive started" : "Demo stopped", message: on ? "Open the Dashboard to watch a simulated drive." : "Demo data was discarded." });
      setTimeout(() => location.reload(), 400);
    };
    $("#backupNow", root).onclick = async (e) => {
      const b = e.currentTarget; b.classList.add("is-loading");
      try { const r = await call("data.backupNow"); toast({ kind: "success", title: "Backup created", message: r.name }); loadData(); }
      catch (err) { toast({ kind: "error", title: "Backup failed", message: err.message }); }
      finally { b.classList.remove("is-loading"); }
    };
    $("#exportAll", root).onclick = async () => {
      try { const r = await call("data.export"); if (r) toast({ kind: "success", title: "Export complete", message: r.path }); }
      catch (err) { toast({ kind: "error", title: "Export failed", message: err.message }); }
    };
    $("#exportCsv", root).onclick = async () => {
      try { const r = await call("data.exportCsv"); if (r) toast({ kind: "success", title: "Logbook exported", message: r }); }
      catch (err) { toast({ kind: "error", title: "Export failed", message: err.message }); }
    };
    $("#importAll", root).onclick = async () => {
      const ok = await confirm({ title: "Import backup?", text: "Importing replaces your current HAULIX data with the backup's contents. A safety backup of the current data is created first.", confirmLabel: "Choose file…" });
      if (ok) call("data.import").catch((err) => toast({ kind: "error", title: "Import failed", message: err.message }));
    };

    const loadSaves = async (profilePath) => {
      const sel = $("#saveSel", root);
      const saves = await call("ets2.saves", { profilePath }).catch(() => []);
      const cur = store.get("settings").ets2.saveSelection;
      sel.innerHTML = html`<option value="latest">Latest save (recommended)</option>${saves.map((s) => html`<option value="${s.name}" ${s.name === cur ? "selected" : ""}>${s.name} · ${f.dateTime(s.savedUtc)}</option>`)}`.toString();
    };
    const loadData = async () => {
      const [counts, backups] = await Promise.all([call("data.counts").catch(() => null), call("data.backups").catch(() => [])]);
      if (counts) $("#counts", root).innerHTML = row("Stored history", "", html`<span class="muted" style="font-size:12px;text-align:right">${f.num(counts.deliveries)} deliveries (${f.num(counts.recorded)} live, ${f.num(counts.imported)} imported) · ${f.num(counts.routes)} routes · ${f.num(counts.routePoints)} GPS points · ${f.num(counts.sessions)} sessions · ${f.num(counts.learnedCities)} learned cities</span>`).toString();
      $("#backups", root).innerHTML = backups.length ? backups.slice(0, 6).map((b) => html`<div class="row"><span class="badge ${b.kind === "auto" ? "" : b.kind === "safety" ? "badge--warn" : "badge--accent"}">${b.kind}</span><span class="grow ellipsis mono">${b.name}</span><span class="faint">${f.dateTime(b.createdUtc)} · ${f.bytes(b.sizeBytes)}</span>
        <button class="btn btn--sm btn--ghost" data-restore="${b.path}">Restore</button></div>`.toString()).join("") : html`<div class="faint" style="font-size:12px;padding:8px 0">No backups yet.</div>`.toString();
      $("#backups", root).querySelectorAll("[data-restore]").forEach((btn) => btn.onclick = async () => {
        const ok = await confirm({ title: "Restore this backup?", text: "Your current data will be replaced. A safety backup is created first.", confirmLabel: "Restore", danger: true });
        if (!ok) return;
        try { await call("data.restore", { path: btn.dataset.restore }); toast({ kind: "success", title: "Backup restored" }); setTimeout(() => location.reload(), 600); }
        catch (err) { toast({ kind: "error", title: "Restore failed", message: err.message }); }
      });
    };
    if (store.get("settings").ets2.profilePath) loadSaves(store.get("settings").ets2.profilePath);
    loadData();

    // Section highlighting + deep links (#/settings/ets2)
    const page = root;
    // Categories (HAULIX / ETS2 / ATS): only the chosen one's sections are shown; deep links pick the right one.
    let cat = CAT_OF[params[0]] || "haulix";
    const secs = () => SECTIONS.filter(([, , , c]) => c === cat).map(([id]) => [id, $(`#sec-${id}`, root)]);
    const mark = () => {
      const list = secs();
      let active = list[0][0];
      for (const [id, el] of list) if (el.getBoundingClientRect().top < 200) active = id;
      root.querySelectorAll("[data-sec]").forEach((a) => a.classList.toggle("is-active", a.dataset.sec === active));
    };
    const showCat = (c, sec) => {
      cat = c;
      root.querySelectorAll("[data-cat]").forEach((el) => el.classList.toggle("hidden", el.dataset.cat !== c));
      root.querySelectorAll("[data-cat-tab]").forEach((b) => { b.classList.toggle("is-active", b.dataset.catTab === c); b.setAttribute("aria-selected", String(b.dataset.catTab === c)); });
      const target = sec && CAT_OF[sec] === c ? sec : secs()[0][0];
      history.replaceState(null, "", `#/settings/${target}`);
      if (sec) setTimeout(() => $(`#sec-${sec}`, root)?.scrollIntoView(), 50);
      else $(".settings-cats", root)?.scrollIntoView();
      mark();
    };
    root.querySelectorAll("[data-cat-tab]").forEach((b) => b.addEventListener("click", () => showCat(b.dataset.catTab)));
    root.querySelectorAll("[data-sec]").forEach((a) => a.addEventListener("click", (e) => { e.preventDefault(); $(`#sec-${a.dataset.sec}`, root).scrollIntoView({ behavior: "smooth" }); history.replaceState(null, "", `#/settings/${a.dataset.sec}`); }));
    page.addEventListener("scroll", mark);
    showCat(cat, params[0] && CAT_OF[params[0]] ? params[0] : null);
    return () => { page.removeEventListener("scroll", mark); offHud(); offVoice.forEach((o) => o?.()); };
  },
};
