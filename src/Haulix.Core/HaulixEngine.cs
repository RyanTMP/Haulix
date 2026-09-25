using System.Reflection;
using System.Text.Json;
using Haulix.Core.Data;
using Haulix.Core.Ets2;
using Haulix.Core.Services;
using Haulix.Core.Settings;
using Haulix.Core.Telemetry;

namespace Haulix.Core;

/// <summary>
/// The HAULIX local backend: owns the database, settings, ETS2 detection, telemetry, recording and backups.
/// Entirely offline – no sockets, no HTTP. The desktop host forwards UI requests to <see cref="Handle"/>
/// and relays <see cref="Push"/> notifications back to the UI.
/// </summary>
public sealed class HaulixEngine : IDisposable
{
    private readonly Timer _heartbeat;
    private readonly Timer _backupTimer;
    private DetectionResult? _detection;
    private DateTime _lastTelemetryPush;
    private readonly object _pushGate = new();

    public HaulixEngine(string dataFolder)
    {
        DataFolder = dataFolder;
        Directory.CreateDirectory(dataFolder);
        Db = new Database(Path.Combine(dataFolder, "haulix.db"));
        DbError = Db.Check();
        Db.Migrate();
        Settings = new SettingsStore(Db);
        Backups = new BackupService(Db, Settings, Path.Combine(dataFolder, "backups"));
        Profiles = new ProfileService(Db, Settings);
        Telemetry = new TelemetryService(() => _detection?.GamePath is not null);
        Recorder = new TripRecorder(Db, () => Profiles.ProfileId, () =>
        {
            var t = Settings.Load().Telemetry;
            return (t.RecordRoutes, t.RecordFreeRoam, Math.Clamp(t.RoutePointSpacingM, 25, 2000));
        });

        Notifier = new JobNotifier(() => Settings.Load()) { IsTruckersMp = TruckersMp.Active };
        Notifier.Raised += n => Push?.Invoke("notify", n);
        Achievements = new Achievements(Db, () => Profiles.Current, () => Notifier.German);

        Queries.PurgeDemo(Db);

        // HAULIX 0.0.8 removed the road map: free the space of the old map cache and the bundled map.
        _ = Task.Run(() =>
        {
            foreach (var old in new[] { Path.Combine(dataFolder, "map"), Path.Combine(AppContext.BaseDirectory, "map-bundle") })
                try { if (Directory.Exists(old)) Directory.Delete(old, recursive: true); } catch (Exception) { }
        });

        Telemetry.Sample += OnSample;
        Telemetry.StatusChanged += s =>
        {
            if (s.Telemetry is not (TelemetryState.Live or TelemetryState.Demo or TelemetryState.Paused)) Recorder.OnDisconnected();
            PushStatus();
        };
        Telemetry.GameEvent += e =>
        {
            Recorder.OnGameEvent(e);
            Notifier.OnGameEvent(e);
            if (e.Type is GameEventType.JobCancelled or GameEventType.JobDelivered) _eta.Reset();
            if (e.Type == GameEventType.JobStarted)
                Push?.Invoke("job", new { type = "started", cargo = e.Snapshot.Cargo, from = e.Snapshot.SourceCity, to = e.Snapshot.DestinationCity, income = e.Snapshot.JobIncome });
        };
        Recorder.DeliveryRecorded += (id, status) =>
        {
            Push?.Invoke("delivery", new { id, status, row = Queries.Delivery(Db, id) });
            Push?.Invoke("dataChanged", new { scope = "logbook" });
            CheckAchievements();
            // The game autosaves after a delivery; refresh the profile shortly after.
            if (!Telemetry.DemoActive) _ = Task.Delay(8000).ContinueWith(_ => Profiles.ReloadAsync());
        };
        Recorder.EventRecorded += (type, amount, detail) => Push?.Invoke("gameEvent", new { type, amount, detail, at = DateTime.UtcNow });
        Profiles.Changed += () =>
        {
            Push?.Invoke("profile", ProfilePayload());
            PushStatus();
            CheckAchievements();
        };

        _heartbeat = new Timer(_ => PushStatus(), null, 1000, 1000);
        _backupTimer = new Timer(_ => RunAutoBackup(), null, TimeSpan.FromMinutes(2), TimeSpan.FromHours(1));
    }

    public string DataFolder { get; }
    public Database Db { get; }
    public string? DbError { get; private set; }
    public SettingsStore Settings { get; }
    public BackupService Backups { get; }
    public ProfileService Profiles { get; }
    public TelemetryService Telemetry { get; }
    public TripRecorder Recorder { get; }
    /// <summary>Job notifications (the desktop host also shows them as an overlay over the game).</summary>
    public JobNotifier Notifier { get; }
    private readonly EtaEstimator _eta = new();
    public Achievements Achievements { get; }
    /// <summary>Online service – locked in 0.0.x; the sample backend feeds the VTC/Online screens in the developer preview.</summary>
    public Online.OnlineService Online { get; } = new();
    private bool _achievementsPrimed;

    /// <summary>Announces achievements reached since the last check (the very first check is silent).</summary>
    private void CheckAchievements()
    {
        try
        {
            var all = Achievements.Evaluate(out var fresh);
            if (_achievementsPrimed) foreach (var a in fresh) Notifier.Achievement(a);
            _achievementsPrimed = true;
            if (fresh.Count > 0) Push?.Invoke("achievements", all);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Achievements failed: {ex}"); }
    }

    public static string Version => typeof(HaulixEngine).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "1.0.0";

    private readonly object _settingsGate = new();

    /// <summary>Push notification to the UI: (event name, payload).</summary>
    public event Action<string, object?>? Push;

    /// <summary>True when this start applied choices from the setup (the host then re-applies autostart etc.).</summary>
    public bool InstallerPreferencesApplied { get; private set; }

    public void Start()
    {
        ApplyInstallerLanguage();
        InstallerPreferencesApplied = ApplyInstallerPreferences();
        var s = Settings.Load();
        Telemetry.IntervalMs = 1000 / Math.Clamp(s.Telemetry.UpdateHz, 1, 30);
        _ = Task.Run(() =>
        {
            RunDetection();
            // First run: pick the most recently played profile automatically when detection is enabled.
            var settings = Settings.Load();
            if (settings.Ets2.AutoDetect && string.IsNullOrEmpty(settings.Ets2.ProfilePath) && _detection?.Profiles.Count > 0)
            {
                settings.Ets2.ProfilePath = _detection.Profiles[0].Path;
                Settings.Save(settings);
            }
            Profiles.Start();
            PushStatus();
        });
        Telemetry.Start();
    }

    /// <summary>The installer stores the language picked during setup; adopt it once, then remove it.</summary>
    private void ApplyInstallerLanguage()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\HAULIX", writable: true);
            if (key?.GetValue("Language") is not string lang || lang.Length == 0) return;
            var s = Settings.Load();
            s.General.Language = lang;
            s.General.LanguageChosen = lang != "auto";
            Settings.Save(s);
            key.DeleteValue("Language", false);
        }
        catch (Exception)
        {
            // Registry unavailable: keep the current language setting.
        }
    }

    /// <summary>
    /// Choices made in the setup (Custom install): stored as JSON under HKCU\Software\HAULIX\SetupPrefs, applied
    /// once on the next start and removed. Only known keys are read. Returns true when something was applied.
    /// </summary>
    public bool ApplyInstallerPreferences()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\HAULIX", writable: true);
            if (key?.GetValue("SetupPrefs") is not string json || json.Length == 0) return false;
            key.DeleteValue("SetupPrefs", false);
            using var doc = JsonDocument.Parse(json);
            var p = doc.RootElement;
            bool? B(string k) => p.TryGetProperty(k, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
            string? S(string k) => p.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var borderless = false;
            UpdateSettings(s =>
            {
                if (S("units") is "metric" or "imperial") s.General.Units = S("units")!;
                if (S("currency") is { Length: 3 } cur) s.General.Currency = cur.ToUpperInvariant();
                if (S("theme") is "dark" or "midnight" or "light") s.General.Theme = S("theme")!;
                if (S("accent") is "amber" or "copper" or "ice" or "signal") s.Appearance.Accent = S("accent")!;
                if (B("launchWithWindows") is { } lw) s.General.LaunchWithWindows = lw;
                if (B("startMinimized") is { } sm) s.General.StartMinimized = sm;
                if (B("minimizeToTray") is { } mt) s.General.MinimizeToTray = mt;
                if (B("updateCheck") is { } uc) s.General.UpdateCheck = uc;
                if (B("hud") is { } hud) s.General.Hud = hud;
                if (B("notifications") is { } n) s.Notifications.Enabled = n;
                if (B("overlay") is { } ov) s.Notifications.Overlay = ov;
                if (B("sounds") is { } so) s.Notifications.Sounds = so;
                if (B("voice") is { } vo) s.Notifications.Voice = vo;
                if (B("afkWarning") is { } afk) s.Notifications.AfkWarning = afk;
                if (B("recordRoutes") is { } rr) s.Telemetry.RecordRoutes = rr;
                if (B("autoBackup") is { } ab) s.Data.AutoBackup = ab;
                borderless = B("borderless") == true;
            });
            if (borderless)
            {
                RunDetection();
                Ets2Display.SetBorderless(_detection?.DocumentsPath);
            }
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Setup preferences ignored: {ex.Message}");
            return false;
        }
    }

    private DetectionResult RunDetection()
    {
        // Manual paths win when valid; otherwise fall back to automatic discovery.
        var e = Settings.Load().Ets2;
        _detection = Ets2Locator.Detect(e.GamePath, e.DocumentsPath);
        return _detection;
    }

    /// <summary>Most recent telemetry sample (for the desktop host: Discord presence, HUD).</summary>
    public TelemetrySnapshot? LastSnapshot { get; private set; }

    private void OnSample(TelemetrySnapshot s)
    {
        LastSnapshot = s;
        Recorder.OnSample(s);
        s.Eta = _eta.Update(s, null, preferRoute: false);
        try { Notifier.OnSample(s); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Notifications failed: {ex}"); }
        lock (_pushGate)
        {
            var now = DateTime.UtcNow;
            // UI receives at most ~10 frames per second regardless of poll rate.
            if ((now - _lastTelemetryPush).TotalMilliseconds < 95) return;
            _lastTelemetryPush = now;
        }
        Push?.Invoke("telemetry", new { snapshot = s, job = Recorder.CurrentJobInfo, routeId = Recorder.CurrentRouteId });
    }

    private void RunAutoBackup()
    {
        try
        {
            var b = Backups.AutoBackupIfDue();
            if (b is not null) Push?.Invoke("backup", b);
        }
        catch (Exception ex)
        {
            Push?.Invoke("toast", new { kind = "warning", title = "Automatic backup failed", message = ex.Message });
        }
    }

    public object StatusPayload()
    {
        var t = Telemetry.Status;
        var p = Profiles;
        return new
        {
            game = t.Game switch { GameState.Running => "running", GameState.NotRunning => "notRunning", _ => "notDetected" },
            telemetry = t.Telemetry.ToString().ToLowerInvariant(),
            pluginRevision = t.PluginRevision,
            gameVersion = t.GameVersion,
            lastSampleUtc = t.LastSampleUtc,
            demo = t.Demo,
            pluginInstalled = _detection?.PluginInstalled ?? false,
            gameDetected = _detection?.GamePath is not null,
            profile = new
            {
                state = p.State.ToString().ToLowerInvariant(),
                id = p.Current?.ProfileId,
                name = p.Current?.ProfileName,
                company = p.Current?.CompanyName,
                parsedUtc = p.LastParsedUtc,
                saveUtc = p.Current?.SaveTimeUtc,
                saveName = p.Current?.SaveName,
                error = p.Error,
            },
            db = new { ok = DbError is null, error = DbError, path = Db.Path, sizeBytes = Db.FileSize() },
            nowUtc = DateTime.UtcNow,
        };
    }

    private void PushStatus() => Push?.Invoke("status", StatusPayload());

    private object? ProfilePayload() => Profiles.Current;

    /// <summary>Dispatches a UI request. Methods needing native dialogs are handled by the host before reaching here.</summary>
    public object? Handle(string method, JsonElement args)
    {
        switch (method)
        {
            case "app.init":
                return new
                {
                    version = Version,
                    settings = Settings.Load(),
                    detection = _detection,
                    status = StatusPayload(),
                    profile = ProfilePayload(),
                    telemetry = Telemetry.Last is { } last ? new { snapshot = last, job = Recorder.CurrentJobInfo, routeId = Recorder.CurrentRouteId } : null,
                    counts = Queries.Counts(Db),
                    dataFolder = DataFolder,
                    systemLanguage = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
                    backupFolder = Backups.BackupFolder,
                };

            case "settings.get": return Settings.Load();
            case "settings.save":
            lock (_settingsGate) // messages arrive on parallel threads; never let two saves interleave
            {
                var old = Settings.Load();
                var next = args.GetProperty("settings").Deserialize<AppSettings>(SettingsStore.Json) ?? old;
                var profileChanged = next.Ets2.ProfilePath != old.Ets2.ProfilePath;
                var saveSelectionChanged = next.Ets2.SaveSelection != old.Ets2.SaveSelection;
                Settings.Save(next);
                Telemetry.IntervalMs = 1000 / Math.Clamp(next.Telemetry.UpdateHz, 1, 30);
                if (profileChanged && next.Ets2.ProfilePath is { } pp) Profiles.SelectProfile(pp);
                else if (saveSelectionChanged) _ = Profiles.ReloadAsync();
                return next;
            }

            case "ets2.detect":
            {
                var d = RunDetection();
                PushStatus();
                return d;
            }
            case "ets2.validatePaths":
            {
                var game = Str(args, "gamePath");
                var docs = Str(args, "documentsPath");
                return new { game = Ets2Locator.ValidGamePath(game), documents = Ets2Locator.ValidDocumentsPath(docs),
                    profiles = Ets2Locator.ValidDocumentsPath(docs) is { } dp ? Ets2Locator.ListProfiles(dp) : new List<ProfileInfo>() };
            }
            case "ets2.saves": return Ets2Locator.ListSaves(Str(args, "profilePath") ?? "");
            case "ets2.selectProfile":
            {
                var path = Str(args, "path") ?? throw new ArgumentException("path required");
                Profiles.SelectProfile(path);
                return Settings.Load();
            }

            case "profile.get": return ProfilePayload();
            case "profile.reload":
                _ = Profiles.ReloadAsync();
                return true;

            case "logbook.query":
            {
                var f = args.TryGetProperty("filter", out var fe) ? fe.Deserialize<LogbookFilter>(SettingsStore.Json) ?? new LogbookFilter() : new LogbookFilter();
                f.IncludeDemo = Telemetry.DemoActive;
                return Queries.Logbook(Db, f);
            }
            case "logbook.get": return Queries.Delivery(Db, args.GetProperty("id").GetInt64());
            case "logbook.recent": return Queries.Recent(Db, Int(args, "limit") ?? 8, Telemetry.DemoActive);
            case "events.recent": return Queries.RecentEvents(Db, Int(args, "limit") ?? 12, Telemetry.DemoActive);

            case "stats.get":
                return Queries.Stats(Db, Str(args, "from") ?? DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-dd"),
                    Str(args, "to") ?? DateTime.UtcNow.ToString("yyyy-MM-dd"), Telemetry.DemoActive);
            case "stats.snapshots": return Profiles.ProfileId is { } pid ? Queries.Snapshots(Db, pid) : Array.Empty<object>();

            case "cities.learned": return Queries.LearnedCities(Db);
            case "notify.test": Notifier.Test(); return true;
            case "online.status": return Online.StatusPayload(Settings.Load().Online);
            case "online.acceptTerms":
                // Consent for the future online services (License Agreement Part B + Privacy Policy), stored with date.
                UpdateSettings(s => { s.Online.AcceptedTermsVersion = Haulix.Core.Online.OnlineService.TermsVersion; s.Online.AcceptedTermsUtc = DateTime.UtcNow; });
                return Online.StatusPayload(Settings.Load().Online);
            case "online.revokeTerms":
                UpdateSettings(s => { s.Online.AcceptedTermsVersion = null; s.Online.AcceptedTermsUtc = null; s.Online.CloudSync = false; s.Online.ShowOnLeaderboards = false; s.Online.ShareWithVtc = false; });
                return Online.StatusPayload(Settings.Load().Online);
            case "legal.get": return LegalText(Str(args, "doc") ?? "license");
            case "online.sample":
                Online.UseSample = args.TryGetProperty("on", out var sampleOn) && sampleOn.GetBoolean();
                return Online.StatusPayload(Settings.Load().Online);
            case "online.me": return Online.Api.MeAsync().GetAwaiter().GetResult();
            case "online.vtcs": return Online.Api.SearchVtcsAsync(Str(args, "query"), Str(args, "language"), Str(args, "region")).GetAwaiter().GetResult();
            case "online.members": return Online.Api.GetMembersAsync(Str(args, "vtcId") ?? "").GetAwaiter().GetResult();
            case "online.jobs": return Online.Api.GetJobsAsync(Str(args, "vtcId") ?? "").GetAwaiter().GetResult();
            case "online.events": return Online.Api.GetEventsAsync(Str(args, "vtcId")).GetAwaiter().GetResult();
            case "online.leaderboard": return Online.Api.GetLeaderboardAsync(Str(args, "metric") ?? "km", Str(args, "period") ?? "week", Str(args, "vtcId")).GetAwaiter().GetResult();
            case "job.current":
                // Job page: live stats, costs, timeline and speed profile of the job in progress.
                return new { detail = Recorder.CurrentJobDetail(), snapshot = LastSnapshot };
            case "achievements.get":
            {
                var list = Achievements.Evaluate(out _);
                _achievementsPrimed = true;
                return list;
            }
            case "update.check":
            {
                var g = Settings.Load().General;
                // "force" = the Check now button: ask even when automatic checks are off.
                return UpdateChecker.Check(g.UpdateFeedUrl, Version, g.UpdateCheck || (args.ValueKind == System.Text.Json.JsonValueKind.Object && args.TryGetProperty("force", out var f) && f.GetBoolean()));
            }
            case "ets2.display": return Ets2Display.Read(_detection?.DocumentsPath);
            case "ets2.setBorderless":
            {
                var err = Ets2Display.SetBorderless(_detection?.DocumentsPath);
                return new { ok = err is null, error = err, display = Ets2Display.Read(_detection?.DocumentsPath) };
            }

            case "data.counts": return Queries.Counts(Db);
            case "data.backups": return Backups.List();
            case "data.backupNow":
                return Backups.Export(Path.Combine(Backups.BackupFolder, $"manual-{DateTime.Now:yyyyMMdd-HHmmss}{BackupService.Extension}"));
            case "data.restore":
                Backups.Import(Str(args, "path") ?? throw new ArgumentException("path required"));
                AfterDataReplaced();
                return true;
            case "data.clear":
                Backups.Clear(Str(args, "scope") ?? "history");
                AfterDataReplaced();
                return true;
            case "data.check":
                DbError = Db.Check();
                return new { ok = DbError is null, error = DbError };

            case "demo.set":
            {
                var on = args.GetProperty("on").GetBoolean();
                Telemetry.SetDemo(on);
                if (!on)
                {
                    Recorder.OnDisconnected();
                    Queries.PurgeDemo(Db);
                }
                PushStatus();
                Push?.Invoke("dataChanged", new { scope = "all" });
                return on;
            }

            default:
                throw new InvalidOperationException($"Unknown method '{method}'.");
        }
    }

    /// <summary>Changes settings from the host side (e.g. the HUD was dragged to a new place) and tells the UI.</summary>
    public AppSettings UpdateSettings(Action<AppSettings> change)
    {
        AppSettings s;
        lock (_settingsGate)
        {
            s = Settings.Load();
            change(s);
            Settings.Save(s);
        }
        Push?.Invoke("settings", s);
        return s;
    }

    /// <summary>Import into an existing file (import/restore from dialog).</summary>
    public void ImportArchive(string path)
    {
        Backups.Import(path);
        AfterDataReplaced();
    }

    private void AfterDataReplaced()
    {
        Settings.Invalidate();
        Profiles.Start();
        Push?.Invoke("dataChanged", new { scope = "all" });
        Push?.Invoke("settings", Settings.Load());
        PushStatus();
    }

    /// <summary>
    /// License agreement, privacy policy or third-party notices. The release ships them next to Haulix.exe
    /// (tools/release/build-release.ps1); development builds read them from the repository root.
    /// </summary>
    private static string LegalText(string doc)
    {
        var (release, source) = doc switch
        {
            "privacy" => ("PRIVACY.txt", "PRIVACY.md"),
            "thirdparty" => ("THIRD-PARTY-NOTICES.txt", "THIRD-PARTY-NOTICES.md"),
            _ => ("LICENSE.txt", "LICENSE"),
        };
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        if (File.Exists(Path.Combine(dir.FullName, release))) return File.ReadAllText(Path.Combine(dir.FullName, release));
        for (var d = dir; d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, source)) && File.Exists(Path.Combine(d.FullName, "Haulix.slnx")))
                return File.ReadAllText(Path.Combine(d.FullName, source));
        return "The document could not be found. Read it at https://github.com/RyanTMP/Haulix";
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

    public void Dispose()
    {
        _heartbeat.Dispose();
        _backupTimer.Dispose();
        Recorder.OnDisconnected();
        Telemetry.Dispose();
        Profiles.Dispose();
    }
}
