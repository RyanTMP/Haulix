using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TruckLib;
using TruckLib.HashFs;
using TruckLib.Models.Ppd;
using TruckLib.ScsMap;
using TruckLib.Sii;

namespace Haulix.Core.Map;

/// <summary>
/// Extracts the road network from the local ETS2 installation (read-only) using TruckLib:
/// roads, junction (prefab) lane connectivity, street geometry for display, and map POIs.
/// The map is processed in sector batches to keep memory around 1–1.5 GB.
/// </summary>
public sealed class RoadNetworkBuilder
{
    // Street classes shared with the UI renderer.
    public const byte Motorway = 0, MainRoad = 1, LocalRoad = 2, Junction = 3;
    public const byte HiddenFlag = 0x10, AvoidFlag = 0x20;
    private const int BatchSize = 6;       // sectors per side in a batch core
    private const float SectorSize = 4000; // Map.SectorSize

    private readonly string _gamePath;
    private readonly Action<double, string>? _progress;

    private readonly Dictionary<ulong, int> _vertexOf = new();
    private readonly List<float> _vx = new(), _vz = new();
    private readonly List<int> _eFrom = new(), _eTo = new(), _eGeomStart = new(), _eGeomCount = new();
    private readonly List<float> _eLen = new();
    private readonly List<bool> _eRev = new();
    private readonly List<byte> _eClass = new();
    private readonly List<float> _geom = new();

    // Display geometry (streets.bin)
    private readonly List<byte> _sClass = new();
    private readonly List<int> _sOffset = new() { 0 };
    private readonly List<float> _sCoords = new();

    private readonly List<MapPoi> _pois = new();
    private readonly List<(string Name, float X, float Z, float W, float H)> _areas = new();
    private readonly Dictionary<string, (double X, double Z, double W)> _cityAcc = new();
    private readonly HashSet<ulong> _done = new();

    private readonly Dictionary<string, string> _prefabDesc = new();
    private readonly Dictionary<string, (int L, int R)> _roadLanes = new();
    private readonly Dictionary<string, PrefabDescriptor?> _ppd = new();
    private AssetLoader _fs = null!;

    /// <summary>Where to keep the per-vertex country ids, so borders can be re-traced without a full rebuild.</summary>
    public string? CountryInputPath { get; init; }

    public RoadNetworkBuilder(string gamePath, Action<double, string>? progress = null)
    {
        _gamePath = gamePath;
        _progress = progress;
    }

    /// <summary>Archives that can contain map sectors or world definitions.</summary>
    public static List<string> MapArchives(string gamePath) =>
        Directory.GetFiles(gamePath, "*.scs")
            .Where(f => Path.GetFileName(f) is "base_map.scs" or "def.scs" or "base.scs" || Path.GetFileName(f).StartsWith("dlc_", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>DLC archives that add map sectors (e.g. "dlc_iberia"), i.e. the map expansions this installation has.</summary>
    public static List<string> MapDlcs(string gamePath)
    {
        var result = new List<string>();
        foreach (var f in MapArchives(gamePath))
        {
            var name = Path.GetFileNameWithoutExtension(f);
            if (!name.StartsWith("dlc_", StringComparison.OrdinalIgnoreCase)) continue;
            IFileSystem? r = null;
            try
            {
                r = HashFsReader.Open(f);
                if (r.GetFiles("/map/europe").Any(x => x.EndsWith(".base", StringComparison.Ordinal))) result.Add(name.ToLowerInvariant());
            }
            catch (Exception)
            {
                // Not a map archive (or no map directory): ignore.
            }
            finally
            {
                (r as IDisposable)?.Dispose();
            }
        }
        return result;
    }

    /// <summary>Version of eurotrucks2.exe, e.g. "1.61.1.12".</summary>
    public static string? ExeVersion(string gamePath)
    {
        var exe = Path.Combine(gamePath, "bin", "win_x64", "eurotrucks2.exe");
        return File.Exists(exe) ? FileVersionInfo.GetVersionInfo(exe).ProductVersion : null;
    }

    /// <summary>Changes whenever the game or a map DLC is updated/added/removed.</summary>
    public static string ComputeCacheKey(string gamePath)
    {
        var sb = new StringBuilder($"v{RoadNetwork.FormatVersion}|");
        var exe = Path.Combine(gamePath, "bin", "win_x64", "eurotrucks2.exe");
        if (File.Exists(exe)) sb.Append(FileVersionInfo.GetVersionInfo(exe).ProductVersion).Append('|');
        foreach (var f in MapArchives(gamePath))
        {
            var fi = new FileInfo(f);
            sb.Append(fi.Name).Append(':').Append(fi.Length).Append(':').Append(fi.LastWriteTimeUtc.Ticks).Append('|');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..24];
    }

    // Country of each road (from its nodes) and real border-crossing vertices, for the country map.
    private readonly List<(int A, int B, byte Country)> _roadCountry = new();
    private readonly HashSet<int> _borderVertices = new();

    public RoadNetwork Build(string cacheKey, string streetsPath, string poisPath, CancellationToken ct) =>
        Build(cacheKey, streetsPath, poisPath, null, ct);

    public RoadNetwork Build(string cacheKey, string streetsPath, string poisPath, string? countriesPath, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        Report(0, "Opening game archives");
        var readers = MapArchives(_gamePath).Select(f => (IFileSystem)HashFsReader.Open(f)).ToList();
        readers.Reverse(); // DLC archives take precedence over base
        try
        {
            _fs = new AssetLoader(readers.ToArray());
            if (!_fs.FileExists("/map/europe.mbd")) throw new InvalidOperationException("The ETS2 map (map/europe.mbd) was not found in the game archives.");

            Report(0.02, "Reading road and prefab definitions");
            LoadDefinitions();

            var sectors = _fs.GetFiles("/map/europe")
                .Where(f => f.EndsWith(".base", StringComparison.Ordinal))
                .Select(Sector.SectorCoordsFromSectorFilePath)
                .Distinct()
                .ToList();
            var all = sectors.ToHashSet();
            var batches = sectors.GroupBy(c => (Math.Floor(c.X / (double)BatchSize), Math.Floor(c.Z / (double)BatchSize))).ToList();

            for (var b = 0; b < batches.Count; b++)
            {
                ct.ThrowIfCancellationRequested();
                Report(0.05 + 0.85 * b / batches.Count, $"Reading map sectors ({b + 1}/{batches.Count})");
                var core = batches[b].ToHashSet();
                var load = all.Where(c => core.Any(k => Math.Abs(k.X - c.X) <= 1 && Math.Abs(k.Z - c.Z) <= 1)).ToList();
                var map = TruckLib.ScsMap.Map.Open("/map/europe.mbd", _fs, load);
                ProcessBatch(map, core);
                map = null;
                GC.Collect();
            }

            Report(0.92, "Writing street map");
            foreach (var (city, acc) in _cityAcc)
                _pois.Add(new MapPoi("city", city, city, (float)(acc.X / acc.W), (float)(acc.Z / acc.W)));
            WriteStreets(streetsPath);
            if (countriesPath is not null)
            {
                Report(0.95, "Tracing country borders");
                var vc = VertexCountries();
                if (CountryInputPath is not null) CountryMapBuilder.SaveInput(CountryInputPath, vc);
                CountryMapBuilder.Write(countriesPath, _vx.ToArray(), _vz.ToArray(), vc, _eFrom.ToArray(), _eTo.ToArray());
            }
            File.WriteAllText(poisPath, JsonSerializer.Serialize(_pois.Select(p => (object)new { k = p.Kind, id = p.Id, c = p.City, x = MathF.Round(p.X, 1), z = MathF.Round(p.Z, 1) })
                .Concat(_areas.Select(a => (object)new { k = "area", id = a.Name, x = MathF.Round(a.X), z = MathF.Round(a.Z), w = MathF.Round(a.W), h = MathF.Round(a.H) }))));

            var net = new RoadNetwork
            {
                CacheKey = cacheKey,
                BuiltUtc = DateTime.UtcNow,
                GameVersion = GameVersion(),
                VX = _vx.ToArray(), VZ = _vz.ToArray(),
                EdgeFrom = _eFrom.ToArray(), EdgeTo = _eTo.ToArray(), EdgeLen = _eLen.ToArray(),
                EdgeGeomStart = _eGeomStart.ToArray(), EdgeGeomCount = _eGeomCount.ToArray(), EdgeGeomReverse = _eRev.ToArray(),
                EdgeClass = _eClass.ToArray(), Geom = _geom.ToArray(), Pois = _pois,
            };
            net.BuildAdjacency();
            Report(1, $"Road map ready: {net.EdgeCount:N0} road segments in {sw.Elapsed.TotalSeconds:0} s");
            return net;
        }
        finally
        {
            foreach (var r in readers) (r as IDisposable)?.Dispose();
        }
    }

    private string GameVersion()
    {
        var exe = Path.Combine(_gamePath, "bin", "win_x64", "eurotrucks2.exe");
        return File.Exists(exe) ? FileVersionInfo.GetVersionInfo(exe).ProductVersion ?? "" : "";
    }

    private void Report(double p, string msg) => _progress?.Invoke(p, msg);

    private void LoadDefinitions()
    {
        foreach (var file in _fs.GetFiles("/def/world"))
        {
            var fn = Path.GetFileName(file);
            if (!fn.EndsWith(".sii", StringComparison.Ordinal)) continue;
            var isPrefab = fn.StartsWith("prefab", StringComparison.Ordinal);
            var isLook = fn.StartsWith("road_look", StringComparison.Ordinal);
            if (!isPrefab && !isLook) continue;
            SiiFile sii;
            try { sii = SiiFile.Open(file, _fs, true); }
            catch (Exception) { continue; }
            foreach (var u in sii.Units)
            {
                if (u.Class == "prefab_model" && u.Attributes.TryGetValue("prefab_desc", out var d) && d is not null)
                    _prefabDesc[u.Name.Replace("prefab.", "", StringComparison.Ordinal)] = d.ToString()!;
                else if (u.Class == "road_look")
                    _roadLanes[u.Name.Replace("road.", "", StringComparison.Ordinal)] = (Count(u, "lanes_left"), Count(u, "lanes_right"));
            }
        }

        static int Count(Unit u, string key) => u.Attributes.TryGetValue(key, out var v) && v is System.Collections.IList l ? l.Count : 0;
    }

    private PrefabDescriptor? Ppd(string model)
    {
        if (_ppd.TryGetValue(model, out var p)) return p;
        p = null;
        if (_prefabDesc.TryGetValue(model, out var path))
        {
            try { p = PrefabDescriptor.Open(path, _fs); }
            catch (Exception) { p = null; }
        }
        _ppd[model] = p;
        return p;
    }

    private int Vertex(INode n)
    {
        if (_vertexOf.TryGetValue(n.Uid, out var i)) return i;
        i = _vx.Count;
        _vertexOf[n.Uid] = i;
        _vx.Add(n.Position.X);
        _vz.Add(n.Position.Z);
        return i;
    }

    private static bool InCore(HashSet<SectorCoordinate> core, Vector3 p) => core.Contains(TruckLib.ScsMap.Map.GetSectorOfCoordinate(p));

    private void ProcessBatch(TruckLib.ScsMap.Map map, HashSet<SectorCoordinate> core)
    {

        foreach (var item in map.MapItems.Values)
        {
            switch (item)
            {
                case Road r when r.Node is not null && r.ForwardNode is not null:
                    if (InCore(core, r.Node.Position) && _done.Add(r.Uid)) AddRoad(r);
                    break;
                case Prefab pf when pf.Nodes.Count > 0 && pf.Nodes[0] is not null:
                    if (InCore(core, pf.Nodes[0].Position) && _done.Add(pf.Uid)) AddPrefab(pf);
                    break;
                case Company c when c.Node is not null:
                    if (InCore(core, c.Node.Position) && _done.Add(c.Uid))
                        _pois.Add(new MapPoi("company", c.CompanyName.String, c.CityName.String, c.Node.Position.X, c.Node.Position.Z));
                    break;
                case Service s when s.Node is not null:
                    if (InCore(core, s.Node.Position) && _done.Add(s.Uid))
                    {
                        var kind = s is FuelPump ? "fuel" : s.ServiceType switch
                        {
                            ServiceType.GasStation => "fuel",
                            ServiceType.ServiceStation => "service",
                            ServiceType.TruckDealer => "dealer",
                            ServiceType.Recruitment => "recruitment",
                            ServiceType.Parking => "parking",
                            _ => "weigh",
                        };
                        _pois.Add(new MapPoi(kind, kind, null, s.Node.Position.X, s.Node.Position.Z));
                    }
                    break;
                case Garage g when g.Node is not null:
                    if (InCore(core, g.Node.Position) && _done.Add(g.Uid))
                        _pois.Add(new MapPoi("garage", g.CityName.String, g.CityName.String, g.Node.Position.X, g.Node.Position.Z));
                    break;
                case Ferry f when f.Node is not null:
                    if (InCore(core, f.Node.Position) && _done.Add(f.Uid))
                        _pois.Add(new MapPoi(f.TrainTransport ? "train" : "ferry", f.Port.String, null, f.Node.Position.X, f.Node.Position.Z));
                    break;
                case CityArea ca when ca.Node is not null:
                    if (InCore(core, ca.Node.Position) && _done.Add(ca.Uid))
                    {
                        var name = ca.Name.String;
                        // Every city area rectangle is drawn as the built-up area on HAULIX's own map style.
                        if (ca.Width > 1 && ca.Height > 1) _areas.Add((name, ca.Node.Position.X, ca.Node.Position.Z, ca.Width, ca.Height));
                        if (!ca.ShowInUi) break;
                        var area = Math.Max(1, ca.Width * ca.Height);
                        var cx = ca.Node.Position.X + ca.Width / 2;
                        var cz = ca.Node.Position.Z + ca.Height / 2;
                        _cityAcc.TryGetValue(name, out var acc);
                        _cityAcc[name] = (acc.X + cx * area, acc.Z + cz * area, acc.W + area);
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Country per graph vertex: roads tagged by their nodes seed the fill, which then spreads along the
    /// road graph but never through a real border crossing (a node whose two sides differ).
    /// </summary>
    private int[] VertexCountries()
    {
        var n = _vx.Count;
        var country = new int[n];
        var adjStart = new int[n + 1];
        foreach (var f in _eFrom) adjStart[f + 1]++;
        foreach (var t in _eTo) adjStart[t + 1]++;
        for (var i = 0; i < n; i++) adjStart[i + 1] += adjStart[i];
        var fill = (int[])adjStart.Clone();
        var adj = new int[_eFrom.Count * 2];
        for (var e = 0; e < _eFrom.Count; e++) { adj[fill[_eFrom[e]]++] = _eTo[e]; adj[fill[_eTo[e]]++] = _eFrom[e]; }

        var q = new Queue<int>();
        foreach (var (a, b, c) in _roadCountry)
        {
            foreach (var v in new[] { a, b })
            {
                if (_borderVertices.Contains(v) || country[v] != 0) continue;
                country[v] = c;
                q.Enqueue(v);
            }
        }
        while (q.Count > 0)
        {
            var u = q.Dequeue();
            for (var k = adjStart[u]; k < adjStart[u + 1]; k++)
            {
                var v = adj[k];
                if (country[v] != 0 || _borderVertices.Contains(v)) continue;
                country[v] = country[u];
                q.Enqueue(v);
            }
        }
        return country;
    }

    private void AddRoad(Road r)
    {
        var a = Vertex(r.Node);
        var b = Vertex(r.ForwardNode);
        if (r.Node is Node bn && r.ForwardNode is Node fn)
        {
            // A node's forward/backward country is the country of the item on that side.
            var c = bn.ForwardCountry != 0 ? bn.ForwardCountry : fn.BackwardCountry;
            if (c != 0) _roadCountry.Add((a, b, c));
            if (bn.IsCountryBorder && bn.ForwardCountry != bn.BackwardCountry) _borderVertices.Add(a);
            if (fn.IsCountryBorder && fn.ForwardCountry != fn.BackwardCountry) _borderVertices.Add(b);
        }
        var len = r.Length;
        if (len <= 0) len = Vector3.Distance(r.Node.Position, r.ForwardNode.Position);

        // Sample the road curve.
        var n = Math.Clamp((int)MathF.Ceiling(len / 35f), 1, 24);
        var start = _geom.Count / 2;
        for (var i = 0; i <= n; i++)
        {
            Vector3 p;
            try { p = r.InterpolateCurve(i / (float)n).Position; }
            catch (Exception) { p = Vector3.Lerp(r.Node.Position, r.ForwardNode.Position, i / (float)n); }
            _geom.Add(p.X);
            _geom.Add(p.Z);
        }
        var count = n + 1;

        bool fwd = true, bwd = true;
        // IsCityRoad is marked obsolete in TruckLib (rarely set by current maps); it only affects street shading.
#pragma warning disable CS0612
        byte cls = r.IsCityRoad ? LocalRoad : MainRoad;
#pragma warning restore CS0612
        if (_roadLanes.TryGetValue(r.RoadType.String, out var lanes))
        {
            var (fl, bl) = r.LeftHandTraffic ? (lanes.L, lanes.R) : (lanes.R, lanes.L);
            fwd = fl > 0;
            bwd = bl > 0;
            if (!fwd && !bwd) fwd = bwd = true;
            if (cls != LocalRoad && Math.Max(lanes.L, lanes.R) >= 2) cls = Motorway;
        }
        // Secret roads (unreleased areas) and "GPS avoid" roads stay routable for connectivity but are
        // penalised like the game's own GPS. Roads merely hidden from the UI map are normal: ETS2 draws only
        // one side of a dual carriageway, so the other direction is "hidden" yet fully drivable.
        var edgeCls = (byte)(cls | (r.Secret ? HiddenFlag : 0) | (r.GpsAvoid ? AvoidFlag : 0));
        if (fwd) AddEdge(a, b, len, start, count, false, edgeCls);
        if (bwd) AddEdge(b, a, len, start, count, true, edgeCls);

        if (r.ShowInUiMap && !r.Secret)
        {
            _sClass.Add(cls);
            for (var i = 0; i < count; i++) { _sCoords.Add(_geom[(start + i) * 2]); _sCoords.Add(_geom[(start + i) * 2 + 1]); }
            _sOffset.Add(_sCoords.Count / 2);
        }
    }

    private void AddEdge(int from, int to, float len, int geomStart, int geomCount, bool reverse, byte cls)
    {
        _eFrom.Add(from); _eTo.Add(to); _eLen.Add(len);
        _eGeomStart.Add(geomStart); _eGeomCount.Add(geomCount); _eRev.Add(reverse); _eClass.Add(cls);
    }

    private static float Yaw(Vector3 dir) => MathF.Atan2(dir.Z, dir.X);
    private static Vector3 Forward(Quaternion q) => Vector3.Transform(new Vector3(0, 0, -1), q);

    private void AddPrefab(Prefab pf)
    {
        var ppd = Ppd(pf.Model.String);
        if (ppd is null || ppd.Nodes.Count != pf.Nodes.Count) return;
        var n = ppd.Nodes.Count;
        var o = pf.Origin % n;

        // ppd space → world (verified: prefab node i ↔ ppd node (i + origin) % n).
        var mapOrigin = pf.Nodes[0];
        var ppdOrigin = ppd.Nodes[o];
        var rot = Matrix3x2.CreateRotation(Yaw(Forward(mapOrigin.Rotation)) - Yaw(ppdOrigin.Direction));
        Vector2 World(Vector3 p)
        {
            var rel = Vector2.Transform(new Vector2(p.X - ppdOrigin.Position.X, p.Z - ppdOrigin.Position.Z), rot);
            return new Vector2(mapOrigin.Position.X + rel.X, mapOrigin.Position.Z + rel.Y);
        }
        int MapIndex(int ppdIdx) => ((ppdIdx - o) % n + n) % n;

        // --- lane connectivity between prefab nodes (entry → exit) via nav curves ---
        var endsAt = new Dictionary<int, int>();
        for (var j = 0; j < n; j++)
            foreach (var c in ppd.Nodes[j].OutputLines ?? Array.Empty<int>())
                if (c >= 0) endsAt[c] = j;

        for (var i = 0; i < n; i++)
        {
            var mi = MapIndex(i);
            if (pf.Nodes[mi] is null) continue;
            var best = new Dictionary<int, (float Len, List<int> Chain)>();
            foreach (var startCurve in ppd.Nodes[i].InputLines ?? Array.Empty<int>())
            {
                if (startCurve < 0 || startCurve >= ppd.NavCurves.Count) continue;
                var q = new Queue<(int C, float L, List<int> Chain)>();
                q.Enqueue((startCurve, ppd.NavCurves[startCurve].Length, new List<int> { startCurve }));
                var seen = new HashSet<int>();
                while (q.Count > 0)
                {
                    var (c, l, chain) = q.Dequeue();
                    if (!seen.Add(c) || chain.Count > 60) continue;
                    if (endsAt.TryGetValue(c, out var j) && j != i)
                    {
                        if (!best.TryGetValue(j, out var bl) || l < bl.Len) best[j] = (l, chain);
                        continue;
                    }
                    foreach (var nx in ppd.NavCurves[c].NextLines)
                        if (nx >= 0 && nx < ppd.NavCurves.Count && !seen.Contains(nx))
                            q.Enqueue((nx, l + ppd.NavCurves[nx].Length, new List<int>(chain) { nx }));
                }
            }
            foreach (var (j, (len, chain)) in best)
            {
                var mj = MapIndex(j);
                if (pf.Nodes[mj] is null) continue;
                var start = _geom.Count / 2;
                var count = 0;
                foreach (var ci in chain)
                {
                    var cv = ppd.NavCurves[ci];
                    foreach (var p in CurvePoints(cv, count == 0))
                    {
                        var w = World(p);
                        _geom.Add(w.X); _geom.Add(w.Y);
                        count++;
                    }
                }
                if (count < 2) continue;
                AddEdge(Vertex(pf.Nodes[mi]), Vertex(pf.Nodes[mj]), Math.Max(len, 1), start, count, false, Junction);
            }
        }

        // --- display geometry from the prefab's UI map points ---
        if (!pf.ShowInUiMap || pf.Secret) return;
        var mps = ppd.MapPoints;
        for (var a = 0; a < mps.Count; a++)
        {
            var mp = mps[a];
            if (mp.RoadSize == RoadSize.Polygon) continue;
            foreach (var nb in mp.Neighbors)
            {
                if (nb <= a || nb >= mps.Count || mps[nb].RoadSize == RoadSize.Polygon) continue;
                var p1 = World(mp.Position);
                var p2 = World(mps[nb].Position);
                _sClass.Add(mp.RoadSize switch
                {
                    RoadSize.FourLanes or RoadSize.FourLaneSplit or RoadSize.ThreeLaneSplit => Motorway,
                    RoadSize.TwoLanes or RoadSize.ThreeLanes or RoadSize.TwoLaneSplit or RoadSize.ThreeLanesOneWay => MainRoad,
                    _ => LocalRoad,
                });
                _sCoords.Add(p1.X); _sCoords.Add(p1.Y); _sCoords.Add(p2.X); _sCoords.Add(p2.Y);
                _sOffset.Add(_sCoords.Count / 2);
            }
        }
    }

    /// <summary>Hermite samples of a prefab nav curve in ppd space.</summary>
    private static IEnumerable<Vector3> CurvePoints(NavCurve c, bool includeStart)
    {
        var p0 = c.StartPosition;
        var p1 = c.EndPosition;
        var len = Math.Max(c.Length, Vector3.Distance(p0, p1));
        var d0 = Forward(c.StartRotation);
        var d1 = Forward(c.EndRotation);
        // Quaternion forward conventions differ between files; orient tangents along the chord.
        var chord = p1 - p0;
        if (Vector3.Dot(d0, chord) < 0) d0 = -d0;
        if (Vector3.Dot(d1, chord) < 0) d1 = -d1;
        var steps = Math.Clamp((int)MathF.Ceiling(len / 8f), 1, 12);
        for (var i = includeStart ? 0 : 1; i <= steps; i++)
            yield return HermiteSpline.Interpolate(p0, p1, d0 * len, d1 * len, i / (float)steps);
    }

    /// <summary>Street geometry for the UI: "HXST", version, counts, classes, offsets, float32 x/z pairs.</summary>
    private void WriteStreets(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var w = new BinaryWriter(File.Create(path));
        var lines = _sClass.Count;
        var points = _sCoords.Count / 2;
        w.Write(Encoding.ASCII.GetBytes("HXST"));
        w.Write(1);
        w.Write(lines);
        w.Write(points);
        foreach (var c in _sClass) w.Write(c);
        var pad = (4 - lines % 4) % 4;
        for (var i = 0; i < pad; i++) w.Write((byte)0);
        foreach (var o in _sOffset) w.Write(o);
        foreach (var v in _sCoords) w.Write(v);
    }
}
