using Haulix.Core.Telemetry;

namespace Haulix.Core.Map;

public enum MapBuildState { Unavailable, Missing, Building, Ready, Error }

public sealed record RouteDestination(string Kind, string Name, string? CompanyId, string? CityId, float X, float Z);

public sealed record RouteState(
    string Mode,                 // "job" | "manual"
    RouteDestination Destination,
    float[] Points,
    float LengthMeters,
    double EstimatedKm,
    DateTime ComputedUtc,
    string? Error,
    /// <summary>"matched" when the route was chosen to match the in-game navigation, "differs" when no
    /// road-map route matches what the game's GPS reports, null when not compared (no game GPS).</summary>
    string? GameRoute = null);

/// <summary>
/// Owns the extracted road network: builds it once per game/DLC version (background, low priority),
/// loads it from cache, and keeps a live route — automatically to the current job's destination company,
/// or to a destination the player picked manually. Re-routes when the truck leaves the route.
/// </summary>
public sealed class MapService : IDisposable
{
    private readonly string _folder;
    private readonly Func<string?> _gamePath;
    private readonly object _gate = new();
    private RoadNetwork? _network;
    private Router? _router;
    private CancellationTokenSource? _buildCts;

    private RouteDestination? _manual;
    private string _jobKey = "";
    private DateTime _lastRouteUtc = DateTime.MinValue;
    private DateTime _lastCheckUtc = DateTime.MinValue;
    private int _offRouteSeconds;
    private int _progressIndex;
    private TelemetrySnapshot? _last;

    public MapService(string dataFolder, Func<string?> gamePath)
    {
        _folder = Path.Combine(dataFolder, "map");
        Directory.CreateDirectory(_folder);
        _gamePath = gamePath;
    }

    public string Folder => _folder;
    public Func<bool> AutoRouteToJob { get; set; } = () => true;
    public MapBuildState State { get; private set; } = MapBuildState.Missing;
    public double Progress { get; private set; }
    public string Message { get; private set; } = "";
    public string? CacheKey { get; private set; }
    public RouteState? Route { get; private set; }

    public event Action? StatusChanged;
    public event Action<RouteState?>? RouteChanged;

    public object StatusPayload() => new
    {
        state = State.ToString().ToLowerInvariant(),
        progress = Progress,
        message = Message,
        key = CacheKey,
        streetsUrl = State == MapBuildState.Ready && CacheKey is not null ? $"https://map.haulix/streets-{CacheKey}.bin" : null,
        poisUrl = State == MapBuildState.Ready && CacheKey is not null ? $"https://map.haulix/pois-{CacheKey}.json" : null,
        countriesUrl = State == MapBuildState.Ready && CacheKey is not null ? $"https://map.haulix/{Path.GetFileName(CountriesPath(CacheKey))}" : null,
        builtUtc = _network?.BuiltUtc,
        gameVersion = _network?.GameVersion,
        segments = _network?.EdgeCount ?? 0,
        source = Source,
        mapDlcs = MapDlcs,
    };

    /// <summary>
    /// Folder with a complete, pre-built map shipped with HAULIX (all map DLCs). Used when the player's own
    /// installation has fewer map DLCs, or when ETS2 is not installed, so everyone sees the whole map.
    /// </summary>
    public string? BundleFolder { get; set; }

    /// <summary>"bundled" when the shipped full map is in use, "local" when built from this PC's game files.</summary>
    public string Source { get; private set; } = "local";
    public IReadOnlyList<string> MapDlcs { get; private set; } = [];

    public sealed record MapMeta(string Key, string? ExeVersion, string? GameVersion, List<string> MapDlcs, DateTime BuiltUtc);

    private static readonly System.Text.Json.JsonSerializerOptions MetaJson = new(System.Text.Json.JsonSerializerDefaults.Web);

    private MapMeta? LoadBundle()
    {
        try
        {
            if (BundleFolder is null) return null;
            var path = Path.Combine(BundleFolder, "bundle.json");
            if (!File.Exists(path)) return null;
            var m = System.Text.Json.JsonSerializer.Deserialize<MapMeta>(File.ReadAllText(path), MetaJson);
            return m is not null && File.Exists(Path.Combine(BundleFolder, $"network-{m.Key}.bin")) ? m : null;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Map DLCs of this installation, cached per cache key (opening every archive takes a moment).</summary>
    private MapMeta LocalMeta(string game, string key)
    {
        var path = MetaPath(key);
        try
        {
            if (File.Exists(path) && System.Text.Json.JsonSerializer.Deserialize<MapMeta>(File.ReadAllText(path), MetaJson) is { } m) return m;
        }
        catch (Exception) { /* rebuild the meta below */ }
        var meta = new MapMeta(key, RoadNetworkBuilder.ExeVersion(game), null, RoadNetworkBuilder.MapDlcs(game), DateTime.UtcNow);
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(meta, MetaJson));
        return meta;
    }

    /// <summary>
    /// The shipped map wins when it covers every map DLC of this installation and either has more of them
    /// or matches the game version exactly (then it is identical to a local build, minus the wait).
    /// </summary>
    private static bool PreferBundle(MapMeta bundle, MapMeta? local)
    {
        if (local is null) return true;
        var b = bundle.MapDlcs.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!local.MapDlcs.All(b.Contains)) return false;
        return b.Count > local.MapDlcs.Count || string.Equals(bundle.ExeVersion, local.ExeVersion, StringComparison.Ordinal);
    }

    /// <summary>Copies the shipped map into the data folder once (same layout as a local build).</summary>
    private void InstallBundle(MapMeta bundle)
    {
        foreach (var src in Directory.GetFiles(BundleFolder!))
        {
            var name = Path.GetFileName(src);
            if (!name.Contains(bundle.Key, StringComparison.Ordinal)) continue;
            var dst = Path.Combine(_folder, name);
            if (!File.Exists(dst) || new FileInfo(dst).Length != new FileInfo(src).Length) File.Copy(src, dst, true);
        }
        File.WriteAllText(MetaPath(bundle.Key), System.Text.Json.JsonSerializer.Serialize(bundle, MetaJson));
    }

    /// <summary>Load the cached network, or build it in the background when <paramref name="autoBuild"/> is set.</summary>
    public void Initialize(bool autoBuild)
    {
        var game = _gamePath();
        var bundle = LoadBundle();
        if ((game is null || !Directory.Exists(game)) && bundle is null)
        {
            SetState(MapBuildState.Unavailable, 0, "ETS2 installation not found");
            return;
        }
        Task.Run(() =>
        {
            try
            {
                var hasGame = game is not null && Directory.Exists(game);
                var local = hasGame ? LocalMeta(game!, RoadNetworkBuilder.ComputeCacheKey(game!)) : null;
                string key;
                if (bundle is not null && PreferBundle(bundle, local))
                {
                    SetState(MapBuildState.Building, 0.5, "Installing the full ETS2 map");
                    InstallBundle(bundle);
                    key = bundle.Key;
                    Source = "bundled";
                    MapDlcs = bundle.MapDlcs;
                    autoBuild = false; // the shipped map is complete; nothing to build
                }
                else
                {
                    key = local!.Key;
                    Source = "local";
                    MapDlcs = local.MapDlcs;
                }
                CacheKey = key;
                var net = RoadNetwork.TryLoad(NetworkPath(key), key);
                if (net is not null && File.Exists(StreetsPath(key)) && File.Exists(PoisPath(key)) && !File.Exists(CountriesPath(key))
                    && CountryMapBuilder.LoadInput(CountryInputPath(key), net.VX.Length) is { } vc)
                {
                    // Only the border tracing changed: redo it from the stored inputs (seconds, no full rebuild).
                    SetState(MapBuildState.Building, 0.95, "Tracing country borders");
                    CountryMapBuilder.Write(CountriesPath(key), net.VX, net.VZ, vc, net.EdgeFrom, net.EdgeTo);
                    CleanupOld(key);
                }
                if (net is not null && File.Exists(StreetsPath(key)) && File.Exists(PoisPath(key)) && File.Exists(CountriesPath(key)))
                {
                    Activate(net);
                    SetState(MapBuildState.Ready, 1, ReadyMessage(net));
                    return;
                }
                SetState(MapBuildState.Missing, 0, "Road map not built yet");
                if (autoBuild && hasGame) Build();
            }
            catch (Exception ex)
            {
                SetState(MapBuildState.Error, 0, ex.Message);
            }
        });
    }

    public void Build()
    {
        var game = _gamePath();
        if (game is null) { SetState(MapBuildState.Unavailable, 0, "ETS2 installation not found"); return; }
        lock (_gate)
        {
            if (State == MapBuildState.Building) return;
            _buildCts = new CancellationTokenSource();
        }
        var ct = _buildCts.Token;
        SetState(MapBuildState.Building, 0, "Starting");
        var thread = new Thread(() =>
        {
            try
            {
                var key = RoadNetworkBuilder.ComputeCacheKey(game);
                CacheKey = key;
                var builder = new RoadNetworkBuilder(game, (p, m) => SetState(MapBuildState.Building, p, m)) { CountryInputPath = CountryInputPath(key) };
                var net = builder.Build(key, StreetsPath(key), PoisPath(key), CountriesPath(key), ct);
                net.Save(NetworkPath(key));
                var meta = new MapMeta(key, RoadNetworkBuilder.ExeVersion(game), net.GameVersion, RoadNetworkBuilder.MapDlcs(game), net.BuiltUtc);
                File.WriteAllText(MetaPath(key), System.Text.Json.JsonSerializer.Serialize(meta, MetaJson));
                Source = "local";
                MapDlcs = meta.MapDlcs;
                CleanupOld(key);
                Activate(net);
                SetState(MapBuildState.Ready, 1, ReadyMessage(net));
                GC.Collect();
            }
            catch (OperationCanceledException)
            {
                SetState(MapBuildState.Missing, 0, "Build cancelled");
            }
            catch (Exception ex)
            {
                SetState(MapBuildState.Error, 0, ex.Message);
            }
        })
        { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Haulix map build" };
        thread.Start();
    }

    public void CancelBuild() => _buildCts?.Cancel();

    private void Activate(RoadNetwork net)
    {
        var router = new Router(net);
        lock (_gate)
        {
            _network = net;
            _router = router;
        }
        _jobKey = ""; // force a fresh job route
        lock (_reconstructed) _reconstructed.Clear();
        if (_last is not null) Update(_last, force: true);
    }

    private string NetworkPath(string key) => Path.Combine(_folder, $"network-{key}.bin");
    private string StreetsPath(string key) => Path.Combine(_folder, $"streets-{key}.bin");
    private string PoisPath(string key) => Path.Combine(_folder, $"pois-{key}.json");
    private string CountriesPath(string key) => Path.Combine(_folder, $"countries-{key}-v{CountryMapBuilder.Version}.json");
    private string CountryInputPath(string key) => Path.Combine(_folder, $"vcountry-{key}.bin");
    private string MetaPath(string key) => Path.Combine(_folder, $"meta-{key}.json");

    private string ReadyMessage(RoadNetwork net) => Source == "bundled"
        ? $"Full ETS2 map {net.GameVersion} (shipped with HAULIX, {MapDlcs.Count} map DLCs)"
        : $"Road map for ETS2 {net.GameVersion} ({MapDlcs.Count} map DLCs)";

    private void CleanupOld(string key)
    {
        foreach (var f in Directory.GetFiles(_folder))
            if (Path.GetFileName(f).StartsWith("meta-", StringComparison.Ordinal)) continue; // tiny; keeps DLC detection cached
            else if (!Path.GetFileName(f).Contains(key, StringComparison.Ordinal)
                || (Path.GetFileName(f).StartsWith("countries-", StringComparison.Ordinal) && f != CountriesPath(key))
                || (Path.GetFileName(f).StartsWith("land-", StringComparison.Ordinal) && Path.GetFileName(f) != Path.GetFileName(CountriesPath(key)).Replace("countries-", "land-").Replace(".json", ".png")))
                try { File.Delete(f); } catch (IOException) { }
    }

    private void SetState(MapBuildState s, double p, string msg)
    {
        var changed = s != State || Math.Abs(p - Progress) > 0.009 || msg != Message;
        State = s; Progress = p; Message = msg;
        if (changed) StatusChanged?.Invoke();
    }

    /* ------------------------------------------------------------ routing */

    public IEnumerable<MapPoi> Search(string kind, string query) =>
        _network?.Pois.Where(p => p.Kind == kind && (p.Id.Contains(query, StringComparison.OrdinalIgnoreCase) || (p.City ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)))
        ?? Enumerable.Empty<MapPoi>();

    public RouteState? SetManual(RouteDestination dest)
    {
        _manual = dest;
        return Recompute("manual", dest);
    }

    public RouteState? SetManualCity(string cityId, string name)
    {
        var city = _network?.Pois.FirstOrDefault(p => p.Kind == "city" && p.Id == cityId);
        if (city is null) return null;
        return SetManual(new RouteDestination("city", name, null, cityId, city.X, city.Z));
    }

    public RouteState? ClearManual()
    {
        _manual = null;
        _jobKey = "";
        if (_last is not null && _last.OnJob) { Update(_last, force: true); return Route; }
        Route = null;
        RouteChanged?.Invoke(null);
        return null;
    }

    public RouteState? Reroute()
    {
        _jobKey = "";
        if (_manual is not null) return Recompute("manual", _manual);
        if (_last is not null) Update(_last, force: true);
        return Route;
    }

    private string? _endedJobKey;
    private readonly Dictionary<string, float[]?> _reconstructed = new();

    /// <summary>Job delivered or cancelled: drop the job route right away (manual routes stay).</summary>
    public void OnJobEnded(TelemetrySnapshot s)
    {
        _endedJobKey = $"{s.DestinationCompanyId}|{s.DestinationCityId}";
        _jobKey = "";
        if (Route is not null && Route.Mode == "job")
        {
            Route = null;
            RouteChanged?.Invoke(null);
        }
    }

    /// <summary>City-to-city route over the road map, for deliveries without a recorded GPS trace.</summary>
    public float[]? ReconstructRoute(string? fromCity, string? toCity)
    {
        var router = _router;
        var net = _network;
        if (router is null || net is null || string.IsNullOrEmpty(fromCity) || string.IsNullOrEmpty(toCity) || fromCity == toCity) return null;
        var key = $"{fromCity}>{toCity}";
        lock (_reconstructed)
            if (_reconstructed.TryGetValue(key, out var cached)) return cached;
        var a = net.Pois.FirstOrDefault(p => p.Kind == "city" && p.Id == fromCity);
        var b = net.Pois.FirstOrDefault(p => p.Kind == "city" && p.Id == toCity);
        float[]? pts = null;
        if (a is not null && b is not null)
        {
            var r = router.Route(a.X, a.Z, null, router.VerticesNear(b.X, b.Z, 900), b.X, b.Z);
            pts = r?.Points.Select(v => MathF.Round(v, 0)).ToArray();
        }
        lock (_reconstructed) _reconstructed[key] = pts;
        return pts;
    }

    /// <summary>In-game km left on HAULIX's current route from the truck's position (world metres × 19), or null.</summary>
    public double? RemainingRouteKm(TelemetrySnapshot s)
    {
        var r = Route;
        if (r is null || r.Error is not null || r.Points.Length < 4) return null;
        var n = r.Points.Length / 2;
        var i = Math.Clamp(_progressIndex + 1, 0, n - 1);
        double m = Math.Sqrt(Math.Pow(r.Points[i * 2] - s.X, 2) + Math.Pow(r.Points[i * 2 + 1] - s.Z, 2));
        for (var k = i; k < n - 1; k++)
            m += Math.Sqrt(Math.Pow(r.Points[k * 2 + 2] - r.Points[k * 2], 2) + Math.Pow(r.Points[k * 2 + 3] - r.Points[k * 2 + 1], 2));
        return m * 19 / 1000;
    }

    /// <summary>Called for every telemetry sample (on the telemetry thread); does real work at most once per second.</summary>
    public void OnSample(TelemetrySnapshot s)
    {
        _last = s;
        var now = DateTime.UtcNow;
        if ((now - _lastCheckUtc).TotalSeconds < 1) return;
        _lastCheckUtc = now;
        Update(s, force: false);
    }

    private void Update(TelemetrySnapshot s, bool force)
    {
        if (_router is null) return;
        if (_manual is not null)
        {
            if (Route is null || force) { Recompute("manual", _manual); return; }
            if (Math.Sqrt(Math.Pow(s.X - _manual.X, 2) + Math.Pow(s.Z - _manual.Z, 2)) < 150)
            {
                // Arrived at the manual destination: hand navigation back to the job.
                _manual = null;
                _jobKey = "";
                Route = null;
                RouteChanged?.Invoke(null);
                return;
            }
            CheckOffRoute(s, "manual", _manual);
            return;
        }

        if (!s.OnJob || string.IsNullOrEmpty(s.DestinationCityId) || !AutoRouteToJob())
        {
            _endedJobKey = null;
            if (Route is not null && Route.Mode == "job") { Route = null; RouteChanged?.Invoke(null); }
            _jobKey = "";
            return;
        }
        var key = $"{s.DestinationCompanyId}|{s.DestinationCityId}";
        // The plugin can report the finished job for a moment longer; don't resurrect its route.
        if (key == _endedJobKey && !force) return;
        _endedJobKey = null;
        if (key != _jobKey || force)
        {
            _jobKey = key;
            var company = _network!.Pois
                .Where(p => p.Kind == "company" && p.Id == s.DestinationCompanyId && p.City == s.DestinationCityId)
                .OrderBy(p => Math.Pow(p.X - s.X, 2) + Math.Pow(p.Z - s.Z, 2))
                .FirstOrDefault();
            RouteDestination dest;
            if (company is not null)
                dest = new RouteDestination("company", $"{s.DestinationCompany}, {s.DestinationCity}", company.Id, company.City, company.X, company.Z);
            else
            {
                var city = _network.Pois.FirstOrDefault(p => p.Kind == "city" && p.Id == s.DestinationCityId);
                if (city is null) { PublishError("job", s, "Destination not found on the road map"); return; }
                dest = new RouteDestination("city", s.DestinationCity, null, city.Id, city.X, city.Z);
            }
            Recompute("job", dest);
            return;
        }
        if (Route is not null) { CheckOffRoute(s, "job", Route.Destination); CheckGameRoute(s); }
    }

    private void CheckOffRoute(TelemetrySnapshot s, string mode, RouteDestination dest)
    {
        var r = Route;
        if (r is null || r.Points.Length < 4) return;
        // Search forward from the last known progress point.
        var best = float.MaxValue; var bestIdx = _progressIndex;
        var from = Math.Max(0, _progressIndex - 20);
        var to = Math.Min(r.Points.Length / 2 - 1, _progressIndex + 600);
        for (var i = from; i < to; i++)
        {
            var d = SegDist((float)s.X, (float)s.Z, r.Points[i * 2], r.Points[i * 2 + 1], r.Points[i * 2 + 2], r.Points[i * 2 + 3]);
            if (d < best) { best = d; bestIdx = i; }
        }
        _progressIndex = bestIdx;
        _offRouteSeconds = best > 90 ? _offRouteSeconds + 1 : 0;
        if (_offRouteSeconds >= 4 && (DateTime.UtcNow - _lastRouteUtc).TotalSeconds > 5)
        {
            _offRouteSeconds = 0;
            Recompute(mode, dest);
        }
    }

    /* ------------------------------------------------------------ following the in-game navigation */

    // The SDK exposes only the remaining distance of the in-game GPS, not its geometry. HAULIX compares it with
    // its own route: when they drift apart (the player picked another route or a waypoint in the game, or the
    // game re-routed), HAULIX plans alternatives and takes the one whose length matches the game.
    private int _gameMismatch;
    private double _lastGameKm;
    private DateTime _lastGameCheckUtc, _lastMatchUtc;
    private volatile bool _matching;

    /// <summary>Raised when HAULIX adopted a different route to follow the in-game navigation (km, matched).</summary>
    public event Action<double, bool>? GameRouteAdjusted;

    private static double Tolerance(double gameKm) => Math.Max(3, gameKm * 0.05) + 1.5;

    private void CheckGameRoute(TelemetrySnapshot s)
    {
        var r = Route;
        var gameKm = s.RouteDistanceKm;
        if (r is null || r.Mode != "job" || r.Error is not null || gameKm < 0.5) { _gameMismatch = 0; _lastGameKm = 0; return; }
        var now = DateTime.UtcNow;
        if ((now - _lastGameCheckUtc).TotalSeconds < 2) return;
        _lastGameCheckUtc = now;

        var ours = RemainingRouteKm(s) ?? 0;
        var off = Math.Abs(gameKm - ours) > Tolerance(gameKm);
        // A sudden jump of the game's distance (more than driving could explain) = the player changed the route.
        var jumped = _lastGameKm > 0 && Math.Abs(gameKm - _lastGameKm) > 4;
        _lastGameKm = gameKm;
        _gameMismatch = off ? _gameMismatch + 1 : 0;

        if (!off)
        {
            if (r.GameRoute != "matched") { Route = r with { GameRoute = "matched" }; RouteChanged?.Invoke(Route); }
            return;
        }
        if ((_gameMismatch >= 3 || jumped) && !_matching && (now - _lastMatchUtc).TotalSeconds > 8)
        {
            _matching = true;
            _lastMatchUtc = now;
            var dest = r.Destination;
            _ = Task.Run(() =>
            {
                try { MatchGame(s, dest, gameKm); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Route matching failed: {ex}"); }
                finally { _matching = false; }
            });
        }
    }

    private void MatchGame(TelemetrySnapshot s, RouteDestination dest, double gameKm)
    {
        var router = _router;
        if (router is null) return;
        var goals = router.VerticesNear(dest.X, dest.Z, dest.Kind == "city" ? 900 : 300);
        var alts = router.Alternatives((float)s.X, (float)s.Z, s.HeadingDeg, goals, dest.X, dest.Z, 5);
        if (alts.Count == 0) return;
        var best = alts.MinBy(a => Math.Abs(a.LengthMeters * 19 / 1000.0 - gameKm))!;
        var km = best.LengthMeters * 19 / 1000.0;
        var matched = Math.Abs(km - gameKm) <= Tolerance(gameKm) * 1.5;
        var current = Route;
        if (current is null || current.Mode != "job") return;
        if (!matched)
        {
            // Nothing on the road map matches: keep the current line but say so.
            if (current.GameRoute != "differs") { Route = current with { GameRoute = "differs" }; RouteChanged?.Invoke(Route); GameRouteAdjusted?.Invoke(gameKm, false); }
            return;
        }
        _progressIndex = 0;
        _lastRouteUtc = DateTime.UtcNow;
        _gameMismatch = 0;
        Route = new RouteState("job", dest, best.Points.Select(v => MathF.Round(v, 1)).ToArray(), best.LengthMeters,
            Math.Round(km, 0), DateTime.UtcNow, null, "matched");
        RouteChanged?.Invoke(Route);
        GameRouteAdjusted?.Invoke(km, true);
    }

    private RouteState? Recompute(string mode, RouteDestination dest)
    {
        var router = _router;
        var s = _last;
        if (router is null) return null;
        if (s is null || (s.X == 0 && s.Z == 0))
        {
            PublishError(mode, s, "Truck position unknown — start ETS2 to plan a route", dest);
            return Route;
        }
        var goals = router.VerticesNear(dest.X, dest.Z, dest.Kind == "city" ? 900 : 300);
        var result = router.Route((float)s.X, (float)s.Z, s.HeadingDeg, goals, dest.X, dest.Z)
                     ?? router.Route((float)s.X, (float)s.Z, null, goals, dest.X, dest.Z);
        _lastRouteUtc = DateTime.UtcNow;
        _progressIndex = 0;
        if (result is null)
        {
            PublishError(mode, s, "No route found on the road map", dest);
            return Route;
        }
        Route = new RouteState(mode, dest, result.Points.Select(v => MathF.Round(v, 1)).ToArray(), result.LengthMeters,
            Math.Round(result.LengthMeters * 19 / 1000.0, 0), DateTime.UtcNow, null);
        RouteChanged?.Invoke(Route);
        return Route;
    }

    private void PublishError(string mode, TelemetrySnapshot? s, string error, RouteDestination? dest = null)
    {
        dest ??= new RouteDestination("city", s?.DestinationCity ?? "", null, s?.DestinationCityId, 0, 0);
        Route = new RouteState(mode, dest, Array.Empty<float>(), 0, 0, DateTime.UtcNow, error);
        RouteChanged?.Invoke(Route);
    }

    private static float SegDist(float x, float z, float ax, float az, float bx, float bz)
    {
        var dx = bx - ax; var dz = bz - az;
        var l2 = dx * dx + dz * dz;
        var t = l2 == 0 ? 0 : Math.Clamp(((x - ax) * dx + (z - az) * dz) / l2, 0, 1);
        var px = ax + dx * t; var pz = az + dz * t;
        return MathF.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz));
    }

    public void Dispose() => _buildCts?.Cancel();
}
