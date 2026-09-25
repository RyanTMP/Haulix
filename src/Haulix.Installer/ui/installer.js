// HAULIX Setup UI. Talks to SetupForm.cs through WebView2 postMessage.
(function () {
  const native = !!(window.chrome && window.chrome.webview);
  const listeners = {};
  const send = (msg) => (native ? window.chrome.webview.postMessage(msg) : mock(msg));
  const on = (evt, fn) => (listeners[evt] = fn);
  const emit = (evt, data) => listeners[evt] && listeners[evt](data);
  if (native) window.chrome.webview.addEventListener("message", (e) => emit(e.data.event, e.data.data));

  const esc = (s) => String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
  const icon = (n) => `<svg fill="none" stroke="currentColor" stroke-width="1.75" stroke-linecap="round" stroke-linejoin="round"><use href="#i-${n}"/></svg>`;
  const $ = (s) => document.querySelector(s);

  const state = { info: null, step: 0, accepted: false, dir: "", desktop: true, startMenu: true, plugin: true, launch: true, removeData: false, installedDir: null, error: null, lang: "en", langChosen: false };

  /* ---- German translation (applied to the rendered page) ---- */
  const DE = {
    "Welcome": "Willkommen", "Options": "Optionen", "Install": "Installieren", "Finish": "Fertig", "Confirm": "Bestätigen", "Remove": "Entfernen", "Done": "Fertig",
    "Install HAULIX": "HAULIX installieren", "Update HAULIX": "HAULIX aktualisieren",
    "Offline telemetry and fleet intelligence for Euro Truck Simulator 2. Everything stays on this PC.": "Offline-Telemetrie und Flottenauswertung für Euro Truck Simulator 2. Alles bleibt auf diesem PC.",
    "Your logbook, settings, backups and road map are kept. Only the program files are replaced.": "Fahrtenbuch, Einstellungen, Sicherungen und Straßenkarte bleiben erhalten. Nur die Programmdateien werden ersetzt.",
    "Live telemetry": "Live-Telemetrie", "Speed, fuel, damage and job progress while you drive.": "Geschwindigkeit, Kraftstoff, Schaden und Auftragsfortschritt während der Fahrt.",
    "Road map & navigation": "Straßenkarte & Navigation", "Real ETS2 streets with routes to your job or any city.": "Echte ETS2-Straßen mit Routen zu deinem Auftrag oder jeder Stadt.",
    "Automatic logbook": "Automatisches Fahrtenbuch", "Every delivery with route, income, fuel and damage.": "Jede Lieferung mit Route, Einnahmen, Verbrauch und Schaden.",
    "Your company": "Deine Firma", "Trucks, trailers, garages and AI drivers from your save.": "Lkw, Auflieger, Garagen und KI-Fahrer aus deinem Spielstand.",
    "I accept the license (GNU GPL v2)": "Ich akzeptiere die Lizenz (GNU GPL v2)", "HAULIX is free software.": "HAULIX ist freie Software.", "View license": "Lizenz anzeigen",
    "Development build.": "Entwicklungs-Build.", "This setup does not contain the app. Build it with tools/release/build-release.ps1.": "Dieses Setup enthält die App nicht. Erstelle es mit tools/release/build-release.ps1.",
    "Cancel": "Abbrechen", "Continue": "Weiter", "Back": "Zurück", "Where to install": "Installationsort", "Browse": "Durchsuchen",
    "HAULIX installs for your Windows user only. No administrator rights are needed.": "HAULIX wird nur für deinen Windows-Benutzer installiert. Keine Administratorrechte nötig.",
    "Space required": "Speicherbedarf", "Requirements": "Voraussetzungen", "Your data": "Deine Daten", "Start menu shortcut": "Startmenü-Verknüpfung",
    "Find HAULIX in the Start menu and Windows search.": "HAULIX im Startmenü und in der Windows-Suche.", "Desktop shortcut": "Desktop-Verknüpfung",
    "Install the ETS2 telemetry plugin": "ETS2-Telemetrie-Plugin installieren", "ETS2 was not found on this PC.": "ETS2 wurde auf diesem PC nicht gefunden.",
    "Already installed. Setup keeps it up to date.": "Bereits installiert. Setup hält es aktuell.",
    "Needed for live data (scs-sdk-plugin 1.12.1, MIT). Copied into ETS2's plugins folder.": "Nötig für Live-Daten (scs-sdk-plugin 1.12.1, MIT). Wird in den plugins-Ordner von ETS2 kopiert.",
    "Close ETS2 first, then restart it after setup.": "Schließe ETS2 vorher und starte es nach dem Setup neu.",
    "Update": "Aktualisieren", "Installing": "Installation", "Updating": "Aktualisierung", "Setting up HAULIX": "HAULIX wird eingerichtet",
    "This takes a few seconds.": "Das dauert nur ein paar Sekunden.", "Preparing": "Vorbereitung", "Please wait…": "Bitte warten…",
    "Ready to drive": "Bereit zur Fahrt", "HAULIX is installed": "HAULIX ist installiert",
    "Start ETS2 and HAULIX switches to LIVE automatically. On first start it builds your road map from the game files (about a minute).": "Starte ETS2 und HAULIX schaltet automatisch auf LIVE. Beim ersten Start wird die Straßenkarte aus den Spieldateien erstellt (ca. eine Minute).",
    "Telemetry plugin installed.": "Telemetrie-Plugin installiert.", "When you start ETS2, accept the \"Advanced SDK features\" prompt once.": "Bestätige beim Start von ETS2 einmalig die Abfrage „Erweiterte SDK-Funktionen“.",
    "One more step for live telemetry:": "Noch ein Schritt für Live-Telemetrie:", "Download plugin": "Plugin herunterladen",
    "ETS2 wasn't found automatically. You can point HAULIX to it in the setup wizard.": "ETS2 wurde nicht automatisch gefunden. Du kannst den Ordner im Einrichtungsassistenten angeben.",
    "Launch HAULIX now": "HAULIX jetzt starten", "Uninstall": "Deinstallieren", "Remove HAULIX": "HAULIX entfernen",
    "Also delete my HAULIX data": "Auch meine HAULIX-Daten löschen", "Removing": "Entfernen", "Uninstalling": "Wird deinstalliert",
    "HAULIX was removed": "HAULIX wurde entfernt", "Your HAULIX data was deleted too.": "Deine HAULIX-Daten wurden ebenfalls gelöscht.",
    "Your data was kept. Reinstall any time to pick up where you left off.": "Deine Daten wurden behalten. Installiere jederzeit neu, um weiterzumachen.", "Safe travels!": "Gute Fahrt!",
    "Something went wrong": "Etwas ist schiefgelaufen", "Setup failed": "Setup fehlgeschlagen", "Close": "Schließen", "Try again": "Erneut versuchen",
    "Offline · no account · no admin rights": "Offline · kein Konto · keine Adminrechte", "Language": "Sprache",
    "Preparing ": "Vorbereitung", "Closing HAULIX": "HAULIX wird geschlossen", "Removing previous version": "Vorherige Version wird entfernt",
    "Adding uninstaller": "Deinstallationsprogramm wird hinzugefügt", "Creating shortcuts": "Verknüpfungen werden erstellt",
    "Registering with Windows": "Registrierung bei Windows", "Installing ETS2 telemetry plugin": "ETS2-Telemetrie-Plugin wird installiert",
    "Telemetry plugin already installed": "Telemetrie-Plugin bereits installiert", "Removing shortcuts": "Verknüpfungen werden entfernt",
    "Removing registry entries": "Registrierungseinträge werden entfernt", "Removing program files": "Programmdateien werden entfernt", "Removing HAULIX data": "HAULIX-Daten werden entfernt",
  };
  const PATTERNS = [
    [/^Update · v(.+) → v(.+)$/, "Update · v$1 → v$2"], [/^Installing (.+)$/, "Installiere $1"],
    [/^HAULIX is running and will be closed during the (update|installation)\.$/, (m) => `HAULIX läuft und wird während der ${m[1] === "update" ? "Aktualisierung" : "Installation"} geschlossen.`],
    [/^The program, its shortcuts and its Windows entries will be removed from$/, "Programm, Verknüpfungen und Windows-Einträge werden entfernt aus"],
    [/^Logbook, settings, backups and road map in (.+)\. Leave this unchecked to keep your history for a later reinstall\.$/, "Fahrtenbuch, Einstellungen, Sicherungen und Straßenkarte in $1. Nicht anhaken, um den Verlauf für eine spätere Neuinstallation zu behalten."],
    [/^install the free SCS telemetry plugin \(scs-sdk-plugin, revision 12\) into ETS2's$/, "installiere das kostenlose SCS-Telemetrie-Plugin (scs-sdk-plugin, Revision 12) in den ETS2-Ordner"],
  ];
  const tr = (s, lang = state.lang) => {
    if (lang !== "de" || !s) return s;
    const k = s.trim();
    if (!k) return s;
    let out = DE[k];
    if (out === undefined) for (const [re, rep] of PATTERNS) { const m = k.match(re); if (m) { out = typeof rep === "function" ? rep(m) : k.replace(re, rep); break; } }
    return out === undefined ? s : s.replace(k, out);
  };
  // Originals are remembered per node so switching back to English restores the static texts too.
  const originals = new WeakMap();
  function translateDom(root) {
    const walk = (n) => {
      if (n.nodeType === 3) {
        if (!/[A-Za-z]/.test(n.nodeValue)) return;
        let src = originals.get(n);
        if (src === undefined || (n.nodeValue !== src && n.nodeValue !== tr(src, "de"))) { src = n.nodeValue; originals.set(n, src); }
        const v = tr(src);
        if (v !== n.nodeValue) n.nodeValue = v;
        return;
      }
      if (n.nodeType !== 1 || n.nodeName === "svg" || n.nodeName === "SCRIPT") return;
      let attrs = originals.get(n);
      if (!attrs) originals.set(n, (attrs = {}));
      for (const a of ["placeholder", "title", "alt"]) {
        const v = n.getAttribute(a);
        if (!v) continue;
        if (attrs[a] === undefined || (v !== attrs[a] && v !== tr(attrs[a], "de"))) attrs[a] = v;
        const tv = tr(attrs[a]);
        if (tv !== v) n.setAttribute(a, tv);
      }
      n.childNodes.forEach(walk);
    };
    walk(root);
  }

  const INSTALL_STEPS = ["Welcome", "Options", "Install", "Finish"];
  const UNINSTALL_STEPS = ["Confirm", "Remove", "Done"];

  function steps() {
    const list = state.info?.mode === "uninstall" ? UNINSTALL_STEPS : INSTALL_STEPS;
    $("#steps").innerHTML = list.map((s, i) => `<li class="${i === state.step ? "is-active" : i < state.step ? "is-done" : ""}"><span class="n">${i < state.step ? "✓" : String(i + 1).padStart(2, "0")}</span>${s}</li>`).join("");
  }

  function actions(html) { $("#actions").innerHTML = html; }

  function render() {
    renderPage();
    translateDom(document.body);
  }

  const langSwitch = () => `<div class="lang-switch"><span class="faint">${"Language"}</span>
    <div class="segmented"><button class="${state.lang === "de" ? "is-active" : ""}" data-a="lang-de">Deutsch</button><button class="${state.lang === "en" ? "is-active" : ""}" data-a="lang-en">English</button></div></div>`;

  function renderPage() {
    steps();
    const i = state.info;
    if (!i) return;
    $("#version").textContent = `v${i.version}`;
    const page = $("#page");
    page.style.animation = "none"; void page.offsetWidth; page.style.animation = "";

    if (state.error) {
      page.innerHTML = `<div class="done-mark done-mark--warn">${icon("triangle-alert")}</div>
        <div class="eyebrow-lg">Something went wrong</div><h1 class="title">Setup failed</h1>
        <p class="lead">${esc(state.error)}</p>`;
      actions(`<button class="btn btn--ghost" data-a="close">Close</button><span class="spacer"></span><button class="btn btn--primary" data-a="retry">Try again</button>`);
      return;
    }

    if (i.mode === "uninstall") return renderUninstall(page);

    if (state.step === 0) {
      const update = i.mode === "update";
      page.innerHTML = `${langSwitch()}<div class="eyebrow-lg">${update ? `Update · v${esc(i.existingVersion || "?")} → v${esc(i.version)}` : "Welcome"}</div>
        <h1 class="title">${update ? "Update HAULIX" : "Install HAULIX"}</h1>
        <p class="lead">${update ? "Your logbook, settings, backups and road map are kept. Only the program files are replaced." : "Offline telemetry and fleet intelligence for Euro Truck Simulator 2. Everything stays on this PC."}</p>
        <div class="features">
          <div class="feature">${icon("gauge")}<div><strong>Live telemetry</strong><span>Speed, fuel, damage and job progress while you drive.</span></div></div>
          <div class="feature">${icon("map")}<div><strong>Road map & navigation</strong><span>Real ETS2 streets with routes to your job or any city.</span></div></div>
          <div class="feature">${icon("book-open")}<div><strong>Automatic logbook</strong><span>Every delivery with route, income, fuel and damage.</span></div></div>
          <div class="feature">${icon("warehouse")}<div><strong>Your company</strong><span>Trucks, trailers, garages and AI drivers from your save.</span></div></div>
        </div>
        <label class="check-row"><input type="checkbox" id="accept" ${state.accepted ? "checked" : ""}>
          <div><div class="t">I accept the license (GNU GPL v2)</div><div class="d">HAULIX is free software. <a class="link" data-a="license">View license</a></div></div></label>
        ${!i.hasPayload ? `<div class="callout callout--warn" style="margin-top:10px">${icon("triangle-alert")}<div><strong>Development build.</strong> This setup does not contain the app. Build it with tools/release/build-release.ps1.</div></div>` : ""}`;
      $("#accept").onchange = (e) => { state.accepted = e.target.checked; $("[data-a=next]").disabled = !state.accepted || !i.hasPayload; };
      actions(`<button class="btn btn--ghost" data-a="close">Cancel</button><span class="spacer"></span><button class="btn btn--primary" data-a="next" ${state.accepted && i.hasPayload ? "" : "disabled"}>Continue</button>`);
    } else if (state.step === 1) {
      page.innerHTML = `<div class="eyebrow-lg">Options</div><h1 class="title">Where to install</h1>
        <p class="lead">HAULIX installs for your Windows user only. No administrator rights are needed.</p>
        <div class="path-box" style="margin-top:22px"><input class="input" id="dir" value="${esc(state.dir)}"><button class="btn" data-a="browse">${icon("folder-open")}Browse</button></div>
        <div class="facts"><span>Space required <b>${esc(i.sizeMb)} MB</b></span><span>Your data <b>${esc(i.dataDir)}</b></span><span>Requirements <b>Windows 10/11 · WebView2 ✓</b></span></div>
        <div class="divider"></div>
        <label class="check-row"><input type="checkbox" id="startMenu" ${state.startMenu ? "checked" : ""}><div><div class="t">Start menu shortcut</div><div class="d">Find HAULIX in the Start menu and Windows search.</div></div></label>
        <label class="check-row"><input type="checkbox" id="desktop" ${state.desktop ? "checked" : ""}><div><div class="t">Desktop shortcut</div></div></label>
        ${i.pluginBundled ? `<label class="check-row"><input type="checkbox" id="plugin" ${state.plugin && i.ets2 ? "checked" : ""} ${i.ets2 ? "" : "disabled"}>
          <div><div class="t">Install the ETS2 telemetry plugin</div><div class="d">${!i.ets2 ? "ETS2 was not found on this PC." : i.plugin ? "Already installed. Setup keeps it up to date." : "Needed for live data (scs-sdk-plugin 1.12.1, MIT). Copied into ETS2's plugins folder."}${i.ets2Running ? " Close ETS2 first, then restart it after setup." : ""}</div></div></label>` : ""}
        ${i.appRunning ? `<div class="callout callout--accent" style="margin-top:8px">${icon("info")}<div>HAULIX is running and will be closed during the ${i.mode === "update" ? "update" : "installation"}.</div></div>` : ""}`;
      $("#dir").oninput = (e) => (state.dir = e.target.value);
      $("#startMenu").onchange = (e) => (state.startMenu = e.target.checked);
      $("#desktop").onchange = (e) => (state.desktop = e.target.checked);
      const pl = $("#plugin"); if (pl) pl.onchange = (e) => (state.plugin = e.target.checked);
      actions(`<button class="btn btn--ghost" data-a="back">Back</button><span class="spacer"></span><button class="btn btn--primary" data-a="install">${icon("download")}${i.mode === "update" ? "Update" : "Install"}</button>`);
    } else if (state.step === 2) {
      page.innerHTML = `<div class="eyebrow-lg">${i.mode === "update" ? "Updating" : "Installing"}</div><h1 class="title">Setting up HAULIX</h1>
        <p class="lead">This takes a few seconds.</p>
        <div class="progress-xl"><i id="bar"></i></div>
        <div class="progress-meta"><span id="msg">Preparing</span><span id="pct">0%</span></div>`;
      actions(`<span class="spacer"></span><button class="btn" disabled>Please wait…</button>`);
    } else {
      page.innerHTML = `<div class="done-mark">${icon("check")}</div>
        <div class="eyebrow-lg">Ready to drive</div><h1 class="title">HAULIX is installed</h1>
        <p class="lead">Start ETS2 and HAULIX switches to LIVE automatically. On first start it builds your road map from the game files (about a minute).</p>
        ${i.ets2 && (state.plugin && i.pluginBundled) ? `<div class="callout callout--ok" style="margin-top:18px">${icon("circle-check")}<div><strong>Telemetry plugin installed.</strong> When you start ETS2, accept the "Advanced SDK features" prompt once.</div></div>` : ""}
        ${i.ets2 && !i.plugin && !(state.plugin && i.pluginBundled) ? `<div class="callout callout--accent" style="margin-top:18px">${icon("plug")}<div><strong>One more step for live telemetry:</strong> install the free SCS telemetry plugin (scs-sdk-plugin, revision 12) into ETS2's <span class="mono">bin\\win_x64\\plugins</span> folder. <a class="link" data-a="plugin">Download plugin</a></div></div>` : ""}
        ${!i.ets2 ? `<div class="callout" style="margin-top:18px">${icon("info")}<div>ETS2 wasn't found automatically. You can point HAULIX to it in the setup wizard.</div></div>` : ""}
        <label class="check-row" style="margin-top:14px"><input type="checkbox" id="launch" ${state.launch ? "checked" : ""}><div><div class="t">Launch HAULIX now</div></div></label>`;
      $("#launch").onchange = (e) => (state.launch = e.target.checked);
      actions(`<span class="spacer"></span><button class="btn btn--primary" data-a="finish">Finish</button>`);
    }
  }

  function renderUninstall(page) {
    const i = state.info;
    if (state.step === 0) {
      page.innerHTML = `${langSwitch()}<div class="eyebrow-lg">Uninstall</div><h1 class="title">Remove HAULIX</h1>
        <p class="lead">The program, its shortcuts and its Windows entries will be removed from <span class="mono">${esc(i.installDir)}</span>.</p>
        <div class="divider"></div>
        <label class="check-row"><input type="checkbox" id="removeData" ${state.removeData ? "checked" : ""}>
          <div><div class="t">Also delete my HAULIX data</div><div class="d">Logbook, settings, backups and road map in ${esc(i.dataDir)}. Leave this unchecked to keep your history for a later reinstall.</div></div></label>`;
      $("#removeData").onchange = (e) => (state.removeData = e.target.checked);
      actions(`<button class="btn btn--ghost" data-a="close">Cancel</button><span class="spacer"></span><button class="btn btn--danger" data-a="uninstall">${icon("trash-2")}Uninstall</button>`);
    } else if (state.step === 1) {
      page.innerHTML = `<div class="eyebrow-lg">Removing</div><h1 class="title">Uninstalling</h1>
        <div class="progress-xl"><i id="bar"></i></div><div class="progress-meta"><span id="msg">Preparing</span><span id="pct">0%</span></div>`;
      actions(`<span class="spacer"></span><button class="btn" disabled>Please wait…</button>`);
    } else {
      page.innerHTML = `<div class="done-mark">${icon("check")}</div><div class="eyebrow-lg">Done</div><h1 class="title">HAULIX was removed</h1>
        <p class="lead">${state.removeData ? "Your HAULIX data was deleted too." : "Your data was kept. Reinstall any time to pick up where you left off."} Safe travels!</p>`;
      actions(`<span class="spacer"></span><button class="btn btn--primary" data-a="close">Close</button>`);
    }
  }

  document.addEventListener("click", (e) => {
    const a = e.target.closest("[data-a]")?.dataset.a;
    if (!a || e.target.closest("[disabled]")) return;
    switch (a) {
      case "close": send({ cmd: "close" }); break;
      case "lang-de": case "lang-en": state.lang = a.slice(5); state.langChosen = true; render(); break;
      case "next": state.step = 1; render(); break;
      case "back": state.step = 0; render(); break;
      case "browse": send({ cmd: "browse", current: state.dir }); break;
      case "license": send({ cmd: "openFile" }); break;
      case "plugin": send({ cmd: "openUrl", url: "https://github.com/RenCloud/scs-sdk-plugin/releases" }); break;
      case "install":
        state.step = 2; render();
        send({ cmd: "install", dir: state.dir, desktop: state.desktop, startMenu: state.startMenu, plugin: state.plugin && state.info.ets2 && state.info.pluginBundled,
          // Only an explicit choice is handed over; otherwise a reinstall would reset the language picked in HAULIX.
          language: state.langChosen ? state.lang : "" });
        break;
      case "uninstall":
        state.step = 1; render();
        send({ cmd: "uninstall", removeData: state.removeData });
        break;
      case "finish":
        if (state.launch) send({ cmd: "launch", dir: state.installedDir });
        else send({ cmd: "close" });
        break;
      case "retry": state.error = null; state.step = state.info.mode === "uninstall" ? 0 : 1; render(); break;
    }
  });

  on("init", (info) => {
    const label = (v) => (v ? String(v).replace(/-([a-z]+)$/i, (_, l) => ` ${l.toUpperCase()}`) : v);
    state.info = { ...info, version: label(info.version), existingVersion: label(info.existingVersion) };
    state.dir = info.installDir;
    state.lang = (info.systemLanguage || navigator.language || "en").slice(0, 2).toLowerCase() === "de" ? "de" : "en";
    render();
  });
  on("browsed", (dir) => { state.dir = dir; const el = $("#dir"); if (el) el.value = dir; });
  on("progress", (p) => {
    const bar = $("#bar"); if (!bar) return;
    bar.style.width = `${Math.round(p.value * 100)}%`;
    $("#pct").textContent = `${Math.round(p.value * 100)}%`;
    $("#msg").textContent = tr(p.message);
  });
  on("installed", (dir) => { state.installedDir = dir; state.step = 3; render(); });
  on("uninstalled", () => { state.step = 2; render(); });
  on("error", (msg) => { state.error = msg; render(); });

  // Icons (shared sprite), then start.
  fetch("assets/icons.svg").then((r) => r.text()).then((svg) => {
    const d = document.createElement("div"); d.style.display = "none"; d.innerHTML = svg; document.body.prepend(d);
  }).finally(() => send({ cmd: "init" }));

  // Browser preview without the native host.
  function mock(msg) {
    const q = new URLSearchParams(location.search);
    setTimeout(() => {
      if (msg.cmd === "init") emit("init", { mode: q.get("mode") || "install", version: "1.1.0", existingVersion: "1.0.0", installDir: "C:\\Users\\you\\AppData\\Local\\Programs\\HAULIX", dataDir: "C:\\Users\\you\\AppData\\Local\\Haulix", hasPayload: true, sizeMb: 190, appRunning: false, ets2: true, plugin: false, pluginBundled: true, ets2Running: false, systemLanguage: q.get("lang") || navigator.language });
      if (msg.cmd === "install" || msg.cmd === "uninstall") {
        let v = 0;
        const t = setInterval(() => {
          v += 0.08;
          emit("progress", { value: Math.min(1, v), message: `Installing file ${Math.round(v * 240)}` });
          if (v >= 1) { clearInterval(t); emit(msg.cmd === "install" ? "installed" : "uninstalled", "C:\\Users\\you\\AppData\\Local\\Programs\\HAULIX"); }
        }, 120);
      }
    }, 50);
  }
})();
