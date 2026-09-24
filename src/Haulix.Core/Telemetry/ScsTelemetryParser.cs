using System.Buffers.Binary;
using System.Text;

namespace Haulix.Core.Telemetry;

/// <summary>
/// Decodes the shared-memory block written by the SCS telemetry plugin (RenCloud scs-sdk-plugin, revision 12).
/// Offsets follow <c>scs-telemetry-common.hpp</c>; every zone starts at a fixed offset.
/// </summary>
public static class ScsTelemetryParser
{
    public const string MapName = "Local\\SCSTelemetry";
    public const int MapSize = 32 * 1024;
    public const uint SupportedRevision = 12;
    private const int Str = 64;

    public static TelemetrySnapshot Parse(ReadOnlySpan<byte> m)
    {
        var s = new TelemetrySnapshot
        {
            CapturedUtc = DateTime.UtcNow,

            // Zone 1 @0
            SdkActive = m[0] != 0,
            Paused = m[4] != 0,
            SdkTimestamp = U64(m, 8),

            // Zone 2 @40 – unsigned ints
            PluginRevision = U32(m, 40),
            GameVersion = $"{U32(m, 44)}.{U32(m, 48)}",
            Game = U32(m, 52) switch { 1 => "ETS2", 2 => "ATS", _ => "Unknown" },
            GameTimeMinutes = U32(m, 64),
            ForwardGears = U32(m, 68),
            ReverseGears = U32(m, 72),
            RetarderSteps = U32(m, 76),
            JobDeadlineGameMinutes = U32(m, 88),
            CargoUnits = U32(m, 96),
            PlannedDistanceKm = U32(m, 100),
            RetarderLevel = U32(m, 108),

            // Zone 3 @500 – ints
            RestStopMinutes = I32(m, 500),
            Gear = I32(m, 504),
            GearDashboard = I32(m, 508),

            // Zone 4 @700 – floats
            TimeScale = F(m, 700),       // game seconds per real second (≈19 in ETS2)
            FuelCapacity = F(m, 704),
            AdBlueCapacity = F(m, 712),
            EngineRpmMax = F(m, 740),
            CargoMassKg = F(m, 748),
            SpeedKmh = F(m, 948) * 3.6,
            EngineRpm = F(m, 952),
            Steering = F(m, 972),
            Throttle = F(m, 976),
            Brake = F(m, 980),
            Clutch = F(m, 984),
            CruiseControlKmh = F(m, 988) * 3.6,
            AirPressure = F(m, 992),
            BrakeTemperature = F(m, 996),
            FuelLitres = F(m, 1000),
            FuelAvgConsumption = F(m, 1004),
            FuelRangeKm = F(m, 1008),
            AdBlueLitres = F(m, 1012),
            OilPressure = F(m, 1016),
            OilTemperature = F(m, 1020),
            WaterTemperature = F(m, 1024),
            BatteryVoltage = F(m, 1028),
            WearEngine = F(m, 1036),
            WearTransmission = F(m, 1040),
            WearCabin = F(m, 1044),
            WearChassis = F(m, 1048),
            WearWheels = F(m, 1052),
            OdometerKm = F(m, 1056),
            RouteDistanceKm = F(m, 1060) / 1000.0,
            RouteTimeSeconds = F(m, 1064),
            SpeedLimitKmh = F(m, 1068) * 3.6,
            CargoDamage = F(m, 1468),

            // Zone 5 @1500 – bools
            CargoLoaded = m[1564] != 0,
            SpecialJob = m[1565] != 0,
            ParkingBrake = m[1566] != 0,
            EngineBrake = m[1567] != 0,
            FuelWarning = m[1570] != 0,
            ElectricOn = m[1575] != 0,
            EngineOn = m[1576] != 0,
            Wipers = m[1577] != 0,
            BlinkerLeft = m[1580] != 0,
            BlinkerRight = m[1581] != 0,
            LightsLowBeam = m[1583] != 0,
            LightsHighBeam = m[1584] != 0,
            LightsBeacon = m[1585] != 0,
            LightsHazard = m[1588] != 0,
            CruiseControl = m[1589] != 0,
            DifferentialLock = m[1608] != 0,
            LiftAxle = m[1609] != 0,

            // Zone 8 @2200 – truck placement (doubles)
            X = D(m, 2200),
            Y = D(m, 2208),
            Z = D(m, 2216),
            HeadingDeg = ((1.0 - D(m, 2224)) * 360.0) % 360.0,
            Pitch = D(m, 2232),
            Roll = D(m, 2240),

            // Zone 9 @2300 – strings
            TruckBrandId = S(m, 2300, Str),
            TruckBrand = S(m, 2364, Str),
            TruckId = S(m, 2428, Str),
            TruckName = S(m, 2492, Str),
            CargoId = S(m, 2556, Str),
            Cargo = S(m, 2620, Str),
            DestinationCityId = S(m, 2684, Str),
            DestinationCity = S(m, 2748, Str),
            DestinationCompanyId = S(m, 2812, Str),
            DestinationCompany = S(m, 2876, Str),
            SourceCityId = S(m, 2940, Str),
            SourceCity = S(m, 3004, Str),
            SourceCompanyId = S(m, 3068, Str),
            SourceCompany = S(m, 3132, Str),
            ShifterType = S(m, 3196, 16),
            LicensePlate = S(m, 3212, Str),
            LicensePlateCountry = S(m, 3340, Str),
            JobMarket = S(m, 3404, 32),

            // Zone 10 @4000
            JobIncome = U64(m, 4000),

            // Zone 12 @4300 – special events
            OnJob = m[4300] != 0,
        };

        s.Flags = new GameplayFlags
        {
            JobFinished = m[4301] != 0,
            JobCancelled = m[4302] != 0,
            JobDelivered = m[4303] != 0,
            Fined = m[4304] != 0,
            Tollgate = m[4305] != 0,
            Ferry = m[4306] != 0,
            Train = m[4307] != 0,
            Refuel = m[4308] != 0,
            RefuelPaid = m[4309] != 0,
        };

        s.Gameplay = new GameplayPayload
        {
            DeliveredTimeMinutes = U32(m, 440),
            JobStartedGameMinute = U32(m, 444),
            JobFinishedGameMinute = U32(m, 448),
            DeliveredXp = I32(m, 640),
            DeliveredCargoDamage = F(m, 1456),
            DeliveredDistanceKm = F(m, 1460),
            RefuelLitres = F(m, 1464),
            AutoParked = m[1613] != 0,
            AutoLoaded = m[1614] != 0,
            FineOffence = S(m, 3436, 32),
            FerrySource = S(m, 3468, Str),
            FerryTarget = S(m, 3532, Str),
            TrainSource = S(m, 3724, Str),
            TrainTarget = S(m, 3788, Str),
            CancelledPenalty = I64(m, 4200),
            DeliveredRevenue = I64(m, 4208),
            FineAmount = I64(m, 4216),
            TollAmount = I64(m, 4224),
            FerryAmount = I64(m, 4232),
            TrainAmount = I64(m, 4240),
        };

        // Zone 14 @6000 – first trailer (1560 bytes each)
        const int t = 6000;
        s.TrailerAttached = m[t + 80] != 0;
        s.TrailerWearChassis = F(m, t + 156);
        s.TrailerWearWheels = F(m, t + 160);
        s.TrailerWearBody = F(m, t + 164);
        s.TrailerBodyType = S(m, t + 920 + 2 * Str, Str);
        s.TrailerBrand = S(m, t + 920 + 4 * Str, Str);
        s.TrailerName = S(m, t + 920 + 5 * Str, Str);
        s.TrailerPlate = S(m, t + 920 + 7 * Str, Str);

        return s;
    }

    private static uint U32(ReadOnlySpan<byte> m, int o) => BinaryPrimitives.ReadUInt32LittleEndian(m[o..]);
    private static int I32(ReadOnlySpan<byte> m, int o) => BinaryPrimitives.ReadInt32LittleEndian(m[o..]);
    private static ulong U64(ReadOnlySpan<byte> m, int o) => BinaryPrimitives.ReadUInt64LittleEndian(m[o..]);
    private static long I64(ReadOnlySpan<byte> m, int o) => BinaryPrimitives.ReadInt64LittleEndian(m[o..]);
    private static double D(ReadOnlySpan<byte> m, int o) => BinaryPrimitives.ReadDoubleLittleEndian(m[o..]);

    private static double F(ReadOnlySpan<byte> m, int o)
    {
        var f = BinaryPrimitives.ReadSingleLittleEndian(m[o..]);
        return float.IsFinite(f) ? f : 0;
    }

    private static string S(ReadOnlySpan<byte> m, int o, int len)
    {
        var slice = m.Slice(o, len);
        var end = slice.IndexOf((byte)0);
        return Encoding.UTF8.GetString(end < 0 ? slice : slice[..end]).Trim();
    }
}
