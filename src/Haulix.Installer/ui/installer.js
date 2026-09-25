// HAULIX Setup UI (0.0.9). Talks to SetupForm.cs through WebView2 postMessage.
// Flow: Welcome (license) → Setup type (Express / Custom) → [Custom options] → Summary → Installing → Done.
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

  // Defaults of a custom install = the HAULIX defaults, so "Custom" without changes behaves like "Express".
  const PREF_DEFAULTS = {
    units: "metric", currency: "EUR", theme: "dark", accent: "amber",
    launchWithWindows: false, startMinimized: false, minimizeToTray: false, updateCheck: true, autoBackup: true,
    hud: false, notifications: true, overlay: true, sounds: true, voice: false, afkWarning: true, recordRoutes: true, borderless: false,
  };
  const state = {
    info: null, page: "welcome", accepted: false, type: "express", tab: "install",
    dir: "", desktop: true, startMenu: true, plugin: true, launch: true, removeData: false,
    prefs: { ...PREF_DEFAULTS }, installedDir: null, error: null, lang: "en", langChosen: false, progress: 0,
  };

  /* ---- German translation (applied to the rendered page) ---- */
  const DE = {
    "Welcome": "Willkommen", "Setup type": "Installationsart", "Options": "Optionen", "Summary": "Übersicht", "Install": "Installieren", "Finish": "Fertig",
    "Confirm": "Bestätigen", "Remove": "Entfernen", "Done": "Fertig", "Language": "Sprache",
    "Install HAULIX": "HAULIX installieren", "Update HAULIX": "HAULIX aktualisieren",
    "Your free logbook and co-driver for Euro Truck Simulator 2.": "Dein kostenloses Fahrtenbuch und Beifahrer für Euro Truck Simulator 2.",
    "Your logbook, settings and backups are kept. Only the program files are replaced.": "Fahrtenbuch, Einstellungen und Sicherungen bleiben erhalten. Nur die Programmdateien werden ersetzt.",
    "Everything stays on this PC · no account · no admin rights": "Alles bleibt auf diesem PC · kein Konto · keine Adminrechte",
    "Live telemetry": "Live-Telemetrie", "Speed, fuel, damage and job progress while you drive.": "Geschwindigkeit, Kraftstoff, Schaden und Auftragsfortschritt während der Fahrt.",
    "Current job & HUD": "Aktueller Auftrag & HUD", "Real-time ETA, costs and a job card over your game.": "Echtzeit-Ankunft, Kosten und eine Auftragskarte über deinem Spiel.",
    "Automatic logbook": "Automatisches Fahrtenbuch", "Every delivery with income, fuel, damage and driving score.": "Jede Lieferung mit Einnahmen, Verbrauch, Schaden und Fahrscore.",
    "Achievements": "Erfolge", "More than 60 goals and a driver rank for your career.": "Über 60 Ziele und ein Fahrerrang für deine Karriere.",
    "Sounds & voices": "Töne & Stimmen", "Chimes and natural voices for notifications.": "Töne und natürliche Stimmen für Benachrichtigungen.",
    "Your company": "Deine Firma", "Trucks, trailers, garages and drivers from your save.": "Lkw, Auflieger, Garagen und Fahrer aus deinem Spielstand.",
    "I have read and accept the License Agreement and the Privacy Policy": "Ich habe den Lizenzvertrag und die Datenschutzerklärung gelesen und akzeptiere sie",
    "Free for personal use": "Kostenlos für den privaten Gebrauch", "Read the license agreement": "Lizenzvertrag lesen", "Read the privacy policy": "Datenschutzerklärung lesen",
    "License agreement": "Lizenzvertrag", "Privacy policy": "Datenschutzerklärung", "Accept and close": "Akzeptieren und schließen",
    "Updated license agreement.": "Aktualisierter Lizenzvertrag.",
    "The license agreement and privacy policy have changed since your version. Please read them and accept them to continue.": "Lizenzvertrag und Datenschutzerklärung haben sich seit deiner Version geändert. Bitte lies sie und akzeptiere sie, um fortzufahren.",
    "Development build.": "Entwicklungs-Build.", "This setup does not contain the app. Build it with tools/release/build-release.ps1.": "Dieses Setup enthält die App nicht. Erstelle es mit tools/release/build-release.ps1.",
    "Cancel": "Abbrechen", "Continue": "Weiter", "Back": "Zurück", "Browse": "Durchsuchen", "Close": "Schließen",
    "How do you want to install?": "Wie möchtest du installieren?", "How do you want to update?": "Wie möchtest du aktualisieren?",
    "Express": "Express", "Custom": "Benutzerdefiniert", "Recommended": "Empfohlen",
    "Install with the recommended settings. You can change everything later in HAULIX.": "Mit den empfohlenen Einstellungen installieren. Du kannst später alles in HAULIX ändern.",
    "Keep your settings and replace only the program files.": "Deine Einstellungen behalten und nur die Programmdateien ersetzen.",
    "Choose the folder, shortcuts, startup, notifications, HUD and look yourself.": "Ordner, Verknüpfungen, Autostart, Benachrichtigungen, HUD und Aussehen selbst wählen.",
    "Start menu and desktop shortcuts": "Startmenü- und Desktop-Verknüpfung", "ETS2 telemetry plugin": "ETS2-Telemetrie-Plugin", "Notifications with sounds": "Benachrichtigungen mit Tönen",
    "Automatic backups and updates": "Automatische Sicherungen und Updates",
    "Install location, shortcuts and plugin": "Installationsort, Verknüpfungen und Plugin", "Startup, notifications, HUD and look": "Autostart, Benachrichtigungen, HUD und Aussehen",
    "Choose your options": "Wähle deine Optionen", "Everything here can be changed later in HAULIX → Settings.": "Alles hier lässt sich später in HAULIX → Einstellungen ändern.",
    "Installation": "Installation", "Startup": "Start", "Driving": "Fahren", "Look & units": "Aussehen & Einheiten",
    "Install location": "Installationsort", "Shortcuts & plugin": "Verknüpfungen & Plugin",
    "HAULIX installs for your Windows user only. No administrator rights are needed.": "HAULIX wird nur für deinen Windows-Benutzer installiert. Keine Administratorrechte nötig.",
    "Space required": "Speicherbedarf", "Your data": "Deine Daten", "Requirements": "Voraussetzungen",
    "Start menu shortcut": "Startmenü-Verknüpfung", "Find HAULIX in the Start menu and Windows search.": "HAULIX im Startmenü und in der Windows-Suche.",
    "Desktop shortcut": "Desktop-Verknüpfung", "An icon on your desktop.": "Ein Symbol auf deinem Desktop.",
    "Install the ETS2 telemetry plugin": "ETS2-Telemetrie-Plugin installieren", "ETS2 was not found on this PC.": "ETS2 wurde auf diesem PC nicht gefunden.",
    "Already installed. Setup keeps it up to date.": "Bereits installiert. Setup hält es aktuell.",
    "Needed for live data (scs-sdk-plugin 1.12.1, MIT). Copied into ETS2's plugins folder.": "Nötig für Live-Daten (scs-sdk-plugin 1.12.1, MIT). Wird in den plugins-Ordner von ETS2 kopiert.",
    "Start with Windows": "Mit Windows starten", "HAULIX starts minimised when you sign in, so every drive is logged.": "HAULIX startet minimiert bei der Anmeldung, damit jede Fahrt protokolliert wird.",
    "Start minimised": "Minimiert starten", "Open HAULIX in the background.": "HAULIX im Hintergrund öffnen.",
    "Keep running in the tray": "Im Infobereich weiterlaufen", "Closing the window keeps HAULIX logging in the background.": "Beim Schließen des Fensters protokolliert HAULIX im Hintergrund weiter.",
    "Check for updates": "Nach Updates suchen", "Ask GitHub for new HAULIX versions on start and every 6 hours.": "Beim Start und alle 6 Stunden bei GitHub nach neuen Versionen fragen.",
    "Automatic backups": "Automatische Sicherungen", "A daily copy of your logbook and settings.": "Eine tägliche Kopie deines Fahrtenbuchs und deiner Einstellungen.",
    "In-game HUD": "Ingame-HUD", "A glass job card over ETS2 with route, ETA and more.": "Eine Glas-Auftragskarte über ETS2 mit Route, Ankunft und mehr.",
    "Job notifications": "Auftrags-Benachrichtigungen", "Accepted, delivered, milestones, deadline and fuel warnings.": "Angenommen, geliefert, Meilensteine, Frist- und Tankwarnungen.",
    "Show notifications over the game": "Benachrichtigungen über dem Spiel", "Cards on top of ETS2 while HAULIX runs in the background.": "Karten über ETS2, während HAULIX im Hintergrund läuft.",
    "Notification sounds": "Benachrichtigungstöne", "A short chime with every notification.": "Ein kurzer Ton bei jeder Benachrichtigung.",
    "Read notifications aloud": "Benachrichtigungen vorlesen", "Windows voice now; natural AI voices can be downloaded in HAULIX.": "Jetzt Windows-Stimme; natürliche KI-Stimmen lassen sich in HAULIX laden.",
    "TruckersMP AFK warning": "TruckersMP-AFK-Warnung", "Warns you before the server's inactivity kick.": "Warnt dich vor dem Inaktivitäts-Kick des Servers.",
    "Record GPS traces": "GPS-Verlauf aufzeichnen", "Needed for the speed profile of each delivery.": "Nötig für das Geschwindigkeitsprofil jeder Lieferung.",
    "Switch ETS2 to borderless fullscreen": "ETS2 auf randloses Vollbild umstellen", "Needed so the HUD and notifications can appear over the game. A backup of config.cfg is kept.": "Nötig, damit HUD und Benachrichtigungen über dem Spiel erscheinen. Eine Sicherung der config.cfg bleibt erhalten.",
    "Theme": "Design", "Dark": "Dunkel", "Midnight": "Mitternacht", "Light": "Hell", "Accent colour": "Akzentfarbe",
    "Units": "Einheiten", "Metric": "Metrisch", "Imperial": "Imperial", "Currency": "Währung", "ETS2 pays in euro; other currencies use approximate rates.": "ETS2 zahlt in Euro; andere Währungen nutzen ungefähre Kurse.",
    "Ready to install": "Bereit zur Installation", "Ready to update": "Bereit zum Aktualisieren", "Check your choices and start.": "Prüfe deine Auswahl und leg los.",
    "Recommended settings": "Empfohlene Einstellungen", "Your settings stay as they are": "Deine Einstellungen bleiben unverändert",
    "Setting up HAULIX": "HAULIX wird eingerichtet", "This takes a few seconds.": "Das dauert nur ein paar Sekunden.", "Preparing": "Vorbereitung",
    "Ready to drive": "Bereit zur Fahrt", "HAULIX is installed": "HAULIX ist installiert", "HAULIX is updated": "HAULIX ist aktualisiert",
    "Start ETS2 and HAULIX switches to LIVE automatically.": "Starte ETS2 und HAULIX schaltet automatisch auf LIVE.",
    "Telemetry plugin installed.": "Telemetrie-Plugin installiert.", "When you start ETS2, accept the \"Advanced SDK features\" prompt once.": "Bestätige beim Start von ETS2 einmal die Frage nach den \"erweiterten SDK-Funktionen\".",
    "One more step for live telemetry:": "Noch ein Schritt für Live-Telemetrie:", "Download plugin": "Plugin herunterladen",
    "ETS2 wasn't found automatically. You can point HAULIX to it in the setup wizard.": "ETS2 wurde nicht automatisch gefunden. Du kannst es im Einrichtungsassistenten von HAULIX angeben.",
    "Launch HAULIX now": "HAULIX jetzt starten", "Start ETS2": "ETS2 starten", "Take a job": "Auftrag annehmen", "Watch HAULIX work": "HAULIX arbeiten lassen",
    "HAULIX switches to LIVE as soon as the game runs.": "HAULIX schaltet auf LIVE, sobald das Spiel läuft.", "Everything about it appears on the Current job page and in the HUD.": "Alles dazu erscheint auf der Seite Aktueller Auftrag und im HUD.",
    "Deliveries land in your logbook and unlock achievements.": "Lieferungen landen im Fahrtenbuch und schalten Erfolge frei.",
    "Website": "Website", "What's new": "Neuigkeiten",
    "Uninstall": "Deinstallieren", "Remove HAULIX": "HAULIX entfernen", "Also delete my HAULIX data": "Auch meine HAULIX-Daten löschen",
    "Removing": "Wird entfernt", "Uninstalling": "Deinstallation", "HAULIX was removed": "HAULIX wurde entfernt",
    "Your HAULIX data was deleted too.": "Deine HAULIX-Daten wurden ebenfalls gelöscht.", "Your data was kept. Reinstall any time to pick up where you left off.": "Deine Daten wurden behalten. Installiere jederzeit neu und mach weiter, wo du aufgehört hast.",
    "Safe travels!": "Gute Fahrt!", "Something went wrong": "Etwas ist schiefgelaufen", "Setup failed": "Setup fehlgeschlagen", "Try again": "Erneut versuchen",
    "Please wait…": "Bitte warten…", "Update": "Aktualisieren",
    "Copying program files": "Programmdateien werden kopiert", "Closing HAULIX": "HAULIX wird geschlossen", "Removing old files": "Alte Dateien werden entfernt", "Finishing": "Abschluss",
    "Adding uninstaller": "Deinstallationsprogramm wird hinzugefügt", "Creating shortcuts": "Verknüpfungen werden erstellt",
    "Registering with Windows": "Registrierung bei Windows", "Installing ETS2 telemetry plugin": "ETS2-Telemetrie-Plugin wird installiert",
    "Telemetry plugin already installed": "Telemetrie-Plugin bereits installiert", "Removing shortcuts": "Verknüpfungen werden entfernt",
    "Removing previous version": "Vorherige Version wird entfernt", "Removing registry entries": "Registrierungseinträge werden entfernt", "Removing program files": "Programmdateien werden entfernt", "Removing HAULIX data": "HAULIX-Daten werden entfernt",
  };
  const PATTERNS = [
    [/^Update · v(.+) → v(.+)$/, "Update · v$1 → v$2"],
    [/^HAULIX is running and will be closed during the (update|installation)\.$/, (m) => `HAULIX läuft und wird während der ${m[1] === "update" ? "Aktualisierung" : "Installation"} geschlossen.`],
    [/^The program, its shortcuts and its Windows entries will be removed from$/, "Programm, Verknüpfungen und Windows-Einträge werden entfernt aus"],
    [/^Logbook, settings and backups in (.+)\. Leave this unchecked to keep your history for a later reinstall\.$/, "Fahrtenbuch, Einstellungen und Sicherungen in $1. Nicht anhaken, um den Verlauf für eine spätere Neuinstallation zu behalten."],
    [/^(Already installed\. Setup keeps it up to date\.|Needed for live data .+|ETS2 was not found on this PC\.) Close ETS2 first, then restart it after setup\.$/, (m) => `${tr(m[1])} Schließe zuerst ETS2 und starte es nach dem Setup neu.`],
    [/^(Dark|Midnight|Light) · (Metric|Imperial) · ([A-Z]{3})$/, (m) => `${DE[m[1]]} · ${DE[m[2]]} · ${m[3]}`],
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
      if (n.nodeType !== 1 || n.nodeName === "svg" || n.nodeName === "SCRIPT" || n.classList?.contains("doc-view__text")) return;
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

  /* ---- Steps ---- */
  const isUninstall = () => state.info?.mode === "uninstall";
  const isUpdate = () => state.info?.mode === "update";
  function flow() {
    if (isUninstall()) return [["confirm", "Confirm"], ["progress", "Remove"], ["done", "Done"]];
    return [["welcome", "Welcome"], ["type", "Setup type"], ...(state.type === "custom" ? [["options", "Options"]] : []),
      ["summary", "Summary"], ["progress", "Install"], ["done", "Finish"]];
  }
  function steps() {
    const list = flow();
    const cur = list.findIndex(([id]) => id === state.page);
    $("#steps").innerHTML = list.map(([, label], i) => `<li class="${i === cur ? "is-active" : i < cur ? "is-done" : ""}"><span class="n">${i < cur ? icon("check") : i + 1}</span>${label}</li>`).join("");
  }
  const go = (page) => { state.page = page; render(); };
  const move = (d) => { const list = flow().map(([id]) => id); go(list[Math.max(0, Math.min(list.length - 1, list.indexOf(state.page) + d))]); };
  const actions = (html) => { $("#actions").innerHTML = html; };

  function render() {
    const i = state.info;
    if (!i) return;
    document.documentElement.lang = state.lang;
    document.documentElement.dataset.accent = state.prefs.accent;
    $("#version").textContent = `v${i.version}`;
    steps();
    renderPage();
    translateDom(document.body);
  }

  const langSwitch = () => `<div class="lang-switch" aria-label="Language">
      <button data-a="lang-en" class="${state.lang === "en" ? "is-active" : ""}">English</button>
      <button data-a="lang-de" class="${state.lang === "de" ? "is-active" : ""}">Deutsch</button></div>`;
  const sw = (key, on, disabled = false) => `<label class="switch"><input type="checkbox" data-pref="${key}" ${on ? "checked" : ""} ${disabled ? "disabled" : ""}><span></span></label>`;
  const opt = (ic, title, desc, control, extra = "") => `<div class="opt ${extra}"><div class="opt__icon">${icon(ic)}</div><div class="opt__text"><div class="t">${title}</div>${desc ? `<div class="d">${desc}</div>` : ""}</div>${control}</div>`;
  const seg = (key, options, value) => `<div class="seg">${options.map(([v, l]) => `<button data-seg="${key}" data-value="${v}" class="${v === value ? "is-active" : ""}">${l}</button>`).join("")}</div>`;
  const ACCENTS = [["amber", "#ffb020"], ["copper", "#e07a3f"], ["ice", "#7cc4ff"], ["signal", "#e8e9eb"]];
  const CURRENCIES = ["EUR", "GBP", "USD", "CHF", "PLN", "CZK", "SEK", "NOK", "DKK", "HUF"];
  const next = (label = "Continue") => `<button class="btn btn--primary btn--lg" data-a="next">${label}${icon("arrow-right")}</button>`;

  function renderPage() {
    const i = state.info;
    const page = $("#page");
    page.style.animation = "none"; void page.offsetWidth; page.style.animation = "";

    if (state.error) {
      page.innerHTML = `<div class="done-mark done-mark--warn">${icon("triangle-alert")}</div>
        <div class="kicker">Something went wrong</div><h1 class="title">Setup failed</h1><p class="lead">${esc(state.error)}</p>`;
      actions(`<button class="btn btn--ghost" data-a="close">Close</button><span class="spacer"></span><button class="btn btn--primary" data-a="retry">Try again</button>`);
      return;
    }
    if (isUninstall()) return renderUninstall(page);
    const update = isUpdate();

    if (state.page === "welcome") {
      page.innerHTML = `<div class="topline"><div class="kicker">${update ? `Update · v${esc(i.existingVersion || "?")} → v${esc(i.version)}` : "Welcome"}</div>${langSwitch()}</div>
        <div class="hero"><div class="hero__mark"><img src="assets/brand/h-logo.png" alt=""></div>
          <div><h1 class="title">${update ? "Update HAULIX" : "Install HAULIX"}</h1>
            <p class="lead">${update ? "Your logbook, settings and backups are kept. Only the program files are replaced." : "Your free logbook and co-driver for Euro Truck Simulator 2."}</p>
            <div class="faint" style="font-size:12px;margin-top:6px">Everything stays on this PC · no account · no admin rights</div></div></div>
        <div class="features">
          <div class="feature">${icon("gauge")}<strong>Live telemetry</strong><span>Speed, fuel, damage and job progress while you drive.</span></div>
          <div class="feature">${icon("briefcase")}<strong>Current job & HUD</strong><span>Real-time ETA, costs and a job card over your game.</span></div>
          <div class="feature">${icon("book-open")}<strong>Automatic logbook</strong><span>Every delivery with income, fuel, damage and driving score.</span></div>
          <div class="feature">${icon("award")}<strong>Achievements</strong><span>More than 60 goals and a driver rank for your career.</span></div>
          <div class="feature">${icon("bell")}<strong>Sounds & voices</strong><span>Chimes and natural voices for notifications.</span></div>
          <div class="feature">${icon("warehouse")}<strong>Your company</strong><span>Trucks, trailers, garages and drivers from your save.</span></div>
        </div>
        ${update && licenseChanged(i.existingVersion) ? `<div class="callout callout--accent">${icon("info")}<div><strong>Updated license agreement.</strong> The license agreement and privacy policy have changed since your version. Please read them and accept them to continue.</div></div>` : ""}
        <label class="consent ${state.accepted ? "is-on" : ""}" style="margin-top:14px"><input class="box" type="checkbox" id="accept" ${state.accepted ? "checked" : ""}>
          <div><div class="t">I have read and accept the License Agreement and the Privacy Policy</div>
            <div class="d"><span>Free for personal use</span> · © 2026 RyanTMP · <a class="link" data-a="license">Read the license agreement</a> · <a class="link" data-a="privacy">Read the privacy policy</a></div></div></label>
        ${!i.hasPayload ? `<div class="callout callout--warn">${icon("triangle-alert")}<div><strong>Development build.</strong> This setup does not contain the app. Build it with tools/release/build-release.ps1.</div></div>` : ""}`;
      $("#accept").onchange = (e) => { state.accepted = e.target.checked; render(); };
      actions(`<button class="btn btn--ghost" data-a="close">Cancel</button><span class="spacer"></span>${next().replace('data-a="next"', `data-a="next" ${state.accepted && i.hasPayload ? "" : "disabled"}`)}`);
    } else if (state.page === "type") {
      page.innerHTML = `<div class="kicker">Setup type</div><h1 class="title">${update ? "How do you want to update?" : "How do you want to install?"}</h1>
        <div class="types">
          <button class="type ${state.type === "express" ? "is-on" : ""}" data-type="express"><span class="tag">Recommended</span>
            <div class="type__icon">${icon("zap")}</div><strong>Express</strong>
            <span>${update ? "Keep your settings and replace only the program files." : "Install with the recommended settings. You can change everything later in HAULIX."}</span>
            ${update ? "" : `<ul><li>Start menu and desktop shortcuts</li><li>ETS2 telemetry plugin</li><li>Notifications with sounds</li><li>Automatic backups and updates</li></ul>`}</button>
          <button class="type ${state.type === "custom" ? "is-on" : ""}" data-type="custom">
            <div class="type__icon">${icon("sliders-horizontal")}</div><strong>Custom</strong>
            <span>Choose the folder, shortcuts, startup, notifications, HUD and look yourself.</span>
            <ul><li>Install location, shortcuts and plugin</li><li>Startup, notifications, HUD and look</li></ul></button>
        </div>
        ${i.appRunning ? `<div class="callout callout--accent">${icon("info")}<div>HAULIX is running and will be closed during the ${update ? "update" : "installation"}.</div></div>` : ""}`;
      actions(`<button class="btn btn--ghost" data-a="back">Back</button><span class="spacer"></span>${next()}`);
    } else if (state.page === "options") {
      const p = state.prefs;
      const TABS = [["install", "Installation", "folder-open"], ["startup", "Startup", "power"], ["driving", "Driving", "truck"], ["look", "Look & units", "palette"]];
      let body = "";
      if (state.tab === "install") {
        body = `<h3 class="section">Install location</h3>
          <div class="path-box"><input class="input" id="dir" value="${esc(state.dir)}"><button class="btn" data-a="browse">${icon("folder-open")}Browse</button></div>
          <div class="faint" style="font-size:12px">HAULIX installs for your Windows user only. No administrator rights are needed.</div>
          <div class="facts"><span>Space required <b>${esc(i.sizeMb)} MB</b></span><span>Your data <b>${esc(i.dataDir)}</b></span><span>Requirements <b>Windows 10/11 · WebView2 ✓</b></span></div>
          <h3 class="section">Shortcuts & plugin</h3>
          ${opt("list", "Start menu shortcut", "Find HAULIX in the Start menu and Windows search.", sw("startMenu", state.startMenu))}
          ${opt("monitor", "Desktop shortcut", "An icon on your desktop.", sw("desktop", state.desktop))}
          ${i.pluginBundled ? opt("plug", "Install the ETS2 telemetry plugin",
            `${!i.ets2 ? "ETS2 was not found on this PC." : i.plugin ? "Already installed. Setup keeps it up to date." : "Needed for live data (scs-sdk-plugin 1.12.1, MIT). Copied into ETS2's plugins folder."}${i.ets2Running ? " Close ETS2 first, then restart it after setup." : ""}`,
            sw("plugin", state.plugin && i.ets2, !i.ets2), i.ets2 ? "" : "opt--disabled") : ""}`;
      } else if (state.tab === "startup") {
        body = `${opt("power", "Start with Windows", "HAULIX starts minimised when you sign in, so every drive is logged.", sw("launchWithWindows", p.launchWithWindows))}
          ${opt("minimize-2", "Start minimised", "Open HAULIX in the background.", sw("startMinimized", p.startMinimized))}
          ${opt("archive", "Keep running in the tray", "Closing the window keeps HAULIX logging in the background.", sw("minimizeToTray", p.minimizeToTray))}
          ${opt("refresh-cw", "Check for updates", "Ask GitHub for new HAULIX versions on start and every 6 hours.", sw("updateCheck", p.updateCheck))}
          ${opt("database", "Automatic backups", "A daily copy of your logbook and settings.", sw("autoBackup", p.autoBackup))}`;
      } else if (state.tab === "driving") {
        body = `${opt("gauge", "In-game HUD", "A glass job card over ETS2 with route, ETA and more.", sw("hud", p.hud))}
          ${opt("bell", "Job notifications", "Accepted, delivered, milestones, deadline and fuel warnings.", sw("notifications", p.notifications))}
          ${opt("layers", "Show notifications over the game", "Cards on top of ETS2 while HAULIX runs in the background.", sw("overlay", p.overlay))}
          ${opt("play", "Notification sounds", "A short chime with every notification.", sw("sounds", p.sounds))}
          ${opt("users", "Read notifications aloud", "Windows voice now; natural AI voices can be downloaded in HAULIX.", sw("voice", p.voice))}
          ${opt("clock", "TruckersMP AFK warning", "Warns you before the server's inactivity kick.", sw("afkWarning", p.afkWarning))}
          ${opt("route", "Record GPS traces", "Needed for the speed profile of each delivery.", sw("recordRoutes", p.recordRoutes))}
          ${opt("maximize-2", "Switch ETS2 to borderless fullscreen", "Needed so the HUD and notifications can appear over the game. A backup of config.cfg is kept.", sw("borderless", p.borderless, !i.ets2), i.ets2 ? "" : "opt--disabled")}`;
      } else {
        body = `${opt("moon", "Theme", "", seg("theme", [["dark", "Dark"], ["midnight", "Midnight"], ["light", "Light"]], p.theme))}
          ${opt("palette", "Accent colour", "", `<div class="swatches">${ACCENTS.map(([k, c]) => `<button data-accent="${k}" style="background:${c}" class="${p.accent === k ? "is-active" : ""}" aria-label="${k}"></button>`).join("")}</div>`)}
          ${opt("route", "Units", "", seg("units", [["metric", "Metric"], ["imperial", "Imperial"]], p.units))}
          ${opt("coins", "Currency", "ETS2 pays in euro; other currencies use approximate rates.", `<select class="select" id="currency">${CURRENCIES.map((c) => `<option ${c === p.currency ? "selected" : ""}>${c}</option>`).join("")}</select>`)}`;
      }
      page.innerHTML = `<div class="kicker">Options</div><h1 class="title">Choose your options</h1>
        <p class="lead">Everything here can be changed later in HAULIX → Settings.</p>
        <div class="tabs">${TABS.map(([id, l, ic]) => `<button data-tab="${id}" class="${state.tab === id ? "is-active" : ""}">${icon(ic)}${l}</button>`).join("")}</div>
        <div>${body}</div>`;
      const dir = $("#dir"); if (dir) dir.oninput = (e) => (state.dir = e.target.value);
      const cur = $("#currency"); if (cur) cur.onchange = (e) => (state.prefs.currency = e.target.value);
      actions(`<button class="btn btn--ghost" data-a="back">Back</button><span class="spacer"></span>${next()}`);
    } else if (state.page === "summary") {
      const p = state.prefs, custom = state.type === "custom";
      const item = (ic, text, on = true) => `<div class="${on ? "" : "off"}">${icon(on ? ic : "circle")}<span>${text}</span></div>`;
      page.innerHTML = `<div class="kicker">Summary</div><h1 class="title">${update ? "Ready to update" : "Ready to install"}</h1>
        <p class="lead">Check your choices and start.</p>
        <div class="facts" style="margin-top:16px"><span>${icon("folder-open")}<b class="mono">${esc(state.dir)}</b></span><span>Space required <b>${esc(i.sizeMb)} MB</b></span></div>
        <div class="summary">
          ${item("list", "Start menu shortcut", state.startMenu)}${item("monitor", "Desktop shortcut", state.desktop)}
          ${i.pluginBundled ? item("plug", "ETS2 telemetry plugin", state.plugin && i.ets2) : ""}
          ${custom ? `${item("power", "Start with Windows", p.launchWithWindows)}${item("gauge", "In-game HUD", p.hud)}${item("bell", "Job notifications", p.notifications)}
            ${item("play", "Notification sounds", p.sounds)}${item("users", "Read notifications aloud", p.voice)}${item("refresh-cw", "Check for updates", p.updateCheck)}
            ${item("database", "Automatic backups", p.autoBackup)}${item("maximize-2", "Switch ETS2 to borderless fullscreen", p.borderless)}
            ${item("palette", `${p.theme === "dark" ? "Dark" : p.theme === "midnight" ? "Midnight" : "Light"} · ${p.units === "metric" ? "Metric" : "Imperial"} · ${p.currency}`)}`
            : item("sparkles", update ? "Your settings stay as they are" : "Recommended settings")}
        </div>`;
      actions(`<button class="btn btn--ghost" data-a="back">Back</button><span class="spacer"></span><button class="btn btn--primary btn--lg" data-a="install">${icon("download")}${update ? "Update" : "Install"}</button>`);
    } else if (state.page === "progress") {
      page.innerHTML = `<div style="text-align:center"><div class="kicker">${update ? "Update" : "Install"}</div><h1 class="title">Setting up HAULIX</h1><p class="lead" style="margin:0 auto">This takes a few seconds.</p></div>
        <div class="ring" id="ring" style="--p:${Math.round(state.progress * 100)}"><b id="pct">${Math.round(state.progress * 100)}%</b></div>
        <div class="progress-msg" id="msg">Preparing</div><div class="progress-file" id="file"></div>`;
      actions(`<span class="spacer"></span><button class="btn" disabled>Please wait…</button>`);
    } else {
      page.innerHTML = `<div class="done-mark">${icon("check")}</div>
        <div class="kicker">Ready to drive</div><h1 class="title">${update ? "HAULIX is updated" : "HAULIX is installed"}</h1>
        <p class="lead">Start ETS2 and HAULIX switches to LIVE automatically.</p>
        ${i.ets2 && state.plugin && i.pluginBundled ? `<div class="callout callout--ok">${icon("circle-check")}<div><strong>Telemetry plugin installed.</strong> When you start ETS2, accept the "Advanced SDK features" prompt once.</div></div>` : ""}
        ${i.ets2 && !i.plugin && !(state.plugin && i.pluginBundled) ? `<div class="callout callout--accent">${icon("plug")}<div><strong>One more step for live telemetry:</strong> <a class="link" data-a="plugin">Download plugin</a></div></div>` : ""}
        ${!i.ets2 ? `<div class="callout">${icon("info")}<div>ETS2 wasn't found automatically. You can point HAULIX to it in the setup wizard.</div></div>` : ""}
        <div class="next">
          <div><span class="n">1</span><b>Start ETS2</b><span>HAULIX switches to LIVE as soon as the game runs.</span></div>
          <div><span class="n">2</span><b>Take a job</b><span>Everything about it appears on the Current job page and in the HUD.</span></div>
          <div><span class="n">3</span><b>Watch HAULIX work</b><span>Deliveries land in your logbook and unlock achievements.</span></div>
        </div>
        <div class="links"><button class="btn" data-a="site">${icon("globe")}Website</button><button class="btn" data-a="changelog">${icon("file-text")}What's new</button></div>
        <label class="consent ${state.launch ? "is-on" : ""}" style="margin-top:16px"><input class="box" type="checkbox" id="launch" ${state.launch ? "checked" : ""}><div><div class="t">Launch HAULIX now</div></div></label>`;
      $("#launch").onchange = (e) => { state.launch = e.target.checked; e.target.closest(".consent").classList.toggle("is-on", state.launch); };
      actions(`<span class="spacer"></span><button class="btn btn--primary btn--lg" data-a="finish">Finish</button>`);
    }
  }

  function renderUninstall(page) {
    const i = state.info;
    if (state.page === "confirm") {
      page.innerHTML = `<div class="topline"><div class="kicker">Uninstall</div>${langSwitch()}</div>
        <div class="hero"><div class="hero__mark">${icon("trash-2")}</div><div><h1 class="title">Remove HAULIX</h1>
          <p class="lead">The program, its shortcuts and its Windows entries will be removed from <span class="mono">${esc(i.installDir)}</span>.</p></div></div>
        <label class="consent ${state.removeData ? "is-on" : ""}"><input class="box" type="checkbox" id="removeData" ${state.removeData ? "checked" : ""}>
          <div><div class="t">Also delete my HAULIX data</div><div class="d">Logbook, settings and backups in ${esc(i.dataDir)}. Leave this unchecked to keep your history for a later reinstall.</div></div></label>`;
      $("#removeData").onchange = (e) => { state.removeData = e.target.checked; e.target.closest(".consent").classList.toggle("is-on", state.removeData); };
      actions(`<button class="btn btn--ghost" data-a="close">Cancel</button><span class="spacer"></span><button class="btn btn--danger btn--lg" data-a="uninstall">${icon("trash-2")}Uninstall</button>`);
    } else if (state.page === "progress") {
      page.innerHTML = `<div style="text-align:center"><div class="kicker">Removing</div><h1 class="title">Uninstalling</h1></div>
        <div class="ring" id="ring" style="--p:0"><b id="pct">0%</b></div><div class="progress-msg" id="msg">Preparing</div><div class="progress-file" id="file"></div>`;
      actions(`<span class="spacer"></span><button class="btn" disabled>Please wait…</button>`);
    } else {
      page.innerHTML = `<div class="done-mark">${icon("check")}</div><div class="kicker">Done</div><h1 class="title">HAULIX was removed</h1>
        <p class="lead">${state.removeData ? "Your HAULIX data was deleted too." : "Your data was kept. Reinstall any time to pick up where you left off."} Safe travels!</p>`;
      actions(`<span class="spacer"></span><button class="btn btn--primary btn--lg" data-a="close">Close</button>`);
    }
  }

  /* ---- License agreement & privacy policy viewer (texts are embedded in the setup) ---- */
  // Version that introduced the current license agreement (RyanTMP Software License Agreement 2.0). Updates from older
  // versions must accept it again. Raise it whenever LICENSE changes (see legal/README.md).
  const LICENSE_SINCE = [0, 0, 9];
  function licenseChanged(v) {
    const n = String(v || "0").split(/[.-]/).map((x) => parseInt(x, 10) || 0);
    for (let k = 0; k < 3; k++) if ((n[k] || 0) !== LICENSE_SINCE[k]) return (n[k] || 0) < LICENSE_SINCE[k];
    return false;
  }
  const docs = {};
  async function openDoc(which) {
    let view = $("#docView");
    if (!view) { view = document.createElement("div"); view.id = "docView"; view.className = "doc-view"; document.body.append(view); }
    view.innerHTML = `<div class="doc-view__panel" role="dialog" aria-modal="true">
        <div class="doc-view__head">
          <div class="doc-view__tabs"><button data-a="doc-license" class="${which === "license" ? "is-active" : ""}">License agreement</button><button data-a="doc-privacy" class="${which === "privacy" ? "is-active" : ""}">Privacy policy</button></div>
          <button class="doc-view__close" data-a="doc-close" aria-label="Close">${icon("x")}</button>
        </div>
        <pre class="doc-view__text">…</pre>
        <div class="doc-view__foot"><span class="faint">© 2026 RyanTMP · HAULIX</span><span class="spacer"></span><button class="btn btn--primary" data-a="doc-accept">Accept and close</button></div>
      </div>`;
    translateDom(view);
    const file = which === "privacy" ? "PRIVACY.txt" : "LICENSE.txt";
    try { docs[file] ??= await fetch(file).then((r) => (r.ok ? r.text() : Promise.reject(new Error(r.status)))); }
    catch { docs[file] = "The document could not be loaded. You can read it at https://github.com/RyanTMP/Haulix"; }
    view.querySelector(".doc-view__text").textContent = docs[file];
  }
  const closeDoc = () => $("#docView")?.remove();
  document.addEventListener("keydown", (e) => { if (e.key === "Escape") closeDoc(); });

  /* ---- Events ---- */
  document.addEventListener("change", (e) => {
    const k = e.target.dataset?.pref;
    if (!k) return;
    if (k === "desktop" || k === "startMenu" || k === "plugin") state[k] = e.target.checked;
    else state.prefs[k] = e.target.checked;
  });
  document.addEventListener("click", (e) => {
    const typeBtn = e.target.closest("[data-type]");
    if (typeBtn) { state.type = typeBtn.dataset.type; render(); return; }
    const tab = e.target.closest("[data-tab]");
    if (tab) { state.tab = tab.dataset.tab; render(); return; }
    const segBtn = e.target.closest("[data-seg]");
    if (segBtn) { state.prefs[segBtn.dataset.seg] = segBtn.dataset.value; render(); return; }
    const acc = e.target.closest("button[data-accent]");
    if (acc) { state.prefs.accent = acc.dataset.accent; render(); return; }
    const a = e.target.closest("[data-a]")?.dataset.a;
    if (!a || e.target.closest("[disabled]")) return;
    switch (a) {
      case "close": send({ cmd: "close" }); break;
      case "lang-de": case "lang-en": state.lang = a.slice(5); state.langChosen = true; render(); break;
      case "next": move(1); break;
      case "back": move(-1); break;
      case "browse": send({ cmd: "browse", current: state.dir }); break;
      case "license": case "doc-license": openDoc("license"); break;
      case "privacy": case "doc-privacy": openDoc("privacy"); break;
      case "doc-close": closeDoc(); break;
      case "doc-accept": closeDoc(); state.accepted = true; render(); break;
      case "plugin": send({ cmd: "openUrl", url: "https://github.com/RenCloud/scs-sdk-plugin/releases" }); break;
      case "site": send({ cmd: "openUrl", url: "https://www.haulix-logging.com" }); break;
      case "changelog": send({ cmd: "openUrl", url: "https://github.com/RyanTMP/Haulix/blob/main/CHANGELOG.md" }); break;
      case "install": {
        state.progress = 0;
        go("progress");
        const i = state.info;
        send({ cmd: "install", dir: state.dir, desktop: state.desktop, startMenu: state.startMenu, plugin: state.plugin && i.ets2 && i.pluginBundled,
          // Only an explicit choice is handed over; otherwise a reinstall would reset the language picked in HAULIX.
          language: state.langChosen ? state.lang : "",
          // Custom installs hand their choices to HAULIX (applied once on its next start).
          prefs: state.type === "custom" ? JSON.stringify(state.prefs) : "" });
        break;
      }
      case "uninstall": go("progress"); send({ cmd: "uninstall", removeData: state.removeData }); break;
      case "finish": if (state.launch) send({ cmd: "launch", dir: state.installedDir }); else send({ cmd: "close" }); break;
      case "retry": state.error = null; state.page = isUninstall() ? "confirm" : "summary"; render(); break;
    }
  });

  on("init", (info) => {
    const label = (v) => (v ? String(v).replace(/-([a-z]+)$/i, (_, l) => ` ${l.toUpperCase()}`) : v);
    state.info = { ...info, version: label(info.version), existingVersion: label(info.existingVersion) };
    state.dir = info.installDir;
    state.page = info.mode === "uninstall" ? "confirm" : "welcome";
    state.lang = (info.systemLanguage || navigator.language || "en").slice(0, 2).toLowerCase() === "de" ? "de" : "en";
    render();
  });
  on("browsed", (dir) => { state.dir = dir; const el = $("#dir"); if (el) el.value = dir; });
  on("progress", (p) => {
    state.progress = p.value;
    const ring = $("#ring"); if (!ring) return;
    ring.style.setProperty("--p", String(Math.round(p.value * 100)));
    $("#pct").textContent = `${Math.round(p.value * 100)}%`;
    // "Installing <file>" shows as a step plus the file name underneath.
    const m = String(p.message || "");
    const file = DE[m] === undefined && m.match(/^Installing (.+)$/);
    $("#msg").textContent = file ? tr("Copying program files") : tr(m);
    $("#file").textContent = file ? file[1] : "";
  });
  on("installed", (dir) => { state.installedDir = dir; go("done"); });
  on("uninstalled", () => go("done"));
  on("error", (msg) => { state.error = msg; render(); });

  // Icons (shared sprite), then start.
  fetch("assets/icons.svg").then((r) => r.text()).then((svg) => {
    const d = document.createElement("div"); d.style.display = "none"; d.innerHTML = svg; document.body.prepend(d);
  }).finally(() => send({ cmd: "init" }));

  // Browser preview without the native host (?mode=update|uninstall, ?lang=de).
  function mock(msg) {
    const q = new URLSearchParams(location.search);
    setTimeout(() => {
      if (msg.cmd === "init") emit("init", { mode: q.get("mode") || "install", version: "0.0.9-beta", existingVersion: "0.0.8-beta", installDir: "C:\\Users\\you\\AppData\\Local\\Programs\\HAULIX", dataDir: "C:\\Users\\you\\AppData\\Local\\Haulix", hasPayload: true, sizeMb: 48, appRunning: false, ets2: true, plugin: false, pluginBundled: true, ets2Running: false, systemLanguage: q.get("lang") || navigator.language });
      if (msg.cmd === "install" || msg.cmd === "uninstall") {
        let v = 0;
        const t = setInterval(() => {
          v += 0.06;
          emit("progress", { value: Math.min(1, v), message: `Installing ui/file-${Math.round(v * 240)}.js` });
          if (v >= 1) { clearInterval(t); emit(msg.cmd === "install" ? "installed" : "uninstalled", "C:\\Users\\you\\AppData\\Local\\Programs\\HAULIX"); }
        }, 120);
      }
    }, 50);
  }
})();
