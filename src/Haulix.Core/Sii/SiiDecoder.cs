using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Haulix.Core.Sii;

public sealed class SiiFormatException(string message) : Exception(message);

/// <summary>
/// Reads ETS2/ATS SII files in all formats the game writes:
/// <list type="bullet">
/// <item><c>ScsC</c> – AES-256-CBC encrypted, zlib-compressed container (wraps one of the others)</item>
/// <item><c>BSII</c> – binary SII</item>
/// <item><c>SiiN</c> – plain text SiiNunit</item>
/// <item><c>3nK</c> – xor-scrambled text</item>
/// </list>
/// </summary>
public static class SiiDecoder
{
    // Public constant used by every SCS game build to encrypt saves.
    private static readonly byte[] ScsCKey =
    {
        0x2a, 0x5f, 0xcb, 0x17, 0x91, 0xd2, 0x2f, 0xb6, 0x02, 0x45, 0xb3, 0xd8, 0x36, 0x9e, 0xd0, 0xb2,
        0xc2, 0x73, 0x71, 0x56, 0x3f, 0xbf, 0x1f, 0x3c, 0x9e, 0xdf, 0x6b, 0x11, 0x82, 0x5a, 0x5d, 0x0a,
    };

    public static SiiDocument Load(string path) => Decode(File.ReadAllBytes(path));

    public static SiiDocument Decode(byte[] data)
    {
        var plain = ToPlain(data, out var binary);
        return binary ? BsiiReader.Read(plain) : SiiTextParser.Parse(Encoding.UTF8.GetString(plain));
    }

    /// <summary>Unwraps encryption/scrambling. Returns the text bytes or BSII bytes.</summary>
    public static byte[] ToPlain(byte[] data, out bool binary)
    {
        for (var depth = 0; depth < 3; depth++)
        {
            switch (Signature(data))
            {
                case "ScsC": data = DecryptScsC(data); continue;
                case "3nK": data = Unscramble3nK(data); continue;
                case "BSII": binary = true; return data;
                case "SiiN": binary = false; return data;
                default:
                    // Some files carry a UTF-8 BOM before SiiNunit.
                    if (data.Length > 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
                    {
                        data = data[3..];
                        continue;
                    }
                    throw new SiiFormatException("Unrecognised SII file format.");
            }
        }
        throw new SiiFormatException("SII container nesting too deep.");
    }

    private static string Signature(byte[] d)
    {
        if (d.Length >= 4)
        {
            var s = Encoding.ASCII.GetString(d, 0, 4);
            if (s is "ScsC" or "BSII" or "SiiN") return s;
        }
        if (d.Length >= 3 && d[0] == (byte)'3' && d[1] == (byte)'n' && d[2] == (byte)'K') return "3nK";
        return "";
    }

    private static byte[] DecryptScsC(byte[] d)
    {
        // Header: "ScsC"(4) HMAC(32) IV(16) uncompressed size(4) payload…
        if (d.Length < 56) throw new SiiFormatException("Truncated ScsC file.");
        var iv = d.AsSpan(36, 16).ToArray();
        var size = BitConverter.ToUInt32(d, 52);

        using var aes = Aes.Create();
        aes.Key = ScsCKey;
        var decrypted = aes.DecryptCbc(d.AsSpan(56), iv, PaddingMode.PKCS7);

        using var input = new MemoryStream(decrypted);
        using var z = new ZLibStream(input, CompressionMode.Decompress);
        var output = new MemoryStream((int)Math.Min(size, 256 * 1024 * 1024));
        z.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] Unscramble3nK(byte[] d)
    {
        // Header: "3nK"(3) version(1) unknown(1) seed(1) data…
        if (d.Length < 6) throw new SiiFormatException("Truncated 3nK file.");
        var seed = d[5];
        var result = new byte[d.Length - 6];
        for (var i = 0; i < result.Length; i++)
        {
            var key = (byte)((((seed << 2) ^ (seed ^ 0xFF)) << 3) ^ seed);
            result[i] = (byte)(d[i + 6] ^ key);
            seed++;
        }
        return result;
    }
}

/// <summary>Line-oriented parser for text SiiNunit files.</summary>
public static class SiiTextParser
{
    public static SiiDocument Parse(string text)
    {
        var doc = new SiiDocument();
        SiiUnit? current = null;
        var inBlockComment = false;

        foreach (var rawLine in text.AsSpan().EnumerateLines())
        {
            var line = rawLine.Trim();
            if (inBlockComment)
            {
                var end = line.IndexOf("*/");
                if (end < 0) continue;
                line = line[(end + 2)..].Trim();
                inBlockComment = false;
            }
            if (line.IsEmpty || line[0] == '#' || line.StartsWith("//")) continue;
            if (line.StartsWith("/*"))
            {
                if (line.IndexOf("*/") < 0) inBlockComment = true;
                continue;
            }

            if (current is null)
            {
                // "SiiNunit", "{", or "type : id {"
                if (line.EndsWith("{") && line.IndexOf(':') > 0)
                {
                    var header = line[..^1].Trim();
                    var colon = header.IndexOf(':');
                    current = new SiiUnit(header[..colon].Trim().ToString(), header[(colon + 1)..].Trim().ToString());
                }
                continue;
            }

            if (line[0] == '}')
            {
                doc.Add(current);
                current = null;
                continue;
            }

            var sep = line.IndexOf(':');
            if (sep <= 0) continue;
            var key = line[..sep].TrimEnd();
            var value = StripTrailingComment(line[(sep + 1)..].Trim()).ToString();

            var bracket = key.IndexOf('[');
            if (bracket > 0 && key[^1] == ']')
            {
                var name = key[..bracket].ToString();
                if (!current.Arrays.TryGetValue(name, out var list))
                {
                    list = new List<string>();
                    current.Arrays[name] = list;
                }
                var idx = key[(bracket + 1)..^1];
                if (idx.IsEmpty || !int.TryParse(idx, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) || i == list.Count)
                {
                    list.Add(value);
                }
                else
                {
                    while (list.Count <= i) list.Add("");
                    list[i] = value;
                }
            }
            else
            {
                current.Values[key.ToString()] = value;
            }
        }
        return doc;
    }

    private static ReadOnlySpan<char> StripTrailingComment(ReadOnlySpan<char> v)
    {
        if (v.IsEmpty || v[0] == '"') return v;
        var hash = v.IndexOf('#');
        if (hash > 0) v = v[..hash].TrimEnd();
        var slash = v.IndexOf("//");
        if (slash > 0) v = v[..slash].TrimEnd();
        return v;
    }
}

/// <summary>Reader for binary SII (BSII) versions 1–3.</summary>
internal sealed class BsiiReader
{
    private const string Base38 = "\0" + "0123456789abcdefghijklmnopqrstuvwxyz_";

    private sealed record Segment(int Type, string Name, Dictionary<uint, string>? Ordinals);
    private sealed record Structure(uint Id, string Name, List<Segment> Segments);

    private readonly BinaryReader _r;
    private readonly uint _version;
    private readonly Dictionary<uint, Structure> _structs = new();

    private BsiiReader(BinaryReader r, uint version)
    {
        _r = r;
        _version = version;
    }

    public static SiiDocument Read(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var r = new BinaryReader(ms);
        r.ReadUInt32(); // "BSII"
        var version = r.ReadUInt32();
        if (version is < 1 or > 3) throw new SiiFormatException($"Unsupported BSII version {version}.");
        return new BsiiReader(r, version).ReadAll();
    }

    private SiiDocument ReadAll()
    {
        var doc = new SiiDocument();
        var s = _r.BaseStream;
        while (s.Position + 4 <= s.Length)
        {
            var blockType = _r.ReadUInt32();
            if (blockType == 0)
            {
                var valid = _r.ReadByte() != 0;
                if (!valid) break; // terminating block
                ReadStructure();
            }
            else
            {
                if (!_structs.TryGetValue(blockType, out var st))
                    throw new SiiFormatException($"BSII data block references unknown structure {blockType}.");
                doc.Add(ReadUnit(st));
            }
        }
        return doc;
    }

    private void ReadStructure()
    {
        var id = _r.ReadUInt32();
        var name = ReadString();
        var segments = new List<Segment>();
        while (true)
        {
            var type = (int)_r.ReadUInt32();
            if (type == 0) break;
            var segName = ReadString();
            Dictionary<uint, string>? ordinals = null;
            if (type == 0x37)
            {
                var count = _r.ReadUInt32();
                ordinals = new Dictionary<uint, string>((int)count);
                for (var i = 0; i < count; i++)
                {
                    var ord = _r.ReadUInt32();
                    ordinals[ord] = ReadString();
                }
            }
            segments.Add(new Segment(type, segName, ordinals));
        }
        _structs[id] = new Structure(id, name, segments);
    }

    private SiiUnit ReadUnit(Structure st)
    {
        var unit = new SiiUnit(st.Name, ReadId());
        foreach (var seg in st.Segments)
        {
            if (IsArray(seg.Type))
            {
                var count = (int)_r.ReadUInt32();
                var list = new List<string>(count);
                for (var i = 0; i < count; i++) list.Add(ReadValue(seg.Type - 1, seg));
                unit.Arrays[seg.Name] = list;
                unit.Values[seg.Name] = count.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                unit.Values[seg.Name] = ReadValue(seg.Type, seg);
            }
        }
        return unit;
    }

    private static bool IsArray(int t) => t is 0x02 or 0x04 or 0x06 or 0x08 or 0x0A or 0x12 or 0x18 or 0x1A
        or 0x26 or 0x28 or 0x2A or 0x2C or 0x32 or 0x34 or 0x36 or 0x3A or 0x3C or 0x3E;

    private string ReadValue(int type, Segment seg)
    {
        switch (type)
        {
            case 0x01: return Quote(ReadString());
            case 0x03: return DecodeToken(_r.ReadUInt64());
            case 0x05: return F(_r.ReadSingle());
            case 0x07: return $"({F(_r.ReadSingle())}, {F(_r.ReadSingle())})";
            case 0x09: return $"({F(_r.ReadSingle())}, {F(_r.ReadSingle())}, {F(_r.ReadSingle())})";
            case 0x11: return $"({_r.ReadInt32()}, {_r.ReadInt32()}, {_r.ReadInt32()})";
            case 0x17: return $"({F(_r.ReadSingle())}, {F(_r.ReadSingle())}, {F(_r.ReadSingle())}, {F(_r.ReadSingle())})";
            case 0x19: return ReadPlacement();
            case 0x25: return _r.ReadInt32().ToString(CultureInfo.InvariantCulture);
            case 0x27:
            case 0x2F: return _r.ReadUInt32().ToString(CultureInfo.InvariantCulture);
            case 0x29: return _r.ReadInt16().ToString(CultureInfo.InvariantCulture);
            case 0x2B: return _r.ReadUInt16().ToString(CultureInfo.InvariantCulture);
            case 0x31: return _r.ReadInt64().ToString(CultureInfo.InvariantCulture);
            case 0x33: return _r.ReadUInt64().ToString(CultureInfo.InvariantCulture);
            case 0x35: return _r.ReadByte() != 0 ? "true" : "false";
            case 0x37:
            {
                var ord = _r.ReadUInt32();
                return seg.Ordinals is not null && seg.Ordinals.TryGetValue(ord, out var s) ? s : ord.ToString(CultureInfo.InvariantCulture);
            }
            case 0x39:
            case 0x3B:
            case 0x3D: return ReadId();
            default: throw new SiiFormatException($"Unsupported BSII value type 0x{type:X2} ({seg.Name}).");
        }
    }

    private string ReadPlacement()
    {
        // 8 floats: position (x,y,z), w (bias in v2+), rotation quaternion (w,x,y,z).
        var x = _r.ReadSingle();
        var y = _r.ReadSingle();
        var z = _r.ReadSingle();
        var w = _r.ReadSingle();
        var rw = _r.ReadSingle();
        var rx = _r.ReadSingle();
        var ry = _r.ReadSingle();
        var rz = _r.ReadSingle();
        if (_version >= 2)
        {
            var bias = (int)w;
            x += ((bias & 0xFFF) - 2048) << 9;
            z += (((bias >> 12) & 0xFFF) - 2048) << 9;
        }
        return $"({F(x)}, {F(y)}, {F(z)}) ({F(rw)}; {F(rx)}, {F(ry)}, {F(rz)})";
    }

    private string ReadId()
    {
        var parts = _r.ReadByte();
        if (parts == 0xFF)
        {
            var v = _r.ReadUInt64();
            var hex = v.ToString("x", CultureInfo.InvariantCulture);
            var groups = new List<string>();
            for (var end = hex.Length; end > 0; end -= 4) groups.Insert(0, hex[Math.Max(0, end - 4)..end]);
            return "_nameless." + string.Join('.', groups);
        }
        if (parts == 0) return "null";
        var sb = new StringBuilder();
        for (var i = 0; i < parts; i++)
        {
            if (i > 0) sb.Append('.');
            sb.Append(DecodeToken(_r.ReadUInt64()));
        }
        return sb.ToString();
    }

    private static string DecodeToken(ulong v)
    {
        var sb = new StringBuilder(12);
        while (v > 0)
        {
            var idx = (int)(v % 38);
            v /= 38;
            if (idx > 0) sb.Append(Base38[idx]);
        }
        return sb.ToString();
    }

    private string ReadString()
    {
        var len = _r.ReadUInt32();
        return len == 0 ? "" : Encoding.UTF8.GetString(_r.ReadBytes((int)len));
    }

    private static string Quote(string s) => "\"" + s.Replace("\"", "\\\"") + "\"";

    private static string F(float f) => f.ToString("0.######", CultureInfo.InvariantCulture);
}
