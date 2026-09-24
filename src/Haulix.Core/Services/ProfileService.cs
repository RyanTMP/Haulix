using System.Text.Json;
using Haulix.Core.Data;
using Haulix.Core.Ets2;
using Haulix.Core.Profiles;
using Haulix.Core.Settings;

namespace Haulix.Core.Services;

public enum ProfileLoadState { None, Loading, Loaded, Cached, Error }

/// <summary>
/// Owns the currently selected ETS2 profile: parses its newest save in the background, re-parses when the game
/// writes a new save, caches the result in SQLite (so HAULIX works with the game closed) and imports
/// the in-game delivery log into the local logbook.
/// </summary>
public sealed class ProfileService : IDisposable
{
    private readonly Database _db;
    private readonly SettingsStore _settings;
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;
    private readonly SemaphoreSlim _parseLock = new(1, 1);

    public ProfileService(Database db, SettingsStore settings)
    {
        _db = db;
        _settings = settings;
    }

    public ProfileData? Current { get; private set; }
    public ProfileLoadState State { get; private set; }
    public string? Error { get; private set; }
    public DateTime? LastParsedUtc { get; private set; }

    public string? ProfileId => Current?.ProfileId ?? (_settings.Load().Ets2.ProfilePath is { } p ? Path.GetFileName(p) : null);

    public event Action? Changed;

    /// <summary>Shows the cached copy immediately, then parses the save in the background.</summary>
    public void Start()
    {
        var path = _settings.Load().Ets2.ProfilePath;
        if (string.IsNullOrEmpty(path)) return;
        LoadCache(Path.GetFileName(path));
        Watch(path);
        _ = ReloadAsync();
    }

    public void SelectProfile(string profilePath)
    {
        var s = _settings.Load();
        s.Ets2.ProfilePath = profilePath;
        _settings.Save(s);
        Current = null;
        LoadCache(Path.GetFileName(profilePath));
        Watch(profilePath);
        _ = ReloadAsync();
    }

    public async Task ReloadAsync()
    {
        var settings = _settings.Load();
        var profilePath = settings.Ets2.ProfilePath;
        if (string.IsNullOrEmpty(profilePath)) return;
        if (!await _parseLock.WaitAsync(0)) return; // a parse is already running

        try
        {
            State = Current is null ? ProfileLoadState.Loading : State;
            Changed?.Invoke();
            var data = await Task.Run(() =>
            {
                var saveDir = settings.Ets2.SaveSelection switch
                {
                    "latest" => null,
                    var name => Directory.Exists(Path.Combine(profilePath, "save", name)) ? Path.Combine(profilePath, "save", name) : null,
                };
                return SaveParser.Parse(profilePath, saveDir);
            });
            ApplyCargoNames(data);
            Current = data;
            State = ProfileLoadState.Loaded;
            Error = null;
            LastParsedUtc = DateTime.UtcNow;
            await Task.Run(() =>
            {
                StoreCache(data);
                StoreSnapshot(data);
                if (settings.Ets2.ImportSaveHistory) ImportDeliveries(data);
            });
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            State = Current is null ? ProfileLoadState.Error : ProfileLoadState.Cached;
        }
        finally
        {
            _parseLock.Release();
            Changed?.Invoke();
        }
    }

    private void Watch(string profilePath)
    {
        _watcher?.Dispose();
        _watcher = null;
        var saveDir = Path.Combine(profilePath, "save");
        if (!_settings.Load().Ets2.WatchSaves || !Directory.Exists(saveDir)) return;
        _watcher = new FileSystemWatcher(saveDir, "game.sii")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        // The game writes several files per save; wait until it has been quiet for a few seconds.
        _watcher.Changed += (_, _) => Debounce();
        _watcher.Created += (_, _) => Debounce();
        _watcher.Renamed += (_, _) => Debounce();
    }

    private void Debounce()
    {
        _debounce?.Dispose();
        _debounce = new Timer(_ => _ = ReloadAsync(), null, 4000, Timeout.Infinite);
    }

    private void LoadCache(string profileId)
    {
        try
        {
            using var c = _db.Open();
            var json = Database.Scalar<string>(c, "SELECT json FROM profile_cache WHERE profile_id = $p", ("$p", profileId));
            if (string.IsNullOrEmpty(json)) return;
            Current = JsonSerializer.Deserialize<ProfileData>(json, SettingsStore.Json);
            State = ProfileLoadState.Cached;
            LastParsedUtc = Current?.ParsedAtUtc;
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Error = $"Cached profile unreadable: {ex.Message}";
        }
    }

    private void StoreCache(ProfileData d)
    {
        using var c = _db.Open();
        Database.Exec(c, """
            INSERT INTO profile_cache(profile_id, parsed_utc, save_path, save_utc, json) VALUES($p, $t, $sp, $su, $j)
            ON CONFLICT(profile_id) DO UPDATE SET parsed_utc = excluded.parsed_utc, save_path = excluded.save_path,
              save_utc = excluded.save_utc, json = excluded.json
            """,
            ("$p", d.ProfileId), ("$t", TripRecorder.Iso(d.ParsedAtUtc)), ("$sp", d.SavePath), ("$su", TripRecorder.Iso(d.SaveTimeUtc)),
            ("$j", JsonSerializer.Serialize(d, SettingsStore.Json)));
    }

    /// <summary>One fleet snapshot per save file version, used for history charts (money, fleet size…).</summary>
    private void StoreSnapshot(ProfileData d)
    {
        using var c = _db.Open();
        var lastSave = Database.Scalar<string>(c, "SELECT json FROM fleet_snapshots WHERE profile_id = $p ORDER BY id DESC LIMIT 1", ("$p", d.ProfileId));
        var marker = TripRecorder.Iso(d.SaveTimeUtc);
        if (lastSave == marker) return;
        var ai = d.Drivers.Where(x => !x.IsPlayer).ToList();
        Database.Exec(c, """
            INSERT INTO fleet_snapshots(profile_id, taken_utc, money, xp, distance_km, trucks, trailers, garages, drivers, ai_revenue, ai_profit, json)
            VALUES($p, $t, $m, $x, $dist, $tr, $tl, $g, $d, $ar, $ap, $j)
            """,
            ("$p", d.ProfileId), ("$t", marker), ("$m", d.Money), ("$x", d.Xp), ("$dist", d.TotalDistanceKm), ("$tr", d.Trucks.Count),
            ("$tl", d.Trailers.Count), ("$g", d.Garages.Count), ("$d", ai.Count), ("$ar", ai.Sum(x => x.Profit.Revenue)),
            ("$ap", ai.Sum(x => x.Profit.Profit)), ("$j", marker));
    }

    /// <summary>Imports the save's delivery log so the logbook has history from before HAULIX was installed.</summary>
    private void ImportDeliveries(ProfileData d)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        foreach (var e in d.Deliveries)
        {
            var src = CityCatalog.Get(e.SourceCity);
            var dst = CityCatalog.Get(e.TargetCity);
            var key = $"save|{d.ProfileId}|{e.StartedGameMinute}|{e.FinishedGameMinute}|{e.CargoId}|{e.SourceCity}|{e.TargetCity}";
            // Skip jobs HAULIX already recorded live (same route & cargo, finished within the same game hour).
            var live = Database.Scalar<long>(c, """
                SELECT COUNT(*) FROM deliveries WHERE source = 'telemetry' AND profile_id = $p AND origin_city_id = $o AND dest_city_id = $t
                  AND cargo_id = $cid AND ABS(COALESCE(game_end_min, 0) - $end) < 90
                """,
                ("$p", d.ProfileId), ("$o", e.SourceCity), ("$t", e.TargetCity), ("$cid", e.CargoId?.Replace("cargo.", "")), ("$end", e.FinishedGameMinute));
            if (live > 0) continue;
            Database.Exec(c, """
                INSERT OR IGNORE INTO deliveries(source, dedupe_key, profile_id, status, game_start_min, game_end_min, truck, truck_brand, driver,
                  cargo, cargo_id, cargo_mass_kg, origin_city, origin_city_id, origin_company, origin_country, dest_city, dest_city_id, dest_company,
                  dest_country, planned_km, distance_km, income, xp, game_minutes, cargo_damage, market)
                VALUES('save', $k, $p, 'delivered', $gs, $ge, $truck, $brand, 'Player', $cargo, $cid, $mass, $oc, $ocid, $oco, $octry,
                  $dc, $dcid, $dco, $dctry, $planned, $dist, $inc, $xp, $gmin, $dmg, $market)
                """,
                ("$k", key), ("$p", d.ProfileId), ("$gs", e.StartedGameMinute), ("$ge", e.FinishedGameMinute), ("$truck", e.Truck),
                ("$brand", e.Truck?.Split(' ').FirstOrDefault()), ("$cargo", e.Cargo), ("$cid", e.CargoId?.Replace("cargo.", "")),
                ("$mass", e.CargoMassKg), ("$oc", src.Name), ("$ocid", e.SourceCity), ("$oco", e.SourceCompany), ("$octry", src.Country),
                ("$dc", dst.Name), ("$dcid", e.TargetCity), ("$dco", e.TargetCompany), ("$dctry", dst.Country),
                ("$planned", (double)e.PlannedDistanceKm), ("$dist", (double)e.DistanceKm), ("$inc", e.Revenue), ("$xp", e.Xp),
                ("$gmin", e.FinishedGameMinute - e.StartedGameMinute), ("$dmg", e.CargoDamage), ("$market", e.Market));
        }
        tx.Commit();
    }

    /// <summary>Cargo ids in saves are truncated (e.g. "used_packag"); use names learned from telemetry when known.</summary>
    private void ApplyCargoNames(ProfileData d)
    {
        Dictionary<string, string> names;
        using (var c = _db.Open())
        {
            names = Database.Rows(c, "SELECT id, name FROM cargo_names")
                .ToDictionary(r => (string)r["id"]!, r => (string)r["name"]!, StringComparer.OrdinalIgnoreCase);
        }
        foreach (var e in d.Deliveries)
        {
            var id = e.CargoId?.Replace("cargo.", "");
            if (id is not null && names.TryGetValue(id, out var n)) e.Cargo = n;
            else e.Cargo = CargoNames.Lookup(id) ?? e.Cargo;
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce?.Dispose();
    }
}
