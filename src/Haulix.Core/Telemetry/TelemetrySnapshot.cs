namespace Haulix.Core.Telemetry;

/// <summary>One sample of ETS2 telemetry, already converted to friendly units (km/h, km, litres, kg, 0–1 wear).</summary>
public sealed class TelemetrySnapshot
{
    public DateTime CapturedUtc { get; set; }
    public ulong SdkTimestamp { get; set; }
    public bool SdkActive { get; set; }
    public bool Paused { get; set; }
    public uint PluginRevision { get; set; }
    public string Game { get; set; } = "";
    public string GameVersion { get; set; } = "";
    public uint GameTimeMinutes { get; set; }
    public int RestStopMinutes { get; set; }
    public bool Demo { get; set; }
    /// <summary>Game seconds per real second (ETS2 ≈ 19); 0 when the plugin does not report it.</summary>
    public double TimeScale { get; set; }
    /// <summary>Arrival estimate, filled in by the engine (game navigation or HAULIX's own route).</summary>
    public Services.EtaInfo? Eta { get; set; }

    // Truck – identity
    public string TruckBrandId { get; set; } = "";
    public string TruckBrand { get; set; } = "";
    public string TruckId { get; set; } = "";
    public string TruckName { get; set; } = "";
    public string LicensePlate { get; set; } = "";
    public string LicensePlateCountry { get; set; } = "";
    public string ShifterType { get; set; } = "";

    // Truck – drivetrain
    public double SpeedKmh { get; set; }
    public double CruiseControlKmh { get; set; }
    public bool CruiseControl { get; set; }
    public double SpeedLimitKmh { get; set; }
    public double EngineRpm { get; set; }
    public double EngineRpmMax { get; set; }
    public int Gear { get; set; }
    public int GearDashboard { get; set; }
    public uint ForwardGears { get; set; }
    public uint ReverseGears { get; set; }
    public uint RetarderLevel { get; set; }
    public uint RetarderSteps { get; set; }
    public bool ParkingBrake { get; set; }
    public bool EngineBrake { get; set; }
    public bool EngineOn { get; set; }
    public bool ElectricOn { get; set; }
    public double Throttle { get; set; }
    public double Brake { get; set; }
    public double Clutch { get; set; }
    public double Steering { get; set; }

    // Truck – fluids & gauges
    public double FuelLitres { get; set; }
    public double FuelCapacity { get; set; }
    public double FuelAvgConsumption { get; set; } // l/km
    public double FuelRangeKm { get; set; }
    public bool FuelWarning { get; set; }
    public double AdBlueLitres { get; set; }
    public double AdBlueCapacity { get; set; }
    public double AirPressure { get; set; }
    public double OilPressure { get; set; }
    public double OilTemperature { get; set; }
    public double WaterTemperature { get; set; }
    public double BatteryVoltage { get; set; }
    public double BrakeTemperature { get; set; }

    // Truck – state
    public double OdometerKm { get; set; }
    public double WearEngine { get; set; }
    public double WearTransmission { get; set; }
    public double WearCabin { get; set; }
    public double WearChassis { get; set; }
    public double WearWheels { get; set; }
    public double TruckDamage => Math.Max(Math.Max(Math.Max(WearEngine, WearTransmission), Math.Max(WearCabin, WearChassis)), WearWheels);

    public bool LightsLowBeam { get; set; }
    public bool LightsHighBeam { get; set; }
    public bool LightsBeacon { get; set; }
    public bool LightsHazard { get; set; }
    public bool BlinkerLeft { get; set; }
    public bool BlinkerRight { get; set; }
    public bool Wipers { get; set; }
    public bool DifferentialLock { get; set; }
    public bool LiftAxle { get; set; }

    // World
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    /// <summary>Heading in degrees, 0 = north, clockwise.</summary>
    public double HeadingDeg { get; set; }
    public double Pitch { get; set; }
    public double Roll { get; set; }

    // Navigation
    public double RouteDistanceKm { get; set; }
    public double RouteTimeSeconds { get; set; }

    // Job
    public bool OnJob { get; set; }
    public bool CargoLoaded { get; set; }
    public bool SpecialJob { get; set; }
    public string CargoId { get; set; } = "";
    public string Cargo { get; set; } = "";
    public double CargoMassKg { get; set; }
    public double CargoDamage { get; set; }
    public uint CargoUnits { get; set; }
    public string SourceCityId { get; set; } = "";
    public string SourceCity { get; set; } = "";
    public string SourceCompanyId { get; set; } = "";
    public string SourceCompany { get; set; } = "";
    public string DestinationCityId { get; set; } = "";
    public string DestinationCity { get; set; } = "";
    public string DestinationCompanyId { get; set; } = "";
    public string DestinationCompany { get; set; } = "";
    public ulong JobIncome { get; set; }
    public uint JobDeadlineGameMinutes { get; set; }
    public uint PlannedDistanceKm { get; set; }
    public string JobMarket { get; set; } = "";

    // Trailer 0
    public bool TrailerAttached { get; set; }
    public string TrailerName { get; set; } = "";
    public string TrailerBrand { get; set; } = "";
    public string TrailerBodyType { get; set; } = "";
    public string TrailerPlate { get; set; } = "";
    public double TrailerWearChassis { get; set; }
    public double TrailerWearWheels { get; set; }
    public double TrailerWearBody { get; set; }
    public double TrailerDamage => Math.Max(Math.Max(TrailerWearChassis, TrailerWearWheels), TrailerWearBody);

    // Gameplay event payloads (valid when the matching event fires)
    public GameplayPayload Gameplay { get; set; } = new();
    public GameplayFlags Flags { get; set; } = new();
}

public sealed class GameplayFlags
{
    public bool JobFinished { get; set; }
    public bool JobCancelled { get; set; }
    public bool JobDelivered { get; set; }
    public bool Fined { get; set; }
    public bool Tollgate { get; set; }
    public bool Ferry { get; set; }
    public bool Train { get; set; }
    public bool Refuel { get; set; }
    public bool RefuelPaid { get; set; }
}

public sealed class GameplayPayload
{
    public long DeliveredRevenue { get; set; }
    public int DeliveredXp { get; set; }
    public double DeliveredCargoDamage { get; set; }
    public double DeliveredDistanceKm { get; set; }
    public uint DeliveredTimeMinutes { get; set; }
    public bool AutoParked { get; set; }
    public bool AutoLoaded { get; set; }
    public uint JobStartedGameMinute { get; set; }
    public uint JobFinishedGameMinute { get; set; }
    public long CancelledPenalty { get; set; }
    public long FineAmount { get; set; }
    public string FineOffence { get; set; } = "";
    public long TollAmount { get; set; }
    public long FerryAmount { get; set; }
    public string FerrySource { get; set; } = "";
    public string FerryTarget { get; set; } = "";
    public long TrainAmount { get; set; }
    public string TrainSource { get; set; } = "";
    public string TrainTarget { get; set; } = "";
    public double RefuelLitres { get; set; }
}
