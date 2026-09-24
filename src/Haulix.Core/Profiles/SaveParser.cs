using System.Globalization;
using System.Text;
using Haulix.Core.Sii;

namespace Haulix.Core.Profiles;

/// <summary>Builds a <see cref="ProfileData"/> from an ETS2 profile folder and one of its saves.</summary>
public static class SaveParser
{
    public static string DecodeProfileName(string folderName)
    {
        try
        {
            if (folderName.Length % 2 != 0) return folderName;
            var bytes = Convert.FromHexString(folderName);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (FormatException)
        {
            return folderName;
        }
    }

    /// <summary>The save whose game.sii was written most recently (autosaves included).</summary>
    public static string? LatestSaveDir(string profileDir)
    {
        var saves = Path.Combine(profileDir, "save");
        if (!Directory.Exists(saves)) return null;
        return new DirectoryInfo(saves).EnumerateDirectories()
            .Select(d => new { Dir = d, Game = new FileInfo(Path.Combine(d.FullName, "game.sii")) })
            .Where(x => x.Game.Exists)
            .OrderByDescending(x => x.Game.LastWriteTimeUtc)
            .Select(x => x.Dir.FullName)
            .FirstOrDefault();
    }

    public static ProfileData Parse(string profileDir, string? saveDir = null)
    {
        saveDir ??= LatestSaveDir(profileDir) ?? throw new FileNotFoundException("No save with game.sii found in profile.", profileDir);
        var gamePath = Path.Combine(saveDir, "game.sii");

        var data = new ProfileData
        {
            ProfileId = Path.GetFileName(profileDir),
            ProfileName = DecodeProfileName(Path.GetFileName(profileDir)),
            SaveName = Path.GetFileName(saveDir),
            SavePath = gamePath,
            SaveTimeUtc = File.GetLastWriteTimeUtc(gamePath),
            ParsedAtUtc = DateTime.UtcNow,
        };

        ReadProfileSii(Path.Combine(profileDir, "profile.sii"), data);
        var doc = SiiDecoder.Decode(ReadShared(gamePath));
        ReadGame(doc, data);
        return data;
    }

    /// <summary>Reads a file the game may be writing at the same moment.</summary>
    private static byte[] ReadShared(string path)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var ms = new MemoryStream();
                fs.CopyTo(ms);
                return ms.ToArray();
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(200 * (attempt + 1));
            }
        }
    }

    private static void ReadProfileSii(string path, ProfileData data)
    {
        if (!File.Exists(path)) return;
        try
        {
            var doc = SiiDecoder.Decode(ReadShared(path));
            var p = doc.First("user_profile");
            if (p is null) return;
            data.CompanyName = p.Str("company_name") ?? "";
            data.PreferredBrand = p.Str("brand");
            data.ActiveModCount = p.Array("active_mods").Count;
            var name = p.Str("profile_name");
            if (!string.IsNullOrEmpty(name)) data.ProfileName = name;
        }
        catch (Exception ex)
        {
            data.Warnings.Add($"profile.sii could not be read: {ex.Message}");
        }
    }

    private static void ReadGame(SiiDocument doc, ProfileData data)
    {
        var economy = doc.First("economy") ?? throw new SiiFormatException("Save has no economy unit.");
        var player = doc.Get(economy.Ref("player")) ?? doc.First("player");
        var bank = doc.Get(economy.Ref("bank")) ?? doc.First("bank");

        data.Xp = economy.Long("experience_points") ?? 0;
        data.Skills = ReadSkills(economy);
        data.GameTimeMinutes = economy.Long("game_time") ?? 0;
        data.TotalDistanceKm = SumArray(economy, "total_distances_by_mode") ?? economy.Long("total_distance") ?? 0;
        data.TotalRealTimeMinutes = (SumArray(economy, "total_real_times_by_mode") ?? economy.Long("total_real_time") ?? 0) ;
        data.CancelledJobs = economy.Long("cancelled_job_count") ?? 0;
        data.TotalFuelLitres = economy.Long("total_fuel_litres") ?? 0;
        data.TotalFuelPrice = economy.Long("total_fuel_price") ?? 0;
        data.ServiceVisits = economy.Long("service_visit_count") ?? 0;
        data.GasStationVisits = economy.Long("gas_station_visit_count") ?? 0;
        data.LastVisitedCity = economy.Str("last_visited_city");
        data.VisitedCities = economy.Array("visited_cities").Select(v => SiiValue.Unquote(v)!).ToList();
        data.UnlockedDealers = economy.Array("unlocked_dealers").Select(v => SiiValue.Unquote(v)!).ToList();
        data.UnlockedRecruitments = economy.Array("unlocked_recruitments").Select(v => SiiValue.Unquote(v)!).ToList();
        data.TransportedCargoTypes = economy.Array("transported_cargo_types").Select(v => Names.Cargo(SiiValue.Unquote(v))).ToList();

        if (bank is not null)
        {
            data.Money = bank.Long("money_account") ?? 0;
            data.LoanLimit = bank.Long("loan_limit") ?? 0;
            data.Loans = bank.Array("loans").Select(id => doc.Get(id)?.Long("amount") ?? 0).Sum();
        }

        if (player is not null)
        {
            data.HqCity = player.Str("hq_city");
            data.HqCityName = CityCatalog.Name(data.HqCity);
        }

        var garages = ReadGarages(doc, economy, data.HqCity);
        var trucks = ReadTrucks(doc, player);
        var trailers = ReadTrailers(doc, player);
        var drivers = ReadDrivers(doc, player, trucks);

        // Link garages ⇄ trucks ⇄ drivers ⇄ trailers.
        foreach (var g in garages)
        {
            foreach (var tid in g.TruckIds)
                if (trucks.TryGetValue(tid, out var t)) { t.GarageId = g.Id; t.GarageCity = g.City; }
            foreach (var did in g.DriverIds)
                if (drivers.TryGetValue(did, out var d)) { d.GarageId = g.Id; d.GarageCity = g.City; }
            foreach (var rid in g.TrailerIds)
                if (trailers.TryGetValue(rid, out var r)) { r.GarageId = g.Id; r.GarageCity = g.City; }

            // Truck and driver slots are parallel arrays.
            var garageUnit = doc.Get(g.Id);
            if (garageUnit is null) continue;
            var vs = garageUnit.Array("vehicles");
            var ds = garageUnit.Array("drivers");
            for (var i = 0; i < Math.Min(vs.Count, ds.Count); i++)
            {
                if (trucks.TryGetValue(vs[i], out var t) && drivers.TryGetValue(ds[i], out var d))
                {
                    t.DriverId ??= d.Id;
                    t.DriverName ??= d.Name;
                    d.TruckId ??= t.Id;
                }
            }
        }

        foreach (var d in drivers.Values)
        {
            if (d.TruckId is not null && trucks.TryGetValue(d.TruckId, out var t))
            {
                d.TruckName = t.Name;
                t.DriverId ??= d.Id;
                t.DriverName ??= d.Name;
            }
        }
        foreach (var r in trailers.Values)
        {
            var d = drivers.Values.FirstOrDefault(x => x.TrailerId == r.Id);
            if (d is not null)
            {
                r.AssignedDriverId = d.Id;
                r.AssignedTruckId = d.TruckId;
            }
        }

        data.Garages = garages.OrderByDescending(g => g.IsHq).ThenByDescending(g => g.Slots).ThenBy(g => g.City).ToList();
        data.Trucks = trucks.Values.OrderByDescending(t => t.IsPlayerTruck).ThenBy(t => t.GarageCity).ThenBy(t => t.Name).ToList();
        data.Trailers = trailers.Values.OrderBy(t => t.GarageCity).ThenBy(t => t.Name).ToList();
        data.Drivers = drivers.Values.OrderByDescending(d => d.IsPlayer).ThenByDescending(d => d.Profit.Profit).ToList();
        data.Deliveries = ReadDeliveryLog(doc, economy);
    }

    private static Skills ReadSkills(SiiUnit u)
    {
        var adr = (int)(u.Long("adr") ?? 0);
        return new Skills
        {
            AdrMask = adr,
            Adr = System.Numerics.BitOperations.PopCount((uint)adr),
            LongDistance = (int)(u.Long("long_dist") ?? 0),
            HighValue = (int)(u.Long("heavy") ?? 0),
            Fragile = (int)(u.Long("fragile") ?? 0),
            JustInTime = (int)(u.Long("urgent") ?? 0),
            EcoDriving = (int)(u.Long("mechanical") ?? 0),
        };
    }

    private static List<Garage> ReadGarages(SiiDocument doc, SiiUnit economy, string? hq)
    {
        var list = new List<Garage>();
        foreach (var gid in economy.Array("garages"))
        {
            var u = doc.Get(gid);
            if (u is null) continue;
            var status = (int)(u.Long("status") ?? 0);
            if (status == 0) continue; // not owned
            var cityId = gid.Replace("garage.", "", StringComparison.Ordinal);
            var city = CityCatalog.Get(cityId);
            var vehicles = u.Array("vehicles");
            var drivers = u.Array("drivers");
            var trailers = u.Array("trailers");
            var g = new Garage
            {
                Id = gid,
                CityId = cityId,
                City = city.Name,
                Country = city.CountryName,
                Status = status,
                Slots = vehicles.Count,
                TruckIds = vehicles.Where(v => v != "null").ToList(),
                DriverIds = drivers.Where(v => v != "null").ToList(),
                TrailerIds = trailers.Where(v => v != "null").ToList(),
                TrailerSlots = trailers.Count,
                Productivity = u.Num("productivity") ?? 0,
                IsHq = string.Equals(cityId, hq, StringComparison.OrdinalIgnoreCase),
                Profit = ReadProfit(doc, u.Ref("profit_log")),
            };
            g.TrucksAssigned = g.TruckIds.Count;
            g.DriversAssigned = g.DriverIds.Count;
            g.TrailersAssigned = g.TrailerIds.Count;
            g.Size = g.Slots switch { <= 1 => "Small", <= 3 => "Medium", _ => "Large" };
            list.Add(g);
        }
        return list;
    }

    private static Dictionary<string, Truck> ReadTrucks(SiiDocument doc, SiiUnit? player)
    {
        var result = new Dictionary<string, Truck>(StringComparer.Ordinal);
        if (player is null) return result;

        var ids = player.Array("trucks").ToList();
        var profitLogs = player.Array("truck_profit_logs");
        var current = player.Ref("assigned_truck") ?? player.Ref("my_truck");

        // Player-owned "my vehicles" carry the parked world position.
        var positions = new Dictionary<string, WorldPoint>();
        foreach (var pvId in player.Array("my_vehicles"))
        {
            var pv = doc.Get(pvId);
            var vid = pv?.Ref("vehicle");
            if (vid is null) continue;
            if (!ids.Contains(vid)) ids.Add(vid);
            var pos = ParsePlacement(pv!.Raw("stored_vehicle_placement"));
            if (pos is not null) positions[vid] = pos;
        }
        var assignedVehicle = doc.Get(player.Ref("assigned_vehicles"))?.Ref("vehicle");

        for (var i = 0; i < ids.Count; i++)
        {
            var u = doc.Get(ids[i]);
            if (u is null || u.Type != "vehicle") continue;
            var t = new Truck { Id = u.Id, OdometerKm = u.Long("odometer") ?? 0 };
            foreach (var accId in u.Array("accessories"))
            {
                var acc = doc.Get(accId);
                if (acc is null) continue;
                t.AccessoryCount++;
                t.AccessoryValue += acc.Long("refund") ?? 0;
                var path = acc.Str("data_path");
                if (path is null || !path.StartsWith("/def/vehicle/truck/", StringComparison.Ordinal)) continue;
                var segs = path["/def/vehicle/truck/".Length..].Split('/');
                if (segs.Length < 2) continue;
                if (segs[1] == "data.sii")
                {
                    var (brandId, brand, model) = Names.Vehicle(segs[0]);
                    t.ModelId = segs[0];
                    t.BrandId = brandId;
                    t.Brand = brand;
                    t.Model = model;
                }
                else if (segs.Length >= 3)
                {
                    var file = Path.GetFileNameWithoutExtension(segs[^1]);
                    switch (segs[1])
                    {
                        case "engine":
                            t.Engine = Names.PrettyCode(file);
                            t.HorsePower = Names.EngineHorsePower(segs[0].Split('.')[0], file);
                            break;
                        case "transmission": t.Transmission = Names.PrettyCode(file); break;
                        case "chassis": t.Chassis = Names.PrettyCode(file); break;
                        case "cabin": t.Cabin = Names.PrettyCode(file); break;
                    }
                }
            }
            t.FuelRelative = u.Num("fuel_relative") ?? 0;
            t.TripFuelLitres = u.Num("trip_fuel_l") ?? 0;
            t.TripDistanceKm = u.Num("trip_distance_km") ?? 0;
            t.TripTimeMinutes = u.Long("trip_time_min") ?? 0;
            t.Wear = new Wear
            {
                Engine = u.Num("engine_wear") ?? 0,
                Transmission = u.Num("transmission_wear") ?? 0,
                Cabin = u.Num("cabin_wear") ?? 0,
                Chassis = u.Num("chassis_wear") ?? 0,
                Wheels = u.Array("wheels_wear").Select(SiiValue.ParseNumber).DefaultIfEmpty(0).Max() ?? 0,
            };
            (t.LicensePlate, t.PlateCountry) = Names.Plate(u.Str("license_plate"));
            t.IsPlayerTruck = t.Id == current || t.Id == assignedVehicle;
            if (positions.TryGetValue(t.Id, out var p)) t.Position = p;
            var logIdx = player.Array("trucks").ToList().IndexOf(t.Id);
            if (logIdx >= 0 && logIdx < profitLogs.Count) t.Profit = ReadProfit(doc, profitLogs[logIdx]);
            result[t.Id] = t;
        }
        return result;
    }

    private static Dictionary<string, Trailer> ReadTrailers(SiiDocument doc, SiiUnit? player)
    {
        var result = new Dictionary<string, Trailer>(StringComparer.Ordinal);
        if (player is null) return result;
        var current = player.Ref("assigned_trailer") ?? player.Ref("my_trailer");

        foreach (var id in player.Array("trailers"))
        {
            var u = doc.Get(id);
            if (u is null || u.Type != "trailer") continue;
            var r = new Trailer
            {
                Id = id,
                CargoMassKg = u.Num("cargo_mass") ?? 0,
                CargoDamage = u.Num("cargo_damage") ?? 0,
                OdometerKm = u.Long("odometer") ?? 0,
                Wear = new Wear
                {
                    Body = u.Num("trailer_body_wear") ?? 0,
                    Chassis = u.Num("chassis_wear") ?? 0,
                    Wheels = u.Array("wheels_wear").Select(SiiValue.ParseNumber).DefaultIfEmpty(0).Max() ?? 0,
                },
                IsPlayerTrailer = id == current,
            };
            (r.LicensePlate, r.PlateCountry) = Names.Plate(u.Str("license_plate"));

            foreach (var accId in u.Array("accessories"))
            {
                var acc = doc.Get(accId);
                if (acc is null) continue;
                r.AccessoryValue += acc.Long("refund") ?? 0;
                var path = acc.Str("data_path");
                if (path is null) continue;
                const string prefix = "/def/vehicle/trailer_owned/";
                if (path.StartsWith(prefix, StringComparison.Ordinal) && path.EndsWith("/data.sii", StringComparison.Ordinal))
                {
                    r.TypeId = path[prefix.Length..^"/data.sii".Length];
                }
            }

            var def = doc.Get(u.Ref("trailer_definition"));
            if (def is not null)
            {
                r.BodyType = Names.Pretty(def.Str("body_type"));
                r.ChainType = Names.Pretty(def.Str("chain_type"));
                r.Axles = (int?)def.Long("axles");
                r.GrossWeightLimitKg = def.Num("gross_trailer_weight_limit");
                r.ChassisMassKg = def.Num("chassis_mass");
                r.VolumeM3 = def.Num("volume");
                r.LengthM = def.Num("length");
                if (string.IsNullOrEmpty(r.TypeId))
                {
                    var src = def.Str("source_name");
                    if (src is not null) r.TypeId = src.Replace("trailer_def.", "", StringComparison.Ordinal);
                }
            }
            var typeParts = r.TypeId.Split('.');
            var brand = typeParts.Length > 1 ? Names.Brand(typeParts[0]) : "";
            var model = Names.Pretty(typeParts.Length > 1 ? typeParts[1] : r.TypeId);
            r.Name = brand is "" or "SCS" ? model : $"{brand} {model}";
            if (string.IsNullOrWhiteSpace(r.Name)) r.Name = r.BodyType ?? "Trailer";
            result[id] = r;
        }
        return result;
    }

    private static Dictionary<string, Driver> ReadDrivers(SiiDocument doc, SiiUnit? player, Dictionary<string, Truck> trucks)
    {
        var result = new Dictionary<string, Driver>(StringComparer.Ordinal);
        if (player is null) return result;

        foreach (var id in player.Array("drivers"))
        {
            var u = doc.Get(id);
            if (u is null) continue;
            var number = id.Replace("driver.", "", StringComparison.Ordinal);
            if (u.Type == "driver_player")
            {
                var playerTruck = trucks.Values.FirstOrDefault(t => t.IsPlayerTruck);
                result[id] = new Driver
                {
                    Id = id,
                    Name = "You",
                    IsPlayer = true,
                    Status = "player",
                    TruckId = playerTruck?.Id,
                    Profit = ReadProfit(doc, u.Ref("profit_log")),
                };
                continue;
            }
            if (u.Type != "driver_ai") continue;

            var d = new Driver
            {
                Id = id,
                Name = $"Driver {number}",
                Xp = u.Long("experience_points") ?? 0,
                Skills = ReadSkills(u),
                Hometown = NullIfEmpty(u.Str("hometown")),
                CurrentCity = NullIfEmpty(u.Str("current_city")),
                TruckId = u.Ref("assigned_truck") ?? u.Ref("adopted_truck"),
                TrailerId = u.Ref("assigned_trailer") ?? u.Ref("adopted_trailer"),
                TrainingPolicy = (u.Long("training_policy") ?? 0) switch
                {
                    1 => "ADR", 2 => "Long distance", 3 => "High value", 4 => "Fragile", 5 => "Just-in-time", 6 => "Eco driving", _ => "Balanced",
                },
                Profit = ReadProfit(doc, u.Ref("profit_log")),
            };

            var job = doc.Get(u.Ref("driver_job"));
            var cargo = job?.Ref("cargo");
            if (job is not null && cargo is not null)
            {
                var (srcCo, srcCity) = Names.CompanyAndCity(job.Ref("source_company"));
                var (dstCo, dstCity) = Names.CompanyAndCity(job.Ref("target_company"));
                d.Job = new DriverJob
                {
                    Cargo = Names.Cargo(cargo),
                    SourceCity = CityCatalog.Name(srcCity),
                    SourceCompany = Names.Company(srcCo),
                    TargetCity = CityCatalog.Name(dstCity),
                    TargetCompany = Names.Company(dstCo),
                    PlannedDistanceKm = job.Long("planned_distance_km") ?? 0,
                };
            }

            d.Status = d.Job is not null ? "on_job"
                : d.TruckId is null ? "available"
                : (u.Long("on_duty_timer") ?? 0) > 0 ? "driving"
                : "resting";
            d.Specialization = d.Profit.CargoCounts.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault();
            result[id] = d;
        }
        return result;
    }

    private static ProfitSummary ReadProfit(SiiDocument doc, string? profitLogId)
    {
        var summary = new ProfitSummary();
        var log = doc.Get(profitLogId);
        if (log is null) return summary;
        summary.DistanceKm = (log.Long("acc_distance_free") ?? 0) + (log.Long("acc_distance_on_job") ?? 0);
        summary.DistanceOnJobKm = log.Long("acc_distance_on_job") ?? 0;
        foreach (var entryId in log.Array("stats_data"))
        {
            var e = doc.Get(entryId);
            if (e is null) continue;
            var revenue = e.Long("revenue") ?? 0;
            var wage = e.Long("wage") ?? 0;
            var maint = e.Long("maintenance") ?? 0;
            var fuel = e.Long("fuel") ?? 0;
            var dist = e.Long("distance") ?? 0;
            summary.Revenue += revenue;
            summary.Wage += wage;
            summary.Maintenance += maint;
            summary.Fuel += fuel;
            summary.DistanceKm += dist;
            var jobs = (int)(e.Long("cargo_count") ?? 0);
            summary.Jobs += jobs;
            var cargo = e.Str("cargo");
            if (!string.IsNullOrEmpty(cargo))
            {
                var name = Names.Cargo(cargo);
                summary.CargoCounts[name] = summary.CargoCounts.GetValueOrDefault(name) + Math.Max(1, jobs);
            }
            summary.Days.Add(new ProfitDay
            {
                Day = (int)(e.Long("timestamp_day") ?? 0),
                Revenue = revenue,
                Costs = wage + maint + fuel,
                DistanceKm = dist,
            });
        }
        summary.Days = summary.Days.OrderBy(d => d.Day).ToList();
        return summary;
    }

    private static List<SaveDelivery> ReadDeliveryLog(SiiDocument doc, SiiUnit economy)
    {
        var list = new List<SaveDelivery>();
        var log = doc.Get(economy.Ref("delivery_log"));
        if (log is null) return list;
        foreach (var entryId in log.Array("entries"))
        {
            var e = doc.Get(entryId);
            var p = e?.Array("params");
            if (p is null || p.Count < 18) continue;
            string S(int i) => SiiValue.Unquote(p[i]) ?? "";
            long L(int i) => SiiValue.ParseLong(p[i]) ?? 0;
            double D(int i) => SiiValue.ParseNumber(p[i]) ?? 0;

            var cargoId = S(3);
            if (string.IsNullOrEmpty(cargoId)) continue; // free-roam / non-job entries
            var (srcCo, srcCity) = Names.CompanyAndCity(S(1));
            var (dstCo, dstCity) = Names.CompanyAndCity(S(2));
            var truckModel = S(16);
            var (_, brand, model) = Names.Vehicle(truckModel);
            list.Add(new SaveDelivery
            {
                FinishedGameMinute = L(0),
                SourceCompany = Names.Company(srcCo),
                SourceCity = srcCity,
                TargetCompany = Names.Company(dstCo),
                TargetCity = dstCity,
                CargoId = cargoId,
                Cargo = Names.Cargo(cargoId),
                Xp = L(4),
                Revenue = L(5),
                DistanceKm = L(6),
                CargoDamage = D(7),
                StartedGameMinute = L(15),
                TruckModelId = truckModel,
                Truck = $"{brand} {model}".Trim(),
                PlannedDistanceKm = L(17),
                Market = p.Count > 18 ? S(18) : null,
                CargoMassKg = p.Count > 22 ? Math.Max(0, D(22)) : 0,
                Units = p.Count > 23 ? (int)L(23) : 0,
            });
        }
        return list;
    }

    private static long? SumArray(SiiUnit u, string key)
    {
        var arr = u.Array(key);
        if (arr.Count == 0) return null;
        return arr.Sum(v => SiiValue.ParseLong(v) ?? 0);
    }

    /// <summary>Parses "(x, y, z) (w; x, y, z)" placements.</summary>
    public static WorldPoint? ParsePlacement(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        var close = raw.IndexOf(')');
        if (raw[0] != '(' || close < 0) return null;
        var parts = raw[1..close].Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 3) return null;
        var x = SiiValue.ParseNumber(parts[0]);
        var y = SiiValue.ParseNumber(parts[1]);
        var z = SiiValue.ParseNumber(parts[2]);
        if (x is null || z is null || (x == 0 && z == 0)) return null;
        return new WorldPoint { X = x.Value, Y = y ?? 0, Z = z.Value };
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
