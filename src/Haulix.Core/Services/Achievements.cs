using System.Globalization;
using Haulix.Core.Data;
using Haulix.Core.Profiles;

namespace Haulix.Core.Services;

/// <summary>
/// One level of an achievement with its progress. Title/description are in the UI language. Levels of the same
/// <see cref="Family"/> (bronze → silver → gold → platinum) are shown together as one card in the UI.
/// </summary>
public sealed record AchievementInfo(string Id, string Icon, string Tier, string Title, string Description,
    double Progress, double Target, bool Unlocked, string? UnlockedUtc,
    string Family = "", string Category = "", int Level = 1, int Levels = 1, int Points = 0);

/// <summary>
/// Local achievements computed from the logbook and the save (no server). Newly reached ones are stored
/// with their date and reported once so HAULIX can announce them.
/// </summary>
public sealed class Achievements(Database db, Func<ProfileData?> profile, Func<bool> german)
{
    private sealed record Lvl(string Id, string Tier, double Target, string En, string De, string? EnDesc = null, string? DeDesc = null);

    private sealed record Family(string Id, string Category, string Icon, string EnDesc, string DeDesc, Func<Stats, double> Value, Lvl[] Levels);

    private sealed record Stats
    {
        public long Deliveries, SaveDeliveries, Perfect, NoDamage, Night, OnTime, NoSpeeding, NoFines, Eco, Special, BestDay, CargoTypes, Companies;
        public double DeliveredKm, MaxMassKg, LongestKm, DriveHours, MaxIncome, TotalIncome;
        public long Money;
        public int Trucks, Garages, Drivers, VisitedCities, Countries;
    }

    public static int PointsFor(string tier) => tier switch { "silver" => 25, "gold" => 50, "platinum" => 100, _ => 10 };

    // Ids of levels that existed before 0.0.7 are kept, so their unlock dates survive.
    private static readonly Family[] Families =
    [
        // ---- Career
        new("deliveries", "career", "package", "Deliver {0} jobs.", "Liefere {0} Aufträge ab.", s => s.Deliveries + s.SaveDeliveries,
        [
            new("first_delivery", "bronze", 1, "First delivery", "Erste Lieferung", "Deliver your first job.", "Liefere deinen ersten Auftrag ab."),
            new("deliveries_10", "bronze", 10, "On the road", "Auf Achse"),
            new("deliveries_50", "silver", 50, "Regular", "Stammfahrer"),
            new("deliveries_250", "gold", 250, "Road veteran", "Straßenveteran"),
            new("deliveries_1000", "platinum", 1000, "Legend of the road", "Legende der Landstraße"),
        ]),
        new("hours", "career", "timer", "Drive {0} hours with HAULIX running.", "Fahre {0} Stunden mit HAULIX.", s => s.DriveHours,
        [
            new("hours_10", "bronze", 10, "Warmed up", "Eingefahren"),
            new("hours_100", "silver", 100, "Long shifts", "Lange Schichten"),
            new("hours_500", "gold", 500, "Life on the road", "Leben auf der Straße"),
        ]),
        new("busy", "career", "zap", "Deliver {0} jobs on one day.", "Liefere {0} Aufträge an einem Tag ab.", s => s.BestDay,
        [
            new("busy_5", "bronze", 5, "Busy day", "Voller Tag"),
            new("busy_10", "silver", 10, "Non-stop", "Nonstop"),
        ]),
        new("night", "career", "moon", "Finish {0} deliveries between 22:00 and 05:00 (game time).", "Beende {0} Lieferungen zwischen 22 und 5 Uhr (Spielzeit).", s => s.Night,
        [
            new("night_10", "bronze", 10, "Night owl", "Nachteule"),
            new("night_50", "silver", 50, "Night shift", "Nachtschicht"),
        ]),

        // ---- Distance
        new("distance", "distance", "route", "Drive {0} km in total.", "Fahre insgesamt {0} km.", s => s.DeliveredKm,
        [
            new("km_10000", "bronze", 10_000, "10,000 km", "10.000 km"),
            new("km_100000", "silver", 100_000, "100,000 km", "100.000 km"),
            new("km_500000", "gold", 500_000, "Half a million", "Halbe Million"),
            new("km_1000000", "platinum", 1_000_000, "Million-kilometre club", "Millionenklub"),
        ]),
        new("longhaul", "distance", "navigation", "One delivery of {0} km or more.", "Eine Lieferung ab {0} km.", s => s.LongestKm,
        [
            new("longhaul_1000", "bronze", 1_000, "Cross-country", "Überland"),
            new("longhaul_1500", "silver", 1_500, "Long haul", "Langstrecke"),
            new("longhaul_2500", "gold", 2_500, "Continental", "Kontinental"),
        ]),

        // ---- Driving
        new("perfect", "driving", "award", "{0} deliveries with a driving score of 95 or more.", "{0} Lieferungen mit einem Fahrscore ab 95.", s => s.Perfect,
        [
            new("perfect_1", "bronze", 1, "Textbook", "Wie im Lehrbuch", "One delivery with a driving score of 95 or more.", "Eine Lieferung mit einem Fahrscore ab 95."),
            new("perfect_10", "silver", 10, "Clean driver", "Sauberer Fahrer"),
            new("perfect_50", "gold", 50, "Model driver", "Vorbildfahrer"),
            new("perfect_200", "platinum", 200, "Flawless", "Makellos"),
        ]),
        new("nodamage", "driving", "shield-check", "{0} deliveries without cargo damage.", "{0} Lieferungen ohne Frachtschaden.", s => s.NoDamage,
        [
            new("nodamage_5", "bronze", 5, "Careful", "Vorsichtig"),
            new("nodamage_25", "silver", 25, "Handle with care", "Mit Samthandschuhen"),
            new("nodamage_100", "gold", 100, "Not a scratch", "Kein Kratzer"),
        ]),
        new("ontime", "driving", "clock", "{0} recorded deliveries on time.", "{0} aufgezeichnete Lieferungen rechtzeitig.", s => s.OnTime,
        [
            new("ontime_5", "bronze", 5, "On schedule", "Im Zeitplan"),
            new("ontime_20", "silver", 20, "Punctual", "Pünktlich"),
            new("ontime_100", "gold", 100, "Like clockwork", "Wie ein Uhrwerk"),
        ]),
        new("nospeed", "driving", "gauge", "{0} deliveries without speeding.", "{0} Lieferungen ohne zu schnell zu fahren.", s => s.NoSpeeding,
        [
            new("nospeed_10", "silver", 10, "Speed-limit hero", "Tempolimit-Held"),
            new("nospeed_50", "gold", 50, "Cruise master", "Tempomat-Meister"),
        ]),
        new("nofines", "driving", "shield", "{0} deliveries without a fine.", "{0} Lieferungen ohne Strafzettel.", s => s.NoFines,
        [
            new("nofines_25", "silver", 25, "Law-abiding", "Gesetzestreu"),
            new("nofines_100", "gold", 100, "Clean record", "Weiße Weste"),
        ]),
        new("eco", "driving", "droplet", "{0} deliveries of 300 km+ using under 28 l/100 km.", "{0} Lieferungen ab 300 km mit unter 28 l/100 km.", s => s.Eco,
        [
            new("eco_10", "silver", 10, "Eco driver", "Spritsparer"),
            new("eco_50", "gold", 50, "Fuel whisperer", "Sprit-Flüsterer"),
        ]),

        // ---- Cargo
        new("heavy", "cargo", "weight", "Deliver a load of {0} t or more.", "Liefere eine Ladung ab {0} t.", s => s.MaxMassKg / 1000,
        [
            new("heavy_25t", "bronze", 25, "Loaded up", "Voll beladen"),
            new("heavy_40t", "silver", 40, "Heavy hauler", "Schwerlast"),
            new("heavy_60t", "gold", 60, "Heavyweight", "Schwergewicht"),
        ]),
        new("cargotypes", "cargo", "layers", "Haul {0} different cargo types.", "Transportiere {0} verschiedene Frachtarten.", s => s.CargoTypes,
        [
            new("cargo_10", "bronze", 10, "Versatile", "Vielseitig"),
            new("cargo_50", "silver", 50, "Specialist", "Spezialist"),
            new("cargo_100", "gold", 100, "Seen it all", "Alles schon gesehen"),
        ]),
        new("special", "cargo", "star", "Complete {0} special transports.", "Schließe {0} Spezialtransporte ab.", s => s.Special,
        [
            new("special_1", "silver", 1, "Special transport", "Spezialtransport", "Complete a special transport.", "Schließe einen Spezialtransport ab."),
            new("special_10", "gold", 10, "Escort needed", "Mit Begleitfahrzeug"),
        ]),

        // ---- Explorer
        new("cities", "explorer", "map-pin", "Visit {0} cities.", "Besuche {0} Städte.", s => s.VisitedCities,
        [
            new("cities_25", "bronze", 25, "Sightseer", "Entdecker"),
            new("cities_100", "silver", 100, "City collector", "Städtesammler"),
            new("cities_250", "gold", 250, "Cartographer", "Kartograf"),
        ]),
        new("countries", "explorer", "globe", "Visit cities in {0} countries.", "Besuche Städte in {0} Ländern.", s => s.Countries,
        [
            new("countries_5", "bronze", 5, "Border crosser", "Grenzgänger"),
            new("countries_15", "gold", 15, "Across Europe", "Quer durch Europa"),
            new("countries_25", "platinum", 25, "Every corner", "Jeder Winkel"),
        ]),
        new("companies", "explorer", "building-2", "Deliver to {0} different companies.", "Beliefere {0} verschiedene Firmen.", s => s.Companies,
        [
            new("companies_25", "bronze", 25, "Well connected", "Gut vernetzt"),
            new("companies_100", "silver", 100, "Known everywhere", "Überall bekannt"),
        ]),

        // ---- Business
        new("money", "business", "badge-euro", "Have €{0} in the bank.", "Habe {0} € auf dem Konto.", s => s.Money,
        [
            new("money_100k", "bronze", 100_000, "Savings", "Erspartes"),
            new("millionaire", "gold", 1_000_000, "Millionaire", "Millionär"),
            new("money_10m", "platinum", 10_000_000, "Tycoon", "Magnat"),
        ]),
        new("bigjob", "business", "coins", "Earn €{0} or more with one delivery.", "Verdiene mit einer Lieferung {0} € oder mehr.", s => s.MaxIncome,
        [
            new("income_20k", "bronze", 20_000, "Good money", "Gutes Geld"),
            new("income_50k", "silver", 50_000, "Jackpot", "Volltreffer"),
            new("income_100k", "gold", 100_000, "Payday", "Zahltag"),
        ]),
        new("earned", "business", "wallet", "Earn €{0} with deliveries in total.", "Verdiene insgesamt {0} € mit Lieferungen.", s => s.TotalIncome,
        [
            new("earned_100k", "bronze", 100_000, "In business", "Im Geschäft"),
            new("earned_1m", "silver", 1_000_000, "Big earner", "Großverdiener"),
            new("earned_10m", "gold", 10_000_000, "Empire", "Imperium"),
        ]),
        new("fleet", "business", "truck", "Own {0} trucks.", "Besitze {0} Lkw.", s => s.Trucks,
        [
            new("fleet_1", "bronze", 1, "Owner-operator", "Selbstfahrer", "Own your first truck.", "Besitze deinen ersten Lkw."),
            new("fleet_10", "silver", 10, "Fleet owner", "Flottenbesitzer"),
            new("fleet_50", "gold", 50, "Logistics group", "Logistikkonzern"),
        ]),
        new("garages", "business", "warehouse", "Own {0} garages.", "Besitze {0} Garagen.", s => s.Garages,
        [
            new("garages_5", "silver", 5, "Expanding", "Expansion"),
            new("garages_20", "gold", 20, "Network", "Netzwerk"),
        ]),
        new("drivers", "business", "users", "Employ {0} drivers.", "Beschäftige {0} Fahrer.", s => s.Drivers,
        [
            new("drivers_5", "bronze", 5, "Employer", "Arbeitgeber"),
            new("drivers_25", "silver", 25, "Team lead", "Teamchef"),
            new("drivers_100", "gold", 100, "Big company", "Großunternehmen"),
        ]),
    ];

    private readonly object _gate = new();

    /// <summary>All achievement levels with progress; <paramref name="newlyUnlocked"/> receives the ones reached just now.</summary>
    public List<AchievementInfo> Evaluate(out List<AchievementInfo> newlyUnlocked)
    {
        lock (_gate)
        {
            var stats = Collect();
            using var c = db.Open();
            var unlocked = Database.Rows(c, "SELECT id, unlocked_utc FROM achievements").ToDictionary(r => (string)r["id"]!, r => (string?)r["unlocked_utc"]);
            var de = german();
            var culture = de ? CultureInfo.GetCultureInfo("de-DE") : CultureInfo.GetCultureInfo("en-GB");
            var list = new List<AchievementInfo>();
            newlyUnlocked = new List<AchievementInfo>();
            foreach (var fam in Families)
            {
                var value = fam.Value(stats);
                for (var i = 0; i < fam.Levels.Length; i++)
                {
                    var l = fam.Levels[i];
                    var reached = value >= l.Target;
                    unlocked.TryGetValue(l.Id, out var when);
                    var info = new AchievementInfo(l.Id, fam.Icon, l.Tier, de ? l.De : l.En,
                        (de ? l.DeDesc : l.EnDesc) ?? string.Format(culture, de ? fam.DeDesc : fam.EnDesc, l.Target.ToString("N0", culture)),
                        Math.Min(value, l.Target), l.Target, when is not null || reached, when, fam.Id, fam.Category, i + 1, fam.Levels.Length, PointsFor(l.Tier));
                    if (reached && when is null)
                    {
                        when = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                        Database.Exec(c, "INSERT OR IGNORE INTO achievements(id, unlocked_utc) VALUES($i, $u)", ("$i", l.Id), ("$u", when));
                        info = info with { UnlockedUtc = when };
                        newlyUnlocked.Add(info);
                    }
                    list.Add(info);
                }
            }
            return list;
        }
    }

    private Stats Collect()
    {
        using var c = db.Open();
        var r = Database.Rows(c, """
            SELECT
              SUM(status = 'delivered' AND source = 'telemetry') AS deliveries,
              SUM(status = 'delivered' AND source = 'save') AS saveDeliveries,
              COALESCE(SUM(CASE WHEN status = 'delivered' THEN distance_km END), 0) AS km,
              SUM(status = 'delivered' AND score >= 95) AS perfect,
              SUM(status = 'delivered' AND cargo_damage IS NOT NULL AND cargo_damage < 0.005) AS nodamage,
              SUM(status = 'delivered' AND game_end_min IS NOT NULL AND ((game_end_min % 1440) >= 1320 OR (game_end_min % 1440) < 300)) AS night,
              COALESCE(MAX(CASE WHEN status = 'delivered' THEN cargo_mass_kg END), 0) AS maxMass,
              COALESCE(MAX(CASE WHEN status = 'delivered' THEN distance_km END), 0) AS longest,
              SUM(status = 'delivered' AND score_detail IS NOT NULL AND score_detail LIKE '%"late":false%') AS ontime,
              SUM(status = 'delivered' AND score_detail IS NOT NULL AND score_detail LIKE '%"speedingPct":0,%') AS nospeed,
              SUM(status = 'delivered' AND score_detail IS NOT NULL AND score_detail LIKE '%"fines":0,%') AS nofines,
              SUM(status = 'delivered' AND distance_km >= 300 AND fuel_used_l > 0 AND fuel_used_l * 100.0 / distance_km < 28) AS eco,
              SUM(status = 'delivered' AND special = 1) AS special,
              COALESCE(SUM(CASE WHEN status = 'delivered' THEN drive_seconds END), 0) / 3600.0 AS hours,
              COALESCE(MAX(CASE WHEN status = 'delivered' THEN income END), 0) AS maxIncome,
              COALESCE(SUM(CASE WHEN status = 'delivered' THEN income END), 0) AS totalIncome,
              COUNT(DISTINCT CASE WHEN status = 'delivered' THEN dest_company || '|' || dest_city END) AS companies
            FROM deliveries WHERE demo = 0
            """)[0];
        var bestDay = Database.Scalar<long>(c, """
            SELECT MAX(n) FROM (SELECT COUNT(*) AS n FROM deliveries
              WHERE demo = 0 AND status = 'delivered' AND source = 'telemetry' AND finished_utc IS NOT NULL
              GROUP BY substr(finished_utc, 1, 10))
            """);
        var cargoIds = Database.Rows(c, "SELECT DISTINCT cargo_id FROM deliveries WHERE demo = 0 AND status = 'delivered' AND cargo_id IS NOT NULL AND cargo_id <> ''")
            .Select(x => (string)x["cargo_id"]!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var p = profile();
        if (p is not null) cargoIds.UnionWith(p.TransportedCargoTypes);
        var countries = p?.VisitedCities.Select(id => CityCatalog.Get(id).Country).Where(x => !string.IsNullOrEmpty(x)).Distinct().Count() ?? 0;
        double D(string k) => r[k] is null ? 0 : Convert.ToDouble(r[k], CultureInfo.InvariantCulture);
        return new Stats
        {
            Deliveries = (long)D("deliveries"), SaveDeliveries = (long)D("saveDeliveries"),
            DeliveredKm = Math.Max(D("km"), p?.TotalDistanceKm ?? 0),
            Perfect = (long)D("perfect"), NoDamage = (long)D("nodamage"), Night = (long)D("night"), OnTime = (long)D("ontime"),
            NoSpeeding = (long)D("nospeed"), NoFines = (long)D("nofines"), Eco = (long)D("eco"), Special = (long)D("special"),
            MaxMassKg = D("maxMass"), LongestKm = D("longest"), DriveHours = D("hours"), MaxIncome = D("maxIncome"), TotalIncome = D("totalIncome"),
            Companies = (long)D("companies"), BestDay = bestDay, CargoTypes = cargoIds.Count,
            Money = p?.Money ?? 0, Trucks = p?.Trucks.Count ?? 0, Garages = p?.Garages.Count(g => g.Status > 0) ?? 0, Drivers = p?.Drivers.Count ?? 0,
            VisitedCities = p?.VisitedCities.Count ?? 0, Countries = countries,
        };
    }
}
