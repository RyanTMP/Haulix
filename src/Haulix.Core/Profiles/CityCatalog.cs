using System.Text.Json;

namespace Haulix.Core.Profiles;

public sealed record CityInfo(string Id, string Name, string? Country, string? CountryName, double? Lat, double? Lon);

/// <summary>Static catalogue of ETS2 cities (embedded <c>cities.json</c>).</summary>
public static class CityCatalog
{
    private static readonly Lazy<(Dictionary<string, CityInfo> Cities, Dictionary<string, string> Countries)> Data = new(Load);

    public static IReadOnlyDictionary<string, string> Countries => Data.Value.Countries;

    public static IEnumerable<CityInfo> All => Data.Value.Cities.Values;

    public static CityInfo Get(string? id)
    {
        if (string.IsNullOrEmpty(id)) return new CityInfo("", "", null, null, null, null);
        return Data.Value.Cities.TryGetValue(id, out var c) ? c : new CityInfo(id, Names.Pretty(id), null, null, null, null);
    }

    public static string Name(string? id) => Get(id).Name;

    public static string? CountryOf(string? id) => Get(id).Country;

    private static (Dictionary<string, CityInfo>, Dictionary<string, string>) Load()
    {
        using var stream = typeof(CityCatalog).Assembly.GetManifestResourceStream("Haulix.Core.Data.cities.json")
            ?? throw new InvalidOperationException("cities.json resource missing");
        using var doc = JsonDocument.Parse(stream);
        var countries = doc.RootElement.GetProperty("countries").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetString() ?? p.Name);
        var cities = new Dictionary<string, CityInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in doc.RootElement.GetProperty("cities").EnumerateArray())
        {
            var id = row[0].GetString()!;
            var cc = row[2].GetString();
            cities[id] = new CityInfo(id, row[1].GetString()!, cc, cc is not null && countries.TryGetValue(cc, out var cn) ? cn : null,
                row[3].GetDouble(), row[4].GetDouble());
        }
        return (cities, countries);
    }
}
