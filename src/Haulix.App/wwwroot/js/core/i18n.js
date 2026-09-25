// HAULIX localisation. English is the source language; other languages are applied by translating
// rendered text (text nodes, placeholders, tooltips) through a dictionary plus patterns for dynamic text.
// A MutationObserver keeps everything that is rendered later (pages, toasts, drawers) translated too.

let lang = "en";
const cache = new Map();

export const LANGUAGES = [["en", "English"], ["de", "Deutsch"]];
export const language = () => lang;
export const locale = () => (lang === "de" ? "de-DE" : "en-GB");

/** Resolve the language from settings, installer choice and Windows. */
export function resolveLanguage(settingValue, languageChosen, systemLanguage) {
  const sys = (systemLanguage || navigator.language || "en").slice(0, 2).toLowerCase();
  const chosen = languageChosen && settingValue && settingValue !== "auto" ? settingValue : null;
  const l = chosen || sys;
  return DICTS[l] || l === "en" ? l : "en";
}

export function setLanguage(l) {
  lang = DICTS[l] ? l : "en";
  cache.clear();
  document.documentElement.lang = lang;
}

/** Translate one string (exact entry or pattern); returns the input when unknown. */
export function t(s) {
  if (lang === "en" || !s) return s;
  const key = s.trim();
  if (!key || key.length > 400) return s;
  let out = cache.get(key);
  if (out === undefined) {
    const d = DICTS[lang];
    out = d.exact[key] ?? null;
    if (out === null) {
      for (const [re, fn] of d.patterns) {
        const m = key.match(re);
        if (m) { out = typeof fn === "function" ? fn(m) : fn.replace(/\$(\d)/g, (_, i) => t(m[+i]) ?? m[+i]); break; }
      }
    }
    cache.set(key, out);
  }
  if (out === null) return s;
  return s.replace(key, out);
}

const ATTRS = ["placeholder", "data-tip", "title", "aria-label", "data-tip-rail", "alt"];
const SKIP = new Set(["SCRIPT", "STYLE", "svg", "SVG", "CANVAS", "use", "path"]);

function translateNode(node) {
  if (node.nodeType === 3) {
    const v = node.nodeValue;
    if (!v || !/[A-Za-z]/.test(v)) return;
    const tv = t(v);
    if (tv !== v) node.nodeValue = tv;
    return;
  }
  if (node.nodeType !== 1 || SKIP.has(node.nodeName) || node.closest?.(".leaflet-tile-container")) return;
  for (const a of ATTRS) {
    const v = node.getAttribute?.(a);
    if (v && /[A-Za-z]/.test(v)) { const tv = t(v); if (tv !== v) node.setAttribute(a, tv); }
  }
  for (const c of node.childNodes) translateNode(c);
}

let observer = null;
export function startAutoTranslate() {
  if (lang === "en" || observer) return;
  translateNode(document.body);
  document.title = t(document.title);
  observer = new MutationObserver((muts) => {
    for (const m of muts) {
      if (m.type === "characterData") translateNode(m.target);
      else if (m.type === "attributes") translateNode(m.target);
      else for (const n of m.addedNodes) translateNode(n);
    }
  });
  observer.observe(document.body, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ATTRS });
  new MutationObserver(() => { const tt = t(document.title); if (tt !== document.title) document.title = tt; })
    .observe(document.querySelector("title"), { childList: true });
}

/* ------------------------------------------------------------------ German */

const de = {
  exact: {
    // Navigation & shell
    "Operate": "Betrieb", "Fleet": "Flotte", "Insight": "Auswertung",
    "Dashboard": "Übersicht", "Telemetry": "Telemetrie", "Map": "Karte", "Logbook": "Fahrtenbuch", "Trucks": "Lkw",
    "Trailers": "Auflieger", "Garages": "Garagen", "Drivers": "Fahrer", "Statistics": "Statistik", "Profile": "Profil",
    "Settings": "Einstellungen", "Setup": "Einrichtung", "Design system": "Designsystem",
    "Search trucks, drivers, cities…": "Lkw, Fahrer, Städte suchen…", "Collapse sidebar": "Seitenleiste einklappen",
    "Expand sidebar": "Seitenleiste ausklappen", "HAULIX dashboard": "HAULIX Übersicht",
    "Running": "Läuft", "Not running": "Nicht gestartet", "Not found": "Nicht gefunden", "Live": "Live", "Demo": "Demo",
    "Paused": "Pausiert", "Waiting": "Wartet", "Stale": "Veraltet", "Unsupported": "Nicht unterstützt", "Offline": "Offline",
    "No plugin": "Kein Plugin", "Loaded": "Geladen", "Cached": "Zwischengespeichert", "Loading": "Lädt", "Error": "Fehler",
    "None": "Keins", "OK": "OK", "Database": "Datenbank", "Profile loaded.": "Profil geladen.",
    "LIVE": "LIVE", "DEMO": "DEMO", "PAUSED": "PAUSIERT", "WAITING": "WARTET", "OFFLINE": "OFFLINE",
    "telemetry": "Telemetrie", "simulated drive": "simulierte Fahrt", "game paused": "Spiel pausiert", "for telemetry": "auf Telemetrie",
    "no plugin": "kein Plugin", "ETS2 not running": "ETS2 läuft nicht", "No telemetry received yet": "Noch keine Telemetrie empfangen",
    "No profile": "Kein Profil", "Select in setup": "In der Einrichtung wählen", "Preview mode": "Vorschaumodus",
    "Running in a browser with sample data. Launch Haulix.exe to read your ETS2 data.": "Läuft im Browser mit Beispieldaten. Starte Haulix.exe, um deine ETS2-Daten zu lesen.",
    "HAULIX could not start": "HAULIX konnte nicht starten", "Retry": "Erneut versuchen",
    "scs-telemetry plugin": "scs-telemetry-Plugin", "scs-telemetry.dll not installed": "scs-telemetry.dll nicht installiert",
    "Deliveries, cargo and cities": "Lieferungen, Fracht und Städte", "local SQLite": "lokale SQLite",
    "New job started": "Neuer Auftrag gestartet", "Job cancelled": "Auftrag abgebrochen", "Fined": "Bußgeld",
    "Toll paid": "Maut bezahlt", "Ferry": "Fähre", "Train": "Zug", "Refuelled": "Getankt",
    "Road map ready": "Straßenkarte bereit", "Road map build failed": "Erstellen der Straßenkarte fehlgeschlagen",

    // Common
    "Cancel": "Abbrechen", "Continue": "Weiter", "Back": "Zurück", "Close": "Schließen", "Clear": "Leeren", "Confirm": "Bestätigen",
    "Save": "Speichern", "Saved": "Gespeichert", "Could not save": "Speichern fehlgeschlagen", "Dismiss": "Schließen",
    "Browse": "Durchsuchen", "Change": "Ändern", "Locate": "Suchen", "Restore": "Wiederherstellen", "Reset": "Zurücksetzen",
    "Export CSV": "CSV exportieren", "Open details": "Details öffnen", "Action failed": "Aktion fehlgeschlagen",
    "Request failed": "Anfrage fehlgeschlagen", "Unknown": "Unbekannt", "Unknown location": "Unbekannter Ort", "You": "Du",
    "Player": "Spieler", "Yes": "Ja", "No": "Nein", "Off": "Aus", "Empty": "Leer", "Select": "Auswählen", "Status": "Status",
    "Source": "Quelle", "Type": "Typ", "Model": "Modell", "Brand": "Marke", "Power": "Leistung", "Length": "Länge", "Volume": "Volumen",
    "Configuration": "Konfiguration", "Plate": "Kennzeichen", "License plate": "Kennzeichen", "Company": "Firma", "City": "Stadt",
    "Cities": "Städte", "Companies": "Firmen", "Country": "Land", "Destination": "Ziel", "Origin": "Start", "Distance": "Strecke",
    "Income": "Einnahmen", "Revenue": "Umsatz", "Profit": "Gewinn", "Expenses": "Ausgaben", "Earnings": "Einnahmen", "Wages": "Löhne",
    "Cargo": "Fracht", "Cargo mass": "Frachtgewicht", "Cargo damage": "Frachtschaden", "Truck": "Lkw", "Trailer": "Auflieger",
    "Driver": "Fahrer", "Garage": "Garage", "Damage": "Schaden", "Fuel": "Kraftstoff", "Speed": "Geschwindigkeit", "Engine": "Motor",
    "Gear": "Gang", "Gears": "Gänge", "Range": "Reichweite", "Mileage": "Laufleistung", "Odometer": "Kilometerstand",
    "Jobs": "Aufträge", "Deliveries": "Lieferungen", "Delivered": "Geliefert", "Cancelled": "Abgebrochen", "Date": "Datum",
    "Route": "Route", "Status ": "Status", "Available": "Verfügbar", "Driving": "Fährt", "On job": "Im Auftrag", "Resting": "Pause",
    "Assigned": "Zugewiesen", "Idle": "Frei", "Your truck": "Dein Lkw", "Attached": "Angehängt", "In use": "In Benutzung",
    "Parked": "Abgestellt", "No driver": "Kein Fahrer", "No garage": "Keine Garage", "No truck": "Kein Lkw",
    "No truck assigned": "Kein Lkw zugewiesen", "Unassigned": "Nicht zugewiesen", "Engine unknown": "Motor unbekannt",
    "Headquarters": "Hauptsitz", "HQ": "HQ", "Small": "Klein", "Medium": "Mittel", "Large": "Groß", "Free roam": "Freie Fahrt",
    "Cards": "Karten", "Table": "Tabelle", "Level": "Stufe", "Skills": "Fähigkeiten", "Visited": "Besucht",

    // Dashboard
    "Current drive": "Aktuelle Fahrt", "Live telemetry": "Live-Telemetrie", "Current route": "Aktuelle Route",
    "Open map": "Karte öffnen", "Finances": "Finanzen", "Last 7 days": "Letzte 7 Tage", "Recent deliveries": "Letzte Lieferungen",
    "View logbook": "Zum Fahrtenbuch", "Recent activity": "Letzte Aktivität", "No data yet": "Noch keine Daten",
    "Waiting for telemetry": "Warte auf Telemetrie", "ETS2 is not running": "ETS2 läuft nicht",
    "HAULIX is connected to the game and waiting for the first telemetry frame.": "HAULIX ist mit dem Spiel verbunden und wartet auf die ersten Telemetriedaten.",
    "Install the scs-telemetry plugin to see live data. Setup explains how.": "Installiere das scs-telemetry-Plugin, um Live-Daten zu sehen. Die Einrichtung erklärt, wie.",
    "Start Euro Truck Simulator 2 and HAULIX will switch to LIVE automatically. Your history, fleet and statistics stay available offline.": "Starte Euro Truck Simulator 2 und HAULIX schaltet automatisch auf LIVE. Verlauf, Flotte und Statistik bleiben offline verfügbar.",
    "Open logbook": "Fahrtenbuch öffnen", "ETS2 settings": "ETS2-Einstellungen", "No active job · roaming": "Kein Auftrag · freie Fahrt",
    "Take a job from the freight market or your company to see route progress, ETA and income here. HAULIX keeps recording your free-roam distance.": "Nimm einen Auftrag vom Frachtmarkt oder deiner Firma an, um hier Fortschritt, Ankunft und Einnahmen zu sehen. HAULIX zeichnet auch deine freie Fahrt auf.",
    "Game ETA": "Ankunft (Spielzeit)", "Location": "Standort", "Fuel range": "Reichweite", "Heading": "Richtung", "Game time": "Spielzeit",
    "Speed limit": "Tempolimit", "Cruise off": "Tempomat aus", "Company balance": "Kontostand", "Recorded deliveries": "Erfasste Lieferungen",
    "Tolls, fines, ferries": "Maut, Bußgelder, Fähren", "No outstanding loans": "Keine offenen Kredite", "From your latest save": "Aus deinem letzten Spielstand",
    "Balance history from saved games": "Kontoverlauf aus Spielständen",
    "Tolls, fines, ferries, trains and cancellation penalties recorded by telemetry": "Maut, Bußgelder, Fähren, Züge und Stornogebühren aus der Telemetrie",
    "No deliveries yet": "Noch keine Lieferungen",
    "Completed jobs appear here automatically. Your in-game delivery history is imported from the save.": "Abgeschlossene Aufträge erscheinen hier automatisch. Dein Lieferverlauf aus dem Spiel wird aus dem Spielstand importiert.",
    "No profile loaded": "Kein Profil geladen", "Select your ETS2 profile to see your fleet.": "Wähle dein ETS2-Profil, um deine Flotte zu sehen.",
    "Run setup": "Einrichtung starten", "AI drivers": "KI-Fahrer", "Fleet utilisation": "Flottenauslastung", "Quiet so far": "Bisher ruhig",
    "Deliveries, tolls, fines, ferries and refuels appear here as they happen.": "Lieferungen, Maut, Bußgelder, Fähren und Tankstopps erscheinen hier, sobald sie passieren.",

    // Telemetry
    "Live from ETS2": "Live aus ETS2", "Simulated drive (demo)": "Simulierte Fahrt (Demo)", "Last known values": "Zuletzt bekannte Werte",
    "Speed & engine": "Geschwindigkeit & Motor", "Last 5 minutes": "Letzte 5 Minuten", "Engine · rpm": "Motor · U/min", "Current job": "Aktueller Auftrag",
    "Vehicle": "Fahrzeug", "Transmission": "Getriebe", "Retarder": "Retarder", "Cruise control": "Tempomat", "Fluids & gauges": "Betriebsstoffe & Anzeigen",
    "Average consumption": "Durchschnittsverbrauch", "AdBlue": "AdBlue", "Air pressure": "Luftdruck", "Oil": "Öl", "Coolant": "Kühlmittel",
    "Brakes": "Bremsen", "Battery": "Batterie", "Location & navigation": "Standort & Navigation", "Route remaining": "Verbleibende Strecke",
    "Navigation estimate in in-game time": "Navigationsschätzung in Spielzeit", "Next rest stop": "Nächste Pause", "World position": "Weltposition",
    "Body type": "Aufbau", "Trailer damage": "Aufliegerschaden", "Session": "Sitzung", "Plugin": "Plugin", "Distance this job": "Strecke dieser Auftrag",
    "Fuel used this job": "Verbrauch dieser Auftrag", "Average / top speed": "Durchschnitt / Höchstgeschwindigkeit", "Last update": "Letzte Aktualisierung",
    "Controls & lights": "Bedienung & Licht", "Driver inputs": "Fahrereingaben", "Throttle": "Gas", "Brake": "Bremse", "Clutch": "Kupplung",
    "Electrics": "Elektrik", "Parking brake": "Feststellbremse", "Engine brake": "Motorbremse", "Low beam": "Abblendlicht", "High beam": "Fernlicht",
    "Beacons": "Rundumleuchten", "Hazards": "Warnblinker", "Left blinker": "Blinker links", "Right blinker": "Blinker rechts", "Wipers": "Scheibenwischer",
    "Diff lock": "Differenzialsperre", "Lift axle": "Liftachse", "Low fuel": "Tank fast leer", "Cabin": "Kabine", "Chassis": "Fahrgestell",
    "Wheels": "Räder", "Body": "Aufbau", "No active job": "Kein aktiver Auftrag",
    "Accept a job in ETS2 to track cargo, route progress and deadline.": "Nimm in ETS2 einen Auftrag an, um Fracht, Fortschritt und Frist zu verfolgen.",
    "Planned distance": "Geplante Strecke", "Remaining": "Verbleibend", "Deadline": "Frist", "Not attached": "Nicht angehängt",
    "ETS2 is offline": "ETS2 ist offline", "Unsupported telemetry plugin": "Telemetrie-Plugin nicht unterstützt",
    "Start the game to see live telemetry.": "Starte das Spiel, um Live-Telemetrie zu sehen.",

    // Map
    "Follow truck": "Lkw folgen", "Show route": "Route zeigen", "Fit all": "Alles zeigen", "Refresh": "Aktualisieren",
    "Layers": "Ebenen", "Current truck": "Aktueller Lkw", "Navigation route": "Navigationsroute", "Driven this trip": "Gefahren (diese Fahrt)",
    "Previous routes": "Frühere Routen", "Streets": "Straßen", "Legend": "Legende", "Job / navigation route": "Auftrags- / Navigationsroute",
    "Streets you have driven": "Von dir befahrene Straßen", "Other streets": "Übrige Straßen", "City names": "Städtenamen", "Country names": "Ländernamen", "Countries & borders": "Länder & Grenzen", "Your garages": "Deine Garagen",
    "Fleet trucks": "Flotten-Lkw", "Fuel stations": "Tankstellen", "Service shops": "Werkstätten", "Truck dealers": "Lkw-Händler",
    "Recruitment agencies": "Personalagenturen", "Parking": "Parkplätze", "Ferries & trains": "Fähren & Züge",
    "Tolls, fines & ferries paid": "Bezahlte Maut, Bußgelder & Fähren", "Points of interest appear when zoomed in.": "Orte werden beim Hineinzoomen angezeigt.",
    "Route history": "Routenverlauf", "Distance recorded": "Erfasste Strecke", "Last 30 days": "Letzte 30 Tage", "Last 90 days": "Letzte 90 Tage",
    "Last year": "Letztes Jahr", "Everything": "Alles", "Road map": "Straßenkarte", "Build road map": "Straßenkarte erstellen",
    "Navigation": "Navigation", "Job": "Auftrag", "Manual": "Manuell", "Reroute": "Neu berechnen", "Back to job route": "Zurück zur Auftragsroute",
    "Arrival (real time)": "Ankunft (Echtzeit)", "Estimated from the HAULIX route and your average speed": "Geschätzt aus der HAULIX-Route und deinem Durchschnittstempo",
    "From the in-game navigation, converted to real time": "Aus dem Spiel-Navi, in Echtzeit umgerechnet", "No route to estimate yet": "Noch keine Route für eine Schätzung",
    "How long you still drive in real minutes (game time runs about 19× faster)": "So lange fährst du noch in echten Minuten (die Spielzeit läuft etwa 19× schneller)",
    "Notifications": "Benachrichtigungen", "Job notifications": "Auftrags-Benachrichtigungen", "Job accepted, delivered or cancelled, and fines.": "Auftrag angenommen, abgeliefert oder abgebrochen sowie Bußgelder.",
    "Progress": "Fortschritt", "Milestones (100, 50, 10 and 2 km left) and halfway, with the real-time arrival.": "Etappen (noch 100, 50, 10 und 2 km) und Halbzeit, mit der Ankunft in Echtzeit.",
    "Warnings": "Warnungen", "Deadline at risk, fuel range too short, new cargo damage and rest needed.": "Frist in Gefahr, Reichweite zu kurz, neuer Frachtschaden und Pause nötig.",
    "Show over the game": "Über dem Spiel anzeigen", "Small HAULIX cards on top of ETS2 while HAULIX runs in the background. Needs ETS2 in windowed or borderless fullscreen mode; exclusive fullscreen hides every overlay.": "Kleine HAULIX-Karten über ETS2, während HAULIX im Hintergrund läuft. ETS2 muss im Fenster- oder randlosen Vollbildmodus laufen; exklusives Vollbild verdeckt jedes Overlay.",
    "Position": "Position", "Screen corner of the monitor ETS2 runs on.": "Bildschirmecke auf dem Monitor, auf dem ETS2 läuft.", "Top right": "Oben rechts", "Top left": "Oben links", "Bottom right": "Unten rechts", "Bottom left": "Unten links",
    "Test": "Test", "Shows a sample notification over all windows.": "Zeigt eine Beispiel-Benachrichtigung über allen Fenstern.", "Send test": "Test senden", "Could not show notification": "Benachrichtigung konnte nicht angezeigt werden",
    "VTC": "VTC", "App": "App",
    "The HAULIX online service is being prepared.": "Der HAULIX-Onlinedienst wird vorbereitet.",
    "Accounts, VTCs, job board, events, leaderboards, live map and cloud sync are built into HAULIX already, but there is no server yet – nothing is sent from this PC.": "Konten, VTCs, Auftragsbörse, Events, Bestenlisten, Live-Karte und Cloud-Sync sind in HAULIX schon eingebaut, aber es gibt noch keinen Server – von diesem PC wird nichts gesendet.",
    "Status": "Status", "Not available yet": "Noch nicht verfügbar", "Developer preview": "Entwickler-Vorschau",
    "Shows the VTC and Online pages with local sample data (no network), to try the screens before the service starts.": "Zeigt die VTC- und Online-Seiten mit lokalen Beispieldaten (ohne Netzwerk), um sie vor dem Start des Dienstes auszuprobieren.",
    "Developer preview on": "Entwickler-Vorschau an", "Developer preview off": "Entwickler-Vorschau aus", "The VTC and Online pages now show sample data.": "Die VTC- und Online-Seiten zeigen jetzt Beispieldaten.",
    "Developer preview with sample data.": "Entwickler-Vorschau mit Beispieldaten.",
    "The HAULIX online service is not running yet – nothing here is real and nothing is sent. Turn it off in Settings → Online.": "Der HAULIX-Onlinedienst läuft noch nicht – nichts hier ist echt und nichts wird gesendet. Ausschalten unter Einstellungen → Online.",
    "Recruiting": "Sucht Fahrer", "Closed": "Geschlossen", "Request to join": "Beitritt anfragen", "Member": "Mitglied", "Role": "Rolle", "Avg. score": "Ø Score",
    "Owner": "Inhaber", "Manager": "Manager", "Driver": "Fahrer", "Trainee": "Azubi", "Online": "Online", "Offline": "Offline", "Reward": "Belohnung", "Open": "Offen",
    "Rank": "Platz", "This week": "Diese Woche", "attending": "Teilnehmer", "members": "Mitglieder", "Destination": "Ziel",
    "Voice": "Stimme", "Speaks notifications – audible even in exclusive fullscreen and on a single monitor.": "Liest Benachrichtigungen vor – hörbar auch im exklusiven Vollbild und mit nur einem Bildschirm.",
    "Voice engine": "Sprachausgabe", "Natural AI voices sound human and run offline on your PC (download once). Windows voices need no download.": "Natürliche KI-Stimmen klingen menschlich und laufen offline auf deinem PC (einmaliger Download). Windows-Stimmen brauchen keinen Download.",
    "Natural AI voice": "Natürliche KI-Stimme", "Windows voice": "Windows-Stimme", "Automatic (UI language)": "Automatisch (Oberflächensprache)",
    "Voices from the open-source Piper project, stored in %LOCALAPPDATA%\\Haulix\\voices – not in the program folder.": "Stimmen aus dem Open-Source-Projekt Piper, gespeichert in %LOCALAPPDATA%\\Haulix\\voices – nicht im Programmordner.",
    "Male voice": "Männliche Stimme", "Female voice": "Weibliche Stimme", "In use": "Aktiv", "Use": "Verwenden", "Remove": "Entfernen",
    "Plays a sample announcement with the chosen voice.": "Spielt eine Beispielansage mit der gewählten Stimme ab.", "Play sample": "Beispiel abspielen", "Voice download failed": "Stimmen-Download fehlgeschlagen",
    "Widgets over ETS2 while you drive, like VTC trackers. Needs borderless fullscreen or window mode (see Notifications → ETS2 display mode).": "Widgets über ETS2 während der Fahrt, wie bei VTC-Trackern. Braucht randloses Vollbild oder Fenstermodus (siehe Benachrichtigungen → ETS2-Anzeigemodus).",
    "How opaque the widgets are over the game.": "Wie deckend die Widgets über dem Spiel sind.", "Job card": "Auftragskarte", "Show the job card": "Auftragskarte anzeigen",
    "Cargo, route, progress and the rows you pick below.": "Fracht, Route, Fortschritt und die Zeilen, die du unten auswählst.", "Rows": "Zeilen", "What the card lists under the route.": "Was die Karte unter der Route auflistet.",
    "Mini map": "Minikarte", "Show the mini map": "Minikarte anzeigen", "The roads around your truck with the route and a remaining-distance footer.": "Die Straßen um deinen Lkw mit Route und Restdistanz am unteren Rand.",
    "Turn with the truck": "Mit dem Lkw drehen", "Driving direction always points up (off: north up).": "Fahrtrichtung zeigt immer nach oben (aus: Norden oben).", "Zoom": "Zoom", "Near": "Nah", "Far": "Weit",
    "0 % = left/top edge, 100 % = right/bottom edge.": "0 % = linker/oberer Rand, 100 % = rechter/unterer Rand.",
    "Shows both widgets over all windows for 10 seconds (with sample values when you are not driving).": "Zeigt beide Widgets 10 Sekunden über allen Fenstern (mit Beispielwerten, wenn du gerade nicht fährst).",
    "CURRENT JOB": "AKTUELLER AUFTRAG", "Damage": "Schaden", "Arrival": "Ankunft", "Remaining": "Verbleibend", "HAULIX needs no account and no server. All data stays on this PC; only the optional update check asks GitHub for the latest version.": "HAULIX braucht kein Konto und keinen Server. Alle Daten bleiben auf diesem PC; nur die optionale Update-Prüfung fragt GitHub nach der neuesten Version.",
    "Check for updates": "Nach Updates suchen", "HAULIX asks the public HAULIX releases on GitHub for a new version on start and every 6 hours – no account, no own server. Only the version number is requested.": "HAULIX fragt beim Start und alle 6 Stunden die öffentlichen HAULIX-Releases auf GitHub nach einer neuen Version – ohne Konto und ohne eigenen Server. Abgefragt wird nur die Versionsnummer.",
    "Custom update source": "Eigene Update-Quelle", "Advanced: URL of your own update manifest (JSON with version, url, notes). Leave empty to use GitHub.": "Für Fortgeschrittene: URL einer eigenen Update-Datei (JSON mit version, url, notes). Leer lassen, um GitHub zu nutzen.",
    "Later": "Später", "Release page": "Release-Seite", "Install now": "Jetzt installieren", "Starting setup…": "Setup wird gestartet…", "Update failed": "Update fehlgeschlagen",
    "No update source is configured in this build.": "In diesem Build ist keine Update-Quelle eingerichtet.", "Show update": "Update anzeigen",
    "In-game HUD": "HUD im Spiel", "Show the HUD": "HUD anzeigen", "A bar of live values over ETS2 while you drive. Needs borderless fullscreen or window mode (see Notifications → ETS2 display mode).": "Eine Leiste mit Live-Werten über ETS2 während der Fahrt. Braucht randloses Vollbild oder Fenstermodus (siehe Benachrichtigungen → ETS2-Anzeigemodus).",
    "Only during a job": "Nur während eines Auftrags", "Hide the HUD in free roam.": "HUD bei freier Fahrt ausblenden.", "Information": "Informationen",
    "Pick what the HUD shows. The speed limit sign is always on the left.": "Wähle, was das HUD anzeigt. Das Tempolimit-Schild steht immer links.",
    "Speed limit": "Tempolimit", "Speed": "Geschwindigkeit", "Remaining distance": "Restdistanz", "Real-time ETA": "Echtzeit-ETA", "Arrival time": "Ankunftszeit", "Game ETA": "Spiel-ETA",
    "Deadline buffer": "Fristpuffer", "Next rest": "Nächste Pause", "Truck wear": "Lkw-Verschleiß",
    "Where on the game screen the HUD sits.": "Wo das HUD auf dem Spielbildschirm sitzt.", "Top centre": "Oben mittig", "Bottom centre": "Unten mittig", "Custom": "Eigene",
    "Horizontal": "Horizontal", "Vertical": "Vertikal", "0 % = left edge, 100 % = right edge.": "0 % = linker Rand, 100 % = rechter Rand.", "0 % = top edge, 100 % = bottom edge.": "0 % = oberer Rand, 100 % = unterer Rand.",
    "Visibility": "Sichtbarkeit", "How opaque the HUD is over the game.": "Wie deckend das HUD über dem Spiel ist.", "Size": "Größe", "Small": "Klein", "Medium": "Mittel", "Large": "Groß",
    "Check position": "Position prüfen", "Shows the HUD over all windows for 10 seconds (with sample values when you are not driving).": "Zeigt das HUD 10 Sekunden über allen Fenstern (mit Beispielwerten, wenn du gerade nicht fährst).",
    "Show on screen": "Auf dem Bildschirm zeigen", "HUD shown for 10 seconds": "HUD wird 10 Sekunden angezeigt", "Check the position on your game monitor.": "Prüfe die Position auf deinem Spielmonitor.",
    "REMAINING": "VERBLEIBEND", "REAL-TIME ETA": "ECHTZEIT-ETA", "ARRIVAL": "ANKUNFT", "GAME ETA": "SPIEL-ETA", "BUFFER": "PUFFER", "FUEL RANGE": "REICHWEITE", "NEXT REST": "NÄCHSTE PAUSE", "TRUCK WEAR": "LKW-VERSCHLEISS", "GAME TIME": "SPIELZEIT",
    "Shows your current drive on your Discord profile (Rich Presence), e.g. cargo, route and arrival. Not available in this HAULIX version yet.": "Zeigt deine aktuelle Fahrt in deinem Discord-Profil (Rich Presence), z. B. Fracht, Route und Ankunft. In dieser HAULIX-Version noch nicht verfügbar.",
    "What's new": "Neuigkeiten", "Everything added since version 0.0.2.": "Alles, was seit Version 0.0.2 dazugekommen ist.", "Open changelog": "Änderungen anzeigen",
    "Achievements": "Erfolge", "My VTC": "Meine VTC", "Events & convoys": "Events & Konvois", "Job board": "Auftragsbörse", "Leaderboards": "Bestenlisten",
    "Online": "Online", "Live map": "Live-Karte", "Cloud sync": "Cloud-Sync",
    "Your virtual trucking company": "Deine virtuelle Spedition", "Drive together": "Gemeinsam fahren", "Jobs posted by your VTC": "Aufträge deiner VTC", "Rankings": "Ranglisten",
    "Friends and colleagues on the map": "Freunde und Kollegen auf der Karte", "Your logbook on every PC": "Dein Fahrtenbuch auf jedem PC",
    "Your company's home in HAULIX: members and roles, company statistics built from everyone's logbooks, and a shared company bank.": "Das Zuhause deiner Spedition in HAULIX: Mitglieder und Rollen, Firmenstatistiken aus den Fahrtenbüchern aller Fahrer und eine gemeinsame Firmenkasse.",
    "Members, roles and invitations": "Mitglieder, Rollen und Einladungen", "Company kilometres, income and deliveries": "Firmen-Kilometer, Einnahmen und Lieferungen", "Company bank and payouts": "Firmenkasse und Auszahlungen",
    "Plan and join convoys: date and time, meeting point, route on the HAULIX map and the server to meet on – with reminders shortly before the start.": "Konvois planen und mitfahren: Datum und Uhrzeit, Treffpunkt, Route auf der HAULIX-Karte und der Server – mit Erinnerung kurz vor dem Start.",
    "Event calendar for your VTC and public convoys": "Eventkalender für deine VTC und öffentliche Konvois", "Meeting point and route on the map": "Treffpunkt und Route auf der Karte", "Reminders over the game before the start": "Erinnerungen über dem Spiel vor dem Start",
    "Your VTC posts jobs – cargo, route and reward – and members take them. HAULIX checks the delivery automatically from telemetry.": "Deine VTC stellt Aufträge ein – Fracht, Route und Belohnung – und Mitglieder übernehmen sie. HAULIX prüft die Lieferung automatisch über die Telemetrie.",
    "Company jobs with cargo, route and reward": "Firmenaufträge mit Fracht, Route und Belohnung", "Automatic proof of delivery": "Automatischer Liefernachweis", "Rewards credited to the company bank": "Belohnungen landen in der Firmenkasse",
    "Rankings within your VTC and across all HAULIX drivers: kilometres, deliveries, income and driving score – weekly, monthly and all time.": "Ranglisten in deiner VTC und über alle HAULIX-Fahrer: Kilometer, Lieferungen, Einnahmen und Fahrscore – wöchentlich, monatlich und gesamt.",
    "Weekly, monthly and all-time rankings": "Wochen-, Monats- und Gesamtranglisten", "Driving score as a fair ranking": "Fahrscore als faire Wertung", "Your VTC against other companies": "Deine VTC gegen andere Speditionen",
    "See your friends and VTC colleagues live on the HAULIX map – where they drive, what they haul and when they arrive.": "Sieh deine Freunde und VTC-Kollegen live auf der HAULIX-Karte – wo sie fahren, was sie laden und wann sie ankommen.",
    "Live positions of friends and your VTC": "Live-Positionen von Freunden und deiner VTC", "Their current job and arrival time": "Ihr aktueller Auftrag und ihre Ankunftszeit", "Navigate to a friend with one click": "Mit einem Klick zu einem Freund navigieren",
    "Optionally back up your logbook, statistics and achievements to your HAULIX account and use them on several PCs. Offline stays the default.": "Sichere Fahrtenbuch, Statistiken und Erfolge optional in deinem HAULIX-Konto und nutze sie auf mehreren PCs. Offline bleibt der Standard.",
    "Automatic backup of your logbook": "Automatische Sicherung deines Fahrtenbuchs", "Same data on every PC": "Dieselben Daten auf jedem PC", "Opt-in – nothing leaves your PC without your consent": "Freiwillig – nichts verlässt deinen PC ohne deine Zustimmung",
    "Milestones of your trucking career · stored locally": "Meilensteine deiner Trucker-Karriere · lokal gespeichert", "Unlocked": "Freigeschaltet", "Save share card": "Teilen-Karte speichern",
    "Achievements unavailable": "Erfolge nicht verfügbar", "BRONZE": "BRONZE", "SILVER": "SILBER", "GOLD": "GOLD", "bronze": "Bronze", "silver": "Silber", "gold": "Gold",
    "Score": "Score", "Driving score 0–100: speeding, damage, fines and punctuality": "Fahrscore 0–100: Geschwindigkeit, Schäden, Bußgelder und Pünktlichkeit",
    "Copy image": "Bild kopieren", "Share card saved": "Teilen-Karte gespeichert", "Could not save image": "Bild konnte nicht gespeichert werden", "Image copied": "Bild kopiert",
    "Paste it into Discord with Ctrl+V.": "Mit Strg+V in Discord einfügen.", "Could not copy image": "Bild konnte nicht kopiert werden",
    "Driving score": "Fahrscore", "Speeding": "Zu schnell", "Cargo damage": "Frachtschaden", "Truck damage": "Lkw-Schaden", "Fines": "Bußgelder", "Late delivery": "Verspätet",
    "yes": "ja", "no": "nein", "Delivered": "Abgeliefert", "Cancelled": "Abgebrochen", "Penalty": "Strafe", "Logged with HAULIX · ETS2 Logger": "Aufgezeichnet mit HAULIX · ETS2 Logger",
    "Discord status": "Discord-Status", "Shows your current drive on your Discord profile (Rich Presence), e.g. cargo, route and arrival. Discord must be running on this PC.": "Zeigt deine aktuelle Fahrt in deinem Discord-Profil (Rich Presence), z. B. Fracht, Route und Ankunft. Discord muss auf diesem PC laufen.",
    "Discord application ID": "Discord-Anwendungs-ID", "Discord Developer Portal": "Discord Developer Portal",
    "In-game HUD": "HUD im Spiel", "A slim bar at the top of the game with the speed limit, remaining distance and real-time arrival. Needs borderless fullscreen or window mode.": "Eine schmale Leiste oben im Spiel mit Tempolimit, Restdistanz und Ankunft in Echtzeit. Braucht randloses Vollbild oder Fenstermodus.",
    "American Truck Simulator": "American Truck Simulator", "Support for ATS (map, profiles and telemetry) is planned.": "Unterstützung für ATS (Karte, Profile und Telemetrie) ist geplant.",
    "Update check": "Update-Prüfung", "Optional: URL of an update manifest (JSON with version, url, notes), e.g. a raw file on GitHub. Leave empty to stay fully offline.": "Optional: URL einer Update-Datei (JSON mit version, url, notes), z. B. eine Raw-Datei auf GitHub. Leer lassen, um komplett offline zu bleiben.",
    "Check now": "Jetzt prüfen", "Enter an update URL first.": "Gib zuerst eine Update-URL ein.", "Download": "Herunterladen", "Open Settings → About to download it.": "Öffne Einstellungen → Über, um es herunterzuladen.",
    "Against the TruckersMP rules – use at your own risk.": "Verstößt gegen die TruckersMP-Regeln – Nutzung auf eigenes Risiko.",
    "The TruckersMP rules list \"bypassing the server-sided auto kick system\" as a violation that can lead to bans up to a permanent ban. An automatic anti-AFK message does exactly that. HAULIX is not responsible for bans. The official way to stay longer is the TruckersMP Patreon tier \"Master Trucker\".": "Die TruckersMP-Regeln werten das Umgehen des automatischen Server-Kicks („bypassing the server-sided auto kick system“) als Verstoß, der bis zum permanenten Bann führen kann. Eine automatische Anti-AFK-Nachricht tut genau das. HAULIX übernimmt keine Verantwortung für Banns. Offiziell länger online bleiben kannst du mit dem TruckersMP-Patreon-Rang „Master Trucker“.",
    "Anti-AFK message": "Anti-AFK-Nachricht",
    "While you are inactive on TruckersMP, HAULIX opens the chat, types your message and sends it once per interval. Only works while ETS2 is the active window.": "Solange du auf TruckersMP inaktiv bist, öffnet HAULIX den Chat, tippt deine Nachricht und sendet sie einmal pro Intervall. Funktioniert nur, solange ETS2 das aktive Fenster ist.",
    "Message": "Nachricht", "Sent in the TruckersMP chat (max. 120 characters).": "Wird im TruckersMP-Chat gesendet (max. 120 Zeichen).",
    "Interval": "Intervall", "Minutes of inactivity between messages. On full servers TruckersMP kicks after 10 minutes.": "Minuten Inaktivität zwischen den Nachrichten. Auf vollen Servern kickt TruckersMP nach 10 Minuten.",
    "Chat key": "Chat-Taste", "The key that opens the TruckersMP chat in your controls (default Y).": "Die Taste, die in deiner Steuerung den TruckersMP-Chat öffnet (Standard Y).",
    "Enable anti-AFK message?": "Anti-AFK-Nachricht aktivieren?", "I accept the risk": "Ich trage das Risiko",
    "Automatically avoiding the TruckersMP inactivity kick is against the TruckersMP rules and can get your account banned, up to permanently. You use this feature at your own risk.": "Den TruckersMP-Inaktivitäts-Kick automatisch zu umgehen, verstößt gegen die TruckersMP-Regeln und kann zum Bann deines Accounts führen, bis hin zu permanent. Du nutzt diese Funktion auf eigenes Risiko.",
    "Anti-AFK message on": "Anti-AFK-Nachricht an", "Only while ETS2 is the active window on TruckersMP.": "Nur solange ETS2 auf TruckersMP das aktive Fenster ist.",
    "Anti-AFK message sent": "Anti-AFK-Nachricht gesendet", "Reset": "Zurücksetzen", "Back to the default message": "Zurück zur Standardnachricht", "ETS2 display mode": "ETS2-Anzeigemodus", "Checking…": "Wird geprüft…", "Switch to borderless": "Auf randlos umstellen",
    "Exclusive fullscreen – the overlay cannot appear over the game. Switch to borderless fullscreen (looks the same).": "Exklusives Vollbild – das Overlay kann nicht über dem Spiel erscheinen. Stell auf randloses Vollbild um (sieht gleich aus).",
    "Borderless fullscreen – notifications appear over the game.": "Randloses Vollbild – Benachrichtigungen erscheinen über dem Spiel.", "Window mode – notifications appear over the game.": "Fenstermodus – Benachrichtigungen erscheinen über dem Spiel.",
    "Unknown (config.cfg not found)": "Unbekannt (config.cfg nicht gefunden)", "Read aloud": "Vorlesen",
    "Speaks notifications with the Windows voice. Also works in exclusive fullscreen and on a single monitor.": "Liest Benachrichtigungen mit der Windows-Stimme vor. Funktioniert auch im exklusiven Vollbild und mit nur einem Bildschirm.",
    "TruckersMP inactivity warning": "TruckersMP-Inaktivitätswarnung",
    "Warns after 8 and 27 minutes without input on TruckersMP, before the server's automatic AFK kick (10 min on full servers, otherwise 30 min). HAULIX never sends input to the game – avoiding the kick automatically is against the TruckersMP rules.": "Warnt nach 8 und 27 Minuten ohne Eingabe auf TruckersMP, bevor der Server automatisch wegen AFK kickt (10 Min. auf vollen Servern, sonst 30 Min.). HAULIX sendet nie Eingaben ans Spiel – den Kick automatisch zu umgehen, verstößt gegen die TruckersMP-Regeln.",
    "Borderless fullscreen set": "Randloses Vollbild eingestellt", "Start ETS2 – HAULIX notifications now appear over the game. A backup of config.cfg was saved.": "Starte ETS2 – HAULIX-Benachrichtigungen erscheinen jetzt über dem Spiel. Eine Sicherung der config.cfg wurde angelegt.",
    "Not changed": "Nicht geändert", "Close ETS2 first: the game rewrites config.cfg when it exits.": "Schließe zuerst ETS2: Das Spiel überschreibt die config.cfg beim Beenden.", "Matches the in-game GPS": "Passt zum Spiel-Navi", "In-game GPS takes another way": "Spiel-Navi fährt anders",
    "HAULIX compares its route with the remaining distance of the in-game GPS and follows your route changes": "HAULIX vergleicht seine Route mit der Restdistanz des Spiel-Navis und folgt deinen Routenänderungen",
    "The in-game GPS reports a distance that no road-map route matches, e.g. a waypoint or a road HAULIX does not know": "Das Spiel-Navi meldet eine Distanz, zu der keine Route der Karte passt, z. B. ein Zwischenziel oder eine Straße, die HAULIX nicht kennt", "Installing the full ETS2 map": "Komplette ETS2-Karte wird eingerichtet", "Find a VTC": "VTC finden", "Soon": "Bald", "Virtual trucking companies": "Virtuelle Speditionen", "Coming later": "Kommt später",
    "This feature is not available in the current HAULIX version.": "Diese Funktion ist in der aktuellen HAULIX-Version nicht verfügbar.",
    "Later you will be able to browse virtual trucking companies here, compare them and apply to join. HAULIX works fully offline today, so your logbook, map and statistics keep running as usual.": "Später kannst du hier virtuelle Speditionen durchsuchen, vergleichen und dich bewerben. HAULIX arbeitet heute komplett offline – Fahrtenbuch, Karte und Statistiken laufen wie gewohnt weiter.",
    "Search VTCs by language, region and play style": "VTCs nach Sprache, Region und Spielstil suchen", "Send join requests from HAULIX": "Beitrittsanfragen direkt aus HAULIX senden", "Share your deliveries with your company": "Deine Lieferungen mit deiner Spedition teilen",
    "Remaining (game GPS)": "Verbleibend (Spiel-GPS)", "Remaining (≈)": "Verbleibend (≈)", "Planning route to your job…": "Route zum Auftrag wird berechnet…",
    "No active job. Search a destination or right-click the map to navigate.": "Kein aktiver Auftrag. Suche ein Ziel oder klicke mit rechts auf die Karte.",
    "Navigate to city or company…": "Navigieren zu Stadt oder Firma…",
    "Tip: right-click anywhere on the map to navigate there. HAULIX plans its own route over the road network, so it may occasionally differ from the in-game GPS.": "Tipp: Rechtsklick auf die Karte, um dorthin zu navigieren. HAULIX berechnet eine eigene Route über das Straßennetz, sie kann also gelegentlich vom Spiel-GPS abweichen.",
    "No matches": "Keine Treffer", "Navigate here": "Hierhin navigieren", "Copy coordinates": "Koordinaten kopieren",
    "No route": "Keine Route", "Routing failed": "Routenberechnung fehlgeschlagen",
    "The destination could not be reached on the road map.": "Das Ziel ist auf der Straßenkarte nicht erreichbar.",
    "No active route": "Keine aktive Route", "Take a job or pick a destination.": "Nimm einen Auftrag an oder wähle ein Ziel.",
    "Map data unavailable": "Kartendaten nicht verfügbar", "Street map could not be loaded": "Straßenkarte konnte nicht geladen werden",
    "ETS2 road map · extracted from your game files": "ETS2-Straßenkarte · aus deinen Spieldateien", "ETS2 world · schematic": "ETS2-Welt · schematisch",
    "Recorded route": "Aufgezeichnete Route", "Delivery route (reconstructed)": "Lieferroute (rekonstruiert)",
    "No GPS trace was recorded for this delivery; the line shows the likely road route.": "Für diese Lieferung wurde keine GPS-Spur aufgezeichnet; die Linie zeigt die wahrscheinliche Straßenroute.",
    "Open delivery": "Lieferung öffnen", "Estimated position": "Geschätzte Position", "From game map": "Aus der Spielkarte",
    "Your garage": "Deine Garage", "Dealer": "Händler", "Recruitment": "Personalagentur", "Open garage": "Garage öffnen",
    "Fuel station": "Tankstelle", "Service shop": "Werkstatt", "Truck dealer": "Lkw-Händler", "Recruitment agency": "Personalagentur",
    "Map point": "Kartenpunkt", "Cancel ": "Abbrechen",
    "Streets and navigation need a one-time road map built from your ETS2 files (about a minute, fully offline).": "Straßen und Navigation brauchen einmalig eine Straßenkarte aus deinen ETS2-Dateien (ca. eine Minute, komplett offline).",
    "Reading streets from your ETS2 files. This happens once per game update and takes about a minute. You can keep playing.": "Straßen werden aus deinen ETS2-Dateien gelesen. Das passiert einmal pro Spielupdate und dauert etwa eine Minute. Du kannst weiterspielen.",
    "ETS2 installation not found, so streets and navigation are unavailable. Set the game folder in Settings → ETS2.": "ETS2-Installation nicht gefunden, daher sind Straßen und Navigation nicht verfügbar. Lege den Spielordner unter Einstellungen → ETS2 fest.",
    "Opening game archives": "Spielarchive werden geöffnet", "Reading road and prefab definitions": "Straßen- und Prefab-Definitionen werden gelesen",
    "Writing street map": "Straßenkarte wird geschrieben", "Tracing country borders": "Ländergrenzen werden ermittelt", "Starting": "Startet",
    "Road map not built yet": "Straßenkarte noch nicht erstellt", "Build cancelled": "Erstellung abgebrochen",
    "ETS2 installation not found": "ETS2-Installation nicht gefunden",
    "Truck position unknown — start ETS2 to plan a route": "Lkw-Position unbekannt – starte ETS2, um eine Route zu planen",
    "No route found on the road map": "Keine Route auf der Straßenkarte gefunden", "Destination not found on the road map": "Ziel nicht auf der Straßenkarte gefunden",

    // Logbook
    "Every delivery, stored locally": "Jede Lieferung, lokal gespeichert", "Search cargo, city, company…": "Fracht, Stadt, Firma suchen…",
    "From date": "Von", "To date": "Bis", "All trucks": "Alle Lkw", "All cargo": "Alle Frachten", "All countries": "Alle Länder",
    "All cities": "Alle Städte", "Any status": "Jeder Status", "All sources": "Alle Quellen", "Recorded live": "Live erfasst",
    "Imported from save": "Aus Spielstand importiert", "Most recent": "Neueste", "Oldest": "Älteste", "Longest": "Längste",
    "Highest income": "Höchste Einnahmen", "Highest XP": "Meiste XP", "Most fuel efficient": "Sparsamste", "Fuel used": "Verbrauch",
    "Avg income": "Ø Einnahmen", "No matching deliveries": "Keine passenden Lieferungen", "Try widening the date range or clearing filters.": "Erweitere den Zeitraum oder setze die Filter zurück.",
    "Your logbook is empty": "Dein Fahrtenbuch ist leer",
    "Finish a job in ETS2 and it appears here with route, fuel, speed and damage. History from your save file is imported automatically.": "Schließe einen Auftrag in ETS2 ab und er erscheint hier mit Route, Verbrauch, Geschwindigkeit und Schaden. Der Verlauf aus deinem Spielstand wird automatisch importiert.",
    "Logbook unavailable": "Fahrtenbuch nicht verfügbar", "Logbook exported": "Fahrtenbuch exportiert", "Export failed": "Export fehlgeschlagen",
    "Filter by this cargo": "Nach dieser Fracht filtern", "Copy route": "Route kopieren", "Could not open delivery": "Lieferung konnte nicht geöffnet werden",
    "Imported from the in-game delivery log": "Aus dem Lieferprotokoll des Spiels importiert", "save": "Spielstand",
    "Recorded GPS trace": "Aufgezeichnete GPS-Spur", "Likely road route (no GPS trace recorded)": "Wahrscheinliche Straßenroute (keine GPS-Spur)",
    "Speed profile": "Geschwindigkeitsverlauf", "Consumption": "Verbrauch", "Average speed": "Durchschnittsgeschwindigkeit", "Top speed": "Höchstgeschwindigkeit",
    "Driving time": "Fahrzeit", "Real time spent driving": "Reale Fahrzeit", "In-game duration": "Dauer (Spielzeit)", "Truck (at finish)": "Lkw (bei Ankunft)",

    // Fleet pages
    "Trucks ": "Lkw", "Assigned to drivers": "Fahrern zugewiesen", "Fleet mileage": "Flotten-Laufleistung", "Component value": "Teilewert",
    "Sum of component refund values from the save": "Summe der Teile-Rückkaufwerte aus dem Spielstand", "Average damage": "Durchschnittlicher Schaden",
    "Filter by name, plate, garage…": "Nach Name, Kennzeichen, Garage filtern…", "Sort: Name": "Sortierung: Name", "Sort: Mileage": "Sortierung: Laufleistung",
    "Sort: Damage": "Sortierung: Schaden", "Sort: Profit": "Sortierung: Gewinn", "Sort: Garage": "Sortierung: Garage", "Sort: Distance": "Sortierung: Strecke",
    "Sort: Experience": "Sortierung: Erfahrung", "No trucks match": "Keine passenden Lkw", "No trucks yet": "Noch keine Lkw",
    "Buy a truck at a dealer in ETS2. HAULIX picks it up from the next save.": "Kaufe einen Lkw bei einem Händler in ETS2. HAULIX übernimmt ihn beim nächsten Speichern.",
    "No ETS2 profile loaded": "Kein ETS2-Profil geladen",
    "HAULIX reads trucks, trailers, garages and drivers from your save game. Choose a profile to get started.": "HAULIX liest Lkw, Auflieger, Garagen und Fahrer aus deinem Spielstand. Wähle ein Profil, um zu starten.",
    "Specification": "Ausstattung", "Assignment & trip": "Zuordnung & Fahrt", "Parts fitted": "Verbaute Teile", "Trip distance": "Fahrtstrecke",
    "Trip fuel": "Fahrtverbrauch", "Trip consumption": "Verbrauch (Fahrt)", "Refund value of fitted parts as stored in the save": "Rückkaufwert der verbauten Teile laut Spielstand",
    "Estimated from the engine code in the save": "Aus dem Motorcode im Spielstand geschätzt", "Wear": "Verschleiß", "Revenue by game day": "Umsatz pro Spieltag",
    "No profit history yet": "Noch kein Gewinnverlauf", "ETS2 records daily revenue and costs once this vehicle completes jobs.": "ETS2 speichert tägliche Einnahmen und Kosten, sobald dieses Fahrzeug Aufträge erledigt.",
    "Go to garage": "Zur Garage", "Deliveries with this truck": "Lieferungen mit diesem Lkw",
    "Loaded now": "Aktuell beladen", "Body types": "Aufbauarten", "Total mileage": "Gesamtlaufleistung", "Filter trailers…": "Auflieger filtern…",
    "All body types": "Alle Aufbauarten", "Assigned to": "Zugewiesen an", "Axles": "Achsen", "No trailers match": "Keine passenden Auflieger",
    "No owned trailers": "Keine eigenen Auflieger", "Gross weight limit": "Zulässiges Gesamtgewicht", "Chassis mass": "Leergewicht",
    "Assigned truck": "Zugewiesener Lkw", "Assigned driver": "Zugewiesener Fahrer", "Parts value": "Teilewert",
    "Truck slots used": "Belegte Lkw-Plätze", "Driver slots used": "Belegte Fahrerplätze", "Slots with both a truck and a driver": "Plätze mit Lkw und Fahrer",
    "Garage revenue": "Garagenumsatz", "Utilisation": "Auslastung", "Slots": "Stellplätze", "Empty truck slot": "Freier Lkw-Platz",
    "revenue": "Umsatz", "Garage level": "Garagenstufe", "Productivity": "Produktivität", "From the save file": "Aus dem Spielstand",
    "Garage upgrades are made in-game. HAULIX reads the garage level and slots from your latest save and refreshes automatically when ETS2 saves.": "Garagen-Ausbauten erfolgen im Spiel. HAULIX liest Stufe und Plätze aus dem letzten Spielstand und aktualisiert automatisch.",
    "No garages owned": "Keine Garagen", "Buy a garage in ETS2 to start building your company. Garages, their trucks and drivers appear here.": "Kaufe eine Garage in ETS2, um deine Firma aufzubauen. Garagen mit ihren Lkw und Fahrern erscheinen hier.",
    "AI drivers from your ETS2 company": "KI-Fahrer deiner ETS2-Firma", "Most profitable": "Profitabelster", "Most active": "Aktivster",
    "Total AI profit": "KI-Gewinn gesamt", "Total AI distance": "KI-Strecke gesamt", "Filter drivers…": "Fahrer filtern…", "All statuses": "Alle Status",
    "Current job ": "Aktueller Auftrag", "Skill": "Können", "No drivers match": "Keine passenden Fahrer", "No AI drivers hired": "Keine KI-Fahrer angestellt",
    "Hire drivers at a recruitment agency in ETS2. HAULIX detects them from your save and tracks their earnings, distance and skills.": "Stelle Fahrer bei einer Personalagentur in ETS2 ein. HAULIX erkennt sie im Spielstand und verfolgt Einnahmen, Strecke und Fähigkeiten.",
    "Margin": "Marge", "Profit as a share of revenue": "Gewinn im Verhältnis zum Umsatz", "Details": "Details", "Hometown": "Heimatstadt",
    "Current city": "Aktuelle Stadt", "Training focus": "Trainingsschwerpunkt", "Specialisation": "Spezialisierung",
    "Most frequently hauled cargo": "Am häufigsten transportierte Fracht", "Fuel & maintenance": "Kraftstoff & Wartung",
    "ADR": "ADR", "Long distance": "Langstrecke", "High value cargo": "Wertvolle Fracht", "Fragile cargo": "Zerbrechliche Fracht",
    "Just-in-time": "Just-in-time", "Just-in-time delivery": "Just-in-time-Lieferung", "Eco driving": "Sparsames Fahren", "Balanced": "Ausgewogen",

    // Statistics
    "Analytics from your local history": "Auswertungen aus deinem lokalen Verlauf", "Economy": "Wirtschaft",
    "7 days": "7 Tage", "30 days": "30 Tage", "90 days": "90 Tage", "1 year": "1 Jahr", "Distance per day": "Strecke pro Tag",
    "Driving time per day": "Fahrzeit pro Tag", "Fuel consumption": "Kraftstoffverbrauch", "Real time spent moving": "Reale Zeit in Bewegung",
    "Based on odometer distance and real driving time": "Basierend auf Kilometerstand und realer Fahrzeit", "in-game km per real hour": "Spiel-km pro realer Stunde",
    "Income per day": "Einnahmen pro Tag", "Profit per day": "Gewinn pro Tag", "Company balance ": "Kontostand", "from saved games": "aus Spielständen",
    "Expenses by type": "Ausgaben nach Art", "Top cargo by income": "Top-Frachten nach Einnahmen", "Per hour": "Pro Stunde",
    "income per distance": "Einnahmen pro Strecke", "income per real driving hour": "Einnahmen pro realer Fahrstunde",
    "tolls, fines, ferries, penalties": "Maut, Bußgelder, Fähren, Strafen",
    "Costs recorded by telemetry. Fuel and repairs are shown per truck from the save.": "Kosten aus der Telemetrie. Kraftstoff und Reparaturen stehen je Lkw aus dem Spielstand.",
    "Tolls": "Maut", "Fines": "Bußgelder", "Ferries": "Fähren", "Trains": "Züge", "Cancellation penalties": "Stornogebühren",
    "No expenses recorded": "Keine Ausgaben erfasst", "No deliveries": "Keine Lieferungen", "Balance history builds up over time": "Kontoverlauf entsteht mit der Zeit",
    "HAULIX stores a snapshot each time ETS2 saves.": "HAULIX speichert bei jedem ETS2-Speichern einen Stand.",
    "HAULIX builds these charts from telemetry it records while you drive. Drive with ETS2 running, or pick a longer range.": "HAULIX erstellt diese Diagramme aus der Telemetrie während du fährst. Fahre mit laufendem ETS2 oder wähle einen längeren Zeitraum.",
    "Truck utilisation": "Lkw-Auslastung", "Trailer utilisation": "Auflieger-Auslastung", "Garage capacity": "Garagenkapazität",
    "Fleet revenue": "Flottenumsatz", "from truck profit logs": "aus den Lkw-Gewinnprotokollen", "Truck mileage": "Lkw-Laufleistung",
    "Truck profit": "Lkw-Gewinn", "save profit logs": "Gewinnprotokolle (Spielstand)", "Garage utilisation": "Garagenauslastung",
    "Distance by truck": "Strecke pro Lkw", "recorded deliveries": "erfasste Lieferungen", "No recorded deliveries": "Keine erfassten Lieferungen",
    "Fleet statistics come from your ETS2 save.": "Flottenstatistiken stammen aus deinem ETS2-Spielstand.", "No AI drivers": "Keine KI-Fahrer",
    "Hire drivers in ETS2 to see their income, distance and profitability here.": "Stelle Fahrer in ETS2 ein, um hier Einnahmen, Strecke und Rentabilität zu sehen.",
    "AI revenue": "KI-Umsatz", "AI profit": "KI-Gewinn", "AI distance": "KI-Strecke", "Driving now": "Fahren gerade", "Average XP": "Ø XP",
    "AI revenue by game day": "KI-Umsatz pro Spieltag", "Driver profit": "Fahrergewinn", "Driver distance": "Fahrerstrecke",
    "Driver experience": "Fahrererfahrung", "Profitability": "Rentabilität", "profit per km": "Gewinn pro km",
    "No driver history in the save yet": "Noch kein Fahrerverlauf im Spielstand", "Statistics unavailable": "Statistik nicht verfügbar",

    // Profile
    "Your ETS2 company, read from the local save": "Deine ETS2-Firma, aus dem lokalen Spielstand", "Reload save": "Spielstand neu laden",
    "Money": "Geld", "Total distance": "Gesamtstrecke", "Total income": "Gesamteinnahmen", "driver profit logs": "Fahrer-Gewinnprotokolle",
    "Loans": "Kredite", "Preferred brand": "Lieblingsmarke", "Active mods": "Aktive Mods", "Real time played": "Reale Spielzeit",
    "Fuel bought": "Getankt", "Fuel stops": "Tankstopps", "Service visits": "Werkstattbesuche", "Last city": "Letzte Stadt",
    "Exploration": "Erkundung", "Unlocked": "Freigeschaltet", "Cities visited": "Besuchte Städte", "Cargo types hauled": "Transportierte Frachtarten",
    "Warnings": "Warnungen", "Other / DLC": "Sonstige / DLC", "Re-reading save": "Spielstand wird neu gelesen",
    "Your profile refreshes when parsing completes.": "Dein Profil wird aktualisiert, sobald das Einlesen fertig ist.", "Reload failed": "Neu laden fehlgeschlagen",

    // Settings
    "Stored locally in your HAULIX database": "Lokal in deiner HAULIX-Datenbank gespeichert", "General": "Allgemein", "Data": "Daten",
    "Appearance": "Darstellung", "About": "Über", "Language": "Sprache", "Automatic (Windows)": "Automatisch (Windows)",
    "Uses your Windows display language unless you choose one.": "Verwendet deine Windows-Anzeigesprache, sofern du keine auswählst.",
    "Units": "Einheiten", "Distances, speeds, volumes and temperatures.": "Strecken, Geschwindigkeiten, Volumen und Temperaturen.",
    "Metric": "Metrisch", "Imperial": "Imperial", "Currency": "Währung",
    "ETS2 pays in euro; other currencies use fixed approximate rates.": "ETS2 zahlt in Euro; andere Währungen nutzen feste Näherungskurse.",
    "ETS2 pays in euro. Other currencies are converted at approximate rates.": "ETS2 zahlt in Euro. Andere Währungen werden zu Näherungskursen umgerechnet.",
    "Theme": "Design", "Dark": "Dunkel", "Midnight": "Mitternacht", "Light": "Hell", "Start page": "Startseite",
    "What HAULIX opens on launch.": "Was HAULIX beim Start öffnet.", "Last visited page": "Zuletzt besuchte Seite",
    "Launch with Windows": "Mit Windows starten", "Start HAULIX minimised when you sign in, so every drive is logged.": "HAULIX beim Anmelden minimiert starten, damit jede Fahrt erfasst wird.",
    "Start minimised": "Minimiert starten", "Keep running in the tray": "Im Infobereich weiterlaufen",
    "Closing the window keeps HAULIX logging in the background.": "Beim Schließen läuft HAULIX im Hintergrund weiter.",
    "ETS2 detected.": "ETS2 erkannt.", "Telemetry plugin installed.": "Telemetrie-Plugin installiert.",
    "Telemetry plugin missing, so live data is unavailable.": "Telemetrie-Plugin fehlt, daher keine Live-Daten.",
    "ETS2 not found automatically.": "ETS2 wurde nicht automatisch gefunden.", "Set the installation folder below.": "Lege unten den Installationsordner fest.",
    "Automatic detection": "Automatische Erkennung", "Find the game through Steam and your Documents folder.": "Spiel über Steam und deinen Dokumente-Ordner finden.",
    "Installation folder": "Installationsordner", "Documents folder": "Dokumente-Ordner", "None selected": "Nichts ausgewählt",
    "Select profile…": "Profil wählen…", "Save to read": "Zu lesender Spielstand", "“Latest” follows autosaves automatically.": "„Neuester“ folgt automatisch den Autosaves.",
    "Latest save": "Neuester Spielstand", "Latest save (recommended)": "Neuester Spielstand (empfohlen)", "Watch for new saves": "Neue Spielstände überwachen",
    "Re-read the profile whenever ETS2 writes a save.": "Profil neu lesen, sobald ETS2 speichert.", "Import in-game delivery history": "Lieferverlauf aus dem Spiel importieren",
    "Add jobs from the save's delivery log to the logbook.": "Aufträge aus dem Lieferprotokoll des Spielstands ins Fahrtenbuch übernehmen.",
    "Re-run detection": "Erkennung erneut ausführen", "Detect again": "Erneut erkennen", "Setup wizard": "Einrichtungsassistent",
    "Detection complete": "Erkennung abgeschlossen", "That doesn't look right": "Das sieht nicht richtig aus",
    "The folder should contain a profiles or steam_profiles folder.": "Der Ordner sollte einen profiles- oder steam_profiles-Ordner enthalten.",
    "Plugin not installed": "Plugin nicht installiert", "Update frequency": "Aktualisierungsrate",
    "How often HAULIX reads telemetry. The UI refreshes at up to 10 Hz.": "Wie oft HAULIX die Telemetrie liest. Die Oberfläche aktualisiert bis zu 10-mal pro Sekunde.",
    "Record routes": "Routen aufzeichnen", "Store GPS breadcrumbs for the map and delivery details.": "GPS-Punkte für Karte und Lieferdetails speichern.",
    "Record free roam": "Freie Fahrt aufzeichnen", "Also record routes driven without a job.": "Auch Fahrten ohne Auftrag aufzeichnen.",
    "Route point spacing": "Abstand der Routenpunkte", "Distance between stored points. Lower gives more detail but a larger database.": "Abstand zwischen gespeicherten Punkten. Kleiner = genauer, aber größere Datenbank.",
    "Telemetry panels": "Telemetrie-Bereiche", "Choose which panels the Telemetry page shows.": "Wähle, welche Bereiche die Telemetrie-Seite zeigt.",
    "Demo mode": "Demomodus", "Simulate a drive to explore HAULIX without the game. Demo data is never saved permanently.": "Simuliert eine Fahrt, um HAULIX ohne Spiel zu erkunden. Demodaten werden nie dauerhaft gespeichert.",
    "Start demo": "Demo starten", "Stop demo": "Demo beenden", "Demo drive started": "Demofahrt gestartet", "Demo stopped": "Demo beendet",
    "Open the Dashboard to watch a simulated drive.": "Öffne die Übersicht, um die simulierte Fahrt zu sehen.", "Demo data was discarded.": "Demodaten wurden verworfen.",
    "Rebuild": "Neu erstellen", "Build road map automatically": "Straßenkarte automatisch erstellen",
    "Read streets from your ETS2 files on first start and after game or DLC updates (about a minute, offline).": "Straßen beim ersten Start und nach Spiel- oder DLC-Updates aus deinen ETS2-Dateien lesen (ca. eine Minute, offline).",
    "Navigate to the current job": "Zum aktuellen Auftrag navigieren",
    "Plan a route to the job's destination company automatically. You can still pick another destination on the map.": "Automatisch eine Route zur Zielfirma des Auftrags planen. Du kannst trotzdem ein anderes Ziel auf der Karte wählen.",
    "Not built yet": "Noch nicht erstellt", "Default zoom": "Standard-Zoom", "Region": "Region", "Wide": "Weit", "Normal": "Normal",
    "Close ": "Nah", "Street": "Straße", "Keep the map centred on your truck while driving.": "Karte beim Fahren auf deinen Lkw zentrieren.",
    "How far back the map shows completed routes.": "Wie weit zurück die Karte gefahrene Routen zeigt.", "Show estimated cities": "Geschätzte Städte anzeigen",
    "Cities HAULIX hasn't visited yet are placed from real-world coordinates.": "Noch nicht besuchte Städte werden anhand realer Koordinaten platziert.",
    "Local tile folder": "Lokaler Kachelordner", "None: schematic map": "Keiner: schematische Karte", "Healthy": "In Ordnung",
    "Automatic backups": "Automatische Sicherungen", "Rolling copies of your database.": "Fortlaufende Kopien deiner Datenbank.",
    "Backup frequency": "Sicherungsintervall", "Every 6 hours": "Alle 6 Stunden", "Daily": "Täglich", "Weekly": "Wöchentlich", "Keep": "Behalten",
    "Backups": "Sicherungen", "Back up now": "Jetzt sichern", "No backups yet.": "Noch keine Sicherungen.", "Export": "Exportieren",
    "Everything (deliveries, routes, sessions, settings) in one .haulix file.": "Alles (Lieferungen, Routen, Sitzungen, Einstellungen) in einer .haulix-Datei.",
    "Export data": "Daten exportieren", "CSV logbook": "Fahrtenbuch als CSV", "Import backup": "Sicherung importieren", "Import…": "Importieren…",
    "Replaces current data. A safety backup is made first.": "Ersetzt die aktuellen Daten. Vorher wird eine Sicherheitskopie erstellt.",
    "Clear data": "Daten löschen", "A safety backup is always created before clearing.": "Vor dem Löschen wird immer eine Sicherheitskopie erstellt.",
    "Clear history": "Verlauf löschen", "Clear caches": "Zwischenspeicher leeren", "Clear history?": "Verlauf löschen?", "Clear caches?": "Zwischenspeicher leeren?",
    "Data cleared": "Daten gelöscht", "A safety backup was saved first.": "Vorher wurde eine Sicherheitskopie gespeichert.", "Clear failed": "Löschen fehlgeschlagen",
    "Backup created": "Sicherung erstellt", "Backup failed": "Sicherung fehlgeschlagen", "Export complete": "Export abgeschlossen",
    "Import backup?": "Sicherung importieren?", "Choose file…": "Datei wählen…", "Import failed": "Import fehlgeschlagen",
    "Restore this backup?": "Diese Sicherung wiederherstellen?", "Your current data will be replaced. A safety backup is created first.": "Deine aktuellen Daten werden ersetzt. Vorher wird eine Sicherheitskopie erstellt.",
    "Backup restored": "Sicherung wiederhergestellt", "Restore failed": "Wiederherstellung fehlgeschlagen", "Stored history": "Gespeicherter Verlauf",
    "Accent colour": "Akzentfarbe", "HAULIX uses one accent for interaction. State colours stay fixed.": "HAULIX nutzt eine Akzentfarbe für Bedienelemente. Statusfarben bleiben fest.",
    "Electric amber": "Elektrisches Bernstein", "Copper": "Kupfer", "Ice blue": "Eisblau", "Signal white": "Signalweiß",
    "Compact mode": "Kompaktmodus", "Tighter spacing for smaller screens.": "Engere Abstände für kleinere Bildschirme.", "Animations": "Animationen",
    "Transitions and live indicators.": "Übergänge und Live-Anzeigen.", "Transparency": "Transparenz", "Slightly translucent panels.": "Leicht durchscheinende Flächen.",
    "Collapse sidebar ": "Seitenleiste einklappen", "Icon-only navigation rail. Always on below 1440 px wide.": "Nur Symbole in der Navigation. Unter 1440 px Breite immer aktiv.",
    "Offline telemetry and fleet intelligence for Euro Truck Simulator 2": "Offline-Telemetrie und Flottenauswertung für Euro Truck Simulator 2",
    "HAULIX needs no account, no server and no internet connection. All data stays on this PC.": "HAULIX braucht kein Konto, keinen Server und kein Internet. Alle Daten bleiben auf diesem PC.",
    "Local only": "Nur lokal", "Open-source components": "Open-Source-Komponenten", "Keyboard": "Tastatur",
    "Colours, type, components and markers used across HAULIX.": "Farben, Schrift, Komponenten und Marker in HAULIX.", "Open reference": "Referenz öffnen",
    "Reads the SCS SDK shared memory written by scs-telemetry.dll (RenCloud scs-sdk-plugin, revision 12).": "Liest den SCS-SDK-Speicher von scs-telemetry.dll (RenCloud scs-sdk-plugin, Revision 12).",

    // Setup wizard
    "First-run configuration": "Ersteinrichtung", "Detect ETS2": "ETS2 erkennen", "Choose profile": "Profil wählen", "Preferences": "Einstellungen",
    "Finish": "Fertig", "Offline telemetry and fleet intelligence for your ETS2 company.": "Offline-Telemetrie und Flottenauswertung für deine ETS2-Firma.",
    "Detecting Euro Truck Simulator 2": "Euro Truck Simulator 2 wird erkannt",
    "HAULIX looks for the game through Steam and reads profiles from your Documents folder. Nothing leaves this PC.": "HAULIX sucht das Spiel über Steam und liest Profile aus deinem Dokumente-Ordner. Nichts verlässt diesen PC.",
    "Game installation": "Spielinstallation", "Not found in your Steam libraries": "In deinen Steam-Bibliotheken nicht gefunden",
    "Profiles & saves": "Profile & Spielstände", "Profiles": "Profile", "No profiles found yet. Create one in ETS2 first.": "Noch keine Profile gefunden. Lege zuerst eines in ETS2 an.",
    "Telemetry plugin": "Telemetrie-Plugin", "Choose your profile": "Wähle dein Profil",
    "HAULIX reads your company, fleet, garages and drivers from this profile's newest save, and follows it automatically as you play.": "HAULIX liest Firma, Flotte, Garagen und Fahrer aus dem neuesten Spielstand dieses Profils und folgt ihm automatisch.",
    "No company yet": "Noch keine Firma", "Steam Cloud": "Steam Cloud", "Local": "Lokal",
    "No profiles were found. Start ETS2, create a profile, and come back. You can also finish setup now and choose a profile later in Settings.": "Keine Profile gefunden. Starte ETS2, lege ein Profil an und komm zurück. Du kannst die Einrichtung auch jetzt abschließen und später ein Profil wählen.",
    "Recommended, so every drive is recorded even if you forget to open HAULIX.": "Empfohlen, damit jede Fahrt erfasst wird, auch wenn du HAULIX nicht öffnest.",
    "Reading your save": "Dein Spielstand wird gelesen", "Parsing save game…": "Spielstand wird eingelesen…",
    "HAULIX stores everything in a local SQLite database. From now on it records deliveries, routes and telemetry automatically whenever ETS2 is running.": "HAULIX speichert alles in einer lokalen SQLite-Datenbank. Ab jetzt zeichnet es Lieferungen, Routen und Telemetrie automatisch auf, wenn ETS2 läuft.",
    "Deliveries imported": "Importierte Lieferungen", "The save could not be read.": "Der Spielstand konnte nicht gelesen werden.",
    "No profile selected. You can choose one later in Settings → ETS2.": "Kein Profil gewählt. Du kannst später unter Einstellungen → ETS2 eines wählen.",
    "Open dashboard": "Übersicht öffnen", "Select a profile or continue without one": "Wähle ein Profil oder fahre ohne fort",
    "Folder not recognised": "Ordner nicht erkannt", "Expected a profiles folder inside.": "Darin wurde ein profiles-Ordner erwartet.",
    "Could not select profile": "Profil konnte nicht gewählt werden",

    // Days, misc
    "Mon": "Mo", "Tue": "Di", "Wed": "Mi", "Thu": "Do", "Fri": "Fr", "Sat": "Sa", "Sun": "So",
    "just now": "gerade eben", "never": "nie", "ETS2 pays in euro": "ETS2 zahlt in Euro",
    "Automatic backup failed": "Automatische Sicherung fehlgeschlagen", "Backup imported": "Sicherung importiert", "Internal error": "Interner Fehler",
  },
  patterns: [
    [/^(\d[\d.,]*) (deliveries|delivery) · stored locally$/, (m) => `${m[1]} ${m[2] === "delivery" ? "Lieferung" : "Lieferungen"} · lokal gespeichert`],
    [/^(\d+)s ago$/, "vor $1 s"], [/^(\d+)m ago$/, "vor $1 min"], [/^(\d+)h ago$/, "vor $1 h"], [/^(\d+)d ago$/, "vor $1 T."],
    [/^last seen (.+)$/, (m) => `zuletzt gesehen ${t(m[1])}`], [/^Last seen (.+)$/, (m) => `Zuletzt gesehen ${t(m[1])}`],
    [/^Synced (.+)$/, (m) => `Synchronisiert ${t(m[1])}`], [/^updated (.+)$/, "aktualisiert $1"], [/^Demo · updated (.+)$/, "Demo · aktualisiert $1"],
    [/^Last telemetry update (.+)$/, "Letzte Telemetrie $1"], [/^Near (.+)$/, "Nahe $1"], [/^In (.+)$/, "In $1"],
    [/^Between cities · nearest (.+)$/, "Zwischen Städten · nächste: $1"], [/^(.+) · (.+) HQ$/, "$1 · Zentrale $2"],
    [/^(\d+) trucks · (.+)$/, "$1 Lkw · $2"], [/^(\d+) owned trailers$/, "$1 eigene Auflieger"], [/^(\d+) garages · HQ (.+)$/, "$1 Garagen · Zentrale $2"],
    [/^(.+) garage$/, (m) => `Garage ${m[1]}`], [/^(.+) garage · (\d+)\/(\d+) trucks$/, "Garage $1 · $2/$3 Lkw"],
    [/^Navigating to (.+)$/, "Navigation nach $1"], [/^≈ (.+) via the road network$/, "≈ $1 über das Straßennetz"],
    [/^Delivered: (.+)$/, "Geliefert: $1"], [/^Delivered (.+) to (.+)$/, "$1 nach $2 geliefert"], [/^Cancelled (.+)$/, "$1 abgebrochen"],
    [/^([\d.,]+%) complete$/, "$1 erledigt"], [/^(.+) remaining$/, "$1 verbleibend"], [/^Cruise (.+)$/, "Tempomat $1"],
    [/^Unlocked (.+)$/, "Freigeschaltet $1"], [/^(.+) % of driving time$/, "$1 % der Fahrzeit"], [/^Update available: (.+)$/, "Update verfügbar: $1"],
    [/^You are using (.+)\. The new setup is downloaded from the HAULIX releases on GitHub and updates HAULIX in place – your logbook and settings are kept\.$/, "Du nutzt $1. Das neue Setup wird aus den HAULIX-Releases auf GitHub geladen und aktualisiert HAULIX direkt – Fahrtenbuch und Einstellungen bleiben erhalten."],
    [/^Update check failed: (.+)$/, "Update-Prüfung fehlgeschlagen: $1"], [/^Version (.+) is available\.$/, "Version $1 ist verfügbar."], [/^HAULIX is up to date \((.+)\)\.$/, "HAULIX ist aktuell ($1)."],
    [/^on time · (.+) spare$/, "pünktlich · $1 Puffer"], [/^Taken by (.+)$/, "Übernommen von $1"], [/^(.+) · limit (\d+)( · cruise (\d+))?$/, (m) => `${m[1]} · Limit ${m[2]}${m[4] ? ` · Tempomat ${m[4]}` : ""}`], [/^(.+) · cruise (\d+)$/, "$1 · Tempomat $2"], [/^Real time · (.+)$/, "Echtzeit · $1"],
    [/^Road map for ETS2 (\S+) \((\d+) map DLCs\)$/, "Straßenkarte für ETS2 $1 ($2 Karten-DLCs)"],
    [/^Full ETS2 map (\S+) \(shipped with HAULIX, (\d+) map DLCs\)$/, "Komplette ETS2-Karte $1 (mit HAULIX geliefert, $2 Karten-DLCs)"],
    [/^due in (.+)$/, "fällig in $1"], [/^(.+) late$/, "$1 zu spät"], [/^Fined · (.+)$/, "Bußgeld · $1"], [/^Refuelled (.+)$/, "Getankt $1"],
    [/^Ferry (.+)$/, "Fähre $1"], [/^Train (.+)$/, "Zug $1"],
    [/^Reading map sectors \((\d+)\/(\d+)\)$/, "Kartensektoren werden gelesen ($1/$2)"], [/^Road map ready: (.+) road segments in (\d+) s$/, "Straßenkarte bereit: $1 Straßenabschnitte in $2 s"],
    [/^Road map for ETS2 (.+)$/, "Straßenkarte für ETS2 $1"], [/^Ready · (.+) road segments · ETS2 (.*) · built (.+)$/, "Bereit · $1 Straßenabschnitte · ETS2 $2 · erstellt $3"],
    [/^Building… (\d+)% · (.+)$/, (m) => `Wird erstellt… ${m[1]} % · ${t(m[2])}`], [/^Failed: (.+)$/, "Fehlgeschlagen: $1"],
    [/^(.+) road segments extracted from your ETS2 files\. Streets and navigation are now available\.$/, "$1 Straßenabschnitte aus deinen ETS2-Dateien gelesen. Straßen und Navigation sind jetzt verfügbar."],
    [/^ETS2 road map · (.+) street segments · (\d+) cities$/, "ETS2-Straßenkarte · $1 Straßenabschnitte · $2 Städte"],
    [/^(\d+) learned cities · schematic map \(build the road map for streets\)$/, "$1 gelernte Städte · schematische Karte (Straßenkarte für Straßen erstellen)"],
    [/^(.+)–(.+) of (.+)$/, "$1–$2 von $3"], [/^Level (\d+) · (\d+) slots?$/, "Stufe $1 · $2 Plätze"], [/^(\d+) trailers$/, "$1 Auflieger"],
    [/^(\d+) jobs$/, "$1 Aufträge"], [/^([\d.,]+) jobs completed$/, "$1 Aufträge erledigt"],
    [/^(\d+) drivers · revenue minus wages, fuel & maintenance$/, "$1 Fahrer · Umsatz minus Löhne, Kraftstoff & Wartung"],
    [/^Game day (\d+)$/, "Spieltag $1"], [/^Fuel ([\d.,]+\s?%)$/, "Kraftstoff $1"], [/^Speed ·$/, "Geschwindigkeit ·"], [/^Day (\d+)$/, "Tag $1"], [/^(\d+)\. (.+)$/, (m) => `${m[1]}. ${t(m[2])}`],
    [/^(\d+) found · save format (.+)$/, "$1 gefunden · Speicherformat $2"],
    [/^(Local|Steam Cloud) · (\d+) saves · last played (.+)$/, (m) => `${t(m[1])} · ${m[2]} Spielstände · zuletzt gespielt ${t(m[3])}`],
    [/^Remaining$/, "Verbleibend"], [/^([\d.,]+) delivered$/, "$1 geliefert"], [/^([\d.,]+) deliveries$/, "$1 Lieferungen"],
    [/^(.+) since (.+)$/, "$1 seit $2"], [/^Revenue (.+)$/, "Umsatz $1"], [/^Profit (.+) · (.+) jobs$/, "Gewinn $1 · $2 Aufträge"],
    [/^(.+) of (.+) trucks have a driver$/, "$1 von $2 Lkw haben einen Fahrer"], [/^(.+) of (.+) in use$/, "$1 von $2 in Benutzung"],
    [/^(.+) trucks in (.+) slots$/, "$1 Lkw auf $2 Plätzen"], [/^of (\d+) drivers$/, "von $1 Fahrern"],
    [/^(.+) all time$/, "$1 insgesamt"], [/^(.+) skill points invested$/, "$1 Fähigkeitspunkte vergeben"], [/^HQ (.+)$/, "Zentrale $1"],
    [/^(\d+) cities visited$/, "$1 Städte besucht"], [/^(.+) · saved (.+)$/, (m) => `${m[1]} · gespeichert ${t(m[2])}`],
    [/^Navigate to (.+)$/, "Nach $1 navigieren"], [/^Go to (.+) garage$/, "Zur Garage $1"], [/^Open (.+)$/, "$1 öffnen"],
    [/^Search logbook for “(.+)”$/, "Fahrtenbuch nach „$1“ durchsuchen"], [/^(\d+) (gallons?|litres?)$/, "$1 $2"],
    [/^HAULIX — (.+)$/, (m) => `HAULIX — ${t(m[1])}`], [/^Version (.+)$/, "Version $1"],
    [/^Showing the last values received (.+)\.$/, (m) => `Zuletzt empfangene Werte ${t(m[1])}.`],
  ],
};

const DICTS = { de };

/** Localised country names (codes from the city catalogue). */
const COUNTRIES_DE = {
  at: "Österreich", be: "Belgien", bg: "Bulgarien", ch: "Schweiz", cz: "Tschechien", de: "Deutschland", dk: "Dänemark", ee: "Estland",
  es: "Spanien", fi: "Finnland", fr: "Frankreich", gr: "Griechenland", hr: "Kroatien", hu: "Ungarn", it: "Italien", lt: "Litauen",
  lu: "Luxemburg", lv: "Lettland", nl: "Niederlande", no: "Norwegen", pl: "Polen", pt: "Portugal", ro: "Rumänien", ru: "Russland",
  se: "Schweden", si: "Slowenien", sk: "Slowakei", tr: "Türkei", uk: "Vereinigtes Königreich", ba: "Bosnien und Herzegowina", rs: "Serbien",
  me: "Montenegro", mk: "Nordmazedonien", al: "Albanien", xk: "Kosovo", ie: "Irland", ad: "Andorra",
};
export const countryNameFor = (code, english) => (lang === "de" && COUNTRIES_DE[code]) || english;
