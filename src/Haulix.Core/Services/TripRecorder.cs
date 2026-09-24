using System.Globalization;
using Haulix.Core.Data;
using Haulix.Core.Profiles;
using Haulix.Core.Telemetry;
using Microsoft.Data.Sqlite;

namespace Haulix.Core.Services;

/// <summary>
/// Turns the live telemetry stream into durable history: job deliveries, GPS breadcrumbs, driving sessions,
/// gameplay events (fines, tolls, ferries…) and learned city coordinates. Runs on the telemetry thread.
/// </summary>
public sealed class TripRecorder
{
    private sealed class ActiveJob
    {
        public DateTime StartedUtc;
        public uint GameStartMinute;
        public double StartOdometer;
        public double FuelUsed;
        public double MaxSpeed;
        public double SpeedSum;
        public int SpeedSamples;
        public double DriveSeconds;
        public double StartX, StartZ;
        public bool StartedAtSource;
        public TelemetrySnapshot Job = null!;
        // Driving score inputs
        public double SpeedingSeconds;
        public double StartTruckDamage;
        public int Fines;
    }

    private sealed class ActiveRoute
    {
        public long Id;
        public int Seq;
        public double LastX, LastZ;
        public double LastHeading;
        public double DistanceKm;
        public DateTime LastFlushUtc = DateTime.UtcNow;
        public readonly List<(double X, double Z, double Speed, DateTime T)> Pending = new();
    }

    private sealed class ActiveSession
    {
        public long Id;
        public DateTime StartedUtc;
        public DateTime LastSampleUtc;
        public DateTime LastFlushUtc;
        public double DistanceKm;
        public double DriveSeconds;
        public double IdleSeconds;
        public double MaxSpeed;
        public double FuelUsed;
        public string? Truck;
    }

    private readonly Database _db;
    private readonly Func<string?> _profileId;
    private readonly Func<(bool RecordRoutes, bool FreeRoam, int SpacingM)> _options;
    private readonly object _lock = new();

    private ActiveJob? _job;
    private ActiveRoute? _route;
    private ActiveSession? _session;
    private TelemetrySnapshot? _prev;
    private DateTime _prevUtc;

    public TripRecorder(Database db, Func<string?> profileId, Func<(bool, bool, int)> options)
    {
        _db = db;
        _profileId = profileId;
        _options = options;
    }

    /// <summary>Raised after a delivery / cancellation row is written (id, status).</summary>
    public event Action<long, string>? DeliveryRecorded;

    /// <summary>Raised for every gameplay event persisted (type, amount, detail).</summary>
    public event Action<string, long?, string>? EventRecorded;

    public long? CurrentRouteId { get { lock (_lock) return _route?.Id; } }

    public object? CurrentJobInfo
    {
        get
        {
            lock (_lock)
            {
                if (_job is null) return null;
                return new
                {
                    startedUtc = _job.StartedUtc,
                    distanceKm = _prev is null ? 0 : Math.Max(0, _prev.OdometerKm - _job.StartOdometer),
                    fuelUsedL = _job.FuelUsed,
                    maxSpeedKmh = _job.MaxSpeed,
                    avgSpeedKmh = _job.SpeedSamples == 0 ? 0 : _job.SpeedSum / _job.SpeedSamples,
                    driveSeconds = _job.DriveSeconds,
                };
            }
        }
    }

    public void OnSample(TelemetrySnapshot s)
    {
        lock (_lock)
        {
            var now = s.CapturedUtc;
            var dt = _prev is null ? 0 : Math.Clamp((now - _prevUtc).TotalSeconds, 0, 5);
            var moving = s.SpeedKmh > 2;
            var fuelDrop = _prev is null ? 0 : Math.Max(0, _prev.FuelLitres - s.FuelLitres);
            if (fuelDrop > 5) fuelDrop = 0; // refuel/teleport artefacts
            var odoDelta = _prev is null ? 0 : s.OdometerKm - _prev.OdometerKm;
            if (odoDelta is < 0 or > 2) odoDelta = 0; // truck change / teleport

            if (!s.Paused)
            {
                // Session
                if (_session is null || (now - _session.LastSampleUtc).TotalSeconds > 90)
                {
                    CloseSession();
                    _session = new ActiveSession { StartedUtc = now, LastFlushUtc = now, Truck = TruckLabel(s) };
                    using var c = _db.Open();
                    _session.Id = Insert(c, "INSERT INTO sessions(profile_id, demo, started_utc, truck) VALUES($p,$d,$s,$t)",
                        ("$p", _profileId()), ("$d", s.Demo ? 1 : 0), ("$s", Iso(now)), ("$t", _session.Truck));
                }
                _session.LastSampleUtc = now;
                _session.DistanceKm += odoDelta;
                _session.FuelUsed += fuelDrop;
                _session.MaxSpeed = Math.Max(_session.MaxSpeed, s.SpeedKmh);
                if (moving) _session.DriveSeconds += dt;
                else if (s.EngineOn) _session.IdleSeconds += dt;
                if ((now - _session.LastFlushUtc).TotalSeconds > 20) FlushSession();

                // Job stats
                if (s.OnJob && _job is null) BeginJob(s, now, atSource: _prev is not null);
                if (_job is not null)
                {
                    _job.FuelUsed += fuelDrop;
                    _job.MaxSpeed = Math.Max(_job.MaxSpeed, s.SpeedKmh);
                    if (moving)
                    {
                        _job.SpeedSum += s.SpeedKmh;
                        _job.SpeedSamples++;
                        _job.DriveSeconds += dt;
                        if (s.SpeedLimitKmh > 1 && s.SpeedKmh > s.SpeedLimitKmh + 5) _job.SpeedingSeconds += dt;
                    }
                }

                if (_route is not null) _route.DistanceKm += odoDelta; // odometer = in-game km
                TrackRoute(s, now);
            }

            _prev = s;
            _prevUtc = now;
        }
    }

    public void OnGameEvent(GameEvent e)
    {
        lock (_lock)
        {
            var s = e.Snapshot;
            switch (e.Type)
            {
                case GameEventType.JobStarted:
                    if (_job is not null && JobKey(_job.Job) != JobKey(s)) _job = null;
                    if (_job is null) BeginJob(s, e.AtUtc, atSource: true);
                    break;
                case GameEventType.JobDelivered:
                    FinishJob(s, e.AtUtc, "delivered");
                    break;
                case GameEventType.JobCancelled:
                    FinishJob(s, e.AtUtc, "cancelled");
                    break;
                case GameEventType.Fined:
                    if (_job is not null) _job.Fines++;
                    RecordEvent(s, e.AtUtc, "fine", s.Gameplay.FineAmount, Names.Pretty(s.Gameplay.FineOffence));
                    break;
                case GameEventType.Tollgate:
                    RecordEvent(s, e.AtUtc, "toll", s.Gameplay.TollAmount, "Toll gate");
                    break;
                case GameEventType.Ferry:
                    RecordEvent(s, e.AtUtc, "ferry", s.Gameplay.FerryAmount, $"{s.Gameplay.FerrySource} → {s.Gameplay.FerryTarget}");
                    break;
                case GameEventType.Train:
                    RecordEvent(s, e.AtUtc, "train", s.Gameplay.TrainAmount, $"{s.Gameplay.TrainSource} → {s.Gameplay.TrainTarget}");
                    break;
                case GameEventType.Refuel:
                    RecordEvent(s, e.AtUtc, "refuel", null, $"{s.Gameplay.RefuelLitres:0} L");
                    break;
            }
        }
    }

    /// <summary>Called when telemetry goes away (game closed / demo stopped).</summary>
    public void OnDisconnected()
    {
        lock (_lock)
        {
            FlushRoute();
            CloseSession();
            _prev = null;
            // Keep _job: the player may resume the same job after restarting the game.
        }
    }

    private void BeginJob(TelemetrySnapshot s, DateTime now, bool atSource)
    {
        _job = new ActiveJob
        {
            StartedUtc = now,
            GameStartMinute = s.GameTimeMinutes,
            StartOdometer = s.OdometerKm,
            StartX = s.X,
            StartZ = s.Z,
            StartedAtSource = atSource && s.SpeedKmh < 5,
            Job = Clone(s),
            StartTruckDamage = s.TruckDamage,
        };
        // Split the breadcrumb trail so this job gets its own route.
        FlushRoute();
        _route = null;
    }

    private void FinishJob(TelemetrySnapshot s, DateTime now, string status)
    {
        var job = _job?.Job ?? s;
        var g = s.Gameplay;
        var profile = _profileId();
        var distance = g.DeliveredDistanceKm > 0 ? g.DeliveredDistanceKm
            : _job is null ? 0 : Math.Max(0, s.OdometerKm - _job.StartOdometer);

        var srcCity = CityCatalog.Get(job.SourceCityId);
        var dstCity = CityCatalog.Get(job.DestinationCityId);
        var dedupe = string.Join('|', status, profile, job.SourceCityId, job.DestinationCityId, job.CargoId,
            g.JobStartedGameMinute > 0 ? g.JobStartedGameMinute : _job?.GameStartMinute ?? 0, s.Demo ? "demo" + now.Ticks : "");

        var finishMinute = g.JobFinishedGameMinute > 0 ? g.JobFinishedGameMinute : s.GameTimeMinutes;
        var score = status == "delivered" && _job is not null
            ? DrivingScore.Compute(_job.DriveSeconds, _job.SpeedingSeconds, g.DeliveredCargoDamage, Math.Max(0, s.TruckDamage - _job.StartTruckDamage),
                _job.Fines, job.JobDeadlineGameMinutes > 0 && finishMinute > job.JobDeadlineGameMinutes)
            : null;

        FlushRoute();
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        var id = Insert(c, """
            INSERT OR IGNORE INTO deliveries(source, dedupe_key, profile_id, demo, status, started_utc, finished_utc, game_start_min, game_end_min,
              truck, truck_brand, truck_plate, trailer, driver, cargo, cargo_id, cargo_mass_kg,
              origin_city, origin_city_id, origin_company, origin_country, dest_city, dest_city_id, dest_company, dest_country,
              planned_km, distance_km, income, xp, penalty, fuel_used_l, avg_speed_kmh, max_speed_kmh, drive_seconds, game_minutes,
              cargo_damage, truck_damage, autopark, autoload, market, special)
            VALUES('telemetry', $key, $profile, $demo, $status, $start, $end, $gs, $ge,
              $truck, $brand, $plate, $trailer, 'Player', $cargo, $cargoId, $mass,
              $oc, $ocid, $oco, $octry, $dc, $dcid, $dco, $dctry,
              $planned, $dist, $income, $xp, $penalty, $fuel, $avg, $max, $drive, $gmin,
              $cdmg, $tdmg, $ap, $al, $market, $special)
            """,
            ("$key", dedupe), ("$profile", profile), ("$demo", s.Demo ? 1 : 0), ("$status", status),
            ("$start", _job is null ? null : Iso(_job.StartedUtc)), ("$end", Iso(now)),
            ("$gs", g.JobStartedGameMinute > 0 ? g.JobStartedGameMinute : _job?.GameStartMinute),
            ("$ge", g.JobFinishedGameMinute > 0 ? g.JobFinishedGameMinute : s.GameTimeMinutes),
            ("$truck", TruckLabel(s)), ("$brand", s.TruckBrand), ("$plate", s.LicensePlate),
            ("$trailer", string.Join(' ', new[] { s.TrailerBrand, s.TrailerName }.Where(x => !string.IsNullOrEmpty(x)))),
            ("$cargo", job.Cargo), ("$cargoId", job.CargoId), ("$mass", job.CargoMassKg),
            ("$oc", NonEmpty(job.SourceCity, srcCity.Name)), ("$ocid", job.SourceCityId), ("$oco", job.SourceCompany), ("$octry", srcCity.Country),
            ("$dc", NonEmpty(job.DestinationCity, dstCity.Name)), ("$dcid", job.DestinationCityId), ("$dco", job.DestinationCompany), ("$dctry", dstCity.Country),
            ("$planned", (double)job.PlannedDistanceKm), ("$dist", distance),
            ("$income", status == "delivered" ? (g.DeliveredRevenue != 0 ? g.DeliveredRevenue : (long)job.JobIncome) : 0),
            ("$xp", status == "delivered" ? g.DeliveredXp : 0),
            ("$penalty", status == "cancelled" ? g.CancelledPenalty : 0),
            ("$fuel", _job?.FuelUsed), ("$avg", _job is null || _job.SpeedSamples == 0 ? null : _job.SpeedSum / _job.SpeedSamples),
            ("$max", _job?.MaxSpeed), ("$drive", _job is null ? null : (long)_job.DriveSeconds),
            ("$gmin", g.DeliveredTimeMinutes > 0 ? g.DeliveredTimeMinutes : null),
            ("$cdmg", status == "delivered" ? g.DeliveredCargoDamage : job.CargoDamage), ("$tdmg", s.TruckDamage),
            ("$ap", g.AutoParked ? 1 : 0), ("$al", g.AutoLoaded ? 1 : 0), ("$market", job.JobMarket), ("$special", job.SpecialJob ? 1 : 0));

        if (id > 0 && score is not null)
            Database.Exec(c, "UPDATE deliveries SET score = $s, score_detail = $d WHERE id = $id",
                ("$s", score.Score), ("$d", System.Text.Json.JsonSerializer.Serialize(score, DrivingScore.Json)), ("$id", id));
        if (id > 0)
        {
            // Attach every route recorded since the job began.
            if (_job is not null)
            {
                Database.Exec(c, "UPDATE routes SET delivery_id = $d, kind = 'job', origin_city = $o, dest_city = $t WHERE delivery_id IS NULL AND started_utc >= $s AND demo = $demo",
                    ("$d", id), ("$o", NonEmpty(job.SourceCity, srcCity.Name)), ("$t", NonEmpty(job.DestinationCity, dstCity.Name)),
                    ("$s", Iso(_job.StartedUtc)), ("$demo", s.Demo ? 1 : 0));
            }
            if (!string.IsNullOrEmpty(job.CargoId) && !string.IsNullOrEmpty(job.Cargo))
            {
                Database.Exec(c, "INSERT INTO cargo_names(id, name) VALUES($i,$n) ON CONFLICT(id) DO UPDATE SET name = excluded.name",
                    ("$i", job.CargoId), ("$n", job.Cargo));
            }
            if (!s.Demo)
            {
                // The truck is at the destination company right now: learn that city's position.
                if (status == "delivered") LearnCity(c, job.DestinationCityId, NonEmpty(job.DestinationCity, dstCity.Name), s.X, s.Z);
                if (_job is { StartedAtSource: true }) LearnCity(c, job.SourceCityId, NonEmpty(job.SourceCity, srcCity.Name), _job.StartX, _job.StartZ);
            }
        }
        tx.Commit();
        _job = null;
        _route = null;
        if (id > 0) DeliveryRecorded?.Invoke(id, status);
    }

    private void TrackRoute(TelemetrySnapshot s, DateTime now)
    {
        var (record, freeRoam, spacing) = _options();
        if (!record) return;
        if (!s.OnJob && !freeRoam && _job is null) return;
        if (s.X == 0 && s.Z == 0) return;

        if (_route is null)
        {
            using var c = _db.Open();
            var id = Insert(c, "INSERT INTO routes(profile_id, demo, kind, started_utc, origin_city, dest_city) VALUES($p,$d,$k,$s,$o,$t)",
                ("$p", _profileId()), ("$d", s.Demo ? 1 : 0), ("$k", s.OnJob ? "job" : "freeroam"), ("$s", Iso(now)),
                ("$o", s.OnJob ? s.SourceCity : null), ("$t", s.OnJob ? s.DestinationCity : null));
            _route = new ActiveRoute { Id = id, LastX = s.X, LastZ = s.Z, LastHeading = s.HeadingDeg };
            _route.Pending.Add((s.X, s.Z, s.SpeedKmh, now));
            return;
        }

        var dx = s.X - _route.LastX;
        var dz = s.Z - _route.LastZ;
        var d = Math.Sqrt(dx * dx + dz * dz);
        if (d > 3000)
        {
            // Ferry, train or teleport: mark a gap (speed -1) so the map doesn't draw a straight line.
            _route.Pending.Add((s.X, s.Z, -1, now));
            _route.LastX = s.X;
            _route.LastZ = s.Z;
        }
        else
        {
            var turn = Math.Abs(((s.HeadingDeg - _route.LastHeading + 540) % 360) - 180);
            if (d >= spacing || (turn > 25 && d >= 40))
            {
                _route.Pending.Add((s.X, s.Z, s.SpeedKmh, now));
                _route.LastX = s.X;
                _route.LastZ = s.Z;
                _route.LastHeading = s.HeadingDeg;
            }
        }
        if (_route.Pending.Count >= 25 || (now - _route.LastFlushUtc).TotalSeconds > 15) FlushRoute();
    }

    private void FlushRoute()
    {
        if (_route is null || _route.Pending.Count == 0) return;
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        foreach (var p in _route.Pending)
        {
            Database.Exec(c, "INSERT OR IGNORE INTO route_points(route_id, seq, x, z, speed, t_utc) VALUES($r,$s,$x,$z,$v,$t)",
                ("$r", _route.Id), ("$s", _route.Seq++), ("$x", p.X), ("$z", p.Z), ("$v", p.Speed), ("$t", Iso(p.T)));
        }
        Database.Exec(c, "UPDATE routes SET ended_utc = $e, distance_km = $d WHERE id = $id",
            ("$e", Iso(_route.Pending[^1].T)), ("$d", _route.DistanceKm), ("$id", _route.Id));
        tx.Commit();
        _route.Pending.Clear();
        _route.LastFlushUtc = DateTime.UtcNow;
    }

    private void FlushSession()
    {
        if (_session is null) return;
        using var c = _db.Open();
        Database.Exec(c, """
            UPDATE sessions SET ended_utc = $e, distance_km = $d, drive_seconds = $ds, idle_seconds = $is,
              max_speed_kmh = $m, fuel_used_l = $f WHERE id = $id
            """,
            ("$e", Iso(_session.LastSampleUtc)), ("$d", _session.DistanceKm), ("$ds", (long)_session.DriveSeconds),
            ("$is", (long)_session.IdleSeconds), ("$m", _session.MaxSpeed), ("$f", _session.FuelUsed), ("$id", _session.Id));
        _session.LastFlushUtc = DateTime.UtcNow;
    }

    private void CloseSession()
    {
        if (_session is null) return;
        FlushSession();
        if (_session.DistanceKm < 0.05 && _session.DriveSeconds < 5)
        {
            using var c = _db.Open();
            Database.Exec(c, "DELETE FROM sessions WHERE id = $id", ("$id", _session.Id));
        }
        _session = null;
    }

    private void RecordEvent(TelemetrySnapshot s, DateTime at, string type, long? amount, string detail)
    {
        using var c = _db.Open();
        Database.Exec(c, "INSERT INTO events(at_utc, profile_id, demo, type, amount, detail, x, z) VALUES($a,$p,$d,$t,$m,$dt,$x,$z)",
            ("$a", Iso(at)), ("$p", _profileId()), ("$d", s.Demo ? 1 : 0), ("$t", type), ("$m", amount), ("$dt", detail), ("$x", s.X), ("$z", s.Z));
        EventRecorded?.Invoke(type, amount, detail);
    }

    private static void LearnCity(SqliteConnection c, string? id, string name, double x, double z)
    {
        if (string.IsNullOrEmpty(id) || (x == 0 && z == 0)) return;
        // Running average: company depots sit around the city, so averaging converges on its centre.
        Database.Exec(c, """
            INSERT INTO cities(id, name, country, x, z, samples, last_utc) VALUES($id, $n, $c, $x, $z, 1, $t)
            ON CONFLICT(id) DO UPDATE SET
              x = (cities.x * cities.samples + excluded.x) / (cities.samples + 1),
              z = (cities.z * cities.samples + excluded.z) / (cities.samples + 1),
              samples = cities.samples + 1, name = excluded.name, last_utc = excluded.last_utc
            """,
            ("$id", id), ("$n", name), ("$c", CityCatalog.CountryOf(id)), ("$x", x), ("$z", z), ("$t", Iso(DateTime.UtcNow)));
    }

    private static long Insert(SqliteConnection c, string sql, params (string, object?)[] args)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql + "; SELECT CASE WHEN changes() > 0 THEN last_insert_rowid() ELSE 0 END;";
        foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static string JobKey(TelemetrySnapshot s) => $"{s.SourceCityId}|{s.DestinationCityId}|{s.CargoId}|{s.JobIncome}";

    private static string TruckLabel(TelemetrySnapshot s) => string.Join(' ', new[] { s.TruckBrand, s.TruckName }.Where(x => !string.IsNullOrEmpty(x)));

    private static string NonEmpty(string? a, string b) => string.IsNullOrWhiteSpace(a) ? b : a;

    public static string Iso(DateTime d) => d.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static TelemetrySnapshot Clone(TelemetrySnapshot s) => new()
    {
        SourceCityId = s.SourceCityId, SourceCity = s.SourceCity, SourceCompany = s.SourceCompany, SourceCompanyId = s.SourceCompanyId,
        DestinationCityId = s.DestinationCityId, DestinationCity = s.DestinationCity, DestinationCompany = s.DestinationCompany,
        DestinationCompanyId = s.DestinationCompanyId, CargoId = s.CargoId, Cargo = s.Cargo, CargoMassKg = s.CargoMassKg,
        JobIncome = s.JobIncome, PlannedDistanceKm = s.PlannedDistanceKm, JobMarket = s.JobMarket, SpecialJob = s.SpecialJob,
        CargoDamage = s.CargoDamage, Demo = s.Demo,
    };
}
