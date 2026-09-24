using System.Diagnostics;
using Haulix.Core.Ets2;
using Haulix.Core.Map;
using Xunit.Abstractions;

namespace Haulix.Core.Tests;

/// <summary>
/// Builds the real road network from the local ETS2 installation (read-only, ~1 minute).
/// Opt-in: set HAULIX_GAME_TESTS=1.
/// </summary>
public class RoadNetworkTests(ITestOutputHelper output)
{
    [Fact]
    public void BuildsNetworkAndRoutesFrankfurtToPrague()
    {
        if (Environment.GetEnvironmentVariable("HAULIX_GAME_TESTS") != "1") return;
        var game = Ets2Locator.FindGamePath();
        if (game is null) return;

        var dir = Directory.CreateTempSubdirectory("haulix-net").FullName;
        try
        {
            var sw = Stopwatch.StartNew();
            var key = RoadNetworkBuilder.ComputeCacheKey(game);
            var net = new RoadNetworkBuilder(game, (p, m) => { if (p is 0 or 1 || (int)(p * 100) % 20 == 0) output.WriteLine($"{p:P0} {m}"); })
                .Build(key, Path.Combine(dir, "streets.bin"), Path.Combine(dir, "pois.json"), CancellationToken.None);
            output.WriteLine($"built in {sw.Elapsed.TotalSeconds:0}s: {net.VertexCount:N0} vertices, {net.EdgeCount:N0} edges, {net.Geom.Length / 2:N0} geometry points, " +
                             $"{net.Pois.Count:N0} POIs, streets.bin {new FileInfo(Path.Combine(dir, "streets.bin")).Length / 1048576.0:0.0} MB, peak {Process.GetCurrentProcess().PeakWorkingSet64 / 1048576} MB");
            foreach (var g in net.Pois.GroupBy(p => p.Kind)) output.WriteLine($"  {g.Key}: {g.Count()}");

            net.Save(Path.Combine(dir, "net.bin"));
            var loaded = RoadNetwork.TryLoad(Path.Combine(dir, "net.bin"), key);
            Assert.NotNull(loaded);
            Assert.Equal(net.EdgeCount, loaded!.EdgeCount);

            var odense = net.Pois.First(p => p.Kind == "city" && p.Id == "odense");
            var odenseCompanies = net.Pois.Where(p => p.Kind == "company" && p.City == "odense").ToList();
            var cx = odenseCompanies.Average(p => p.X); var cz = odenseCompanies.Average(p => p.Z);
            var off = Math.Sqrt(Math.Pow(odense.X - cx, 2) + Math.Pow(odense.Z - cz, 2));
            output.WriteLine($"odense city centre ({odense.X:0},{odense.Z:0}) vs company centroid ({cx:0},{cz:0}): {off:0} m");
            Assert.True(off < 1500);

            var router = new Router(loaded);
            var dest = net.Pois.First(p => p.Kind == "company" && p.Id == "tradeaux" && p.City == "prague");
            sw.Restart();
            var route = router.Route(-5355, 2818, 300, router.VerticesNear(dest.X, dest.Z, 300), dest.X, dest.Z)
                        ?? router.Route(-5355, 2818, null, router.VerticesNear(dest.X, dest.Z, 300), dest.X, dest.Z);
            Assert.NotNull(route);
            var km = route!.LengthMeters * 19 / 1000;
            output.WriteLine($"Frankfurt → Tradeaux Praha: {route.LengthMeters:0} m world ≈ {km:0} km in-game, {route.Points.Length / 2} points, {sw.ElapsedMilliseconds} ms");
            Assert.InRange(km, 380, 620);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
