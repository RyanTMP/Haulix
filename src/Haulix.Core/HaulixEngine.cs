using System.Reflection;
using System.Text.Json;
using Haulix.Core.Data;
using Haulix.Core.Ets2;
using Haulix.Core.Map;
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

        Map = new MapService(dataFolder, () => _detection?.GamePath)
        {
            // Full map (all map DLCs) shipped next to the app; see tools/release/build-release.ps1.
            BundleFolder = Path.Combine(AppContext.BaseDirectory, "map-bundle"),
        };
        Notifier = new JobNotifier(() => Settings.Load()) { IsTruckersMp = TruckersMp.Active };
        Notifier.Raised += n => Push?.Invoke("notify", n);
        Map.GameRouteAdjusted += (km, matched) => Notifier.RouteAdjusted(km, matched);
        Achievements = new Achievements(Db, () => Profiles.Current, () => Notifier.German);
        Map.StatusChanged += () => Push?.Invoke("mapStatus", Map.StatusPayload());
        Map.RouteChanged += r => Push?.Invoke("route", r);
        Map.AutoRouteToJob = () => Settings.Load().Map.AutoRouteToJob;

        Queries.PurgeDemo(Db);

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
            if (e.Type is GameEventType.JobCancelled or GameEventType.JobDelivered) Map.OnJobEnded(e.Snapshot);
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
    public MapService Map { get; }
    /// <summary>Job notifications (the desktop host also shows them as an overlay over the game).</summary>
    public JobNotifier Notifier { get; }
    private readonly EtaEstimator _eta = new();
    public Achievements Achievements { get; }
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

    public void Start()
    {
        ApplyInstallerLanguage();
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
            Map.Initialize(Settings.Load().Map.AutoBuildRoadMap);
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
        try { Map.OnSample(s); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Routing failed: {ex}"); }
        s.Eta = _eta.Update(s, Map.RemainingRouteKm(s), preferRoute: Map.Route?.Mode == "manual");
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
                    mapStatus = Map.StatusPayload(),
                    route = Map.Route,
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

            case "map.get":
            {
                var days = Settings.Load().Map.RouteHistoryDays;
                var payload = Queries.Map(Db, days, Telemetry.DemoActive, Recorder.CurrentRouteId);
                // Deliveries without a GPS trace get a route reconstructed over the road map.
                foreach (var d in Queries.DeliveriesWithoutRoutes(Db, days, Telemetry.DemoActive))
                {
                    var pts = Map.ReconstructRoute(d["originCityId"] as string, d["destCityId"] as string);
                    if (pts is null) continue;
                    payload.Routes.Add(new
                    {
                        id = -(long)d["id"]!, kind = "reconstructed", startedUtc = d["finishedUtc"], endedUtc = d["finishedUtc"],
                        distanceKm = d["distanceKm"], deliveryId = d["id"], originCity = d["originCity"], destCity = d["destCity"],
                        cargo = d["cargo"], income = d["income"], points = pts, current = false, source = d["source"], gameEndMin = d["gameEndMin"],
                    });
                }
                return new { cities = payload.Cities, routes = payload.Routes, events = payload.Events, historyDays = days };
            }
            case "notify.test": Notifier.Test(); return true;
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
            case "map.driven": return Queries.DrivenPoints(Db, Telemetry.DemoActive);
            case "route.reconstruct":
                return Map.ReconstructRoute(Str(args, "from"), Str(args, "to"));

            case "map.status": return Map.StatusPayload();
            case "map.build": Map.Build(); return Map.StatusPayload();
            case "map.cancelBuild": Map.CancelBuild(); return true;
            case "route.get": return Map.Route;
            case "route.clear": return Map.ClearManual();
            case "route.reroute": return Map.Reroute();
            case "route.setPoint":
                return Map.SetManual(new RouteDestination("point", Str(args, "name") ?? "Map point", null, null,
                    (float)args.GetProperty("x").GetDouble(), (float)args.GetProperty("z").GetDouble()));
            case "route.setCity":
                return Map.SetManualCity(Str(args, "cityId") ?? "", Str(args, "name") ?? Str(args, "cityId") ?? "");
            case "route.setCompany":
                return Map.SetManual(new RouteDestination("company", Str(args, "name") ?? "Company", Str(args, "companyId"), Str(args, "cityId"),
                    (float)args.GetProperty("x").GetDouble(), (float)args.GetProperty("z").GetDouble()));

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

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

    public void Dispose()
    {
        _heartbeat.Dispose();
        _backupTimer.Dispose();
        Recorder.OnDisconnected();
        Map.Dispose();
        Telemetry.Dispose();
        Profiles.Dispose();
    }
}
