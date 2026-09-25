using System.Text.Json;
using System.Text.Json.Serialization;
using Haulix.Core.Data;

namespace Haulix.Core.Settings;

public sealed class AppSettings
{
    public bool SetupComplete { get; set; }
    public GeneralSettings General { get; set; } = new();
    public Ets2Settings Ets2 { get; set; } = new();
    public TelemetrySettings Telemetry { get; set; } = new();
    public DataSettings Data { get; set; } = new();
    public AppearanceSettings Appearance { get; set; } = new();
    public NotificationSettings Notifications { get; set; } = new();
    public TruckersMpSettings TruckersMp { get; set; } = new();
    public HudSettings Hud { get; set; } = new();
    public OnlineSettings Online { get; set; } = new();
}

public sealed class GeneralSettings
{
    /// <summary>"auto" (Windows display language), "en" or "de".</summary>
    public string Language { get; set; } = "auto";
    /// <summary>True once the user picked a language (settings or installer); otherwise Windows decides.</summary>
    public bool LanguageChosen { get; set; }
    public string Units { get; set; } = "metric";        // metric | imperial
    public string Currency { get; set; } = "EUR";
    public string Theme { get; set; } = "dark";           // dark | midnight | light
    public string StartPage { get; set; } = "dashboard";  // dashboard | last
    public bool StartMinimized { get; set; }
    public bool LaunchWithWindows { get; set; }
    public bool MinimizeToTray { get; set; }
    /// <summary>Check the HAULIX releases on GitHub for new versions (on start and every 6 hours).</summary>
    public bool UpdateCheck { get; set; } = true;
    /// <summary>Optional custom update manifest URL (JSON: version, url, notes). Empty = GitHub releases.</summary>
    public string UpdateFeedUrl { get; set; } = "";
    /// <summary>Version whose changelog the user has seen (the "What's new" window opens after an update).</summary>
    public string? LastSeenVersion { get; set; }
    /// <summary>Discord Rich Presence: shows your current drive on your Discord profile.</summary>
    public bool DiscordPresence { get; set; } = true;
    /// <summary>Application ID of the Discord app used for Rich Presence (Discord Developer Portal).</summary>
    public string DiscordAppId { get; set; } = "";
    /// <summary>Small always-on-top HUD over the game: speed limit, real-time ETA, next milestone.</summary>
    public bool Hud { get; set; }
}

public sealed class Ets2Settings
{
    public bool AutoDetect { get; set; } = true;
    public string? GamePath { get; set; }
    public string? DocumentsPath { get; set; }
    public string? ProfilePath { get; set; }
    public string SaveSelection { get; set; } = "latest";  // latest | autosave | <save folder name>
    public bool WatchSaves { get; set; } = true;
    public bool ImportSaveHistory { get; set; } = true;
}

public sealed class TelemetrySettings
{
    public int UpdateHz { get; set; } = 10;
    public bool RecordRoutes { get; set; } = true;
    public bool RecordFreeRoam { get; set; } = true;
    public int RoutePointSpacingM { get; set; } = 150;
    public Dictionary<string, bool> Fields { get; set; } = new()
    {
        ["vehicle"] = true, ["drivetrain"] = true, ["fluids"] = true, ["damage"] = true,
        ["lights"] = true, ["navigation"] = true, ["job"] = true, ["trailer"] = true,
    };
}

public sealed class DataSettings
{
    public bool AutoBackup { get; set; } = true;
    public int BackupIntervalHours { get; set; } = 24;
    public int BackupKeep { get; set; } = 10;
    public string? BackupFolder { get; set; }
}

/// <summary>
/// In-game HUD shown over ETS2 (borderless/windowed mode); the master switch is General.Hud.
/// A job card like SpedV (route, progress, ETA, …) – the game has its own map, so there is no mini map.
/// </summary>
public sealed class HudSettings
{
    public bool CardEnabled { get; set; } = true;
    /// <summary>topLeft | topCenter | topRight | middleLeft | middleRight | bottomLeft | bottomCenter | bottomRight | custom</summary>
    public string Position { get; set; } = "topRight";
    /// <summary>Custom position: centre of the card in percent of the screen (0–100). Set by dragging ("Place on screen").</summary>
    public double X { get; set; } = 85;
    public double Y { get; set; } = 20;
    /// <summary>Distance from the screen edge in pixels (corner positions).</summary>
    public int Margin { get; set; } = 16;
    /// <summary>Legacy size preset (small | medium | large); used while <see cref="Scale"/> is 0.</summary>
    public string Size { get; set; } = "medium";
    /// <summary>Size in percent (60–180); 0 = from <see cref="Size"/>.</summary>
    public int Scale { get; set; }
    /// <summary>Card width in pixels at 100 % (220–420).</summary>
    public int Width { get; set; } = 290;
    /// <summary>dark | glass | light | contrast</summary>
    public string Theme { get; set; } = "dark";
    /// <summary>app (same as HAULIX) | amber | copper | green | blue | red | purple | white</summary>
    public string Accent { get; set; } = "app";
    /// <summary>compact | normal | roomy</summary>
    public string Density { get; set; } = "normal";
    public bool Rounded { get; set; } = true;
    public bool ShowHeader { get; set; } = true;
    public bool ShowCargo { get; set; } = true;
    public bool ShowRoute { get; set; } = true;
    public bool ShowProgress { get; set; } = true;
    /// <summary>Rows in order: remaining, etaReal, arrival, etaGame, deadline, income, company, speed, speedLimit, cruise, gear,
    /// fuel, fuelRange, rest, damage, truckDamage, trailerDamage, gameTime, clock.</summary>
    public List<string> Fields { get; set; } = ["remaining", "etaReal", "arrival", "deadline", "speed", "fuelRange"];
    /// <summary>Visibility in percent (20–100).</summary>
    public int Opacity { get; set; } = 90;
    /// <summary>Only while a job is active.</summary>
    public bool OnlyOnJob { get; set; }
}

/// <summary>
/// Anti-AFK message for TruckersMP. Off by default: automatically avoiding the server's inactivity kick is
/// against the TruckersMP rules and can get the account banned; the user enables it at their own risk.
/// </summary>
public sealed class TruckersMpSettings
{
    public bool AntiAfk { get; set; }
    /// <summary>The chat message that is sent while the player is inactive.</summary>
    public string Message { get; set; } = DefaultMessage;

    public const string DefaultMessage = "AFK - back soon! Logging my trips with HAULIX, free ETS2 tracker: www.haulix-logging.com";
    /// <summary>The default of HAULIX 0.0.3 – 0.0.6; replaced by the new default when still unchanged.</summary>
    public const string LegacyDefaultMessage = "AFK - back soon";
    /// <summary>Minutes of inactivity between messages (the server kicks after 10 min when full).</summary>
    public int IntervalMinutes { get; set; } = 8;
    /// <summary>Key that opens the TruckersMP chat (default Y).</summary>
    public string ChatKey { get; set; } = "Y";
    /// <summary>Set once the user confirmed the rules warning.</summary>
    public bool RiskAccepted { get; set; }
}

public sealed class NotificationSettings
{
    /// <summary>Notifications about the tracked job (accepted, delivered, cancelled, fines).</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Distance milestones and halfway.</summary>
    public bool Progress { get; set; } = true;
    /// <summary>Deadline, fuel, cargo damage and rest warnings.</summary>
    public bool Warnings { get; set; } = true;
    /// <summary>Show them in an always-on-top overlay over the game while HAULIX is in the background.</summary>
    public bool Overlay { get; set; } = true;
    /// <summary>Read notifications aloud (Windows speech) – works even in exclusive fullscreen.</summary>
    public bool Voice { get; set; }
    /// <summary>natural (Piper AI voice, downloaded on request) | windows (installed Windows voices).</summary>
    public string VoiceEngine { get; set; } = "natural";
    /// <summary>Piper voice id (e.g. de_DE-thorsten-medium); empty = default for the UI language.</summary>
    public string VoiceId { get; set; } = "";
    /// <summary>Windows voice name; empty = the first voice of the UI language.</summary>
    public string WindowsVoice { get; set; } = "";
    /// <summary>Speaking rate, 0.7 (slow) – 1.4 (fast).</summary>
    public double VoiceRate { get; set; } = 1.0;
    /// <summary>Volume 0–100.</summary>
    public int VoiceVolume { get; set; } = 90;
    /// <summary>Play a short sound with notifications (HAULIX's own synthesised chimes).</summary>
    public bool Sounds { get; set; } = true;
    /// <summary>soft (bell-like) | digital</summary>
    public string SoundStyle { get; set; } = "soft";
    /// <summary>Sound volume 0–100.</summary>
    public int SoundVolume { get; set; } = 70;
    /// <summary>Sound for job updates: accepted, delivered, cancelled, fines.</summary>
    public bool SoundJob { get; set; } = true;
    /// <summary>Sound for milestones, route changes and achievements.</summary>
    public bool SoundProgress { get; set; } = true;
    /// <summary>Sound for deadline, fuel, damage and rest warnings.</summary>
    public bool SoundWarnings { get; set; } = true;
    /// <summary>Alarm sound with the TruckersMP inactivity warning.</summary>
    public bool SoundAfk { get; set; } = true;
    /// <summary>On TruckersMP: warn before the server's inactivity kick (10 min on full servers, 30 min otherwise).</summary>
    public bool AfkWarning { get; set; } = true;
    /// <summary>Screen corner for the overlay: topRight | topLeft | bottomRight | bottomLeft.</summary>
    public string Position { get; set; } = "topRight";
}

public sealed class AppearanceSettings
{
    public string Accent { get; set; } = "amber";  // amber | copper | ice | signal
    public bool Compact { get; set; }
    public bool Animations { get; set; } = true;
    public bool Transparency { get; set; } = true;
    public bool SidebarCollapsed { get; set; }
}

/// <summary>
/// Consent and privacy choices for the future HAULIX online services. Privacy by default: nothing is shared
/// until the user accepts the terms and turns a feature on.
/// </summary>
public sealed class OnlineSettings
{
    /// <summary>Version of the license agreement (Part B) and privacy policy the user accepted; null = not yet.</summary>
    public string? AcceptedTermsVersion { get; set; }
    public DateTime? AcceptedTermsUtc { get; set; }
    /// <summary>Show my name and results on public leaderboards.</summary>
    public bool ShowOnLeaderboards { get; set; }
    /// <summary>Back up my logbook to my HAULIX account.</summary>
    public bool CloudSync { get; set; }
    /// <summary>Share my deliveries with my VTC.</summary>
    public bool ShareWithVtc { get; set; }
}

/// <summary>Persists <see cref="AppSettings"/> as one JSON document in the local database.</summary>
public sealed class SettingsStore(Database db)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
    };

    private AppSettings? _cached;

    public AppSettings Load()
    {
        if (_cached is not null) return _cached;
        using var c = db.Open();
        var json = Database.Scalar<string>(c, "SELECT value FROM settings WHERE key = 'app'");
        _cached = string.IsNullOrEmpty(json) ? new AppSettings() : JsonSerializer.Deserialize<AppSettings>(json, Json) ?? new AppSettings();
        if (_cached.TruckersMp.Message?.Trim() == TruckersMpSettings.LegacyDefaultMessage) _cached.TruckersMp.Message = TruckersMpSettings.DefaultMessage;
        return _cached;
    }

    public void Save(AppSettings s)
    {
        _cached = s;
        using var c = db.Open();
        Database.Exec(c, "INSERT INTO settings(key, value) VALUES('app', $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value",
            ("$v", JsonSerializer.Serialize(s, Json)));
    }

    public void Invalidate() => _cached = null;
}
