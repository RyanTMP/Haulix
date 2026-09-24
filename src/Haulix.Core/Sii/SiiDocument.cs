using System.Globalization;

namespace Haulix.Core.Sii;

/// <summary>A single SII unit, e.g. <c>vehicle : _nameless.236.8573.3968 { ... }</c>.</summary>
public sealed class SiiUnit
{
    public SiiUnit(string type, string id)
    {
        Type = type;
        Id = id;
    }

    public string Type { get; }
    public string Id { get; }

    /// <summary>Scalar attributes, raw text as written by the game (quotes preserved).</summary>
    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

    /// <summary>Array attributes (<c>key[i]: v</c> / <c>key[]: v</c>).</summary>
    public Dictionary<string, List<string>> Arrays { get; } = new(StringComparer.Ordinal);

    public string? Raw(string key) => Values.TryGetValue(key, out var v) ? v : null;

    public string? Str(string key) => SiiValue.Unquote(Raw(key));

    public long? Long(string key) => SiiValue.ParseLong(Raw(key));

    public double? Num(string key) => SiiValue.ParseNumber(Raw(key));

    public bool? Bool(string key) => Raw(key) switch
    {
        "true" => true,
        "false" => false,
        _ => null,
    };

    /// <summary>Reference to another unit; <c>null</c> for the SII <c>null</c> literal.</summary>
    public string? Ref(string key)
    {
        var v = Raw(key);
        return v is null or "null" or "\"\"" ? null : SiiValue.Unquote(v);
    }

    public IReadOnlyList<string> Array(string key) =>
        Arrays.TryGetValue(key, out var list) ? list : System.Array.Empty<string>();

    public override string ToString() => $"{Type} : {Id}";
}

public sealed class SiiDocument
{
    private readonly Dictionary<string, SiiUnit> _byId = new(StringComparer.Ordinal);

    public List<SiiUnit> Units { get; } = new();

    public void Add(SiiUnit unit)
    {
        Units.Add(unit);
        _byId[unit.Id] = unit;
    }

    public SiiUnit? Get(string? id) => id is not null && _byId.TryGetValue(id, out var u) ? u : null;

    public IEnumerable<SiiUnit> OfType(string type) => Units.Where(u => u.Type == type);

    public SiiUnit? First(string type) => Units.FirstOrDefault(u => u.Type == type);
}

public static class SiiValue
{
    public static string? Unquote(string? v)
    {
        if (v is null) return null;
        if (v.Length >= 2 && v[0] == '"' && v[^1] == '"') return v[1..^1];
        return v;
    }

    public static long? ParseLong(string? v)
    {
        if (string.IsNullOrEmpty(v) || v is "nil" or "null") return null;
        v = Unquote(v)!;
        if (long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return l;
        var d = ParseNumber(v);
        return d is null ? null : (long)Math.Round(d.Value);
    }

    /// <summary>Parses SII numbers, including the <c>&amp;3f625f7c</c> hex-encoded float form.</summary>
    public static double? ParseNumber(string? v)
    {
        if (string.IsNullOrEmpty(v) || v is "nil" or "null") return null;
        v = Unquote(v)!;
        if (v.Length == 9 && v[0] == '&' &&
            uint.TryParse(v.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var bits))
        {
            return BitConverter.UInt32BitsToSingle(bits);
        }
        return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
    }
}
