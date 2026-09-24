using System.Globalization;
using System.Text;

namespace Haulix.Core.Map;

/// <summary>
/// Derives country borders and label positions from the country ids ETS2 stores on road nodes.
/// A coarse land raster (cells near roads) is coloured by the nearest country-tagged node; the
/// edges between different countries are traced into polylines and smoothed.
/// </summary>
public static class CountryMapBuilder
{
    /// <summary>Bumped whenever the tracing changes; the file is then re-traced from the stored inputs.</summary>
    public const int Version = 6; // 6: soft landmass image for the map background

    private const float Cell = 400f;           // world metres
    private const int LandDilate = 10;         // cells around road vertices counted as land (closes gaps between roads)
    private const float MaxTagDistance = 15000f;
    private const int MinComponentCells = 250; // smaller patches of a country are merged into their surroundings
    private const float SimplifyTolerance = 900f;
    private const float MinBorderLength = 6000f;
    private const int BorderNearRoad = 6;      // border lines are only drawn this close (cells) to a road: stops them running out to sea
    private const float CrossingReach = 8000f;  // a border needs a road crossing between its two countries this close

    public static void SaveInput(string path, int[] vertexCountry)
    {
        using var w = new BinaryWriter(File.Create(path));
        w.Write(vertexCountry.Length);
        foreach (var c in vertexCountry) w.Write(c);
    }

    public static int[]? LoadInput(string path, int expectedLength)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var r = new BinaryReader(File.OpenRead(path));
            var n = r.ReadInt32();
            if (n != expectedLength) return null;
            var a = new int[n];
            for (var i = 0; i < n; i++) a[i] = r.ReadInt32();
            return a;
        }
        catch (IOException) { return null; } // includes a truncated file (EndOfStreamException)
    }

    // TruckLib.ScsMap.Lookup.CountryId.Ets2 field names → ISO-ish codes used by the UI (city catalogue).
    private static readonly Dictionary<string, string> Codes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Albania"] = "al", ["Austria"] = "at", ["Belgium"] = "be", ["BosniaAndHerzegovina"] = "ba", ["Bulgaria"] = "bg",
        ["Croatia"] = "hr", ["Czechia"] = "cz", ["Denmark"] = "dk", ["Estonia"] = "ee", ["Finland"] = "fi", ["France"] = "fr",
        ["Germany"] = "de", ["Hungary"] = "hu", ["Italy"] = "it", ["Latvia"] = "lv", ["Lithuania"] = "lt", ["Luxembourg"] = "lu",
        ["Montenegro"] = "me", ["Netherlands"] = "nl", ["Norway"] = "no", ["Poland"] = "pl", ["Portugal"] = "pt",
        ["RepublicOfMacedonia"] = "mk", ["Romania"] = "ro", ["Russia"] = "ru", ["Serbia"] = "rs", ["Slovakia"] = "sk",
        ["Slovenia"] = "si", ["Spain"] = "es", ["Sweden"] = "se", ["Switzerland"] = "ch", ["Turkey"] = "tr",
        ["UnitedKingdom"] = "uk", ["Andorra"] = "ad", ["Greece"] = "gr", ["RepublicOfIreland"] = "ie", ["Kosovo"] = "xk",
        ["Liechtenstein"] = "li", ["Monaco"] = "mc", ["SanMarino"] = "sm", ["Belarus"] = "by", ["Ukraine"] = "ua", ["Moldova"] = "md",
    };

    private static readonly Lazy<Dictionary<int, string>> IdToCode = new(() =>
    {
        var map = new Dictionary<int, string>();
        foreach (var f in typeof(TruckLib.ScsMap.Lookup.CountryId.Ets2).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
        {
            if (f.GetValue(null) is not { } v) continue;
            var id = Convert.ToInt32(v, CultureInfo.InvariantCulture);
            map[id] = Codes.TryGetValue(f.Name, out var code) ? code : f.Name.ToLowerInvariant();
        }
        return map;
    });

    public static string CodeOf(int countryId) =>
        IdToCode.Value.TryGetValue(countryId, out var c) ? c : countryId == 204 ? "xk" : $"c{countryId}";

    public static void Write(string path, float[] landX, float[] landZ, int[] vertexCountry, int[] edgeFrom, int[] edgeTo)
    {
        var tagged = new List<(float X, float Z, int Country)>();
        for (var i = 0; i < landX.Length; i++)
            if (vertexCountry[i] != 0) tagged.Add((landX[i], landZ[i], vertexCountry[i]));
        if (tagged.Count == 0 || landX.Length == 0)
        {
            File.WriteAllText(path, "{\"countries\":[],\"borders\":[]}");
            return;
        }

        float minX = landX.Min() - Cell * 10, minZ = landZ.Min() - Cell * 10;
        float maxX = landX.Max() + Cell * 10, maxZ = landZ.Max() + Cell * 10;
        var w = (int)Math.Ceiling((maxX - minX) / Cell) + 1;
        var h = (int)Math.Ceiling((maxZ - minZ) / Cell) + 1;

        // Land mask (generous, for colouring) and a tight mask around roads (for where borders are drawn)
        var land = new bool[w * h];
        var nearRoad = new bool[w * h];
        for (var i = 0; i < landX.Length; i++)
        {
            var cx = (int)((landX[i] - minX) / Cell);
            var cz = (int)((landZ[i] - minZ) / Cell);
            for (var dz = -LandDilate; dz <= LandDilate; dz++)
            for (var dx = -LandDilate; dx <= LandDilate; dx++)
            {
                if (dx * dx + dz * dz > LandDilate * LandDilate) continue;
                var x = cx + dx; var z = cz + dz;
                if (x < 0 || z < 0 || x >= w || z >= h) continue;
                land[z * w + x] = true;
                if (dx * dx + dz * dz <= BorderNearRoad * BorderNearRoad) nearRoad[z * w + x] = true;
            }
        }

        // Spatial hash of tagged nodes
        const float bucket = 5000f;
        var buckets = new Dictionary<long, List<int>>();
        for (var i = 0; i < tagged.Count; i++)
        {
            var key = Key((int)MathF.Floor(tagged[i].X / bucket), (int)MathF.Floor(tagged[i].Z / bucket));
            if (!buckets.TryGetValue(key, out var l)) buckets[key] = l = new List<int>();
            l.Add(i);
        }

        var country = new int[w * h];
        for (var z = 0; z < h; z++)
        for (var x = 0; x < w; x++)
        {
            if (!land[z * w + x]) continue;
            var px = minX + (x + 0.5f) * Cell;
            var pz = minZ + (z + 0.5f) * Cell;
            var bx = (int)MathF.Floor(px / bucket); var bz = (int)MathF.Floor(pz / bucket);
            float best = float.MaxValue; var bestC = 0;
            for (var r = 0; r <= (int)(MaxTagDistance / bucket); r++)
            {
                for (var dz = -r; dz <= r; dz++)
                for (var dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                    if (!buckets.TryGetValue(Key(bx + dx, bz + dz), out var list)) continue;
                    foreach (var i in list)
                    {
                        var d = (tagged[i].X - px) * (tagged[i].X - px) + (tagged[i].Z - pz) * (tagged[i].Z - pz);
                        if (d < best) { best = d; bestC = tagged[i].Country; }
                    }
                }
                if (bestC != 0 && MathF.Sqrt(best) < r * bucket) break; // nothing closer can exist in outer rings
            }
            if (bestC != 0 && MathF.Sqrt(best) <= MaxTagDistance) country[z * w + x] = bestC;
        }

        var landFile = WriteLand(path, land, w, h);

        MajorityFilter(country, w, h, passes: 2, radius: 2);
        MergeSmallPatches(country, w, h);

        // Border segments on the cell grid (corner coordinates)
        var adj = new Dictionary<long, List<long>>();
        var segPair = new Dictionary<(long, long), long>();
        void AddSeg(int ax, int az, int bx2, int bz2, int c1, int c2)
        {
            var a = Key(ax, az); var b = Key(bx2, bz2);
            segPair[(a, b)] = segPair[(b, a)] = Pair(c1, c2);
            if (!adj.TryGetValue(a, out var la)) adj[a] = la = new List<long>();
            if (!adj.TryGetValue(b, out var lb)) adj[b] = lb = new List<long>();
            la.Add(b); lb.Add(a);
        }
        for (var z = 0; z < h; z++)
        for (var x = 0; x < w; x++)
        {
            var c = country[z * w + x];
            if (c == 0) continue;
            if (x + 1 < w) { var n = country[z * w + x + 1]; if (n != 0 && n != c && nearRoad[z * w + x] && nearRoad[z * w + x + 1]) AddSeg(x + 1, z, x + 1, z + 1, c, n); }
            if (z + 1 < h) { var n = country[(z + 1) * w + x]; if (n != 0 && n != c && nearRoad[z * w + x] && nearRoad[(z + 1) * w + x]) AddSeg(x, z + 1, x + 1, z + 1, c, n); }
        }

        // Road border crossings (edges joining vertices of two countries): only these make a border real.
        // Coastlines of two countries facing each other across a strait have none and are dropped.
        // The border node itself is often untagged, so look at each node together with its neighbours.
        var seenCountries = new Dictionary<int, HashSet<int>>();
        void Note(int v, int c) { if (c == 0) return; if (!seenCountries.TryGetValue(v, out var s)) seenCountries[v] = s = new(); s.Add(c); }
        for (var e = 0; e < edgeFrom.Length; e++)
        {
            int a = edgeFrom[e], b = edgeTo[e];
            Note(a, vertexCountry[a]); Note(a, vertexCountry[b]);
            Note(b, vertexCountry[b]); Note(b, vertexCountry[a]);
        }
        var crossings = new Dictionary<long, List<(float X, float Z)>>();
        foreach (var (v, set) in seenCountries)
        {
            if (set.Count < 2) continue;
            var list = set.ToArray();
            for (var i = 0; i < list.Length; i++)
            for (var j = i + 1; j < list.Length; j++)
            {
                var k = Pair(list[i], list[j]);
                if (!crossings.TryGetValue(k, out var l)) crossings[k] = l = new();
                l.Add((landX[v], landZ[v]));
            }
        }
        bool Crossed(long pair, List<(float X, float Z)> line)
        {
            if (!crossings.TryGetValue(pair, out var pts)) return false;
            const float r2 = CrossingReach * CrossingReach;
            foreach (var (px, pz) in line)
                foreach (var (qx, qz) in pts)
                    if ((px - qx) * (px - qx) + (pz - qz) * (pz - qz) < r2) return true;
            return false;
        }

        // Chain into polylines
        var used = new HashSet<(long, long)>();
        var polylines = new List<List<(float X, float Z)>>();
        foreach (var start in adj.Keys.OrderBy(k => adj[k].Count == 2 ? 1 : 0))
        {
            foreach (var next in adj[start])
            {
                if (used.Contains((start, next))) continue;
                var chain = new List<long> { start };
                var prev = start; var cur = next;
                used.Add((start, next)); used.Add((next, start));
                while (true)
                {
                    chain.Add(cur);
                    var nbs = adj[cur];
                    if (nbs.Count != 2) break;
                    var nx = nbs[0] == prev ? nbs[1] : nbs[0];
                    if (used.Contains((cur, nx))) break;
                    used.Add((cur, nx)); used.Add((nx, cur));
                    prev = cur; cur = nx;
                }
                if (chain.Count < 3) continue;
                var line = chain.Select(k => (minX + (int)(k >> 32) * Cell, minZ + (int)(k & 0xffffffff) * Cell)).ToList();
                if (Length(line) < MinBorderLength) continue; // stray fragments only add clutter
                if (!Crossed(segPair[(chain[0], chain[1])], line)) continue;
                polylines.Add(Smooth(Simplify(line, SimplifyTolerance)));
            }
        }

        // Labels: land cell nearest to each country's mean position
        var sums = new Dictionary<int, (double X, double Z, int N)>();
        for (var z = 0; z < h; z++)
        for (var x = 0; x < w; x++)
        {
            var c = country[z * w + x];
            if (c == 0) continue;
            sums.TryGetValue(c, out var s);
            sums[c] = (s.X + x, s.Z + z, s.N + 1);
        }
        var sb = new StringBuilder("{\"countries\":[");
        var first = true;
        foreach (var (c, s) in sums.Where(kv => kv.Value.N > 20))
        {
            var mx = s.X / s.N; var mz = s.Z / s.N;
            int bx = 0, bz = 0; var bd = double.MaxValue;
            for (var z = 0; z < h; z++)
            for (var x = 0; x < w; x++)
            {
                if (country[z * w + x] != c) continue;
                var d = (x - mx) * (x - mx) + (z - mz) * (z - mz);
                if (d < bd) { bd = d; bx = x; bz = z; }
            }
            if (!first) sb.Append(',');
            first = false;
            sb.Append(CultureInfo.InvariantCulture, $"{{\"id\":{c},\"code\":\"{CodeOf(c)}\",\"x\":{minX + (bx + 0.5f) * Cell:0},\"z\":{minZ + (bz + 0.5f) * Cell:0},\"cells\":{s.N}}}");
        }
        sb.Append(CultureInfo.InvariantCulture, $"],\"land\":{{\"file\":\"{landFile}\",\"x0\":{minX:0},\"z0\":{minZ:0},\"x1\":{minX + w * Cell:0},\"z1\":{minZ + h * Cell:0}}}");
        sb.Append(",\"borders\":[");
        for (var i = 0; i < polylines.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('[');
            sb.Append(string.Join(',', polylines[i].Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.X:0},{p.Z:0}"))));
            sb.Append(']');
        }
        sb.Append("]}");
        File.WriteAllText(path, sb.ToString());
    }

    private static long Key(int x, int z) => ((long)x << 32) | (uint)z;
    private static long Pair(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

    /// <summary>
    /// Soft landmass (areas around the road network) as a white/alpha PNG next to the country file; the map
    /// tints it per theme. Returns the file name.
    /// </summary>
    private static string WriteLand(string countriesPath, bool[] land, int w, int h)
    {
        var a = new float[w * h];
        for (var i = 0; i < a.Length; i++) a[i] = land[i] ? 1f : 0f;
        for (var pass = 0; pass < 3; pass++) a = BoxBlur(a, w, h, 2);
        var px = new byte[w * h];
        for (var i = 0; i < px.Length; i++) px[i] = (byte)Math.Clamp(MathF.Round(MathF.Pow(a[i], 0.8f) * 255), 0, 255);
        var name = Path.GetFileName(countriesPath).Replace("countries-", "land-").Replace(".json", ".png");
        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(countriesPath)!, name), Png.GrayAlpha(px, w, h));
        return name;
    }

    private static float[] BoxBlur(float[] src, int w, int h, int r)
    {
        var tmp = new float[src.Length];
        var dst = new float[src.Length];
        for (var z = 0; z < h; z++)
        for (var x = 0; x < w; x++)
        {
            float s = 0; var n = 0;
            for (var k = -r; k <= r; k++) { var xx = x + k; if (xx < 0 || xx >= w) continue; s += src[z * w + xx]; n++; }
            tmp[z * w + x] = s / n;
        }
        for (var z = 0; z < h; z++)
        for (var x = 0; x < w; x++)
        {
            float s = 0; var n = 0;
            for (var k = -r; k <= r; k++) { var zz = z + k; if (zz < 0 || zz >= h) continue; s += tmp[zz * w + x]; n++; }
            dst[z * w + x] = s / n;
        }
        return dst;
    }

    /// <summary>Replaces each land cell by the most common country around it (removes ragged, noisy edges).</summary>
    private static void MajorityFilter(int[] country, int w, int h, int passes, int radius)
    {
        var counts = new Dictionary<int, int>();
        for (var pass = 0; pass < passes; pass++)
        {
            var next = (int[])country.Clone();
            for (var z = 0; z < h; z++)
            for (var x = 0; x < w; x++)
            {
                var c = country[z * w + x];
                if (c == 0) continue;
                counts.Clear();
                for (var dz = -radius; dz <= radius; dz++)
                for (var dx = -radius; dx <= radius; dx++)
                {
                    int xx = x + dx, zz = z + dz;
                    if (xx < 0 || zz < 0 || xx >= w || zz >= h) continue;
                    var n = country[zz * w + xx];
                    if (n != 0) counts[n] = counts.GetValueOrDefault(n) + 1;
                }
                var best = c; var bestN = counts.GetValueOrDefault(c);
                foreach (var (k, v) in counts) if (v > bestN) { best = k; bestN = v; }
                next[z * w + x] = best;
            }
            Array.Copy(next, country, country.Length);
        }
    }

    /// <summary>Small islands of one country inside another (tagging noise) are given to the surrounding country.</summary>
    private static void MergeSmallPatches(int[] country, int w, int h)
    {
        var seen = new bool[country.Length];
        var queue = new Queue<int>();
        var cells = new List<int>();
        var around = new Dictionary<int, int>();
        for (var start = 0; start < country.Length; start++)
        {
            if (seen[start] || country[start] == 0) continue;
            var c = country[start];
            cells.Clear(); around.Clear();
            queue.Enqueue(start); seen[start] = true;
            while (queue.Count > 0)
            {
                var i = queue.Dequeue();
                cells.Add(i);
                int x = i % w, z = i / w;
                foreach (var j in new[] { x > 0 ? i - 1 : -1, x < w - 1 ? i + 1 : -1, z > 0 ? i - w : -1, z < h - 1 ? i + w : -1 })
                {
                    if (j < 0) continue;
                    var n = country[j];
                    if (n == c) { if (!seen[j]) { seen[j] = true; queue.Enqueue(j); } }
                    else if (n != 0) around[n] = around.GetValueOrDefault(n) + 1;
                }
            }
            if (cells.Count >= MinComponentCells || around.Count == 0) continue;
            var to = around.MaxBy(kv => kv.Value).Key;
            foreach (var i in cells) country[i] = to;
        }
    }

    private static float Length(List<(float X, float Z)> pts)
    {
        var len = 0f;
        for (var i = 1; i < pts.Count; i++) len += MathF.Sqrt((pts[i].X - pts[i - 1].X) * (pts[i].X - pts[i - 1].X) + (pts[i].Z - pts[i - 1].Z) * (pts[i].Z - pts[i - 1].Z));
        return len;
    }

    /// <summary>Douglas–Peucker: turns the raster staircase into a few straight runs.</summary>
    private static List<(float X, float Z)> Simplify(List<(float X, float Z)> pts, float tol)
    {
        if (pts.Count < 3) return pts;
        var keep = new bool[pts.Count];
        keep[0] = keep[^1] = true;
        var stack = new Stack<(int A, int B)>();
        stack.Push((0, pts.Count - 1));
        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop();
            var (ax, az) = pts[a]; var (bx, bz) = pts[b];
            float dx = bx - ax, dz = bz - az, len = MathF.Sqrt(dx * dx + dz * dz);
            var far = -1; var fd = tol;
            for (var i = a + 1; i < b; i++)
            {
                var d = len < 1e-3f
                    ? MathF.Sqrt((pts[i].X - ax) * (pts[i].X - ax) + (pts[i].Z - az) * (pts[i].Z - az))
                    : MathF.Abs(dx * (az - pts[i].Z) - dz * (ax - pts[i].X)) / len;
                if (d > fd) { fd = d; far = i; }
            }
            if (far < 0) continue;
            keep[far] = true;
            stack.Push((a, far)); stack.Push((far, b));
        }
        return pts.Where((_, i) => keep[i]).ToList();
    }

    /// <summary>Chaikin corner cutting (three passes), endpoints kept.</summary>
    private static List<(float X, float Z)> Smooth(List<(float X, float Z)> pts)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            if (pts.Count < 3) break;
            var o = new List<(float, float)> { pts[0] };
            for (var i = 0; i < pts.Count - 1; i++)
            {
                var (ax, az) = pts[i]; var (bx, bz) = pts[i + 1];
                o.Add((ax * 0.75f + bx * 0.25f, az * 0.75f + bz * 0.25f));
                o.Add((ax * 0.25f + bx * 0.75f, az * 0.25f + bz * 0.75f));
            }
            o.Add(pts[^1]);
            pts = o;
        }
        return pts;
    }
}
