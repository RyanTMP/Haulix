using System.Globalization;
using Haulix.Core.Data;
using Haulix.Core.Profiles;

namespace Haulix.Core.Services;

/// <summary>An achievement with its progress. Title/description are in the UI language.</summary>
public sealed record AchievementInfo(string Id, string Icon, string Tier, string Title, string Description,
    double Progress, double Target, bool Unlocked, string? UnlockedUtc);

/// <summary>
/// Local achievements computed from the logbook and the save (no server). Newly reached ones are stored
/// with their date and reported once so HAULIX can announce them.
/// </summary>
public sealed class Achievements(Database db, Func<ProfileData?> profile, Func<bool> german)
{
    private sealed record Def(string Id, string Icon, string Tier, string En, string EnDesc, string De, string DeDesc, double Target, Func<Stats, double> Value);

    private sealed record Stats(long Deliveries, double DeliveredKm, long Perfect, long NoDamage, long Night, double MaxMassKg,
        double LongestKm, long OnTime, long Money, int Trucks, int VisitedCities, int Countries, long SaveDeliveries);

    private static readonly Def[] Defs =
    [
        new("first_delivery", "package", "bronze", "First delivery", "Deliver your first job.", "Erste Lieferung", "Liefere deinen ersten Auftrag ab.", 1, s => s.Deliveries + s.SaveDeliveries),
        new("deliveries_50", "package", "silver", "Regular", "Deliver 50 jobs.", "Stammfahrer", "Liefere 50 Aufträge ab.", 50, s => s.Deliveries + s.SaveDeliveries),
        new("deliveries_250", "package", "gold", "Road veteran", "Deliver 250 jobs.", "Straßenveteran", "Liefere 250 Aufträge ab.", 250, s => s.Deliveries + s.SaveDeliveries),
        new("km_10000", "route", "bronze", "10,000 km", "Drive 10,000 km in total.", "10.000 km", "Fahre insgesamt 10.000 km.", 10_000, s => s.DeliveredKm),
        new("km_100000", "route", "silver", "100,000 km", "Drive 100,000 km in total.", "100.000 km", "Fahre insgesamt 100.000 km.", 100_000, s => s.DeliveredKm),
        new("km_500000", "route", "gold", "Half a million", "Drive 500,000 km in total.", "Halbe Million", "Fahre insgesamt 500.000 km.", 500_000, s => s.DeliveredKm),
        new("perfect_10", "award", "silver", "Clean driver", "10 deliveries with a driving score of 95 or more.", "Sauberer Fahrer", "10 Lieferungen mit einem Fahrscore ab 95.", 10, s => s.Perfect),
        new("nodamage_25", "shield-check", "silver", "Handle with care", "25 deliveries without cargo damage.", "Mit Samthandschuhen", "25 Lieferungen ohne Frachtschaden.", 25, s => s.NoDamage),
        new("ontime_20", "clock", "bronze", "Punctual", "20 recorded deliveries on time.", "Pünktlich", "20 aufgezeichnete Lieferungen rechtzeitig.", 20, s => s.OnTime),
        new("night_10", "moon", "bronze", "Night owl", "Finish 10 deliveries between 22:00 and 05:00 (game time).", "Nachteule", "Beende 10 Lieferungen zwischen 22 und 5 Uhr (Spielzeit).", 10, s => s.Night),
        new("heavy_40t", "weight", "silver", "Heavy hauler", "Deliver a load of 40 t or more.", "Schwerlast", "Liefere eine Ladung ab 40 t.", 40_000, s => s.MaxMassKg),
        new("longhaul_1500", "navigation", "silver", "Long haul", "One delivery of 1,500 km or more.", "Langstrecke", "Eine Lieferung ab 1.500 km.", 1_500, s => s.LongestKm),
        new("cities_100", "map-pin", "silver", "City collector", "Visit 100 cities.", "Städtesammler", "Besuche 100 Städte.", 100, s => s.VisitedCities),
        new("countries_15", "globe", "gold", "Across Europe", "Visit cities in 15 countries.", "Quer durch Europa", "Besuche Städte in 15 Ländern.", 15, s => s.Countries),
        new("fleet_10", "truck", "silver", "Fleet owner", "Own 10 trucks.", "Flottenbesitzer", "Besitze 10 Lkw.", 10, s => s.Trucks),
        new("millionaire", "badge-euro", "gold", "Millionaire", "Have €1,000,000 in the bank.", "Millionär", "Habe 1.000.000 € auf dem Konto.", 1_000_000, s => s.Money),
    ];

    private readonly object _gate = new();

    /// <summary>All achievements with progress; <paramref name="newlyUnlocked"/> receives the ones reached just now.</summary>
    public List<AchievementInfo> Evaluate(out List<AchievementInfo> newlyUnlocked)
    {
        lock (_gate)
        {
            var stats = Collect();
            using var c = db.Open();
            var unlocked = Database.Rows(c, "SELECT id, unlocked_utc FROM achievements").ToDictionary(r => (string)r["id"]!, r => (string?)r["unlocked_utc"]);
            var de = german();
            var list = new List<AchievementInfo>();
            newlyUnlocked = new List<AchievementInfo>();
            foreach (var d in Defs)
            {
                var value = d.Value(stats);
                var reached = value >= d.Target;
                unlocked.TryGetValue(d.Id, out var when);
                if (reached && when is null)
                {
                    when = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                    Database.Exec(c, "INSERT OR IGNORE INTO achievements(id, unlocked_utc) VALUES($i, $u)", ("$i", d.Id), ("$u", when));
                    newlyUnlocked.Add(Info(d, value, true, when, de));
                }
                list.Add(Info(d, value, when is not null, when, de));
            }
            return list;
        }
    }

    private static AchievementInfo Info(Def d, double value, bool unlocked, string? when, bool de) =>
        new(d.Id, d.Icon, d.Tier, de ? d.De : d.En, de ? d.DeDesc : d.EnDesc, Math.Min(value, d.Target), d.Target, unlocked, when);

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
              SUM(status = 'delivered' AND score_detail IS NOT NULL AND score_detail LIKE '%"late":false%') AS ontime
            FROM deliveries WHERE demo = 0
            """)[0];
        var p = profile();
        var countries = p?.VisitedCities.Select(id => CityCatalog.Get(id).Country).Where(x => !string.IsNullOrEmpty(x)).Distinct().Count() ?? 0;
        double D(string k) => r[k] is null ? 0 : Convert.ToDouble(r[k], CultureInfo.InvariantCulture);
        return new Stats((long)D("deliveries"), Math.Max(D("km"), p?.TotalDistanceKm ?? 0), (long)D("perfect"), (long)D("nodamage"), (long)D("night"),
            D("maxMass"), D("longest"), (long)D("ontime"), p?.Money ?? 0, p?.Trucks.Count ?? 0, p?.VisitedCities.Count ?? 0, countries, (long)D("saveDeliveries"));
    }
}
