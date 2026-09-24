namespace Haulix.Core.Map;

public sealed record RouteResult(float[] Points, float LengthMeters, int Edges, int[] EdgeIds);

/// <summary>A* routing over <see cref="RoadNetwork"/> with heading-aware start snapping.</summary>
public sealed class Router
{
    private const float Cell = 500f;
    private readonly RoadNetwork _n;
    private readonly Dictionary<long, List<int>> _grid = new();

    public Router(RoadNetwork network)
    {
        _n = network;
        for (var e = 0; e < _n.EdgeCount; e++)
        {
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (var (x, z) in _n.EdgePoints(e))
            {
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z);
            }
            for (var cx = (int)MathF.Floor(minX / Cell); cx <= (int)MathF.Floor(maxX / Cell); cx++)
            for (var cz = (int)MathF.Floor(minZ / Cell); cz <= (int)MathF.Floor(maxZ / Cell); cz++)
            {
                var key = Key(cx, cz);
                if (!_grid.TryGetValue(key, out var list)) _grid[key] = list = new List<int>();
                list.Add(e);
            }
        }
    }

    private static long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

    /// <summary>Directed edge nearest to a position, preferring edges aligned with the heading (degrees, 0 = north, clockwise).</summary>
    public (int Edge, int Segment, float T, float Distance)? Snap(float x, float z, double? headingDeg, float maxDistance = 250)
    {
        float hx = 0, hz = 0;
        if (headingDeg is { } h)
        {
            hx = (float)Math.Sin(h * Math.PI / 180);
            hz = (float)-Math.Cos(h * Math.PI / 180);
        }
        (int, int, float, float)? best = null;
        var bestScore = float.MaxValue;
        var r = (int)MathF.Ceiling(maxDistance / Cell);
        var ccx = (int)MathF.Floor(x / Cell);
        var ccz = (int)MathF.Floor(z / Cell);
        for (var cx = ccx - r; cx <= ccx + r; cx++)
        for (var cz = ccz - r; cz <= ccz + r; cz++)
        {
            if (!_grid.TryGetValue(Key(cx, cz), out var edges)) continue;
            foreach (var e in edges)
            {
                var pts = _n.EdgePoints(e).ToArray();
                for (var i = 0; i < pts.Length - 1; i++)
                {
                    var (ax, az) = pts[i];
                    var (bx, bz) = pts[i + 1];
                    var dx = bx - ax; var dz = bz - az;
                    var l2 = dx * dx + dz * dz;
                    var t = l2 == 0 ? 0 : Math.Clamp(((x - ax) * dx + (z - az) * dz) / l2, 0, 1);
                    var px = ax + dx * t; var pz = az + dz * t;
                    var d = MathF.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz));
                    if (d > maxDistance) continue;
                    var score = d;
                    if (headingDeg is not null && l2 > 0)
                    {
                        var cos = (dx * hx + dz * hz) / MathF.Sqrt(l2);
                        score += (1 - cos) * 40; // wrong-way edges cost up to 80 m extra
                    }
                    if (score < bestScore) { bestScore = score; best = (e, i, t, d); }
                }
            }
        }
        return best;
    }

    /// <summary>Vertices within <paramref name="radius"/> of a point (goal set for a destination).</summary>
    public HashSet<int> VerticesNear(float x, float z, float radius)
    {
        var set = new HashSet<int>();
        var r = (int)MathF.Ceiling(radius / Cell);
        var ccx = (int)MathF.Floor(x / Cell);
        var ccz = (int)MathF.Floor(z / Cell);
        for (var cx = ccx - r; cx <= ccx + r; cx++)
        for (var cz = ccz - r; cz <= ccz + r; cz++)
        {
            if (!_grid.TryGetValue(Key(cx, cz), out var edges)) continue;
            foreach (var e in edges)
            foreach (var v in new[] { _n.EdgeFrom[e], _n.EdgeTo[e] })
            {
                var dx = _n.VX[v] - x; var dz = _n.VZ[v] - z;
                if (dx * dx + dz * dz <= radius * radius) set.Add(v);
            }
        }
        return set;
    }

    /// <summary>Route from a world position (snapped to the road, respecting heading) to any vertex of a goal set.</summary>
    /// <param name="penalty">Optional extra cost factor per edge (used to find alternative routes).</param>
    public RouteResult? Route(float x, float z, double? headingDeg, IReadOnlyCollection<int> goals, float goalX, float goalZ,
        IReadOnlyDictionary<int, float>? penalty = null)
    {
        if (goals.Count == 0) return null;
        var snap = Snap(x, z, headingDeg);
        if (snap is null) return null;
        var (se, seg, t, _) = snap.Value;

        // Partial first edge: from the projected point to the edge end.
        var firstPts = _n.EdgePoints(se).ToArray();
        var lead = new List<float>();
        var (ax, az) = firstPts[seg];
        var (bx, bz) = firstPts[seg + 1];
        lead.Add(ax + (bx - ax) * t); lead.Add(az + (bz - az) * t);
        float leadLen = 0;
        float lx = lead[0], lz = lead[1];
        for (var i = seg + 1; i < firstPts.Length; i++)
        {
            leadLen += MathF.Sqrt((firstPts[i].X - lx) * (firstPts[i].X - lx) + (firstPts[i].Z - lz) * (firstPts[i].Z - lz));
            lx = firstPts[i].X; lz = firstPts[i].Z;
            lead.Add(lx); lead.Add(lz);
        }
        var start = _n.EdgeTo[se];
        var uTurn = Reverse(se);

        var dist = new Dictionary<int, float> { [start] = 0 };
        var prevEdge = new Dictionary<int, int>();
        var pq = new PriorityQueue<int, float>();
        pq.Enqueue(start, H(start));
        var goalSet = goals as HashSet<int> ?? goals.ToHashSet();
        var hit = -1;
        var visited = 0;
        while (pq.TryDequeue(out var u, out var prio))
        {
            var du = dist[u];
            if (prio > du + H(u) + 0.5f) continue;
            if (goalSet.Contains(u)) { hit = u; break; }
            if (++visited > 2_000_000) break;
            for (var k = _n.AdjStart[u]; k < _n.AdjStart[u + 1]; k++)
            {
                var e = _n.AdjEdges[k];
                if (e == uTurn && u == start) continue; // no immediate U-turn
                var v = _n.EdgeTo[e];
                var nd = du + _n.EdgeLen[e] * Cost(_n.EdgeClass[e]) * (penalty is not null && penalty.TryGetValue(e, out var pf) ? pf : 1f);
                if (!dist.TryGetValue(v, out var dv) || nd < dv)
                {
                    dist[v] = nd;
                    prevEdge[v] = e;
                    pq.Enqueue(v, nd + H(v));
                }
            }
        }
        if (hit < 0) return null;

        var edges = new List<int>();
        for (var v = hit; v != start; v = _n.EdgeFrom[prevEdge[v]]) edges.Add(prevEdge[v]);
        edges.Reverse();

        var pts = new List<float>(lead);
        float length = leadLen;
        foreach (var e in edges)
        {
            length += _n.EdgeLen[e];
            var first = true;
            foreach (var (px, pz) in _n.EdgePoints(e))
            {
                if (first) { first = false; if (pts.Count >= 2 && Near(pts[^2], pts[^1], px, pz)) continue; }
                pts.Add(px); pts.Add(pz);
            }
        }
        return new RouteResult(pts.ToArray(), length, edges.Count + 1, edges.ToArray());

        float H(int v)
        {
            var dx = _n.VX[v] - goalX; var dz = _n.VZ[v] - goalZ;
            // Admissible: goals lie within the radius and the cheapest cost factor is 0.92.
            return Math.Max(0, MathF.Sqrt(dx * dx + dz * dz) - 400) * 0.92f;
        }
    }

    /// <summary>
    /// Up to <paramref name="count"/> clearly different routes to the same goal: each further route is planned
    /// with the edges of the previous ones made more expensive (penalty method).
    /// </summary>
    public List<RouteResult> Alternatives(float x, float z, double? headingDeg, IReadOnlyCollection<int> goals, float goalX, float goalZ, int count)
    {
        var result = new List<RouteResult>();
        var penalty = new Dictionary<int, float>();
        for (var i = 0; i < count * 2 && result.Count < count; i++)
        {
            var r = Route(x, z, headingDeg, goals, goalX, goalZ, penalty.Count > 0 ? penalty : null);
            if (r is null) break;
            var ids = r.EdgeIds.ToHashSet();
            // Keep it only when it differs enough from every route found so far.
            if (result.All(o => o.EdgeIds.Count(ids.Contains) < 0.8 * Math.Max(1, Math.Min(o.EdgeIds.Length, ids.Count))))
                result.Add(r);
            foreach (var e in r.EdgeIds) penalty[e] = penalty.GetValueOrDefault(e, 1f) * 1.5f;
        }
        return result;
    }

    // Slight preference for faster roads, like the in-game GPS.
    private static float Cost(byte cls)
    {
        if ((cls & RoadNetworkBuilder.HiddenFlag) != 0) return 40f;
        var c = (cls & 0x0F) switch { 0 => 0.92f, 1 => 1f, 2 => 1.12f, _ => 1f };
        return (cls & RoadNetworkBuilder.AvoidFlag) != 0 ? c * 4f : c;
    }

    private static bool Near(float ax, float az, float bx, float bz) => Math.Abs(ax - bx) < 0.5f && Math.Abs(az - bz) < 0.5f;

    // Edges are not paired explicitly; the U-turn guard only needs "same road, opposite direction".
    private int Reverse(int e)
    {
        var from = _n.EdgeFrom[e]; var to = _n.EdgeTo[e];
        for (var k = _n.AdjStart[to]; k < _n.AdjStart[to + 1]; k++)
        {
            var r = _n.AdjEdges[k];
            if (_n.EdgeTo[r] == from && _n.EdgeGeomStart[r] == _n.EdgeGeomStart[e]) return r;
        }
        return -1;
    }
}
