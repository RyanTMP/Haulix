using System.IO.MemoryMappedFiles;

namespace Haulix.Core.Telemetry;

public interface ITelemetrySource : IDisposable
{
    string Name { get; }

    /// <summary>Returns the current sample, or <c>null</c> when the source is unavailable.</summary>
    TelemetrySnapshot? Read();

    /// <summary>Releases any handle so the game can free its shared memory (e.g. after the game exits).</summary>
    void Release();
}

/// <summary>Reads the <c>Local\SCSTelemetry</c> memory-mapped file written by scs-telemetry.dll.</summary>
public sealed class ScsTelemetrySource : ITelemetrySource
{
    private MemoryMappedFile? _mmf;
    private MemoryMappedViewAccessor? _view;
    private readonly byte[] _buffer = new byte[ScsTelemetryParser.MapSize];

    public string Name => "scs-telemetry";

    public TelemetrySnapshot? Read()
    {
        if (_view is null && !TryOpen()) return null;
        try
        {
            _view!.ReadArray(0, _buffer, 0, _buffer.Length);
            return ScsTelemetryParser.Parse(_buffer);
        }
        catch (Exception)
        {
            Release();
            return null;
        }
    }

    private bool TryOpen()
    {
        try
        {
            _mmf = MemoryMappedFile.OpenExisting(ScsTelemetryParser.MapName, MemoryMappedFileRights.Read);
            _view = _mmf.CreateViewAccessor(0, ScsTelemetryParser.MapSize, MemoryMappedFileAccess.Read);
            return true;
        }
        catch (Exception)
        {
            Release();
            return false;
        }
    }

    public void Release()
    {
        _view?.Dispose();
        _mmf?.Dispose();
        _view = null;
        _mmf = null;
    }

    public void Dispose() => Release();
}

/// <summary>
/// Simulated drive used for the DEMO mode and UI development. Drives a loop between a few cities,
/// emitting realistic telemetry and a delivery event at each destination.
/// </summary>
public sealed class DemoTelemetrySource : ITelemetrySource
{
    private sealed record Leg(string FromId, string From, string FromCo, string ToId, string To, string ToCo,
        string CargoId, string Cargo, double MassKg, (double X, double Z)[] Path, ulong Income);

    private static readonly Leg[] Legs =
    {
        // Coordinates follow the UI's default map projection so the demo lines up with estimated city positions.
        new("hamburg", "Hamburg", "Tradeaux", "bremen", "Bremen", "Posped", "logs", "Logs", 22400,
            new[] { (-37.0, -14918.0), (-900.0, -14400.0), (-1900.0, -13600.0), (-2800.0, -13350.0), (-3700.0, -12700.0), (-4440.0, -12168.0) }, 8475),
        new("bremen", "Bremen", "Posped", "osnabruck", "Osnabrück", "Wilnet Transport", "machine_parts", "Machine Parts", 14300,
            new[] { (-4440.0, -12168.0), (-5100.0, -11100.0), (-5600.0, -9900.0), (-6500.0, -8700.0), (-7215.0, -7488.0) }, 6120),
        new("osnabruck", "Osnabrück", "Wilnet Transport", "hamburg", "Hamburg", "Tradeaux", "canned_food", "Canned Food", 18800,
            new[] { (-7215.0, -7488.0), (-5700.0, -8600.0), (-4300.0, -9900.0), (-2900.0, -11500.0), (-1300.0, -13300.0), (-37.0, -14918.0) }, 9210),
    };

    private readonly TelemetrySnapshot _s = new();
    private readonly DateTime _started = DateTime.UtcNow;
    private DateTime _last = DateTime.UtcNow;
    private int _leg;
    private double _progress; // metres along current leg
    private double _eventHold;
    private double _fuel = 612;
    private double _odometer = 128_432;
    private uint _gameMinutes = 40_000;
    private ulong _tick;

    public string Name => "demo";

    public DemoTelemetrySource()
    {
        _s.Demo = true;
        _s.SdkActive = true;
        _s.PluginRevision = ScsTelemetryParser.SupportedRevision;
        _s.Game = "ETS2";
        _s.GameVersion = "1.18";
        _s.TruckBrandId = "scania";
        _s.TruckBrand = "Scania";
        _s.TruckId = "vehicle.scania.s_2016";
        _s.TruckName = "S";
        _s.LicensePlate = "HX 2026";
        _s.LicensePlateCountry = "Germany";
        _s.ShifterType = "arcade";
        _s.ForwardGears = 12;
        _s.ReverseGears = 4;
        _s.RetarderSteps = 5;
        _s.EngineRpmMax = 2500;
        _s.FuelCapacity = 800;
        _s.AdBlueCapacity = 80;
        _s.AdBlueLitres = 61;
        _s.TrailerAttached = true;
        _s.TrailerName = "Timber";
        _s.TrailerBrand = "Krone";
        _s.TrailerBodyType = "Log";
        _s.TrailerPlate = "HX 5541";
        _s.EngineOn = _s.ElectricOn = true;
    }

    public TelemetrySnapshot? Read()
    {
        var now = DateTime.UtcNow;
        var dt = Math.Clamp((now - _last).TotalSeconds, 0, 0.5);
        _last = now;
        _tick++;
        var leg = Legs[_leg];
        var legLength = PathLength(leg.Path);

        _s.Flags = new GameplayFlags();
        if (_eventHold > 0)
        {
            _eventHold -= dt;
            _s.SpeedKmh = 0;
            _s.EngineRpm = 650;
            _s.Gear = 0;
            _s.ParkingBrake = true;
            if (_eventHold <= 0)
            {
                _leg = (_leg + 1) % Legs.Length;
                _progress = 0;
                _s.ParkingBrake = false;
            }
        }
        else
        {
            var t = (now - _started).TotalSeconds;
            var remaining = legLength - _progress;
            var cruise = 86 + 6 * Math.Sin(t / 23.0);
            var target = remaining < 900 ? Math.Max(12, remaining / 900 * cruise) : _progress < 600 ? 30 + _progress / 600 * cruise : cruise;
            _s.SpeedKmh += (target - _s.SpeedKmh) * Math.Min(1, dt * 0.8);
            // Demo time runs 6x so a leg takes about a minute. World metres × 19 ≈ in-game km.
            var metres = _s.SpeedKmh / 3.6 * dt * 6;
            _progress += metres;
            _odometer += metres / 1000 * 19;
            _fuel = Math.Max(40, _fuel - metres / 1000 * 19 * 0.31);
            _gameMinutes += (uint)Math.Max(0, Math.Round(dt * 40 / 60 * 19));
            _s.Gear = Math.Clamp((int)(_s.SpeedKmh / 7.5) + 1, 1, 12);
            _s.EngineRpm = 900 + (_s.SpeedKmh % 7.5) / 7.5 * 700 + Math.Sin(t) * 20;
            _s.Throttle = Math.Clamp((target - _s.SpeedKmh) / 10 + 0.4, 0, 1);
            _s.CruiseControl = _s.SpeedKmh > 70;
            _s.CruiseControlKmh = _s.CruiseControl ? 88 : 0;
            _s.ParkingBrake = false;
            _s.EngineBrake = remaining < 900;

            if (_progress >= legLength)
            {
                _s.Flags = new GameplayFlags { JobDelivered = true, JobFinished = true };
                _s.Gameplay = new GameplayPayload
                {
                    DeliveredRevenue = (long)leg.Income,
                    DeliveredXp = (int)(legLength / 1000 * 1.6 + 120),
                    DeliveredDistanceKm = legLength / 1000 * 19,
                    DeliveredCargoDamage = 0.004,
                    DeliveredTimeMinutes = 240,
                    JobStartedGameMinute = _gameMinutes - 240,
                    JobFinishedGameMinute = _gameMinutes,
                };
                _eventHold = 6;
                _progress = legLength;
            }
        }

        var (x, z, heading) = PointAt(leg.Path, Math.Min(_progress, legLength));
        _s.X = x;
        _s.Z = z;
        _s.Y = 12;
        _s.HeadingDeg = heading;
        _s.CapturedUtc = now;
        _s.SdkTimestamp = _tick;
        _s.GameTimeMinutes = _gameMinutes;
        _s.OdometerKm = _odometer;
        _s.FuelLitres = _fuel;
        _s.FuelAvgConsumption = 0.31;
        _s.FuelRangeKm = _fuel / 0.31;
        _s.AirPressure = 125 + Math.Sin(_tick / 40.0) * 3;
        _s.OilPressure = 62;
        _s.OilTemperature = 94;
        _s.WaterTemperature = 88;
        _s.BatteryVoltage = 27.6;
        _s.WearEngine = 0.012;
        _s.WearCabin = 0.008;
        _s.WearChassis = 0.01;
        _s.WearTransmission = 0.006;
        _s.WearWheels = 0.018;
        _s.TrailerWearChassis = 0.004;
        _s.SpeedLimitKmh = 90;
        _s.OnJob = _eventHold <= 0;
        _s.CargoLoaded = _s.OnJob;
        _s.CargoId = leg.CargoId;
        _s.Cargo = leg.Cargo;
        _s.CargoMassKg = leg.MassKg;
        _s.SourceCityId = leg.FromId;
        _s.SourceCity = leg.From;
        _s.SourceCompany = leg.FromCo;
        _s.DestinationCityId = leg.ToId;
        _s.DestinationCity = leg.To;
        _s.DestinationCompany = leg.ToCo;
        _s.JobIncome = leg.Income;
        _s.PlannedDistanceKm = (uint)(legLength / 1000 * 19);
        _s.RouteDistanceKm = Math.Max(0, legLength - _progress) / 1000 * 19;
        _s.RouteTimeSeconds = _s.RouteDistanceKm / 80 * 3600;
        _s.JobDeadlineGameMinutes = _gameMinutes + 600;
        _s.JobMarket = "cargo_market";
        _s.CargoDamage = 0.002;
        _s.RestStopMinutes = 420;
        return _s;
    }

    public void Release() { }

    public void Dispose() { }

    private static double PathLength((double X, double Z)[] p)
    {
        double sum = 0;
        for (var i = 1; i < p.Length; i++) sum += Dist(p[i - 1], p[i]);
        return sum;
    }

    private static double Dist((double X, double Z) a, (double X, double Z) b) => Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Z - a.Z, 2));

    private static (double X, double Z, double Heading) PointAt((double X, double Z)[] p, double d)
    {
        for (var i = 1; i < p.Length; i++)
        {
            var seg = Dist(p[i - 1], p[i]);
            if (d <= seg || i == p.Length - 1)
            {
                var f = seg == 0 ? 0 : Math.Min(1, d / seg);
                var dx = p[i].X - p[i - 1].X;
                var dz = p[i].Z - p[i - 1].Z;
                // Heading: 0 = north (-Z), clockwise.
                var heading = (Math.Atan2(dx, -dz) * 180 / Math.PI + 360) % 360;
                return (p[i - 1].X + dx * f, p[i - 1].Z + dz * f, heading);
            }
            d -= seg;
        }
        return (p[^1].X, p[^1].Z, 0);
    }
}
