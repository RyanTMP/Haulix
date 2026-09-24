using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Haulix.Core.Profiles;
using Haulix.Core.Sii;
using Xunit.Abstractions;

namespace Haulix.Core.Tests;

public class SiiTests(ITestOutputHelper output)
{
    private const string Sample = """
        SiiNunit
        {
        economy : _nameless.1.2 {
         bank: _nameless.1.3
         player: _nameless.1.4
         experience_points: 1234
         adr: 5
         long_dist: 2
         visited_cities: 2
         visited_cities[0]: hamburg
         visited_cities[1]: bremen
         garages: 1
         garages[0]: garage.berlin
        }
        # a comment
        bank : _nameless.1.3 {
         money_account: 98765
        }
        player : _nameless.1.4 {
         hq_city: berlin
         trucks: 1
         trucks[0]: _nameless.9.1
         drivers: 0
         trailers: 0
        }
        garage : garage.berlin {
         vehicles: 3
         vehicles[0]: _nameless.9.1
         vehicles[1]: null
         vehicles[2]: null
         drivers: 3
         drivers[0]: null
         drivers[1]: null
         drivers[2]: null
         trailers: 0
         status: 2
        }
        vehicle : _nameless.9.1 {
         fuel_relative: &3f000000
         odometer: 128432
         engine_wear: &3c23d70a
         accessories: 2
         accessories[0]: _nameless.9.2
         accessories[1]: _nameless.9.3
         license_plate: "AB<img src=/x.mat>123|germany"
        }
        vehicle_accessory : _nameless.9.2 {
         data_path: "/def/vehicle/truck/scania.s_2016/data.sii"
        }
        vehicle_accessory : _nameless.9.3 {
         data_path: "/def/vehicle/truck/scania.s_2016/engine/dc16_770.sii"
         refund: 12000
        }
        }
        """;

    [Fact]
    public void ParsesTextSii()
    {
        var doc = SiiDecoder.Decode(Encoding.UTF8.GetBytes(Sample));
        var eco = doc.First("economy")!;
        Assert.Equal(1234, eco.Long("experience_points"));
        Assert.Equal(new[] { "hamburg", "bremen" }, eco.Array("visited_cities"));
        Assert.Equal(0.5, doc.Get("_nameless.9.1")!.Num("fuel_relative"));
        Assert.Null(doc.Get("garage.berlin")!.Array("vehicles").Skip(1).Select(v => v == "null" ? null : v).First());
    }

    [Fact]
    public void DecryptsScsCContainer()
    {
        var encrypted = MakeScsC(Encoding.UTF8.GetBytes(Sample));
        var doc = SiiDecoder.Decode(encrypted);
        Assert.Equal(98765, doc.First("bank")!.Long("money_account"));
    }

    [Fact]
    public void ParsesBinarySii()
    {
        // Hand-built BSII v2: struct 1 "bank" { money_account:int64, name:string, flag:bool, owner:id }
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("BSII")); w.Write(2u);
        w.Write(0u); w.Write((byte)1); w.Write(1u); WriteStr(w, "bank");
        w.Write(0x31u); WriteStr(w, "money_account");
        w.Write(0x01u); WriteStr(w, "name");
        w.Write(0x35u); WriteStr(w, "flag");
        w.Write(0x39u); WriteStr(w, "owner");
        w.Write(0u);
        // data block
        w.Write(1u);
        w.Write((byte)0xFF); w.Write(0x23685733968UL);
        w.Write(4242L); WriteStr(w, "Haulix"); w.Write((byte)1);
        w.Write((byte)1); w.Write(Encode38("player"));
        // terminator
        w.Write(0u); w.Write((byte)0);

        var doc = SiiDecoder.Decode(ms.ToArray());
        var bank = doc.First("bank")!;
        Assert.Equal("_nameless.236.8573.3968", bank.Id);
        Assert.Equal(4242, bank.Long("money_account"));
        Assert.Equal("Haulix", bank.Str("name"));
        Assert.True(bank.Bool("flag"));
        Assert.Equal("player", bank.Ref("owner"));
    }

    [Fact]
    public void SaveParserBuildsProfileFromTextSave()
    {
        var dir = Directory.CreateTempSubdirectory("haulix-test");
        try
        {
            var profile = Directory.CreateDirectory(Path.Combine(dir.FullName, "4861756C6978"));
            var save = Directory.CreateDirectory(Path.Combine(profile.FullName, "save", "autosave"));
            File.WriteAllText(Path.Combine(save.FullName, "game.sii"), Sample);

            var data = SaveParser.Parse(profile.FullName);
            Assert.Equal("Haulix", data.ProfileName);
            Assert.Equal(98765, data.Money);
            Assert.Equal(2, data.Skills.Adr);
            var truck = Assert.Single(data.Trucks);
            Assert.Equal("Scania S", truck.Name);
            Assert.Equal(770, truck.HorsePower);
            Assert.Equal("AB 123", truck.LicensePlate);
            Assert.Equal("Berlin", truck.GarageCity);
            var garage = Assert.Single(data.Garages);
            Assert.Equal(3, garage.Slots);
            Assert.True(garage.IsHq);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Theory]
    [InlineData("scania", "dc16_770", 770)]
    [InlineData("volvo", "d13k540", 540)]
    [InlineData("man", "d3876_471a", 640)]
    public void EstimatesHorsePower(string brand, string engine, int hp) =>
        Assert.Equal(hp, Names.EngineHorsePower(brand, engine));

    /// <summary>Smoke test against real ETS2 profiles on this machine (read-only). Skips silently when none exist.</summary>
    [Fact]
    public void ParsesRealProfilesIfPresent()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Euro Truck Simulator 2");
        foreach (var sub in new[] { "profiles", "steam_profiles" })
        {
            var dir = Path.Combine(root, sub);
            if (!Directory.Exists(dir)) continue;
            foreach (var p in Directory.GetDirectories(dir))
            {
                if (SaveParser.LatestSaveDir(p) is null) continue;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var data = SaveParser.Parse(p);
                output.WriteLine($"{data.ProfileName} / {data.CompanyName}: money {data.Money}, xp {data.Xp}, trucks {data.Trucks.Count}, " +
                                 $"trailers {data.Trailers.Count}, garages {data.Garages.Count}, drivers {data.Drivers.Count}, " +
                                 $"deliveries {data.Deliveries.Count} ({sw.ElapsedMilliseconds} ms)");
                foreach (var t in data.Trucks.Take(3))
                    output.WriteLine($"   {t.Name} {t.Engine} {t.HorsePower}hp {t.OdometerKm}km @ {t.GarageCity} plate {t.LicensePlate}");
                foreach (var d in data.Deliveries.TakeLast(2))
                    output.WriteLine($"   {d.SourceCity}->{d.TargetCity} {d.Cargo} {d.DistanceKm}km €{d.Revenue} xp{d.Xp}");
                Assert.False(string.IsNullOrEmpty(data.ProfileName));
            }
        }
    }

    private static void WriteStr(BinaryWriter w, string s)
    {
        var b = Encoding.UTF8.GetBytes(s);
        w.Write((uint)b.Length);
        w.Write(b);
    }

    private static ulong Encode38(string s)
    {
        const string chars = "\0" + "0123456789abcdefghijklmnopqrstuvwxyz_";
        ulong v = 0;
        for (var i = s.Length - 1; i >= 0; i--) v = v * 38 + (ulong)chars.IndexOf(s[i]);
        return v;
    }

    private static byte[] MakeScsC(byte[] plain)
    {
        byte[] key =
        {
            0x2a, 0x5f, 0xcb, 0x17, 0x91, 0xd2, 0x2f, 0xb6, 0x02, 0x45, 0xb3, 0xd8, 0x36, 0x9e, 0xd0, 0xb2,
            0xc2, 0x73, 0x71, 0x56, 0x3f, 0xbf, 0x1f, 0x3c, 0x9e, 0xdf, 0x6b, 0x11, 0x82, 0x5a, 0x5d, 0x0a,
        };
        using var zms = new MemoryStream();
        using (var z = new ZLibStream(zms, CompressionLevel.Optimal, true)) z.Write(plain);
        var iv = RandomNumberGenerator.GetBytes(16);
        using var aes = Aes.Create();
        aes.Key = key;
        var enc = aes.EncryptCbc(zms.ToArray(), iv, PaddingMode.PKCS7);
        using var o = new MemoryStream();
        o.Write(Encoding.ASCII.GetBytes("ScsC"));
        o.Write(new byte[32]);
        o.Write(iv);
        o.Write(BitConverter.GetBytes((uint)plain.Length));
        o.Write(enc);
        return o.ToArray();
    }
}
