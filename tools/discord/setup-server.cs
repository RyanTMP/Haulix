#:property PublishAot=false
// HAULIX Discord server setup
// Richtet einen LEEREN Discord-Server komplett ein: Rollen, Kategorien, Kanäle, Rechte, Community-Modus,
// Foren mit Tags, Willkommensbildschirm, Onboarding, AutoMod, Server-Icon, Info-Nachrichten (DE + EN),
// Einladungslink und einen Webhook für Release-Meldungen.
//
// Nutzung (siehe tools/discord/README.md):
//   $env:DISCORD_BOT_TOKEN = "<dein Bot-Token>"      # bleibt nur in deiner Konsole
//   dotnet run tools/discord/setup-server.cs -- <Server-ID>
//
// Das Skript läuft einmal auf einem frischen Server. Die Standardkanäle von Discord ("Allgemein" usw.) werden
// entfernt; bestehende Kanäle mit anderen Namen bleiben unberührt.
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

var token = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN");
if (string.IsNullOrWhiteSpace(token) || args.Length < 1)
{
    Console.WriteLine("Nutzung: $env:DISCORD_BOT_TOKEN = \"...\"; dotnet run tools/discord/setup-server.cs -- <Server-ID>");
    return 1;
}
var guildId = args[0].Trim();
var root = FindRepoRoot();

using var http = new HttpClient { BaseAddress = new Uri("https://discord.com/api/v10/") };
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bot", token.Trim());
http.DefaultRequestHeaders.UserAgent.ParseAdd("DiscordBot (https://github.com/RyanTMP/Haulix, 1.0)");

const string Website = "https://www.haulix-logging.com";
const string Releases = "https://github.com/RyanTMP/Haulix/releases/latest";
const string Repo = "https://github.com/RyanTMP/Haulix";
const int Amber = 0xFFB020, Green = 0x3DD68C, Blue = 0x6CA8FF, Red = 0xF0474F, Purple = 0xA78BFA, Grey = 0x9BA1AA;

// ---------------------------------------------------------------- permissions
static string P(params int[] bits) => bits.Aggregate(0UL, (a, b) => a | (1UL << b)).ToString();
const int CreateInvite = 0, Kick = 1, Ban = 2, Admin = 3, AddReactions = 6, AuditLog = 7, Stream = 9, View = 10,
    Send = 11, ManageMessages = 13, EmbedLinks = 14, AttachFiles = 15, History = 16, MentionEveryone = 17, ExternalEmojis = 18,
    Connect = 20, Speak = 21, Mute = 22, Deafen = 23, Move = 24, Vad = 25, ChangeNick = 26, ManageNicks = 27,
    AppCommands = 31, ManageThreads = 34, PublicThreads = 35, PrivateThreads = 36, SendInThreads = 38, Timeout = 40, VoiceMessages = 46;
var everyonePerms = P(CreateInvite, AddReactions, Stream, View, Send, EmbedLinks, AttachFiles, History, ExternalEmojis, Connect, Speak, Vad,
    ChangeNick, AppCommands, PublicThreads, SendInThreads, VoiceMessages);
var modPerms = P(Kick, Ban, AuditLog, ManageMessages, MentionEveryone, Mute, Deafen, Move, ManageNicks, ManageThreads, Timeout);
var readOnlyDeny = P(Send, PublicThreads, PrivateThreads, SendInThreads);

Console.WriteLine("HAULIX Discord-Setup");
var guild = (await Api(HttpMethod.Get, $"guilds/{guildId}"))!.AsObject();
Console.WriteLine($"  Server: {guild["name"]}");
var everyoneId = guildId;

// ---------------------------------------------------------------- clean the default channels of a new server
var existing = (await Api(HttpMethod.Get, $"guilds/{guildId}/channels"))!.AsArray();
string[] defaults = ["general", "allgemein", "general-chat", "text channels", "textkanäle", "voice channels", "sprachkanäle", "General", "Allgemein"];
foreach (var ch in existing)
{
    var name = (string)ch!["name"]!;
    if (defaults.Contains(name, StringComparer.OrdinalIgnoreCase))
    {
        await Api(HttpMethod.Delete, $"channels/{ch["id"]}");
        Console.WriteLine($"  - Standardkanal entfernt: {name}");
    }
}

// ---------------------------------------------------------------- roles (created bottom-up; the list is top-down)
Console.WriteLine("Rollen …");
await Api(HttpMethod.Patch, $"guilds/{guildId}/roles/{everyoneId}", new JsonObject { ["permissions"] = everyonePerms });
var roleSpecs = new (string Key, string Name, int Color, bool Hoist, bool Mention, string Perms)[]
{
    ("founder", "👑 Gründer", Amber, true, false, P(Admin)),
    ("team", "🛠️ HAULIX Team", Amber, true, true, modPerms),
    ("mod", "🛡️ Moderator", Blue, true, true, modPerms),
    ("beta", "🧪 Beta-Tester", Purple, true, true, "0"),
    ("creator", "🎥 Content Creator", Red, true, false, "0"),
    ("vtc", "🏢 VTC-Manager", Green, false, false, "0"),
    ("driver", "🚚 Fahrer", Grey, false, false, "0"),
    ("de", "🇩🇪 Deutsch", 0, false, false, "0"),
    ("en", "🇬🇧 English", 0, false, false, "0"),
    ("ets2", "🇪🇺 ETS2", 0, false, false, "0"),
    ("ats", "🇺🇸 ATS", 0, false, false, "0"),
    ("tmp", "🛣️ TruckersMP", 0, false, false, "0"),
    ("pingUpdates", "🔔 Updates", 0, false, true, "0"),
    ("pingEvents", "🔔 Events & Konvois", 0, false, true, "0"),
    ("pingBeta", "🔔 Beta-News", 0, false, true, "0"),
};
var roles = new Dictionary<string, string>();
var existingRoles = (await Api(HttpMethod.Get, $"guilds/{guildId}/roles"))!.AsArray();
foreach (var r in roleSpecs)
{
    var found = existingRoles.FirstOrDefault(x => (string)x!["name"]! == r.Name);
    var role = found ?? await Api(HttpMethod.Post, $"guilds/{guildId}/roles", new JsonObject
    {
        ["name"] = r.Name, ["color"] = r.Color, ["hoist"] = r.Hoist, ["mentionable"] = r.Mention, ["permissions"] = r.Perms,
    });
    roles[r.Key] = (string)role!["id"]!;
    Console.WriteLine($"  + {r.Name}");
}
// Order: first spec highest. Positions start above @everyone (0) and stay below the bot's own role.
var pos = new JsonArray();
for (var i = 0; i < roleSpecs.Length; i++) pos.Add(new JsonObject { ["id"] = roles[roleSpecs[i].Key], ["position"] = roleSpecs.Length - i });
try { await Api(HttpMethod.Patch, $"guilds/{guildId}/roles", pos); } catch (Exception ex) { Warn("Rollen-Reihenfolge", ex); }
try { await Api(HttpMethod.Put, $"guilds/{guildId}/members/{guild["owner_id"]}/roles/{roles["founder"]}"); Console.WriteLine("  Gründer-Rolle an den Server-Besitzer vergeben"); }
catch (Exception ex) { Warn("Gründer-Rolle vergeben", ex); }

// ---------------------------------------------------------------- channels
Console.WriteLine("Kanäle …");
JsonObject Ow(string id, string allow = "0", string deny = "0") => new() { ["id"] = id, ["type"] = 0, ["allow"] = allow, ["deny"] = deny };
JsonArray ReadOnly() => [Ow(everyoneId, P(AddReactions), readOnlyDeny), Ow(roles["team"], P(Send, SendInThreads, PublicThreads))];
JsonArray Hidden(params string[] visibleFor) => [Ow(everyoneId, "0", P(View, Connect)), .. visibleFor.Select(v => (JsonNode)Ow(roles[v], P(View, Send, Connect, Speak, History)))];

var ids = new Dictionary<string, string>();
async Task<string> Category(string key, string name, JsonArray? ow = null)
{
    var c = await Api(HttpMethod.Post, $"guilds/{guildId}/channels", new JsonObject { ["name"] = name, ["type"] = 4, ["permission_overwrites"] = ow ?? new JsonArray() });
    Console.WriteLine($"  [{name}]");
    return ids[key] = (string)c!["id"]!;
}
async Task<string> Channel(string key, string name, int type, string parent, string? topic = null, JsonArray? ow = null, JsonObject? extra = null)
{
    var body = new JsonObject { ["name"] = name, ["type"] = type, ["parent_id"] = parent };
    if (topic is not null) body["topic"] = topic;
    if (ow is not null) body["permission_overwrites"] = ow;
    if (extra is not null) foreach (var kv in extra) body[kv.Key] = kv.Value?.DeepClone();
    var c = await Api(HttpMethod.Post, $"guilds/{guildId}/channels", body);
    Console.WriteLine($"    #{name}");
    return ids[key] = (string)c!["id"]!;
}

var info = await Category("catInfo", "📌 HAULIX");
await Channel("welcome", "👋│willkommen", 0, info, "Willkommen bei HAULIX – dem kostenlosen Fahrtenbuch für ETS2 · Welcome!", ReadOnly());
await Channel("rules", "📜│regeln", 0, info, "Regeln des Servers · Server rules", ReadOnly());
await Channel("news", "📣│ankündigungen", 0, info, "Neuigkeiten rund um HAULIX · News", ReadOnly());
await Channel("changelog", "📝│changelog", 0, info, "Neue HAULIX-Versionen automatisch von GitHub · New releases", ReadOnly());
await Channel("download", "⬇️│download", 0, info, "HAULIX herunterladen · Download", ReadOnly());
await Channel("roadmap", "🗺️│roadmap", 0, info, "Was als Nächstes kommt · What's coming", ReadOnly());
await Channel("roles", "🎭│rollen", 0, info, "Wähle deine Rollen über Kanäle & Rollen · Pick your roles", ReadOnly());

var community = await Category("catCommunity", "💬 COMMUNITY");
await Channel("chat", "💬│allgemein", 0, community, "Alles rund ums Truckern und HAULIX (Deutsch)");
await Channel("english", "🇬🇧│english", 0, community, "General chat in English");
await Channel("screens", "📸│screenshots", 0, community, "Eure schönsten Bilder aus ETS2 & ATS · Your best shots", extra: new() { ["rate_limit_per_user"] = 10 });
await Channel("tours", "🚚│meine-touren", 0, community, "Teilt eure HAULIX-Share-Cards und Touren · Share your HAULIX cards");
await Channel("achievements", "🏆│erfolge", 0, community, "Neue Erfolge und Fahrerränge feiern · Celebrate achievements");
await Channel("offtopic", "🎲│off-topic", 0, community, "Alles, was nicht ums Truckern geht · Anything else");

var support = await Category("catSupport", "🆘 HILFE & FEEDBACK");
await Channel("faq", "❓│faq", 0, support, "Häufige Fragen · Frequently asked questions", ReadOnly());

var trucking = await Category("catTrucking", "🚛 TRUCKING");
await Channel("tmp", "🛣️│truckersmp", 0, trucking, "TruckersMP-Server, Treffpunkte, Staus · TruckersMP talk");
await Channel("vtc", "🤝│vtc-suche", 0, trucking, "VTCs stellen sich vor und suchen Fahrer · VTCs & recruiting", extra: new() { ["rate_limit_per_user"] = 3600 });

var beta = await Category("catBeta", "🧪 BETA", Hidden("beta", "team", "mod"));
await Channel("betaChat", "🧪│beta-chat", 0, beta, "Für Beta-Tester: neue Versionen vor allen anderen");
await Channel("betaFeedback", "📋│beta-feedback", 0, beta, "Rückmeldungen zu Test-Versionen");

var voice = await Category("catVoice", "🔊 SPRACHE · VOICE");
await Channel("vConvoy1", "🚚 Konvoi 1", 2, voice);
await Channel("vConvoy2", "🚚 Konvoi 2", 2, voice);
await Channel("vTalk", "💬 Quatschen · Talk", 2, voice);
await Channel("vChill", "🎧 Chill-Fahrt", 2, voice, extra: new() { ["user_limit"] = 4 });
await Channel("vSupport", "🆘 Support-Talk", 2, voice);

var team = await Category("catTeam", "🛡️ TEAM", Hidden("team", "mod"));
await Channel("teamChat", "🛠️│team-chat", 0, team);
await Channel("modLog", "📋│mod-log", 0, team, "AutoMod-Meldungen und Moderation");
await Channel("teamVoice", "🔒 Team-Besprechung", 2, team);

// ---------------------------------------------------------------- community mode, announcement channel, forums
Console.WriteLine("Community-Modus …");
var communityOn = false;
try
{
    await Api(HttpMethod.Patch, $"guilds/{guildId}", new JsonObject
    {
        ["verification_level"] = 1, ["explicit_content_filter"] = 2, ["default_message_notifications"] = 1,
        ["features"] = new JsonArray("COMMUNITY"), ["rules_channel_id"] = ids["rules"], ["public_updates_channel_id"] = ids["modLog"],
        ["preferred_locale"] = "de", ["system_channel_id"] = ids["chat"],
        ["description"] = "Die Community von HAULIX – dem kostenlosen Fahrtenbuch & Beifahrer für Euro Truck Simulator 2.",
    });
    communityOn = true;
    Console.WriteLine("  Community aktiviert");
    await Api(HttpMethod.Patch, $"channels/{ids["news"]}", new JsonObject { ["type"] = 5 });
    Console.WriteLine("  #ankündigungen ist jetzt ein Ankündigungskanal (folgbar)");
    await Api(HttpMethod.Patch, $"channels/{ids["changelog"]}", new JsonObject { ["type"] = 5 });
}
catch (Exception ex) { Warn("Community-Modus", ex); }

JsonArray Tags(params (string Name, string Emoji)[] t) => new(t.Select(x => (JsonNode)new JsonObject { ["name"] = x.Name, ["emoji_name"] = x.Emoji }).ToArray());
try
{
    await Channel("supportForum", "🆘│support", 15, support, "Stell deine Frage – ein Beitrag pro Problem. Gib deine HAULIX-Version an (Einstellungen → Über). · One post per problem, please include your HAULIX version.",
        extra: new() { ["available_tags"] = Tags(("Installation", "💿"), ("Telemetrie", "📡"), ("Karte", "🗺️"), ("HUD", "🖥️"), ("Stimme & Töne", "🔊"), ("Fahrtenbuch", "📒"), ("Gelöst", "✅")), ["default_reaction_emoji"] = new JsonObject { ["emoji_name"] = "👍" } });
    await Channel("bugs", "🐛│bug-reports", 15, support, "Fehler melden: Was hast du gemacht, was ist passiert, was hast du erwartet? Screenshots helfen. · Report bugs with steps and screenshots.",
        extra: new() { ["available_tags"] = Tags(("Neu", "🆕"), ("Bestätigt", "🔎"), ("In Arbeit", "🛠️"), ("Behoben", "✅"), ("Kein Fehler", "🚫")) });
    await Channel("ideas", "💡│ideen", 15, support, "Wünsche und Ideen für HAULIX – stimmt mit 👍 ab! · Suggest features and vote with 👍.",
        extra: new() { ["available_tags"] = Tags(("HUD", "🖥️"), ("Karte", "🗺️"), ("Fahrtenbuch", "📒"), ("VTC", "🏢"), ("Geplant", "📌"), ("Umgesetzt", "✅")), ["default_reaction_emoji"] = new JsonObject { ["emoji_name"] = "👍" } });
    await Channel("events", "🗓️│konvois-events", 15, trucking, "Konvois und Events planen: Datum, Uhrzeit, Server, Treffpunkt, Route. · Plan convoys and events.",
        extra: new() { ["available_tags"] = Tags(("TruckersMP", "🛣️"), ("Singleplayer", "🚚"), ("Konvoi", "🚛"), ("Event", "🎉")) });
}
catch (Exception ex) { Warn("Foren (brauchen den Community-Modus)", ex); }

// ---------------------------------------------------------------- server icon
try
{
    var icon = Path.Combine(root, "src", "Haulix.App", "wwwroot", "assets", "brand", "app-tile.png");
    await Api(HttpMethod.Patch, $"guilds/{guildId}", new JsonObject { ["icon"] = "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(icon)) });
    Console.WriteLine("Server-Icon gesetzt");
}
catch (Exception ex) { Warn("Server-Icon", ex); }

// ---------------------------------------------------------------- messages
Console.WriteLine("Nachrichten …");
JsonObject Embed(string title, string text, int color, string? image = null, params (string Name, string Value, bool Inline)[] fields)
{
    var e = new JsonObject { ["title"] = title, ["description"] = text, ["color"] = color, ["footer"] = new JsonObject { ["text"] = "HAULIX · ETS2 Logger" } };
    if (fields.Length > 0) e["fields"] = new JsonArray(fields.Select(f => (JsonNode)new JsonObject { ["name"] = f.Name, ["value"] = f.Value, ["inline"] = f.Inline }).ToArray());
    if (image is not null) e["image"] = new JsonObject { ["url"] = image };
    return e;
}
JsonArray Links(params (string Label, string Url, string Emoji)[] l) => [new JsonObject
{
    ["type"] = 1,
    ["components"] = new JsonArray(l.Select(x => (JsonNode)new JsonObject { ["type"] = 2, ["style"] = 5, ["label"] = x.Label, ["url"] = x.Url, ["emoji"] = new JsonObject { ["name"] = x.Emoji } }).ToArray()),
}];
async Task Post(string channel, JsonArray embeds, JsonArray? components = null)
{
    var body = new JsonObject { ["embeds"] = embeds };
    if (components is not null) body["components"] = components;
    try { await Api(HttpMethod.Post, $"channels/{ids[channel]}/messages", body); }
    catch (Exception ex) { Warn($"Nachricht in {channel}", ex); }
}
string C(string key) => ids.TryGetValue(key, out var id) ? $"<#{id}>" : $"#{key}";
string R(string key) => $"<@&{roles[key]}>";
const string Banner = "https://raw.githubusercontent.com/RyanTMP/Haulix/main/branding/Haulix_ETS2_Logger.png";

await Post("welcome", [
    Embed("👋 Willkommen bei HAULIX!", $"""
        **HAULIX** ist dein kostenloses Fahrtenbuch und Beifahrer für **Euro Truck Simulator 2**.
        Jede Lieferung wird automatisch gespeichert, du siehst Live-Daten zu deinem Auftrag, eine Karte der ganzen Spielwelt und ein HUD über dem Spiel – ohne Konto, ohne Werbung.

        **So geht's los**
        1️⃣ Lies die {C("rules")}
        2️⃣ Hol dir HAULIX in {C("download")}
        3️⃣ Wähle deine Rollen unter **Kanäle & Rollen**
        4️⃣ Sag Hallo in {C("chat")} oder {C("english")}

        Fragen? → {C("faq")} und {C("supportForum")}
        """, Amber, Banner),
    Embed("👋 Welcome to HAULIX!", $"""
        **HAULIX** is your free logbook and co-driver for **Euro Truck Simulator 2** – automatic job logging, a live job page, the full ETS2 map and an in-game HUD. No account, no ads.

        Read the {C("rules")}, grab HAULIX in {C("download")}, pick your roles in **Channels & Roles** and say hi in {C("english")}.
        """, Amber),
], Links(("Download", Releases, "⬇️"), ("Website", Website, "🌐"), ("GitHub", Repo, "💻")));

await Post("rules", [
    Embed("📜 Regeln", """
        **1. Respekt** – Kein Beleidigen, Hetzen, Diskriminieren oder Belästigen. Wir sind alle hier, um Spaß am Truckern zu haben.
        **2. Kein Spam** – Keine Werbung, Fremd-Einladungen oder Massen-Pings. VTC-Werbung nur in der VTC-Suche.
        **3. Richtiger Kanal** – Hilfe im Support-Forum, Fehler in Bug-Reports, Ideen in Ideen.
        **4. Keine illegalen Inhalte** – Keine Cracks, Piraterie, geleakten DLCs oder Cheats.
        **5. Jugendfrei** – Keine NSFW-, Gewalt- oder Schockinhalte.
        **6. Datenschutz** – Keine privaten Daten anderer posten.
        **7. TruckersMP** – Die TruckersMP-Regeln gelten weiter. Die Anti-AFK-Funktion von HAULIX verstößt gegen deren Regeln; wir helfen nicht beim Umgehen von Bans.
        **8. Team** – Anweisungen des Teams gelten. Probleme mit einer Entscheidung? Schreib dem Team privat.
        **9. Discord** – Es gelten die Discord-Nutzungsbedingungen und Community-Richtlinien.

        Verstöße können zu Verwarnung, Timeout, Kick oder Bann führen.
        """, Amber),
    Embed("📜 Rules (English)", """
        **1.** Be respectful – no insults, hate or harassment. **2.** No spam or advertising (VTC ads only in the VTC channel).
        **3.** Use the right channel. **4.** No piracy, cracks, leaked DLCs or cheats. **5.** Keep it SFW. **6.** Don't share other people's private data.
        **7.** TruckersMP rules still apply – HAULIX's anti-AFK option breaks them and we won't help with bans. **8.** Follow the team's instructions.
        **9.** Discord's Terms of Service and Community Guidelines apply.
        """, Grey),
]);

await Post("download", [
    Embed("⬇️ HAULIX herunterladen", """
        **1.** Lade **`Haulix.exe`** aus dem neuesten Release herunter (Button unten).
        **2.** Starte die Datei und klicke **Installieren** – ohne Adminrechte, nur für deinen Windows-Benutzer. Das Telemetrie-Plugin für ETS2 wird automatisch eingerichtet.
        **3.** Starte ETS2 und bestätige einmal die Frage nach den *erweiterten SDK-Funktionen*. HAULIX zeigt dann **LIVE**.

        **Voraussetzungen:** Windows 10 oder 11 und ETS2. Fehlt die WebView2-Runtime, installiert das Setup sie selbst. .NET brauchst du nicht.
        **„Der Computer wurde geschützt"?** HAULIX ist noch nicht signiert → *Weitere Informationen → Trotzdem ausführen*.
        **Updates** kommen automatisch – HAULIX fragt beim Start nach.
        """, Green,
        fields: [("🇬🇧 English", "Download **Haulix.exe**, run it, click **Install**, start ETS2 and accept the SDK prompt once. Windows 10/11 – WebView2 is installed automatically if missing.", false)]),
], Links(("Haulix.exe herunterladen", Releases, "⬇️"), ("Changelog", $"{Repo}/blob/main/CHANGELOG.md", "📝"), ("Website", Website, "🌐")));

await Post("faq", [
    Embed("❓ Häufige Fragen · FAQ", $"""
        **HAULIX zeigt OFFLINE, obwohl ETS2 läuft?**
        Das Telemetrie-Plugin fehlt oder wurde nicht bestätigt. Einstellungen → ETS2 → Spiel & Profil zeigt den Status; beim Spielstart die SDK-Frage mit *OK* bestätigen.

        **HUD oder Benachrichtigungen erscheinen nicht im Spiel?**
        ETS2 muss im *randlosen Vollbild* oder Fenstermodus laufen. HAULIX stellt das um: Einstellungen → ETS2 → Benachrichtigungen & Töne → ETS2-Anzeigemodus.

        **Wo liegen meine Daten?**
        Nur auf deinem PC in `%LOCALAPPDATA%\Haulix`. Es gibt kein Konto und keine Datenübertragung – nur die Update-Prüfung fragt GitHub nach neuen Versionen.

        **Funktioniert HAULIX mit TruckersMP?**
        Ja. Die AFK-Warnung ist erlaubt; die optionale Anti-AFK-Nachricht verstößt gegen die TruckersMP-Regeln und ist standardmäßig aus.

        **Kommt ATS-Unterstützung?**
        Ja, geplant – siehe {C("roadmap")}.

        **Mein Problem steht hier nicht?** → {C("supportForum")}
        """, Blue),
]);

await Post("roadmap", [
    Embed("🗺️ Roadmap", """
        **✅ Schon da**
        Fahrtenbuch mit Fahrscore · Aktueller Auftrag mit Echtzeit-ETA · komplette ETS2-Karte · Ingame-HUD (frei platzierbar) · Benachrichtigungen, Töne & KI-Stimmen · 60+ Erfolge · Deutsch & Englisch · Auto-Updates

        **🔜 Als Nächstes**
        VTC finden & beitreten · Firmenseiten · Events & Konvois · Auftragsbörse · Bestenlisten · Live-Karte mit Freunden · Cloud-Sync

        **🔭 Später**
        American Truck Simulator · Discord-Status (Rich Presence)

        Wünsche? → Ideen-Forum 💡
        """, Purple),
]);

await Post("roles", [
    Embed("🎭 Rollen", $"""
        Deine Rollen wählst du ganz oben in der Kanalliste unter **Kanäle & Rollen** (Channels & Roles).

        **Sprache:** {R("de")} · {R("en")}
        **Spiele:** {R("ets2")} · {R("ats")} · {R("tmp")}
        **Benachrichtigungen:** {R("pingUpdates")} – neue Versionen · {R("pingEvents")} – Konvois & Events · {R("pingBeta")} – Test-Versionen

        **Team & Besonderes**
        {R("founder")} – Gründer von HAULIX
        {R("team")} / {R("mod")} – helfen und moderieren
        {R("beta")} – testen neue Versionen vorab (Zugang zu 🧪 BETA)
        {R("creator")} – zeigen HAULIX in Videos & Streams
        {R("vtc")} – leiten eine VTC
        """, Amber),
]);

await Post("changelog", [
    Embed("📝 Changelog", $"Hier erscheinen neue HAULIX-Versionen automatisch, sobald sie auf GitHub veröffentlicht werden. {R("pingUpdates")} bekommt Bescheid.\nNew HAULIX releases appear here automatically.", Amber),
], Links(("Alle Versionen", $"{Repo}/releases", "📦")));

await Post("vtc", [Embed("🤝 VTC-Suche", "Stell deine VTC vor oder such eine: Name, Sprache, Spiel, TruckersMP ja/nein, Link. **Eine Nachricht pro Stunde**, bitte keine Einladungen per DM.\nIntroduce your VTC or look for one – one post per hour.", Green)]);

// ---------------------------------------------------------------- welcome screen, onboarding, automod
if (communityOn)
{
    try
    {
        await Api(HttpMethod.Patch, $"guilds/{guildId}/welcome-screen", new JsonObject
        {
            ["enabled"] = true,
            ["description"] = "Die Community von HAULIX – dem kostenlosen Fahrtenbuch & Beifahrer für ETS2.",
            ["welcome_channels"] = new JsonArray(
                new JsonObject { ["channel_id"] = ids["download"], ["description"] = "HAULIX herunterladen", ["emoji_name"] = "⬇️" },
                new JsonObject { ["channel_id"] = ids["rules"], ["description"] = "Die Regeln lesen", ["emoji_name"] = "📜" },
                new JsonObject { ["channel_id"] = ids["supportForum"], ["description"] = "Hilfe bekommen", ["emoji_name"] = "🆘" },
                new JsonObject { ["channel_id"] = ids["chat"], ["description"] = "Mit anderen Truckern reden", ["emoji_name"] = "💬" }),
        });
        Console.WriteLine("Willkommensbildschirm gesetzt");
    }
    catch (Exception ex) { Warn("Willkommensbildschirm", ex); }

    try
    {
        JsonObject Opt(string title, string desc, string emoji, params string[] roleKeys) => new()
        {
            ["title"] = title, ["description"] = desc, ["emoji"] = new JsonObject { ["name"] = emoji },
            ["role_ids"] = new JsonArray(roleKeys.Select(k => (JsonNode)roles[k]).ToArray()), ["channel_ids"] = new JsonArray(),
        };
        JsonObject Prompt(string title, bool single, bool required, params JsonObject[] options) => new()
        {
            ["type"] = 0, ["title"] = title, ["single_select"] = single, ["required"] = required, ["in_onboarding"] = true, ["options"] = new JsonArray(options),
        };
        await Api(HttpMethod.Put, $"guilds/{guildId}/onboarding", new JsonObject
        {
            ["enabled"] = true, ["mode"] = 0,
            ["default_channel_ids"] = new JsonArray(new[] { "welcome", "rules", "news", "changelog", "download", "roadmap", "roles", "faq", "chat", "english", "screens", "tours", "achievements", "offtopic", "tmp" }
                .Where(ids.ContainsKey).Select(k => (JsonNode)ids[k]).ToArray()),
            ["prompts"] = new JsonArray(
                Prompt("Welche Sprache sprichst du? · Language", false, true,
                    Opt("Deutsch", "Deutschsprachige Community", "🇩🇪", "de"), Opt("English", "English-speaking community", "🇬🇧", "en")),
                Prompt("Was spielst du? · What do you play?", false, false,
                    Opt("Euro Truck Simulator 2", "", "🇪🇺", "ets2"), Opt("American Truck Simulator", "", "🇺🇸", "ats"), Opt("TruckersMP", "Multiplayer", "🛣️", "tmp")),
                Prompt("Worüber willst du informiert werden? · Notifications", false, false,
                    Opt("Updates", "Neue HAULIX-Versionen", "🔔", "pingUpdates"), Opt("Events & Konvois", "Gemeinsame Fahrten", "🚛", "pingEvents"),
                    Opt("Beta-News", "Test-Versionen vorab", "🧪", "pingBeta"))),
        });
        Console.WriteLine("Onboarding (Kanäle & Rollen) eingerichtet");
    }
    catch (Exception ex) { Warn("Onboarding (kann auch in den Servereinstellungen → Onboarding eingerichtet werden)", ex); }
}

try
{
    JsonArray Actions(bool block = true) => new(new JsonNode[]
    {
        block ? new JsonObject { ["type"] = 1, ["metadata"] = new JsonObject { ["custom_message"] = "Diese Nachricht wurde von AutoMod blockiert. · Blocked by AutoMod." } } : null!,
        new JsonObject { ["type"] = 2, ["metadata"] = new JsonObject { ["channel_id"] = ids["modLog"] } },
    }.Where(x => x is not null).ToArray());
    var exempt = new JsonArray((JsonNode)roles["team"], roles["mod"]);
    await Api(HttpMethod.Post, $"guilds/{guildId}/auto-moderation/rules", new JsonObject { ["name"] = "Spam", ["event_type"] = 1, ["trigger_type"] = 3, ["actions"] = Actions(), ["enabled"] = true, ["exempt_roles"] = exempt.DeepClone() });
    await Api(HttpMethod.Post, $"guilds/{guildId}/auto-moderation/rules", new JsonObject { ["name"] = "Massen-Pings", ["event_type"] = 1, ["trigger_type"] = 5, ["trigger_metadata"] = new JsonObject { ["mention_total_limit"] = 5 }, ["actions"] = Actions(), ["enabled"] = true, ["exempt_roles"] = exempt.DeepClone() });
    await Api(HttpMethod.Post, $"guilds/{guildId}/auto-moderation/rules", new JsonObject { ["name"] = "Fremde Discord-Einladungen", ["event_type"] = 1, ["trigger_type"] = 1, ["trigger_metadata"] = new JsonObject { ["regex_patterns"] = new JsonArray("(discord\\.gg|discord(app)?\\.com/invite)/\\w+") }, ["actions"] = Actions(), ["enabled"] = true, ["exempt_roles"] = exempt.DeepClone() });
    await Api(HttpMethod.Post, $"guilds/{guildId}/auto-moderation/rules", new JsonObject { ["name"] = "Beleidigungen", ["event_type"] = 1, ["trigger_type"] = 4, ["trigger_metadata"] = new JsonObject { ["presets"] = new JsonArray(1, 2, 3) }, ["actions"] = Actions(), ["enabled"] = true });
    Console.WriteLine("AutoMod-Regeln angelegt (Meldungen in #mod-log)");
}
catch (Exception ex) { Warn("AutoMod", ex); }

// ---------------------------------------------------------------- invite + release webhook
string? inviteUrl = null, hookUrl = null;
try
{
    var inv = await Api(HttpMethod.Post, $"channels/{ids["welcome"]}/invites", new JsonObject { ["max_age"] = 0, ["max_uses"] = 0, ["unique"] = false });
    inviteUrl = $"https://discord.gg/{inv!["code"]}";
}
catch (Exception ex) { Warn("Einladungslink", ex); }
try
{
    var hook = await Api(HttpMethod.Post, $"channels/{ids["changelog"]}/webhooks", new JsonObject { ["name"] = "HAULIX Releases" });
    hookUrl = $"https://discord.com/api/webhooks/{hook!["id"]}/{hook["token"]}";
}
catch (Exception ex) { Warn("Webhook", ex); }

Console.WriteLine();
Console.WriteLine("Fertig! 🎉");
if (inviteUrl is not null) Console.WriteLine($"  Einladungslink (dauerhaft): {inviteUrl}");
if (hookUrl is not null)
{
    var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "haulix-discord-webhook.txt");
    File.WriteAllText(file, hookUrl + "/github" + Environment.NewLine);
    Console.WriteLine($"  Release-Webhook gespeichert in: {file}");
    Console.WriteLine("  (geheim halten – Anleitung für GitHub steht in tools/discord/README.md)");
}
return 0;

// ---------------------------------------------------------------- helpers
async Task<JsonNode?> Api(HttpMethod method, string path, JsonNode? body = null)
{
    for (var attempt = 0; ; attempt++)
    {
        using var req = new HttpRequestMessage(method, path);
        if (body is not null) req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var res = await http.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        if (res.StatusCode == (HttpStatusCode)429 && attempt < 8)
        {
            var wait = JsonNode.Parse(text)?["retry_after"]?.GetValue<double>() ?? 2;
            await Task.Delay(TimeSpan.FromSeconds(wait + 0.25));
            continue;
        }
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"{(int)res.StatusCode} {method} {path}: {text}");
        await Task.Delay(250); // stay well below the rate limits
        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }
}

static void Warn(string what, Exception ex) =>
    Console.WriteLine($"  ! {what} übersprungen: {(ex.Message.Length > 300 ? ex.Message[..300] : ex.Message)}");

static string FindRepoRoot()
{
    var d = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (d is not null && !File.Exists(Path.Combine(d.FullName, "Haulix.slnx"))) d = d.Parent;
    return d?.FullName ?? Directory.GetCurrentDirectory();
}
