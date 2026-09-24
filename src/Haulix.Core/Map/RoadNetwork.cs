using System.IO.Compression;

namespace Haulix.Core.Map;

/// <summary>A point of interest extracted from the game map (city, company, service, garage, ferry).</summary>
public sealed record MapPoi(string Kind, string Id, string? City, float X, float Z);

/// <summary>
/// Routable ETS2 road graph extracted from the game files. Vertices are map nodes (world metres),
/// edges are directed road segments or junction (prefab) connections, each with its own polyline.
/// </summary>
public sealed class RoadNetwork
{
    public const int FormatVersion = 5; // 5: city area rectangles in the POI file

    public string CacheKey { get; set; } = "";
    public DateTime BuiltUtc { get; set; }
    public string GameVersion { get; set; } = "";

    public float[] VX { get; set; } = [];
    public float[] VZ { get; set; } = [];
    public int[] EdgeFrom { get; set; } = [];
    public int[] EdgeTo { get; set; } = [];
    public float[] EdgeLen { get; set; } = [];
    /// <summary>Start index (in points) into <see cref="Geom"/>.</summary>
    public int[] EdgeGeomStart { get; set; } = [];
    public int[] EdgeGeomCount { get; set; } = [];
    public bool[] EdgeGeomReverse { get; set; } = [];
    /// <summary>Road-class of the edge (0 motorway, 1 main, 2 local, 3 junction) — used for route cost.</summary>
    public byte[] EdgeClass { get; set; } = [];
    public float[] Geom { get; set; } = [];

    public List<MapPoi> Pois { get; set; } = new();

    public int VertexCount => VX.Length;
    public int EdgeCount => EdgeFrom.Length;

    // Built on load
    public int[] AdjStart { get; private set; } = [];
    public int[] AdjEdges { get; private set; } = [];

    public void BuildAdjacency()
    {
        var counts = new int[VertexCount + 1];
        foreach (var f in EdgeFrom) counts[f + 1]++;
        for (var i = 0; i < VertexCount; i++) counts[i + 1] += counts[i];
        var fill = (int[])counts.Clone();
        var adj = new int[EdgeCount];
        for (var e = 0; e < EdgeCount; e++) adj[fill[EdgeFrom[e]]++] = e;
        AdjStart = counts;
        AdjEdges = adj;
    }

    /// <summary>Edge polyline in traversal order.</summary>
    public IEnumerable<(float X, float Z)> EdgePoints(int e)
    {
        var s = EdgeGeomStart[e];
        var n = EdgeGeomCount[e];
        if (!EdgeGeomReverse[e])
            for (var i = 0; i < n; i++) yield return (Geom[(s + i) * 2], Geom[(s + i) * 2 + 1]);
        else
            for (var i = n - 1; i >= 0; i--) yield return (Geom[(s + i) * 2], Geom[(s + i) * 2 + 1]);
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        using (var fs = File.Create(tmp))
        using (var z = new GZipStream(fs, CompressionLevel.Fastest))
        using (var w = new BinaryWriter(z))
        {
            w.Write("HAULIXNET");
            w.Write(FormatVersion);
            w.Write(CacheKey);
            w.Write(BuiltUtc.ToBinary());
            w.Write(GameVersion);
            WriteArr(w, VX); WriteArr(w, VZ);
            WriteArr(w, EdgeFrom); WriteArr(w, EdgeTo); WriteArr(w, EdgeLen);
            WriteArr(w, EdgeGeomStart); WriteArr(w, EdgeGeomCount);
            w.Write(EdgeGeomReverse.Length); foreach (var b in EdgeGeomReverse) w.Write(b);
            w.Write(EdgeClass.Length); w.Write(EdgeClass);
            WriteArr(w, Geom);
            w.Write(Pois.Count);
            foreach (var p in Pois) { w.Write(p.Kind); w.Write(p.Id); w.Write(p.City ?? ""); w.Write(p.X); w.Write(p.Z); }
        }
        File.Move(tmp, path, true);
    }

    public static RoadNetwork? TryLoad(string path, string expectedKey)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var fs = File.OpenRead(path);
            using var z = new GZipStream(fs, CompressionMode.Decompress);
            using var r = new BinaryReader(z);
            if (r.ReadString() != "HAULIXNET" || r.ReadInt32() != FormatVersion) return null;
            var n = new RoadNetwork { CacheKey = r.ReadString() };
            if (n.CacheKey != expectedKey) return null;
            n.BuiltUtc = DateTime.FromBinary(r.ReadInt64());
            n.GameVersion = r.ReadString();
            n.VX = ReadF(r); n.VZ = ReadF(r);
            n.EdgeFrom = ReadI(r); n.EdgeTo = ReadI(r); n.EdgeLen = ReadF(r);
            n.EdgeGeomStart = ReadI(r); n.EdgeGeomCount = ReadI(r);
            var rc = r.ReadInt32(); n.EdgeGeomReverse = new bool[rc]; for (var i = 0; i < rc; i++) n.EdgeGeomReverse[i] = r.ReadBoolean();
            var cc = r.ReadInt32(); n.EdgeClass = r.ReadBytes(cc);
            n.Geom = ReadF(r);
            var pc = r.ReadInt32();
            for (var i = 0; i < pc; i++)
            {
                var kind = r.ReadString(); var id = r.ReadString(); var city = r.ReadString();
                n.Pois.Add(new MapPoi(kind, id, city.Length == 0 ? null : city, r.ReadSingle(), r.ReadSingle()));
            }
            n.BuildAdjacency();
            return n;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void WriteArr(BinaryWriter w, float[] a) { w.Write(a.Length); foreach (var v in a) w.Write(v); }
    private static void WriteArr(BinaryWriter w, int[] a) { w.Write(a.Length); foreach (var v in a) w.Write(v); }
    private static float[] ReadF(BinaryReader r) { var n = r.ReadInt32(); var a = new float[n]; for (var i = 0; i < n; i++) a[i] = r.ReadSingle(); return a; }
    private static int[] ReadI(BinaryReader r) { var n = r.ReadInt32(); var a = new int[n]; for (var i = 0; i < n; i++) a[i] = r.ReadInt32(); return a; }
}
