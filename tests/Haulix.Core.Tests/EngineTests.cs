using System.Buffers.Binary;
using System.Text;
using Haulix.Core.Data;
using Haulix.Core.Services;
using Haulix.Core.Settings;
using Haulix.Core.Telemetry;
using Microsoft.Data.Sqlite;

namespace Haulix.Core.Tests;

public sealed class TempDb : IDisposable
{
    public TempDb()
    {
        Folder = Directory.CreateTempSubdirectory("haulix-db").FullName;
        Db = new Database(Path.Combine(Folder, "haulix.db"));
        Db.Migrate();
    }

    public string Folder { get; }
    public Database Db { get; }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Folder, true); } catch (IOException) { }
    }
}

public class TelemetryParserTests
{
    [Fact]
    public void ParsesRevision12Layout()
    {
        var m = new byte[ScsTelemetryParser.MapSize];
        m[0] = 1; // sdkActive
        BinaryPrimitives.WriteUInt64LittleEndian(m.AsSpan(8), 123456);
        BinaryPrimitives.WriteUInt32LittleEndian(m.AsSpan(40), 12);
        BinaryPrimitives.WriteUInt32LittleEndian(m.AsSpan(52), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(m.AsSpan(64), 4321);
        BinaryPrimitives.WriteUInt32LittleEndian(m.AsSpan(68), 12);
        BinaryPrimitives.WriteInt32LittleEndian(m.AsSpan(504), 7);
        BinaryPrimitives.WriteSingleLittleEndian(m.AsSpan(704), 800f);
        BinaryPrimitives.WriteSingleLittleEndian(m.AsSpan(948), 25f);       // 90 km/h
        BinaryPrimitives.WriteSingleLittleEndian(m.AsSpan(952), 1350f);
        BinaryPrimitives.WriteSingleLittleEndian(m.AsSpan(1000), 412f);
        BinaryPrimitives.WriteSingleLittleEndian(m.AsSpan(1056), 128432f);
        BinaryPrimitives.WriteSingleLittleEndian(m.AsSpan(1060), 312000f);  // metres
        m[1566] = 1; // parking brake
        m[1589] = 1; // cruise control
        BinaryPrimitives.WriteDoubleLittleEndian(m.AsSpan(2200), -5400.5);
        BinaryPrimitives.WriteDoubleLittleEndian(m.AsSpan(2216), -39650.25);
        BinaryPrimitives.WriteDoubleLittleEndian(m.AsSpan(2224), 0.25);    // heading: west
        Str(m, 2364, "Scania");
        Str(m, 2492, "S");
        Str(m, 2620, "Logs");
        Str(m, 2748, "Bremen");
        Str(m, 3004, "Hamburg");
        BinaryPrimitives.WriteUInt64LittleEndian(m.AsSpan(4000), 8475);
        BinaryPrimitives.WriteInt64LittleEndian(m.AsSpan(4208), 8475);
        m[4300] = 1; // onJob
        m[4303] = 1; // jobDelivered
        m[6000 + 80] = 1; // trailer attached
        Str(m, 6000 + 920 + 5 * 64, "Timber");

        var s = ScsTelemetryParser.Parse(m);
        Assert.True(s.SdkActive);
        Assert.Equal(12u, s.PluginRevision);
        Assert.Equal("ETS2", s.Game);
        Assert.Equal(90, s.SpeedKmh, 3);
        Assert.Equal(7, s.Gear);
        Assert.Equal(412, s.FuelLitres, 3);
        Assert.Equal(312, s.RouteDistanceKm, 3);
        Assert.True(s.ParkingBrake);
        Assert.True(s.CruiseControl);
        Assert.Equal(-5400.5, s.X);
        Assert.Equal(-39650.25, s.Z);
        Assert.Equal(270, s.HeadingDeg, 3);
        Assert.Equal("Scania", s.TruckBrand);
        Assert.Equal("Logs", s.Cargo);
        Assert.Equal("Hamburg", s.SourceCity);
        Assert.Equal("Bremen", s.DestinationCity);
        Assert.Equal(8475UL, s.JobIncome);
        Assert.Equal(8475, s.Gameplay.DeliveredRevenue);
        Assert.True(s.OnJob);
        Assert.True(s.Flags.JobDelivered);
        Assert.True(s.TrailerAttached);
        Assert.Equal("Timber", s.TrailerName);
    }

    private static void Str(byte[] m, int offset, string v) => Encoding.UTF8.GetBytes(v).CopyTo(m, offset);
}

public class RecorderTests
{
    private static TelemetrySnapshot Sample(double x, double z, double odo, double fuel, bool onJob, double speed = 80) => new()
    {
        CapturedUtc = DateTime.UtcNow,
        SdkActive = true,
        X = x, Z = z, OdometerKm = odo, FuelLitres = fuel, SpeedKmh = speed, EngineOn = true,
        OnJob = onJob, Cargo = "Logs", CargoId = "logs", SourceCity = "Hamburg", SourceCityId = "hamburg",
        DestinationCity = "Bremen", DestinationCityId = "bremen", JobIncome = 8475, PlannedDistanceKm = 120,
        TruckBrand = "Scania", TruckName = "S", GameTimeMinutes = 1000,
    };

    [Fact]
    public void RecordsDeliveryRouteSessionAndLearnsCity()
    {
        using var t = new TempDb();
        var rec = new TripRecorder(t.Db, () => "profile1", () => (true, true, 100));
        long? deliveryId = null;
        rec.DeliveryRecorded += (id, _) => deliveryId = id;

        var start = Sample(0, 0, 1000, 500, onJob: true, speed: 0);
        rec.OnSample(start);
        rec.OnGameEvent(new GameEvent(GameEventType.JobStarted, start, DateTime.UtcNow));
        for (var i = 1; i <= 40; i++)
        {
            Thread.Sleep(2);
            rec.OnSample(Sample(i * 150, i * 20, 1000 + i * 0.5, 500 - i * 0.2, onJob: true));
        }
        var end = Sample(6000, 800, 1020, 492, onJob: false, speed: 0);
        end.Gameplay = new GameplayPayload { DeliveredRevenue = 8475, DeliveredXp = 612, DeliveredDistanceKm = 118, JobStartedGameMinute = 1000, JobFinishedGameMinute = 1240 };
        rec.OnGameEvent(new GameEvent(GameEventType.JobDelivered, end, DateTime.UtcNow));
        rec.OnDisconnected();

        Assert.NotNull(deliveryId);
        using var c = t.Db.Open();
        var d = Database.Rows(c, "SELECT * FROM deliveries WHERE id = $id", ("$id", deliveryId))[0];
        Assert.Equal("delivered", d["status"]);
        Assert.Equal(8475L, d["income"]);
        Assert.Equal(612L, d["xp"]);
        Assert.Equal(118.0, (double)d["distance_km"]!, 3);
        Assert.True((double)d["fuel_used_l"]! > 7.5);
        Assert.Equal("de", d["dest_country"]);

        var points = Database.Scalar<long>(c, "SELECT COUNT(*) FROM route_points rp JOIN routes r ON r.id = rp.route_id WHERE r.delivery_id = $id", ("$id", deliveryId));
        Assert.True(points > 20, $"expected breadcrumbs, got {points}");
        Assert.Equal(1, Database.Scalar<long>(c, "SELECT COUNT(*) FROM cities WHERE id = 'bremen'"));
        Assert.Equal(1, Database.Scalar<long>(c, "SELECT COUNT(*) FROM sessions"));

        // A second identical delivered event (plugin flag still latched) must not duplicate the row.
        rec.OnGameEvent(new GameEvent(GameEventType.JobDelivered, end, DateTime.UtcNow));
        Assert.Equal(1, Database.Scalar<long>(c, "SELECT COUNT(*) FROM deliveries"));
    }

    [Fact]
    public void DemoDataIsPurged()
    {
        using var t = new TempDb();
        var rec = new TripRecorder(t.Db, () => "p", () => (true, true, 100));
        var s = Sample(0, 0, 10, 100, true);
        s.Demo = true;
        rec.OnSample(s);
        rec.OnGameEvent(new GameEvent(GameEventType.JobDelivered, s, DateTime.UtcNow));
        using var c = t.Db.Open();
        Assert.Equal(1, Database.Scalar<long>(c, "SELECT COUNT(*) FROM deliveries WHERE demo = 1"));
        Queries.PurgeDemo(t.Db);
        Assert.Equal(0, Database.Scalar<long>(c, "SELECT COUNT(*) FROM deliveries"));
        Assert.Equal(0, Database.Scalar<long>(c, "SELECT COUNT(*) FROM cities"));
    }
}

public class DataTests
{
    [Fact]
    public void MigrationIsIdempotent()
    {
        using var t = new TempDb();
        t.Db.Migrate();
        using var c = t.Db.Open();
        Assert.Equal(Database.SchemaVersion, Database.Scalar<long>(c, "PRAGMA user_version"));
        Assert.Null(t.Db.Check());
    }

    [Fact]
    public void ExportClearImportRoundTrip()
    {
        using var t = new TempDb();
        var settings = new SettingsStore(t.Db);
        var s = settings.Load();
        s.General.Units = "imperial";
        settings.Save(s);
        using (var c = t.Db.Open())
        {
            Database.Exec(c, "INSERT INTO deliveries(source, dedupe_key, status, cargo, income) VALUES('telemetry', 'k1', 'delivered', 'Logs', 8475)");
        }
        var backups = new BackupService(t.Db, settings, Path.Combine(t.Folder, "backups"));
        var file = Path.Combine(t.Folder, "export.haulix");
        backups.Export(file);

        backups.Clear("all");
        using (var c = t.Db.Open())
            Assert.Equal(0, Database.Scalar<long>(c, "SELECT COUNT(*) FROM deliveries"));

        backups.Import(file);
        using (var c = t.Db.Open())
        {
            Assert.Equal(1, Database.Scalar<long>(c, "SELECT COUNT(*) FROM deliveries"));
            Assert.Equal(8475, Database.Scalar<long>(c, "SELECT income FROM deliveries"));
        }
        Assert.Equal("imperial", settings.Load().General.Units);
        Assert.Contains(backups.List(), b => b.Kind == "safety");
    }

    [Fact]
    public void LogbookFiltersAndSorts()
    {
        using var t = new TempDb();
        using (var c = t.Db.Open())
        {
            Database.Exec(c, "INSERT INTO deliveries(source, dedupe_key, status, cargo, origin_city, dest_city, distance_km, income, finished_utc) VALUES('telemetry','a','delivered','Logs','Hamburg','Bremen',120,5000,'2026-09-01T10:00:00Z')");
            Database.Exec(c, "INSERT INTO deliveries(source, dedupe_key, status, cargo, origin_city, dest_city, distance_km, income, finished_utc) VALUES('telemetry','b','delivered','Cement','Kiel','Berlin',380,11000,'2026-09-02T10:00:00Z')");
        }
        var json = System.Text.Json.JsonSerializer.Serialize(Queries.Logbook(t.Db, new LogbookFilter { Sort = "income" }), SettingsStore.Json);
        Assert.True(json.IndexOf("Cement", StringComparison.Ordinal) < json.IndexOf("\"Logs\"", StringComparison.Ordinal));
        var filtered = System.Text.Json.JsonSerializer.Serialize(Queries.Logbook(t.Db, new LogbookFilter { Search = "Kiel" }), SettingsStore.Json);
        Assert.Contains("\"count\":1", filtered);
    }
}
