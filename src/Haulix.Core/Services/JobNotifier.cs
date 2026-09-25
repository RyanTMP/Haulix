using System.Globalization;
using Haulix.Core.Settings;
using Haulix.Core.Telemetry;

namespace Haulix.Core.Services;

/// <summary>A notification about the tracked job. Kind: info | success | warning | critical.</summary>
public sealed record Notice(string Kind, string Category, string Title, string Message, DateTime AtUtc);

/// <summary>
/// Watches the current job and raises short notifications when something worth knowing happens while
/// driving: job accepted, distance milestones, halfway, deadline at risk, fuel range too short, new cargo
/// damage, rest needed, delivered / cancelled, fines. Each notice fires once per job.
/// </summary>
public sealed class JobNotifier(Func<AppSettings> settings)
{
    private static readonly double[] Milestones = [100, 50, 10, 2];

    public event Action<Notice>? Raised;

    private string _jobKey = "";
    private readonly HashSet<string> _fired = new();
    private double _lastDamageNotice;
    private DateTime _lastCheckUtc;

    /// <summary>Is ETS2 running with TruckersMP? (set by the engine)</summary>
    public Func<bool> IsTruckersMp { get; set; } = () => false;

    // Inactivity on TruckersMP: warnings before the server's auto kick. The optional anti-AFK message
    // (Settings → TruckersMP, off by default, against the TruckersMP rules) is sent by the desktop host.
    private DateTime _activeUtc = DateTime.UtcNow;
    private (double X, double Z, double Steer, double Throttle, double Brake, double Clutch) _lastInput;
    private int _afkStage;

    /// <summary>Last time the player gave any input (only tracked while TruckersMP runs).</summary>
    public DateTime LastActivityUtc => _activeUtc;

    private void CheckAfk(TelemetrySnapshot s, DateTime now)
    {
        var input = (s.X, s.Z, s.Steering, s.Throttle, s.Brake, s.Clutch);
        var moved = Math.Abs(input.X - _lastInput.X) + Math.Abs(input.Z - _lastInput.Z) > 2
                    || Math.Abs(input.Steering - _lastInput.Steer) + Math.Abs(input.Throttle - _lastInput.Throttle)
                     + Math.Abs(input.Brake - _lastInput.Brake) + Math.Abs(input.Clutch - _lastInput.Clutch) > 0.02;
        _lastInput = (input.X, input.Z, input.Steering, input.Throttle, input.Brake, input.Clutch);
        if (moved || !IsTruckersMp())
        {
            _activeUtc = now;
            _afkStage = 0;
            return;
        }
        // The anti-AFK message keeps the player online; warnings would only be noise then.
        if (!settings().Notifications.AfkWarning || settings().TruckersMp.AntiAfk) return;
        var idle = (now - _activeUtc).TotalMinutes;
        if (idle >= 8 && _afkStage < 1) { _afkStage = 1; RaiseAlways("warning", "afk", T("afkTitle"), T("afkMsg10")); }
        else if (idle >= 27 && _afkStage < 2) { _afkStage = 2; RaiseAlways("critical", "afk", T("afkTitle"), T("afkMsg30")); }
    }

    // Service reminders: once at 15 % and once at 30 % wear; re-armed after a repair (below 5 %).
    private int _truckWearLevel, _trailerWearLevel;

    private void CheckWear(string part, double wear, ref int level)
    {
        if (wear < 0.05) { level = 0; return; }
        var reached = wear >= 0.30 ? 2 : wear >= 0.15 ? 1 : 0;
        if (reached <= level) return;
        level = reached;
        Maintenance(part, wear);
    }

    private void RaiseAlways(string kind, string category, string title, string message) =>
        Raised?.Invoke(new Notice(kind, category, title, message, DateTime.UtcNow));

    public void OnSample(TelemetrySnapshot s)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastCheckUtc).TotalSeconds < 1) return;
        _lastCheckUtc = now;
        CheckAfk(s, now);
        CheckWear("truck", s.TruckDamage, ref _truckWearLevel);
        if (s.TrailerAttached) CheckWear("trailer", s.TrailerDamage, ref _trailerWearLevel);
        if (!s.OnJob || string.IsNullOrEmpty(s.DestinationCityId))
        {
            _jobKey = "";
            return;
        }

        var key = $"{s.CargoId}|{s.SourceCompanyId}|{s.DestinationCompanyId}|{s.DestinationCityId}|{s.JobDeadlineGameMinutes}";
        // The game GPS always points at the job; the ETA may follow a manual HAULIX destination instead.
        var remaining = s.RouteDistanceKm > 0 ? s.RouteDistanceKm : s.Eta is { TargetsJob: true } je ? je.RemainingKm : (double?)null;
        if (key != _jobKey)
        {
            _jobKey = key;
            _fired.Clear();
            _lastDamageNotice = s.CargoDamage;
            // Milestones already behind us at the start (short jobs) are not announced.
            var start = remaining ?? s.PlannedDistanceKm;
            foreach (var m in Milestones) if (start <= m * 1.3) _fired.Add($"km{m}");
            if (s.PlannedDistanceKm < 100) _fired.Add("half");
            Raise("info", "job", T("jobTitle", s.Cargo, s.DestinationCity),
                T("jobStarted", s.SourceCity, s.DestinationCompany, s.DestinationCity, Money(s.JobIncome), Dist(s.PlannedDistanceKm)));
            return;
        }
        if (s.Paused) return;

        var eta = s.Eta is { TargetsJob: true } e ? T("etaSuffix", Duration(e.RealSeconds), Clock(e.ArrivalUtc)) : "";

        // Distance milestones and halfway
        if (remaining is { } left)
        {
            foreach (var m in Milestones)
                if (left <= m && Once($"km{m}"))
                {
                    Raise("info", "progress", T("milestoneTitle", Dist(m), s.DestinationCity), m <= 2 ? T("almostThere", s.DestinationCompany) : eta.TrimStart(' ', '·'));
                    break;
                }
            if (s.PlannedDistanceKm > 0 && left <= s.PlannedDistanceKm / 2 && Once("half"))
                Raise("info", "progress", T("halfTitle"), T("halfMsg", Dist(left), s.DestinationCity) + eta);
        }

        // Deadline
        if (s.Eta?.DeadlineMarginGameMinutes is { } margin)
        {
            if (margin < 0 && Once("late"))
                Raise("critical", "warning", T("lateTitle"), T("lateMsg", GameDuration(-margin)));
            else if (margin is >= 0 and < 60 && Once("tight"))
                Raise("warning", "warning", T("tightTitle"), T("tightMsg", GameDuration(margin)));
        }

        // Fuel range shorter than the way to go
        if (remaining is { } rem && s.FuelRangeKm > 0 && rem > s.FuelRangeKm + 20 && Once("fuel"))
            Raise("warning", "warning", T("fuelTitle"), T("fuelMsg", Dist(s.FuelRangeKm), Dist(rem)));
        if (remaining is { } rem2 && s.FuelRangeKm > rem2 + 50) _fired.Remove("fuel"); // refuelled: warn again if needed

        // New cargo damage (every further 2 %)
        if (s.CargoDamage - _lastDamageNotice >= 0.02)
        {
            _lastDamageNotice = s.CargoDamage;
            Raise("warning", "warning", T("damageTitle"), T("damageMsg", (s.CargoDamage * 100).ToString("0.#", Culture)));
        }

        // Rest
        if (s.RestStopMinutes is > 0 and <= 60 && Once("rest"))
            Raise("warning", "warning", T("restTitle"), T("restMsg", GameDuration(s.RestStopMinutes)));
        if (s.RestStopMinutes > 120) _fired.Remove("rest");
    }

    public void OnGameEvent(GameEvent e)
    {
        var g = e.Snapshot.Gameplay;
        switch (e.Type)
        {
            case GameEventType.JobDelivered:
                Raise("success", "job", T("deliveredTitle", e.Snapshot.Cargo), T("deliveredMsg", Money(g.DeliveredRevenue), g.DeliveredXp.ToString(Culture)));
                _jobKey = "";
                break;
            case GameEventType.JobCancelled:
                Raise("warning", "job", T("cancelledTitle"), T("cancelledMsg", Money(g.CancelledPenalty)));
                _jobKey = "";
                break;
            case GameEventType.Fined:
                Raise("warning", "job", T("fineTitle"), T("fineMsg", Money(g.FineAmount), Offence(g.FineOffence)));
                break;
        }
    }

    /// <summary>An achievement was reached.</summary>
    public void Achievement(AchievementInfo a) => Raise("success", "achievement", T("achTitle", a.Title), a.Description);

    /// <summary>Truck or trailer wear crossed a service threshold.</summary>
    public void Maintenance(string part, double wear) =>
        Raise(wear >= 0.3 ? "critical" : "warning", "warning", T("wearTitle"), T("wearMsg", T(part), (wear * 100).ToString("0", Culture)));

    /// <summary>Sample notification (Settings → Notifications → Test); always shown, even over HAULIX.</summary>
    public void Test() => Raised?.Invoke(new Notice("info", "test", T("testTitle"), T("testMsg"), DateTime.UtcNow));

    private bool Once(string id) => _fired.Add(id);

    private void Raise(string kind, string category, string title, string message)
    {
        var n = settings().Notifications;
        if (!n.Enabled) return;
        if (category == "progress" && !n.Progress) return;
        if (category == "warning" && !n.Warnings) return;
        Raised?.Invoke(new Notice(kind, category, title, message.Trim(), DateTime.UtcNow));
    }

    /* ---------------- formatting & language ---------------- */

    public bool German
    {
        get
        {
            var g = settings().General;
            var lang = g.LanguageChosen && g.Language is "en" or "de" ? g.Language : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return lang == "de";
        }
    }

    private CultureInfo Culture => German ? CultureInfo.GetCultureInfo("de-DE") : CultureInfo.GetCultureInfo("en-GB");

    private string Money(double eur) => string.Format(Culture, "{0:N0} €", eur);

    private string Dist(double km) => settings().General.Units == "imperial"
        ? string.Format(Culture, "{0:N0} mi", km * 0.621371)
        : string.Format(Culture, km < 10 ? "{0:0.#} km" : "{0:N0} km", km);

    private static string Duration(double seconds)
    {
        var m = (int)Math.Round(seconds / 60);
        return m < 60 ? $"{Math.Max(1, m)} min" : $"{m / 60} h {m % 60:00} min";
    }

    private static string GameDuration(int minutes) => minutes < 60 ? $"{minutes} min" : $"{minutes / 60} h {minutes % 60:00} min";

    private static string Clock(DateTime utc) => utc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

    private static string Offence(string id) => string.IsNullOrEmpty(id) ? "" : id.Replace('_', ' ');

    private string T(string key, params object[] args) =>
        string.Format(Culture, (German ? De : En)[key], args);

    private static readonly Dictionary<string, string> En = new()
    {
        ["jobTitle"] = "Job accepted: {0} → {1}",
        ["jobStarted"] = "From {0} to {1}, {2} · {3} · {4}",
        ["etaSuffix"] = " · arrival in {0} (≈ {1})",
        ["milestoneTitle"] = "{0} to {1}",
        ["almostThere"] = "Almost there — {0} is just ahead.",
        ["halfTitle"] = "Halfway there",
        ["halfMsg"] = "{0} left to {1}",
        ["lateTitle"] = "Deadline at risk",
        ["lateMsg"] = "At this pace you arrive about {0} (game time) after the deadline.",
        ["tightTitle"] = "Deadline is tight",
        ["tightMsg"] = "Only {0} (game time) to spare at the current pace.",
        ["fuelTitle"] = "Refuel on the way",
        ["fuelMsg"] = "Fuel range {0}, but {1} still to go.",
        ["damageTitle"] = "Cargo damaged",
        ["damageMsg"] = "Cargo damage is now {0} %.",
        ["restTitle"] = "Rest needed soon",
        ["restMsg"] = "Sleep required in {0} (game time).",
        ["deliveredTitle"] = "Delivered: {0}",
        ["deliveredMsg"] = "{0} earned · {1} XP",
        ["cancelledTitle"] = "Job cancelled",
        ["cancelledMsg"] = "Penalty {0}",
        ["fineTitle"] = "Fined",
        ["fineMsg"] = "{0} · {1}",
        ["afkTitle"] = "Inactive on TruckersMP",
        ["afkMsg10"] = "8 minutes without input. On full servers TruckersMP kicks after 10 minutes of inactivity.",
        ["afkMsg30"] = "27 minutes without input. TruckersMP kicks after 30 minutes of inactivity.",
        ["achTitle"] = "Achievement unlocked: {0}",
        ["wearTitle"] = "Service your vehicle",
        ["wearMsg"] = "{0} wear is at {1} %. Visit a service shop soon.",
        ["truck"] = "Truck",
        ["trailer"] = "Trailer",
        ["testTitle"] = "Job updates appear here",
        ["testMsg"] = "While you drive, HAULIX shows milestones, deadline and fuel warnings over the game.",
    };

    private static readonly Dictionary<string, string> De = new()
    {
        ["jobTitle"] = "Auftrag angenommen: {0} → {1}",
        ["jobStarted"] = "Von {0} nach {1}, {2} · {3} · {4}",
        ["etaSuffix"] = " · Ankunft in {0} (≈ {1} Uhr)",
        ["milestoneTitle"] = "Noch {0} bis {1}",
        ["almostThere"] = "Gleich da — {0} liegt direkt vor dir.",
        ["halfTitle"] = "Halbzeit",
        ["halfMsg"] = "Noch {0} bis {1}",
        ["lateTitle"] = "Frist in Gefahr",
        ["lateMsg"] = "Bei diesem Tempo kommst du etwa {0} (Spielzeit) nach der Frist an.",
        ["tightTitle"] = "Frist wird knapp",
        ["tightMsg"] = "Nur noch {0} (Spielzeit) Puffer beim aktuellen Tempo.",
        ["fuelTitle"] = "Unterwegs tanken",
        ["fuelMsg"] = "Reichweite {0}, aber noch {1} zu fahren.",
        ["damageTitle"] = "Fracht beschädigt",
        ["damageMsg"] = "Frachtschaden liegt jetzt bei {0} %.",
        ["restTitle"] = "Bald Pause nötig",
        ["restMsg"] = "Schlafen nötig in {0} (Spielzeit).",
        ["deliveredTitle"] = "Abgeliefert: {0}",
        ["deliveredMsg"] = "{0} verdient · {1} XP",
        ["cancelledTitle"] = "Auftrag abgebrochen",
        ["cancelledMsg"] = "Strafe {0}",
        ["fineTitle"] = "Bußgeld",
        ["fineMsg"] = "{0} · {1}",
        ["afkTitle"] = "Inaktiv auf TruckersMP",
        ["afkMsg10"] = "Seit 8 Minuten keine Eingabe. Auf vollen Servern kickt TruckersMP nach 10 Minuten Inaktivität.",
        ["afkMsg30"] = "Seit 27 Minuten keine Eingabe. TruckersMP kickt nach 30 Minuten Inaktivität.",
        ["achTitle"] = "Erfolg freigeschaltet: {0}",
        ["wearTitle"] = "Fahrzeug in die Werkstatt",
        ["wearMsg"] = "{0}-Verschleiß liegt bei {1} %. Fahre bald in eine Werkstatt.",
        ["truck"] = "Lkw",
        ["trailer"] = "Auflieger",
        ["testTitle"] = "Hier erscheinen Auftrags-Updates",
        ["testMsg"] = "Während der Fahrt zeigt HAULIX Etappen, Fristen- und Tankwarnungen über dem Spiel an.",
    };
}
