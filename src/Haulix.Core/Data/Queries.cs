using System.Globalization;
using System.Text;
using Haulix.Core.Profiles;

namespace Haulix.Core.Data;

public sealed class LogbookFilter
{
    public string? Search { get; set; }
    public string? From { get; set; }        // ISO date (inclusive)
    public string? To { get; set; }          // ISO date (inclusive)
    public string? Truck { get; set; }
    public string? Driver { get; set; }
    public string? Cargo { get; set; }
    public string? Country { get; set; }
    public string? City { get; set; }
    public double? MinDistance { get; set; }
    public double? MaxDistance { get; set; }
    public long? MinIncome { get; set; }
    public long? MaxIncome { get; set; }
    public string? Status { get; set; }      // delivered | cancelled
    public string? Source { get; set; }      // telemetry | save
    public string Sort { get; set; } = "recent"; // recent | longest | income | xp | efficiency | oldest
    public int Offset { get; set; }
    public int Limit { get; set; } = 50;
    public bool IncludeDemo { get; set; }
    public string? ProfileId { get; set; }
}

public static class Queries
{
    private const string DeliveryColumns = """
        id, source, status, started_utc AS startedUtc, finished_utc AS finishedUtc, game_start_min AS gameStartMin, game_end_min AS gameEndMin,
        truck, truck_brand AS truckBrand, truck_plate AS truckPlate, trailer, driver, cargo, cargo_id AS cargoId, cargo_mass_kg AS cargoMassKg,
        origin_city AS originCity, origin_city_id AS originCityId, origin_company AS originCompany, origin_country AS originCountry,
        dest_city AS destCity, dest_city_id AS destCityId, dest_company AS destCompany, dest_country AS destCountry,
        planned_km AS plannedKm, distance_km AS distanceKm, income, xp, penalty, fuel_used_l AS fuelUsedL, avg_speed_kmh AS avgSpeedKmh,
        max_speed_kmh AS maxSpeedKmh, drive_seconds AS driveSeconds, game_minutes AS gameMinutes, cargo_damage AS cargoDamage,
        truck_damage AS truckDamage, autopark, autoload, market, special, demo, score, score_detail AS scoreDetail
        """;

    public static object Logbook(Database db, LogbookFilter f)
    {
        var where = new List<string> { "1=1" };
        var args = new List<(string, object?)>();
        if (!f.IncludeDemo) where.Add("demo = 0");
        if (!string.IsNullOrEmpty(f.ProfileId)) { where.Add("(profile_id = $profile OR profile_id IS NULL)"); args.Add(("$profile", f.ProfileId)); }
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            where.Add("(cargo LIKE $q OR origin_city LIKE $q OR dest_city LIKE $q OR origin_company LIKE $q OR dest_company LIKE $q OR truck LIKE $q)");
            args.Add(("$q", $"%{f.Search.Trim()}%"));
        }
        if (!string.IsNullOrEmpty(f.From)) { where.Add("finished_utc >= $from"); args.Add(("$from", f.From)); }
        if (!string.IsNullOrEmpty(f.To)) { where.Add("finished_utc < date($to, '+1 day')"); args.Add(("$to", f.To)); }
        if (!string.IsNullOrEmpty(f.Truck)) { where.Add("truck = $truck"); args.Add(("$truck", f.Truck)); }
        if (!string.IsNullOrEmpty(f.Driver)) { where.Add("driver = $driver"); args.Add(("$driver", f.Driver)); }
        if (!string.IsNullOrEmpty(f.Cargo)) { where.Add("cargo = $cargo"); args.Add(("$cargo", f.Cargo)); }
        if (!string.IsNullOrEmpty(f.Country)) { where.Add("(origin_country = $country OR dest_country = $country)"); args.Add(("$country", f.Country)); }
        if (!string.IsNullOrEmpty(f.City)) { where.Add("(origin_city = $city OR dest_city = $city)"); args.Add(("$city", f.City)); }
        if (f.MinDistance is not null) { where.Add("distance_km >= $mind"); args.Add(("$mind", f.MinDistance)); }
        if (f.MaxDistance is not null) { where.Add("distance_km <= $maxd"); args.Add(("$maxd", f.MaxDistance)); }
        if (f.MinIncome is not null) { where.Add("income >= $mini"); args.Add(("$mini", f.MinIncome)); }
        if (f.MaxIncome is not null) { where.Add("income <= $maxi"); args.Add(("$maxi", f.MaxIncome)); }
        if (!string.IsNullOrEmpty(f.Status)) { where.Add("status = $status"); args.Add(("$status", f.Status)); }
        if (!string.IsNullOrEmpty(f.Source)) { where.Add("source = $source"); args.Add(("$source", f.Source)); }

        var order = f.Sort switch
        {
            "longest" => "distance_km DESC",
            "income" => "income DESC",
            "xp" => "xp DESC",
            "efficiency" => "CASE WHEN fuel_used_l > 0 AND distance_km > 0 THEN fuel_used_l / distance_km ELSE 999 END ASC",
            "oldest" => "COALESCE(finished_utc, '0000') ASC, game_end_min ASC",
            _ => "COALESCE(finished_utc, '0000') DESC, game_end_min DESC, id DESC",
        };
        var sqlWhere = string.Join(" AND ", where);
        args.Add(("$limit", Math.Clamp(f.Limit, 1, 500)));
        args.Add(("$offset", Math.Max(0, f.Offset)));

        using var c = db.Open();
        var rows = Database.Rows(c, $"SELECT {DeliveryColumns} FROM deliveries WHERE {sqlWhere} ORDER BY {order} LIMIT $limit OFFSET $offset", args.ToArray());
        var totals = Database.Rows(c, $"""
            SELECT COUNT(*) AS count, COALESCE(SUM(distance_km),0) AS distanceKm, COALESCE(SUM(income),0) AS income,
                   COALESCE(SUM(xp),0) AS xp, COALESCE(SUM(fuel_used_l),0) AS fuelL
            FROM deliveries WHERE {sqlWhere}
            """, args.ToArray())[0];

        var baseWhere = f.IncludeDemo ? "1=1" : "demo = 0";
        return new
        {
            rows,
            totals,
            facets = new
            {
                trucks = Distinct(c, $"SELECT DISTINCT truck FROM deliveries WHERE {baseWhere} AND truck IS NOT NULL AND truck <> '' ORDER BY truck"),
                drivers = Distinct(c, $"SELECT DISTINCT driver FROM deliveries WHERE {baseWhere} AND driver IS NOT NULL ORDER BY driver"),
                cargo = Distinct(c, $"SELECT DISTINCT cargo FROM deliveries WHERE {baseWhere} AND cargo IS NOT NULL AND cargo <> '' ORDER BY cargo"),
                cities = Distinct(c, $"SELECT origin_city FROM deliveries WHERE {baseWhere} UNION SELECT dest_city FROM deliveries WHERE {baseWhere} ORDER BY 1"),
                countries = Distinct(c, $"SELECT origin_country FROM deliveries WHERE {baseWhere} UNION SELECT dest_country FROM deliveries WHERE {baseWhere} ORDER BY 1")
                    .Select(cc => new { code = cc, name = CityCatalog.Countries.TryGetValue(cc, out var n) ? n : cc.ToUpperInvariant() }),
            },
        };
    }

    public static object? Delivery(Database db, long id)
    {
        using var c = db.Open();
        var rows = Database.Rows(c, $"SELECT {DeliveryColumns} FROM deliveries WHERE id = $id", ("$id", id));
        if (rows.Count == 0) return null;
        var routes = Database.Rows(c, "SELECT id FROM routes WHERE delivery_id = $id ORDER BY started_utc", ("$id", id)).Select(r => (long)r["id"]!).ToList();
        var points = new List<Dictionary<string, object?>>();
        foreach (var r in routes)
            points.AddRange(Database.Rows(c, "SELECT x, z, speed, t_utc AS t FROM route_points WHERE route_id = $r ORDER BY seq", ("$r", r)));
        return new { delivery = rows[0], points };
    }

    public static object Recent(Database db, int limit, bool includeDemo)
    {
        using var c = db.Open();
        return Database.Rows(c, $"SELECT {DeliveryColumns} FROM deliveries WHERE demo <= $demo ORDER BY COALESCE(finished_utc, '0000') DESC, game_end_min DESC, id DESC LIMIT $l",
            ("$demo", includeDemo ? 1 : 0), ("$l", limit));
    }

    public static object RecentEvents(Database db, int limit, bool includeDemo)
    {
        using var c = db.Open();
        return Database.Rows(c, "SELECT id, at_utc AS at, type, amount, detail FROM events WHERE demo <= $demo ORDER BY at_utc DESC LIMIT $l",
            ("$demo", includeDemo ? 1 : 0), ("$l", limit));
    }

    /// <summary>Per-day aggregates for the statistics screen.</summary>
    public static object Stats(Database db, string from, string to, bool includeDemo)
    {
        using var c = db.Open();
        var d = includeDemo ? 1 : 0;
        var args = new (string, object?)[] { ("$from", from), ("$to", to), ("$demo", d) };

        var deliveries = Database.Rows(c, """
            SELECT substr(finished_utc, 1, 10) AS day, COUNT(*) AS jobs, SUM(CASE WHEN status='delivered' THEN 1 ELSE 0 END) AS delivered,
                   COALESCE(SUM(distance_km),0) AS distanceKm, COALESCE(SUM(income),0) AS income, COALESCE(SUM(xp),0) AS xp,
                   COALESCE(SUM(penalty),0) AS penalties, COALESCE(SUM(fuel_used_l),0) AS fuelL, COALESCE(SUM(drive_seconds),0) AS driveSeconds,
                   MAX(max_speed_kmh) AS maxSpeed
            FROM deliveries
            WHERE finished_utc IS NOT NULL AND finished_utc >= $from AND finished_utc < date($to, '+1 day') AND demo <= $demo
            GROUP BY day ORDER BY day
            """, args);
        var sessions = Database.Rows(c, """
            SELECT substr(started_utc, 1, 10) AS day, COALESCE(SUM(distance_km),0) AS distanceKm, COALESCE(SUM(drive_seconds),0) AS driveSeconds,
                   COALESCE(SUM(idle_seconds),0) AS idleSeconds, COALESCE(MAX(max_speed_kmh),0) AS maxSpeed, COALESCE(SUM(fuel_used_l),0) AS fuelL,
                   COUNT(*) AS sessions
            FROM sessions
            WHERE started_utc >= $from AND started_utc < date($to, '+1 day') AND demo <= $demo
            GROUP BY day ORDER BY day
            """, args);
        var expenses = Database.Rows(c, """
            SELECT substr(at_utc, 1, 10) AS day, type, COALESCE(SUM(amount),0) AS amount, COUNT(*) AS count
            FROM events
            WHERE at_utc >= $from AND at_utc < date($to, '+1 day') AND demo <= $demo AND amount IS NOT NULL
            GROUP BY day, type ORDER BY day
            """, args);
        var byCargo = Database.Rows(c, """
            SELECT cargo, COUNT(*) AS jobs, COALESCE(SUM(income),0) AS income, COALESCE(SUM(distance_km),0) AS distanceKm
            FROM deliveries WHERE demo <= $demo AND status = 'delivered' AND cargo IS NOT NULL AND cargo <> ''
              AND (finished_utc IS NULL OR (finished_utc >= $from AND finished_utc < date($to, '+1 day')))
            GROUP BY cargo ORDER BY income DESC LIMIT 12
            """, args);
        var byTruck = Database.Rows(c, """
            SELECT truck, COUNT(*) AS jobs, COALESCE(SUM(income),0) AS income, COALESCE(SUM(distance_km),0) AS distanceKm,
                   COALESCE(SUM(fuel_used_l),0) AS fuelL
            FROM deliveries WHERE demo <= $demo AND truck IS NOT NULL AND truck <> ''
              AND (finished_utc IS NULL OR (finished_utc >= $from AND finished_utc < date($to, '+1 day')))
            GROUP BY truck ORDER BY distanceKm DESC LIMIT 12
            """, args);
        var allTime = Database.Rows(c, """
            SELECT COUNT(*) AS jobs, COALESCE(SUM(distance_km),0) AS distanceKm, COALESCE(SUM(income),0) AS income, COALESCE(SUM(xp),0) AS xp,
                   COALESCE(MAX(distance_km),0) AS longestKm, COALESCE(MAX(income),0) AS bestIncome, COALESCE(SUM(fuel_used_l),0) AS fuelL,
                   COALESCE(SUM(CASE WHEN fuel_used_l > 0 THEN distance_km END),0) AS fuelDistanceKm
            FROM deliveries WHERE demo <= $demo AND status = 'delivered'
            """, args)[0];
        var sessionTotals = Database.Rows(c, """
            SELECT COALESCE(SUM(distance_km),0) AS distanceKm, COALESCE(SUM(drive_seconds),0) AS driveSeconds, COALESCE(MAX(max_speed_kmh),0) AS maxSpeed,
                   COALESCE(SUM(fuel_used_l),0) AS fuelL, COUNT(*) AS sessions
            FROM sessions WHERE demo <= $demo
            """, args)[0];

        return new { deliveries, sessions, expenses, byCargo, byTruck, allTime, sessionTotals };
    }

    public static object Snapshots(Database db, string profileId)
    {
        using var c = db.Open();
        return Database.Rows(c, """
            SELECT taken_utc AS t, money, xp, distance_km AS distanceKm, trucks, trailers, garages, drivers, ai_revenue AS aiRevenue, ai_profit AS aiProfit
            FROM fleet_snapshots WHERE profile_id = $p ORDER BY taken_utc
            """, ("$p", profileId));
    }

    /// <summary>City positions HAULIX learned while driving (world coordinates), used to name the truck's location.</summary>
    public static List<Dictionary<string, object?>> LearnedCities(Database db)
    {
        using var c = db.Open();
        return Database.Rows(c, "SELECT id, name, country, x, z, samples FROM cities WHERE x IS NOT NULL");
    }


    public static string LogbookCsv(Database db, bool includeDemo)
    {
        using var c = db.Open();
        var rows = Database.Rows(c, $"SELECT {DeliveryColumns} FROM deliveries WHERE demo <= $d ORDER BY COALESCE(finished_utc,'0000'), game_end_min", ("$d", includeDemo ? 1 : 0));
        var sb = new StringBuilder();
        if (rows.Count == 0) return "id\n";
        sb.AppendLine(string.Join(',', rows[0].Keys));
        foreach (var r in rows)
            sb.AppendLine(string.Join(',', r.Values.Select(v => Csv(v))));
        return sb.ToString();
    }

    private static string Csv(object? v)
    {
        var s = v switch
        {
            null => "",
            double d => d.ToString(CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => v.ToString() ?? "",
        };
        return s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    private static List<string> Distinct(Microsoft.Data.Sqlite.SqliteConnection c, string sql) =>
        Database.Rows(c, sql).Select(r => r.Values.First()?.ToString()).Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).ToList();

    public static void PurgeDemo(Database db)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        Database.Exec(c, "DELETE FROM route_points WHERE route_id IN (SELECT id FROM routes WHERE demo = 1)");
        Database.Exec(c, "DELETE FROM routes WHERE demo = 1");
        Database.Exec(c, "DELETE FROM deliveries WHERE demo = 1");
        Database.Exec(c, "DELETE FROM sessions WHERE demo = 1");
        Database.Exec(c, "DELETE FROM events WHERE demo = 1");
        tx.Commit();
    }

    public static object Counts(Database db)
    {
        using var c = db.Open();
        return Database.Rows(c, """
            SELECT (SELECT COUNT(*) FROM deliveries WHERE demo = 0) AS deliveries,
                   (SELECT COUNT(*) FROM deliveries WHERE demo = 0 AND source = 'telemetry') AS recorded,
                   (SELECT COUNT(*) FROM deliveries WHERE demo = 0 AND source = 'save') AS imported,
                   (SELECT COUNT(*) FROM routes WHERE demo = 0) AS routes,
                   (SELECT COUNT(*) FROM route_points) AS routePoints,
                   (SELECT COUNT(*) FROM sessions WHERE demo = 0) AS sessions,
                   (SELECT COUNT(*) FROM events WHERE demo = 0) AS events,
                   (SELECT COUNT(*) FROM cities WHERE x IS NOT NULL) AS learnedCities,
                   (SELECT COUNT(*) FROM fleet_snapshots) AS snapshots
            """)[0];
    }
}
