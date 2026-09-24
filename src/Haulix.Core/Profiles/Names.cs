using System.Globalization;
using System.Text.RegularExpressions;

namespace Haulix.Core.Profiles;

/// <summary>Turns SCS identifiers (<c>scania.s_2016</c>, <c>dc16_770</c>, <c>cargo.med_vaccine</c>) into display names.</summary>
public static partial class Names
{
    private static readonly Dictionary<string, string> Brands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["scania"] = "Scania", ["volvo"] = "Volvo", ["man"] = "MAN", ["daf"] = "DAF", ["renault"] = "Renault",
        ["mercedes"] = "Mercedes-Benz", ["iveco"] = "IVECO", ["krone"] = "Krone", ["schmitz"] = "Schmitz Cargobull",
        ["kogel"] = "Kögel", ["schwarzmuller"] = "Schwarzmüller", ["tirsan"] = "Tirsan", ["wielton"] = "Wielton",
        ["feldbinder"] = "Feldbinder", ["scs"] = "SCS", ["fruehauf"] = "Fruehauf", ["lecitrailer"] = "Lecitrailer",
    };

    private static readonly Dictionary<string, string> Models = new(StringComparer.OrdinalIgnoreCase)
    {
        ["scania.s_2016"] = "S", ["scania.r_2016"] = "R", ["scania.r"] = "R 2009", ["scania.streamline"] = "Streamline",
        ["scania.s_2024e"] = "S BEV", ["volvo.fh16"] = "FH16 2009", ["volvo.fh16_2012"] = "FH16 2012",
        ["volvo.fh_2021"] = "FH 2021", ["volvo.fh_2024"] = "FH 2024", ["volvo.fh_2024e"] = "FH Electric",
        ["man.tgx"] = "TGX", ["man.tgx_euro6"] = "TGX Euro 6", ["man.tgx_2020"] = "TGX 2020",
        ["daf.xf"] = "XF 105", ["daf.xf_euro6"] = "XF Euro 6", ["daf.2021"] = "XF 2021", ["daf.xg"] = "XG",
        ["daf.xg_plus"] = "XG+", ["daf.xf_2021"] = "XF 2021", ["daf.xd"] = "XD",
        ["renault.magnum"] = "Magnum", ["renault.premium"] = "Premium", ["renault.t"] = "T", ["renault.t_evo"] = "T Evolution",
        ["renault.etech_t"] = "E-Tech T",
        ["mercedes.actros"] = "Actros MP3", ["mercedes.actros2014"] = "Actros 2014", ["mercedes.actros2019"] = "Actros 2019",
        ["mercedes.actros_l"] = "Actros L", ["mercedes.eactros600"] = "eActros 600",
        ["iveco.stralis"] = "Stralis", ["iveco.hiway"] = "Stralis Hi-Way", ["iveco.sway"] = "S-Way",
    };

    public static string Brand(string? brandId) =>
        brandId is null ? "" : Brands.TryGetValue(brandId, out var b) ? b : Pretty(brandId);

    /// <summary>"scania.s_2016" → ("scania", "Scania", "S").</summary>
    public static (string BrandId, string Brand, string Model) Vehicle(string? modelId)
    {
        if (string.IsNullOrEmpty(modelId)) return ("", "", "");
        modelId = modelId.Replace("vehicle.", "", StringComparison.Ordinal);
        var dot = modelId.IndexOf('.');
        var brandId = dot > 0 ? modelId[..dot] : modelId;
        var model = Models.TryGetValue(modelId, out var m) ? m : dot > 0 ? PrettyCode(modelId[(dot + 1)..]) : "";
        return (brandId, Brand(brandId), model);
    }

    /// <summary>Estimates horsepower from an engine file name, e.g. <c>dc16_770</c> → 770, <c>d3876_471a</c> (MAN, kW) → 640.</summary>
    public static int? EngineHorsePower(string brandId, string engineId)
    {
        var nums = NumberRegex().Matches(engineId).Select(m => int.Parse(m.Value, CultureInfo.InvariantCulture)).ToList();
        if (nums.Count == 0) return null;
        var candidate = nums.LastOrDefault(n => n is >= 150 and <= 999);
        if (candidate == 0) return null;
        // MAN engine files are named by kW output.
        if (brandId.Equals("man", StringComparison.OrdinalIgnoreCase) && candidate < 500)
            return (int)Math.Round(candidate * 1.36 / 10.0) * 10;
        return candidate;
    }

    public static string Cargo(string? cargoId)
    {
        if (string.IsNullOrEmpty(cargoId)) return "";
        var id = cargoId.Replace("cargo.", "", StringComparison.Ordinal);
        return CargoNames.Lookup(id) ?? Pretty(id);
    }

    /// <summary>"company.volatile.norrsken.karlstad" → ("norrsken", "karlstad").</summary>
    public static (string Company, string CityId) CompanyAndCity(string? companyId)
    {
        if (string.IsNullOrEmpty(companyId)) return ("", "");
        var parts = companyId.Replace("company.volatile.", "", StringComparison.Ordinal).Split('.');
        return parts.Length >= 2 ? (parts[0], parts[^1]) : (parts[0], "");
    }

    public static string Company(string? id) => string.IsNullOrEmpty(id) ? "" : Pretty(id).Replace(" Log", " Logistics", StringComparison.Ordinal);

    /// <summary>"med_vaccine" → "Med Vaccine".</summary>
    public static string Pretty(string? id)
    {
        if (string.IsNullOrEmpty(id)) return "";
        var words = id.Replace('.', ' ').Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select(w => w.Length <= 1 ? w.ToUpperInvariant() : char.ToUpperInvariant(w[0]) + w[1..]));
    }

    /// <summary>Code-like ids keep capitals: "ato3512f_r_aso" → "ATO3512F R ASO", "allison_retarder" → "Allison Retarder".</summary>
    public static string PrettyCode(string? id)
    {
        if (string.IsNullOrEmpty(id)) return "";
        var words = id.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select(w => w.Any(char.IsDigit) || w.Length <= 3
            ? w.ToUpperInvariant()
            : char.ToUpperInvariant(w[0]) + w[1..]));
    }

    /// <summary>Strips the formatting markup from a save-game license plate: <c>"1AL&lt;img ...&gt;0889|czech"</c>.</summary>
    public static (string? Plate, string? Country) Plate(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return (null, null);
        var pipe = raw.LastIndexOf('|');
        var country = pipe >= 0 ? raw[(pipe + 1)..] : null;
        var text = pipe >= 0 ? raw[..pipe] : raw;
        text = TagRegex().Replace(text, " ");
        text = SpaceRegex().Replace(text, " ").Trim();
        return (text.Length == 0 ? null : text, country is null ? null : Pretty(country));
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaceRegex();
}
