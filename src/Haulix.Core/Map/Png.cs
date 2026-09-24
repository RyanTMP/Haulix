using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Haulix.Core.Map;

/// <summary>Minimal PNG writer (no image library needed in Core).</summary>
internal static class Png
{
    private static readonly uint[] CrcTable = BuildCrc();

    /// <summary>8-bit grey + alpha image: white pixels whose alpha is <paramref name="alpha"/>.</summary>
    public static byte[] GrayAlpha(byte[] alpha, int w, int h)
    {
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            var row = new byte[1 + w * 2];
            for (var y = 0; y < h; y++)
            {
                row[0] = 0; // filter: none
                for (var x = 0; x < w; x++) { row[1 + x * 2] = 255; row[2 + x * 2] = alpha[y * w + x]; }
                z.Write(row);
            }
        }
        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, w);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), h);
        ihdr[8] = 8; ihdr[9] = 4; // bit depth 8, colour type 4 (grey + alpha)
        Chunk(png, "IHDR", ihdr);
        Chunk(png, "IDAT", raw.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);
        var t = Encoding.ASCII.GetBytes(type);
        s.Write(t);
        s.Write(data);
        var crc = 0xFFFFFFFFu;
        foreach (var b in t) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        foreach (var b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        Span<byte> c = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(c, crc ^ 0xFFFFFFFFu);
        s.Write(c);
    }

    private static uint[] BuildCrc()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}
