# HAULIX Discord-Server einrichten

`setup-server.cs` richtet einen **leeren** Discord-Server in etwa einer Minute komplett ein. Dein Bot-Token bleibt dabei nur in deiner Konsole – er wird nirgends gespeichert.

## Was du bekommst

**Rollen:** 👑 Gründer (automatisch für dich) · 🛠️ HAULIX Team · 🛡️ Moderator · 🧪 Beta-Tester · 🎥 Content Creator · 🏢 VTC-Manager · 🚚 Fahrer · Sprache (🇩🇪 / 🇬🇧) · Spiele (ETS2 / ATS / TruckersMP) · Pings (🔔 Updates, Events & Konvois, Beta-News)

| Kategorie | Kanäle |
|---|---|
| 📌 HAULIX | 👋 willkommen · 📜 regeln · 📣 ankündigungen (folgbar) · 📝 changelog · ⬇️ download · 🗺️ roadmap · 🎭 rollen – alle schreibgeschützt |
| 💬 COMMUNITY | 💬 allgemein · 🇬🇧 english · 📸 screenshots · 🚚 meine-touren · 🏆 erfolge · 🎲 off-topic |
| 🆘 HILFE & FEEDBACK | ❓ faq · 🆘 support (Forum) · 🐛 bug-reports (Forum) · 💡 ideen (Forum mit 👍-Abstimmung) |
| 🚛 TRUCKING | 🛣️ truckersmp · 🤝 vtc-suche (1 Beitrag/Std.) · 🗓️ konvois-events (Forum) |
| 🧪 BETA | beta-chat · beta-feedback – nur Beta-Tester & Team |
| 🔊 SPRACHE | Konvoi 1 & 2 · Quatschen · Chill-Fahrt · Support-Talk |
| 🛡️ TEAM | team-chat · mod-log · Team-Besprechung – nur Team |

Außerdem: Community-Modus, Server-Icon, fertige Texte (Willkommen, Regeln, Download, FAQ, Roadmap, Rollen) auf Deutsch & Englisch mit Link-Buttons, Willkommensbildschirm, Onboarding („Kanäle & Rollen": Sprache, Spiele, Pings), AutoMod (Spam, Massen-Pings, fremde Einladungen, Beleidigungen → Meldung in #mod-log), dauerhafter Einladungslink und ein Webhook für neue Releases.

## So geht's (ca. 5 Minuten)

1. **Server anlegen:** In Discord links auf **＋ → Eigenen erstellen → Für mich und meine Freunde**, Name z. B. `HAULIX`.
2. **Server-ID kopieren:** Discord-Einstellungen → Erweitert → **Entwicklermodus** an. Dann Rechtsklick auf den Server → **Server-ID kopieren**.
3. **Bot erstellen:** <https://discord.com/developers/applications> → **New Application** („HAULIX") → links **Bot** → **Reset Token** → Token kopieren (niemandem zeigen!).
4. **Bot einladen:** links **OAuth2 → URL Generator** → Scopes **bot**, Bot Permissions **Administrator** → erzeugte URL öffnen → deinen Server wählen → Autorisieren.
5. **Skript starten** (PowerShell im HAULIX-Ordner):

   ```powershell
   $env:DISCORD_BOT_TOKEN = "HIER-DEN-TOKEN-EINFÜGEN"
   dotnet run tools/discord/setup-server.cs -- DEINE-SERVER-ID
   Remove-Item Env:DISCORD_BOT_TOKEN
   ```

6. **Fertig.** Das Skript zeigt den Einladungslink und legt `haulix-discord-webhook.txt` auf deinen Desktop.

## Danach (optional)

- **Releases automatisch in #changelog:** GitHub → Repo **Settings → Webhooks → Add webhook** → Payload URL = Inhalt von `haulix-discord-webhook.txt` (endet auf `/github`), Content type `application/json`, **Let me select individual events → nur „Releases"** → Add. Die Datei danach löschen – die URL ist geheim.
- **Bot-Rechte zurücknehmen:** Der Bot braucht nach dem Setup kein Administrator mehr. Du kannst ihn kicken oder seine Rolle entschärfen – der Server bleibt so, wie er ist.
- **Banner & Einladungshintergrund** gibt es erst mit Server-Boosts (Servereinstellungen → Server-Profil).
- **Link in HAULIX & README:** Schick mir den Einladungslink, dann baue ich ihn in HAULIX (Über-Seite) und ins README ein.

Falls etwas übersprungen wird (Meldung mit „!"), läuft der Rest trotzdem durch – die Meldung sagt, was fehlt.
