namespace Haulix.Core.Profiles;

/// <summary>Everything HAULIX extracts from one ETS2 profile + save. Serialised to the UI as-is.</summary>
public sealed class ProfileData
{
    public string ProfileId { get; set; } = "";
    public string ProfileName { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string? PreferredBrand { get; set; }
    public string SaveName { get; set; } = "";
    public string SavePath { get; set; } = "";
    public DateTime SaveTimeUtc { get; set; }
    public DateTime ParsedAtUtc { get; set; }

    public long Money { get; set; }
    public long Loans { get; set; }
    public long LoanLimit { get; set; }
    public long Xp { get; set; }
    public Skills Skills { get; set; } = new();
    public string? HqCity { get; set; }
    public string? HqCityName { get; set; }
    public long GameTimeMinutes { get; set; }
    public long TotalDistanceKm { get; set; }
    public long TotalRealTimeMinutes { get; set; }
    public long CancelledJobs { get; set; }
    public long TotalFuelLitres { get; set; }
    public long TotalFuelPrice { get; set; }
    public long ServiceVisits { get; set; }
    public long GasStationVisits { get; set; }
    public string? LastVisitedCity { get; set; }
    public List<string> VisitedCities { get; set; } = new();
    public List<string> UnlockedDealers { get; set; } = new();
    public List<string> UnlockedRecruitments { get; set; } = new();
    public List<string> TransportedCargoTypes { get; set; } = new();
    public int ActiveModCount { get; set; }

    public List<Truck> Trucks { get; set; } = new();
    public List<Trailer> Trailers { get; set; } = new();
    public List<Garage> Garages { get; set; } = new();
    public List<Driver> Drivers { get; set; } = new();
    public List<SaveDelivery> Deliveries { get; set; } = new();

    public List<string> Warnings { get; set; } = new();
}

public sealed class Skills
{
    public int AdrMask { get; set; }
    public int Adr { get; set; }
    public int LongDistance { get; set; }
    public int HighValue { get; set; }
    public int Fragile { get; set; }
    public int JustInTime { get; set; }
    public int EcoDriving { get; set; }
    public int Total => Adr + LongDistance + HighValue + Fragile + JustInTime + EcoDriving;
}

public sealed class Wear
{
    public double Engine { get; set; }
    public double Transmission { get; set; }
    public double Cabin { get; set; }
    public double Chassis { get; set; }
    public double Wheels { get; set; }
    public double Body { get; set; }

    /// <summary>ETS2 shows the worst component as the truck's overall damage.</summary>
    public double Overall => new[] { Engine, Transmission, Cabin, Chassis, Wheels, Body }.Max();
}

public sealed class WorldPoint
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
}

public sealed class Truck
{
    public string Id { get; set; } = "";
    public string BrandId { get; set; } = "";
    public string Brand { get; set; } = "";
    public string ModelId { get; set; } = "";
    public string Model { get; set; } = "";
    public string Name => $"{Brand} {Model}".Trim();
    public string? Engine { get; set; }
    public int? HorsePower { get; set; }
    public string? Transmission { get; set; }
    public string? Chassis { get; set; }
    public string? Cabin { get; set; }
    public long OdometerKm { get; set; }
    public double FuelRelative { get; set; }
    public double TripFuelLitres { get; set; }
    public double TripDistanceKm { get; set; }
    public long TripTimeMinutes { get; set; }
    public Wear Wear { get; set; } = new();
    public string? LicensePlate { get; set; }
    public string? PlateCountry { get; set; }
    public long AccessoryValue { get; set; }
    public int AccessoryCount { get; set; }
    public string? GarageId { get; set; }
    public string? GarageCity { get; set; }
    public string? DriverId { get; set; }
    public string? DriverName { get; set; }
    public bool IsPlayerTruck { get; set; }
    public WorldPoint? Position { get; set; }
    public ProfitSummary Profit { get; set; } = new();
    public string Status => DriverId is null ? "idle" : IsPlayerTruck ? "active" : "assigned";
}

public sealed class Trailer
{
    public string Id { get; set; } = "";
    public string TypeId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? BodyType { get; set; }
    public string? ChainType { get; set; }
    public int? Axles { get; set; }
    public double? GrossWeightLimitKg { get; set; }
    public double? ChassisMassKg { get; set; }
    public double? VolumeM3 { get; set; }
    public double? LengthM { get; set; }
    public double CargoMassKg { get; set; }
    public double CargoDamage { get; set; }
    public long OdometerKm { get; set; }
    public Wear Wear { get; set; } = new();
    public string? LicensePlate { get; set; }
    public string? PlateCountry { get; set; }
    public long AccessoryValue { get; set; }
    public string? GarageId { get; set; }
    public string? GarageCity { get; set; }
    public string? AssignedTruckId { get; set; }
    public string? AssignedDriverId { get; set; }
    public bool IsPlayerTrailer { get; set; }
    public WorldPoint? Position { get; set; }
}

public sealed class Garage
{
    public string Id { get; set; } = "";
    public string CityId { get; set; } = "";
    public string City { get; set; } = "";
    public string? Country { get; set; }
    public int Status { get; set; }
    public string Size { get; set; } = "";
    public int Slots { get; set; }
    public int TrucksAssigned { get; set; }
    public int DriversAssigned { get; set; }
    public int TrailerSlots { get; set; }
    public int TrailersAssigned { get; set; }
    public double Productivity { get; set; }
    public bool IsHq { get; set; }
    public List<string> TruckIds { get; set; } = new();
    public List<string> DriverIds { get; set; } = new();
    public List<string> TrailerIds { get; set; } = new();
    public ProfitSummary Profit { get; set; } = new();
    public double Utilization => Slots == 0 ? 0 : Math.Min(TrucksAssigned, DriversAssigned) / (double)Slots;
}

public sealed class Driver
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsPlayer { get; set; }
    public string? GarageId { get; set; }
    public string? GarageCity { get; set; }
    public string? TruckId { get; set; }
    public string? TruckName { get; set; }
    public string? TrailerId { get; set; }
    public string? Hometown { get; set; }
    public string? CurrentCity { get; set; }
    public string Status { get; set; } = "unknown";
    public long Xp { get; set; }
    public Skills Skills { get; set; } = new();
    public string? TrainingPolicy { get; set; }
    public DriverJob? Job { get; set; }
    public ProfitSummary Profit { get; set; } = new();
    public string? Specialization { get; set; }
}

public sealed class DriverJob
{
    public string? Cargo { get; set; }
    public string? SourceCity { get; set; }
    public string? SourceCompany { get; set; }
    public string? TargetCity { get; set; }
    public string? TargetCompany { get; set; }
    public long PlannedDistanceKm { get; set; }
}

public sealed class ProfitSummary
{
    public long Revenue { get; set; }
    public long Wage { get; set; }
    public long Maintenance { get; set; }
    public long Fuel { get; set; }
    public long DistanceKm { get; set; }
    public long DistanceOnJobKm { get; set; }
    public int Jobs { get; set; }
    public long Profit => Revenue - Wage - Maintenance - Fuel;
    public List<ProfitDay> Days { get; set; } = new();
    public Dictionary<string, int> CargoCounts { get; set; } = new();
}

public sealed class ProfitDay
{
    public int Day { get; set; }
    public long Revenue { get; set; }
    public long Costs { get; set; }
    public long DistanceKm { get; set; }
}

/// <summary>An entry of the in-game delivery log (history before HAULIX was installed).</summary>
public sealed class SaveDelivery
{
    public long FinishedGameMinute { get; set; }
    public long StartedGameMinute { get; set; }
    public string? SourceCompany { get; set; }
    public string? SourceCity { get; set; }
    public string? TargetCompany { get; set; }
    public string? TargetCity { get; set; }
    public string? CargoId { get; set; }
    public string? Cargo { get; set; }
    public long Xp { get; set; }
    public long Revenue { get; set; }
    public long DistanceKm { get; set; }
    public long PlannedDistanceKm { get; set; }
    public double CargoDamage { get; set; }
    public string? TruckModelId { get; set; }
    public string? Truck { get; set; }
    public string? Market { get; set; }
    public double CargoMassKg { get; set; }
    public int Units { get; set; }
}
