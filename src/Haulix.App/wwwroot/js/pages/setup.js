import { html, cx, $ } from "../core/html.js";
import { icon } from "../core/icons.js";
import { store } from "../core/store.js";
import { segmented, select, toast } from "../components/ui.js";
import { saveSettings, changeLanguage } from "../app.js";
import { currencyList } from "../core/format.js";
import * as f from "../core/format.js";

const STEPS = ["Detect ETS2", "Choose profile", "Preferences", "Finish"];

export default {
  title: "Setup",
  crumb: () => "First-run configuration",

  render() {
    return html`<div class="setup">
      <div class="setup__brand"><img class="brand-img" src="assets/brand/banner.png" alt="HAULIX ETS2 Logger"><p>Offline telemetry and fleet intelligence for your ETS2 company.</p></div>
      <div class="steps" id="steps"></div>
      <section class="card"><div class="card__body" id="stepBody" style="padding:28px"></div>
        <footer class="card__foot"><button class="btn btn--ghost" id="back">${icon("chevron-left")}Back</button><span class="spacer"></span><span class="faint" id="stepHint" style="font-size:12px"></span><button class="btn btn--primary" id="next">Continue${icon("chevron-right")}</button></footer>
      </section>
    </div>`;
  },

  async mount(root, { call, params }) {
    let step = Math.min(3, Math.max(0, +(params[0] || 0) || 0));
    let det = store.get("detection") || (await call("ets2.detect"));
    let chosen = store.get("settings").ets2.profilePath || det.profiles?.[0]?.path || null;

    const checks = () => {
      const c = (ok, title, desc, action = "", warn = false) => html`<div class="${cx("check", ok ? "is-ok" : warn ? "is-warn" : "is-bad")}">
        <div class="check__icon">${icon(ok ? "check" : warn ? "triangle-alert" : "x")}</div>
        <div><div class="check__title">${title}</div><div class="check__desc">${desc}</div></div>${action}</div>`;
      return html`<h2 class="label" style="margin-bottom:6px">Detecting Euro Truck Simulator 2</h2>
        <p class="muted" style="margin-bottom:20px">HAULIX looks for the game through Steam and reads profiles from your Documents folder. Nothing leaves this PC.</p>
        <div class="check-list">
          ${c(!!det.gamePath, "Game installation", det.gamePath || "Not found in your Steam libraries", html`<button class="btn btn--sm" data-pick="game">${icon("folder-open")}${det.gamePath ? "Change" : "Locate"}</button>`)}
          ${c(!!det.documentsPath, "Profiles & saves", det.documentsPath || "Documents\\Euro Truck Simulator 2 not found", html`<button class="btn btn--sm" data-pick="docs">${icon("folder-open")}${det.documentsPath ? "Change" : "Locate"}</button>`)}
          ${c(det.profiles?.length > 0, "Profiles", det.profiles?.length ? `${det.profiles.length} found · save format ${det.saveFormat || "unknown"}` : "No profiles found yet. Create one in ETS2 first.")}
          ${c(det.pluginInstalled, "Telemetry plugin", det.pluginInstalled ? det.pluginPath : "scs-telemetry.dll is not in bin\\win_x64\\plugins. HAULIX still works from saves, but without live data.", "", true)}
        </div>
        ${!det.pluginInstalled ? html`<div class="callout" style="margin-top:16px">${icon("info")}<div><strong>Live telemetry</strong> needs the free <strong>scs-sdk-plugin</strong> (RenCloud, revision 12). Copy <span class="mono">scs-telemetry.dll</span> into <span class="mono">${det.gamePath ? `${det.gamePath}\\bin\\win_x64\\plugins` : "…\\bin\\win_x64\\plugins"}</span>, then restart ETS2. HAULIX detects it automatically.</div></div>` : ""}`;
    };

    const profiles = () => html`<h2 class="label" style="margin-bottom:6px">Choose your profile</h2>
      <p class="muted" style="margin-bottom:20px">HAULIX reads your company, fleet, garages and drivers from this profile's newest save, and follows it automatically as you play.</p>
      ${det.profiles?.length ? html`<div class="profile-pick">${det.profiles.map((p) => html`<button class="${cx("profile-option", p.path === chosen && "is-selected")}" data-profile="${p.path}">
          <strong>${p.name}</strong><span class="muted">${p.companyName || "No company yet"}</span>
          <span class="faint" style="font-size:12px">${p.kind === "steam" ? "Steam Cloud" : "Local"} · ${p.saveCount} saves · last played ${p.lastSaveUtc ? f.ago(p.lastSaveUtc) : "never"}</span>
          ${p.xp ? html`<span class="num faint" style="font-size:12px">${f.num(p.xp)} XP · ${f.dist(p.distanceKm)}</span>` : ""}</button>`)}</div>`
      : html`<div class="callout callout--warn">${icon("triangle-alert")}<div>No profiles were found. Start ETS2, create a profile, and come back. You can also finish setup now and choose a profile later in Settings.</div></div>`}`;

    const prefs = () => {
      const s = store.get("settings");
      return html`<h2 class="label" style="margin-bottom:20px">Preferences</h2>
        <div class="setting"><div><div class="setting__title">Language</div><div class="setting__desc">Uses your Windows display language unless you choose one.</div></div><div class="setting__control">${select("general.language", [["auto", "Automatic (Windows)"], ["en", "English"], ["de", "Deutsch"]], s.general.languageChosen ? s.general.language : "auto")}</div></div>
        <div class="setting"><div><div class="setting__title">Units</div></div><div class="setting__control">${segmented("general.units", [["metric", "Metric"], ["imperial", "Imperial"]], s.general.units)}</div></div>
        <div class="setting"><div><div class="setting__title">Currency</div><div class="setting__desc">ETS2 pays in euro. Other currencies are converted at approximate rates.</div></div><div class="setting__control">${select("general.currency", currencyList.map((c) => [c, c]), s.general.currency)}</div></div>
        <div class="setting"><div><div class="setting__title">Theme</div></div><div class="setting__control">${segmented("general.theme", [["dark", "Dark"], ["midnight", "Midnight"], ["light", "Light"]], s.general.theme)}</div></div>
        <div class="setting"><div><div class="setting__title">Launch with Windows</div><div class="setting__desc">Recommended, so every drive is recorded even if you forget to open HAULIX.</div></div><div class="setting__control"><label class="toggle"><input type="checkbox" name="general.launchWithWindows" ${s.general.launchWithWindows ? "checked" : ""}><span></span></label></div></div>`;
    };

    const finish = () => {
      const p = store.get("profile");
      const st = store.get("status");
      return html`<h2 class="label" style="margin-bottom:6px">Reading your save</h2>
        <p class="muted" style="margin-bottom:20px">HAULIX stores everything in a local SQLite database. From now on it records deliveries, routes and telemetry automatically whenever ETS2 is running.</p>
        ${p ? html`<div class="detail-hero" style="margin-bottom:18px">
            <div class="stat"><div class="stat__label">Company</div><div class="stat__value ellipsis" style="font-size:18px">${p.companyName || p.profileName}</div></div>
            <div class="stat"><div class="stat__label">Trucks</div><div class="stat__value">${p.trucks.length}</div></div>
            <div class="stat"><div class="stat__label">Garages</div><div class="stat__value">${p.garages.length}</div></div>
            <div class="stat"><div class="stat__label">Deliveries imported</div><div class="stat__value">${p.deliveries.length}</div></div>
          </div><div class="callout callout--ok">${icon("circle-check")}<div><strong>Profile loaded.</strong> ${f.money(p.money)} in the bank · ${f.num(p.xp)} XP · ${f.dist(p.totalDistanceKm)} driven.</div></div>`
          : st?.profile?.state === "error" ? html`<div class="callout callout--crit">${icon("circle-alert")}<div><strong>The save could not be read.</strong> ${st.profile.error}</div></div>`
          : chosen ? html`<div class="row" style="gap:12px"><span class="btn is-loading" style="width:34px"></span><span class="muted">Parsing save game…</span></div>`
          : html`<div class="callout">${icon("info")}<div>No profile selected. You can choose one later in Settings → ETS2.</div></div>`}`;
    };

    const draw = () => {
      $("#steps", root).innerHTML = STEPS.map((s, i) => `<div class="${i < step ? "is-done" : i === step ? "is-active" : ""}"><i></i>${i + 1}. ${s}</div>`).join("");
      $("#stepBody", root).innerHTML = [checks, profiles, prefs, finish][step]().toString();
      $("#back", root).style.visibility = step === 0 ? "hidden" : "visible";
      $("#next", root).innerHTML = html`${step === 3 ? "Open dashboard" : "Continue"}${icon(step === 3 ? "arrow-right" : "chevron-right")}`.toString();
      $("#stepHint", root).textContent = step === 1 && !chosen ? "Select a profile or continue without one" : "";
    };
    draw();
    const offP = store.on("profile", () => { if (step === 3) draw(); });
    const offS = store.on("status", (s) => { if (step === 3 && s?.profile?.state === "error") draw(); });

    root.addEventListener("click", async (e) => {
      const pick = e.target.closest("[data-pick]");
      if (pick) {
        const path = await call("dialog.pickFolder", { title: pick.dataset.pick === "game" ? "Select the Euro Truck Simulator 2 installation folder" : "Select Documents\\Euro Truck Simulator 2" });
        if (!path) return;
        const v = await call("ets2.validatePaths", pick.dataset.pick === "game" ? { gamePath: path } : { documentsPath: path });
        if (pick.dataset.pick === "game" ? !v.game : !v.documents) { toast({ kind: "warning", title: "Folder not recognised", message: pick.dataset.pick === "game" ? "Expected bin\\win_x64\\eurotrucks2.exe inside." : "Expected a profiles folder inside." }); return; }
        await saveSettings((s) => { if (pick.dataset.pick === "game") s.ets2.gamePath = path; else s.ets2.documentsPath = path; });
        det = await call("ets2.detect");
        store.set("detection", det);
        chosen = chosen || det.profiles?.[0]?.path || null;
        draw();
        return;
      }
      const prof = e.target.closest("[data-profile]");
      if (prof) { chosen = prof.dataset.profile; draw(); return; }
      const seg = e.target.closest("[data-action=seg]");
      if (seg) {
        root.querySelectorAll(`[data-seg="${seg.dataset.name}"] button`).forEach((b) => b.classList.toggle("is-active", b === seg));
        const [a, b] = seg.dataset.name.split(".");
        saveSettings((s) => (s[a][b] = seg.dataset.value));
      }
    });
    root.addEventListener("change", (e) => {
      const el = e.target;
      if (!el.name?.includes(".")) return;
      if (el.name === "general.language") {
        // Stay on this step while the wizard is rebuilt in the new language.
        history.replaceState(null, "", "#/setup/2");
        changeLanguage(el.value).catch((err) => toast({ kind: "error", title: "Could not save", message: err.message }));
        return;
      }
      const [a, b] = el.name.split(".");
      saveSettings((s) => (s[a][b] = el.type === "checkbox" ? el.checked : el.value));
    });

    $("#back", root).onclick = () => { step = Math.max(0, step - 1); draw(); };
    $("#next", root).onclick = async () => {
      if (step === 1 && chosen && chosen !== store.get("settings").ets2.profilePath) {
        await call("ets2.selectProfile", { path: chosen }).catch((err) => toast({ kind: "error", title: "Could not select profile", message: err.message }));
        store.set("settings", await call("settings.get"));
      }
      if (step === 3) {
        await saveSettings((s) => (s.setupComplete = true));
        location.hash = "#/dashboard";
        return;
      }
      step++;
      draw();
    };
    return () => { offP(); offS(); };
  },
};
