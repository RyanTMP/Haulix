namespace Haulix.App;

/// <summary>
/// HAULIX's own notification sounds, synthesised at runtime (no sound files, no third-party audio):
/// short chimes for job updates, milestones, warnings and a louder alarm for the TruckersMP AFK warning.
/// Two styles: "soft" (bell-like) and "digital" (clean electronic beeps).
/// </summary>
internal static class NotificationSounds
{
    public static readonly string[] Kinds = ["job", "success", "info", "warning", "critical", "afk"];

    private const int Rate = 44_100;
    private static readonly Dictionary<string, byte[]> Cache = new();

    /// <summary>Which sound a notification gets.</summary>
    public static string For(string kind, string category) => category switch
    {
        "afk" => "afk",
        "achievement" => "success",
        "job" when kind == "success" => "success",
        "job" when kind is "warning" or "critical" => "warning",
        "job" => "job",
        _ => kind switch { "critical" => "critical", "warning" => "warning", "success" => "success", _ => "info" },
    };

    /// <summary>16-bit mono PCM WAV of the given sound.</summary>
    public static byte[] Wav(string sound, string style)
    {
        var key = $"{style}/{sound}";
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var w)) return (byte[])w.Clone();
            w = Build(sound, style == "digital");
            Cache[key] = w;
            return (byte[])w.Clone();
        }
    }

    // (frequency Hz, start s, length s)
    private static byte[] Build(string sound, bool digital)
    {
        (double F, double At, double Len)[] notes = sound switch
        {
            "job" => [(659.25, 0, 0.5), (987.77, 0.12, 0.6)],                                  // E5 → B5: "new job"
            "success" => [(523.25, 0, 0.45), (659.25, 0.1, 0.45), (783.99, 0.2, 0.45), (1046.5, 0.3, 0.8)], // C major arpeggio
            "warning" => [(880, 0, 0.35), (698.46, 0.2, 0.5)],                                  // A5 → F5, falling
            "critical" => [(987.77, 0, 0.18), (987.77, 0.24, 0.18), (987.77, 0.48, 0.3)],
            "afk" => [(1174.7, 0, 0.16), (880, 0.18, 0.16), (1174.7, 0.36, 0.16), (880, 0.54, 0.16), (1174.7, 0.72, 0.16), (880, 0.9, 0.3)],
            _ => [(1046.5, 0, 0.45)],                                                            // info: single ping
        };
        var loud = sound is "afk" or "critical" ? 0.55 : 0.38;
        var total = notes.Max(n => n.At + n.Len) + 0.05;
        var samples = new double[(int)(total * Rate)];
        foreach (var (f, at, len) in notes)
        {
            var start = (int)(at * Rate);
            var count = (int)(len * Rate);
            for (var i = 0; i < count && start + i < samples.Length; i++)
            {
                var t = (double)i / Rate;
                var attack = Math.Min(1, t / 0.006);
                double v;
                if (digital)
                {
                    // Soft square-ish tone with a short gate.
                    var env = attack * (t < len - 0.03 ? 1 : Math.Max(0, (len - t) / 0.03));
                    v = env * (Math.Sin(2 * Math.PI * f * t) + 0.25 * Math.Sin(6 * Math.PI * f * t) + 0.1 * Math.Sin(10 * Math.PI * f * t)) * 0.7;
                }
                else
                {
                    // Bell: fundamental + soft partials with exponential decay.
                    var env = attack * Math.Exp(-t * (sound is "afk" or "critical" ? 9 : 6.5));
                    v = env * (Math.Sin(2 * Math.PI * f * t) + 0.35 * Math.Sin(4 * Math.PI * f * t) * Math.Exp(-t * 6) + 0.12 * Math.Sin(6.01 * Math.PI * f * t) * Math.Exp(-t * 10));
                }
                samples[start + i] += v * loud;
            }
        }

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        var dataLen = samples.Length * 2;
        w.Write("RIFF"u8.ToArray()); w.Write(36 + dataLen); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(dataLen);
        foreach (var s in samples) w.Write((short)Math.Clamp(s * short.MaxValue, short.MinValue, short.MaxValue));
        w.Flush();
        return ms.ToArray();
    }
}
