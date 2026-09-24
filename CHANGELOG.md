# Changelog

All notable changes to HAULIX. The same list appears in the app under *What's new* (Settings → About).

## 0.0.5 BETA

- **In-game HUD, fully customisable** – Choose what it shows: speed limit, speed (red when too fast), remaining distance, real-time ETA, arrival time, game ETA, deadline buffer, fuel range, next rest, truck wear and game time.
- **HUD position, size and visibility** – Six preset positions or a custom spot with sliders, three sizes, adjustable opacity (20–100 %), "only during a job", a live preview in Settings and a 10-second check on your game screen.
- **What's new window** – Opens once after every update with every change since 0.0.2. Open it again any time in Settings → About.
- **Updates from GitHub** – HAULIX checks the public HAULIX releases on GitHub for new versions – no own server needed – and can download and start the new setup for you.
- **Discord status: coming later** – The Discord Rich Presence setting is locked until it is ready; nothing is sent to Discord.
- **Fix: map controls over dialogs** – The map's + and − buttons no longer show through the What's new window and other dialogs.

<details><summary>Deutsch</summary>

- **HUD im Spiel, frei einstellbar** – Wähle, was angezeigt wird: Tempolimit, Geschwindigkeit (rot bei zu schnell), Restdistanz, Echtzeit-ETA, Ankunftszeit, Spiel-ETA, Fristpuffer, Reichweite, nächste Pause, Lkw-Verschleiß und Spielzeit.
- **HUD-Position, Größe und Sichtbarkeit** – Sechs feste Positionen oder eine eigene per Schieberegler, drei Größen, einstellbare Deckkraft (20–100 %), „nur während eines Auftrags“, Live-Vorschau in den Einstellungen und ein 10-Sekunden-Test auf dem Spielbildschirm.
- **Neuigkeiten-Fenster** – Erscheint einmal nach jedem Update mit allen Änderungen seit 0.0.2. Unter Einstellungen → Über jederzeit wieder aufrufbar.
- **Updates über GitHub** – HAULIX prüft die öffentlichen HAULIX-Releases auf GitHub auf neue Versionen – ganz ohne eigenen Server – und kann das neue Setup für dich herunterladen und starten.
- **Discord-Status: kommt später** – Die Einstellung für Discord Rich Presence ist gesperrt, bis sie fertig ist; es wird nichts an Discord gesendet.
- **Behoben: Kartenknöpfe über Fenstern** – Die + und − Knöpfe der Karte scheinen nicht mehr durch das Neuigkeiten-Fenster und andere Dialoge.

</details>

## 0.0.4 BETA

- **Driving score** – Every recorded delivery gets a score from 0 to 100: points off for speeding (time above the limit + 5 km/h), cargo damage, new truck damage, fines and late delivery. Score column and a breakdown ring in the logbook.
- **Achievements** – New page with 16 bronze, silver and gold achievements from your logbook and save (e.g. 10,000 km, Night owl, Across Europe, Millionaire), announced when you reach them.
- **Service reminders** – A notification when truck or trailer wear reaches 15 % and 30 %; armed again after a repair.
- **Share cards** – Save or copy a delivery or your achievements as a 1200×630 image in the HAULIX style for Discord and social media.
- **In-game HUD** – A slim bar over the game with speed limit, remaining distance and real-time arrival.
- **Update check** – Optional check of an update manifest URL on start.
- **Coming soon areas** – New VTC and Online sections: Find a VTC, My VTC, Events & convoys, Job board, Leaderboards, Live map and Cloud sync – each with a preview. ATS support announced.
- **Database update** – The logbook stores the driving score and unlocked achievements (automatic migration, your data is kept).

<details><summary>Deutsch</summary>

- **Fahrscore** – Jede aufgezeichnete Lieferung bekommt einen Score von 0 bis 100: Abzüge für zu schnelles Fahren (Zeit über Limit + 5 km/h), Frachtschaden, neuen Lkw-Schaden, Bußgelder und Verspätung. Score-Spalte und Aufschlüsselung im Fahrtenbuch.
- **Erfolge** – Neue Seite mit 16 Bronze-, Silber- und Gold-Erfolgen aus Fahrtenbuch und Spielstand (z. B. 10.000 km, Nachteule, Quer durch Europa, Millionär), mit Benachrichtigung beim Freischalten.
- **Werkstatt-Erinnerungen** – Eine Benachrichtigung, wenn Lkw- oder Auflieger-Verschleiß 15 % und 30 % erreicht; nach einer Reparatur wieder aktiv.
- **Teilen-Karten** – Lieferungen oder Erfolge als 1200×630-Bild im HAULIX-Stil für Discord und Social Media speichern oder kopieren.
- **HUD im Spiel** – Eine schmale Leiste über dem Spiel mit Tempolimit, Restdistanz und Ankunft in Echtzeit.
- **Update-Prüfung** – Optionale Prüfung einer Update-URL beim Start.
- **Demnächst-Bereiche** – Neue Bereiche VTC und Online: VTC finden, Meine VTC, Events & Konvois, Auftragsbörse, Bestenlisten, Live-Karte und Cloud-Sync – jeweils mit Vorschau. ATS-Unterstützung angekündigt.
- **Datenbank-Update** – Das Fahrtenbuch speichert Fahrscore und freigeschaltete Erfolge (automatische Umstellung, deine Daten bleiben erhalten).

</details>

## 0.0.3 DEVKIT

- **Real-time ETA** – Arrival in real minutes with clock time, converted from the game's navigation with the game's time scale (≈19×). Without an in-game GPS route HAULIX estimates from its own route and your average speed.
- **Deadline buffer** – Game ETA with "on time · spare" or "late" on the dashboard, telemetry page and map.
- **Notifications over the game** – HAULIX cards over ETS2: job accepted, 100/50/10/2 km left, halfway, deadline at risk or tight, fuel range too short, cargo damage, rest needed, route changed, delivered, cancelled, fined. Each type can be switched off; corner selectable; test button.
- **Single-monitor support** – HAULIX detects exclusive fullscreen and switches ETS2 to borderless fullscreen with one click (backup of config.cfg). Notifications can also be read aloud with the Windows voice.
- **Follows your in-game route** – HAULIX compares its route with the in-game GPS distance; when you change the route in the game it plans alternatives and follows the matching one.
- **Custom map style** – Soft landmass and built-up city areas under the roads.
- **TruckersMP** – Warning after 8 and 27 minutes of inactivity. Optional anti-AFK chat message with your own text, reset button, interval and chat key – against the TruckersMP rules, use at your own risk.
- **Find a VTC** – New VTC section in the sidebar.
- **Fixes** – Settings no longer save on every click, the language switches instantly and reliably, reinstalling keeps your language, the ETA no longer jumps in cities, reversing no longer breaks the recorded route, the installer's language switch works both ways.

<details><summary>Deutsch</summary>

- **Echtzeit-ETA** – Ankunft in echten Minuten mit Uhrzeit, umgerechnet aus dem Spiel-Navi mit der Zeitskala des Spiels (≈19×). Ohne Route im Spiel-Navi schätzt HAULIX über die eigene Route und deine Durchschnittsgeschwindigkeit.
- **Fristpuffer** – Spiel-ETA mit „pünktlich · Puffer“ oder „zu spät“ auf Übersicht, Telemetrie und Karte.
- **Benachrichtigungen über dem Spiel** – HAULIX-Karten über ETS2: Auftrag angenommen, noch 100/50/10/2 km, Halbzeit, Frist in Gefahr oder knapp, Reichweite zu kurz, Frachtschaden, Pause nötig, Route geändert, abgeliefert, abgebrochen, Bußgeld. Jede Art abschaltbar, Ecke wählbar, Test-Knopf.
- **Ein-Bildschirm-Unterstützung** – HAULIX erkennt exklusives Vollbild und stellt ETS2 mit einem Klick auf randloses Vollbild um (mit Sicherung der config.cfg). Benachrichtigungen können auch mit der Windows-Stimme vorgelesen werden.
- **Folgt deiner Route im Spiel** – HAULIX vergleicht seine Route mit der Distanz des Spiel-Navis; änderst du die Route im Spiel, plant HAULIX Alternativen und folgt der passenden.
- **Eigener Kartenstil** – Weiche Landmasse und bebaute Stadtgebiete unter den Straßen.
- **TruckersMP** – Warnung nach 8 und 27 Minuten Inaktivität. Optionale Anti-AFK-Chatnachricht mit eigenem Text, Zurücksetzen-Knopf, Intervall und Chat-Taste – gegen die TruckersMP-Regeln, auf eigenes Risiko.
- **VTC finden** – Neuer VTC-Bereich in der Seitenleiste.
- **Korrekturen** – Einstellungen speichern nicht mehr bei jedem Klick, die Sprache wechselt sofort und zuverlässig, eine Neuinstallation behält deine Sprache, die ETA springt in Städten nicht mehr, Rückwärtsfahren unterbricht die aufgezeichnete Route nicht mehr, der Sprachumschalter im Installer funktioniert in beide Richtungen.

</details>

## 0.0.2

- **The whole map for everyone** – The setup ships the full ETS2 road map with all map DLCs (Going East, Scandinavia, France, Italy, Baltic, Iberia, West Balkans, Balkans, Greece). Players with fewer DLCs – or without the game installed – still see every street; same DLCs and version skip the one-time build.
- **Map colours and legend** – Streets you have driven (blue), your job route (amber), other streets, city names (white) and country names (purple), explained in a legend.
- **Streets, borders and names always on** – The road map, city and country names are part of the base map and no longer switchable; country border lines were removed because they were imprecise.
- **Small windows** – Map, profile and all other pages adapt to narrow windows (map panels become a Layers button, toolbar icons only).
- **German and English** – Language auto-detected from Windows, selectable in the installer and in Settings.
- **Fixes** – Language changes in Settings are saved reliably, updates no longer run stale UI files, cancelled jobs no longer leave their route on the map.

<details><summary>Deutsch</summary>

- **Die ganze Karte für alle** – Das Setup liefert die komplette ETS2-Straßenkarte mit allen Karten-DLCs mit (Going East, Skandinavien, Frankreich, Italien, Baltikum, Iberia, Westbalkan, Balkan, Griechenland). Wer weniger DLCs hat – oder das Spiel gar nicht installiert hat – sieht trotzdem jede Straße; bei gleichen DLCs und gleicher Version entfällt der einmalige Aufbau.
- **Kartenfarben und Legende** – Befahrene Straßen (blau), Auftragsroute (amber), übrige Straßen, Städtenamen (weiß) und Ländernamen (lila), erklärt in einer Legende.
- **Straßen und Namen immer an** – Straßenkarte, Städte- und Ländernamen gehören zur Grundkarte und sind nicht mehr abschaltbar; die Grenzlinien wurden entfernt, weil sie ungenau waren.
- **Kleine Fenster** – Karte, Profil und alle anderen Seiten passen sich schmalen Fenstern an (Kartenpanels werden zum Ebenen-Knopf, Werkzeugleiste nur mit Symbolen).
- **Deutsch und Englisch** – Sprache wird aus Windows erkannt und ist im Installer und in den Einstellungen wählbar.
- **Korrekturen** – Sprachwechsel in den Einstellungen werden zuverlässig gespeichert, Updates laden keine alten Oberflächendateien mehr, abgebrochene Aufträge hinterlassen keine Route mehr auf der Karte.

</details>

