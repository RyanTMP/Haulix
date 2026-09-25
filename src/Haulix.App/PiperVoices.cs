using System.Diagnostics;
using System.IO.Compression;

namespace Haulix.App;

/// <summary>
/// Natural AI voices via Piper (open-source neural text-to-speech that runs offline on the PC). The engine and
/// the voices are downloaded on request into %LOCALAPPDATA%\Haulix\voices – not into the install folder – from
/// the official Piper releases on GitHub and the Piper voice collection on Hugging Face.
/// </summary>
internal static class PiperVoices
{
    public sealed record Voice(string Id, string Name, string Lang, string Gender, int SizeMb, string Path);

    private const string EngineUrl = "https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_windows_amd64.zip";
    private const string VoiceBase = "https://huggingface.co/rhasspy/piper-voices/resolve/v1.0.0/";

    public static readonly Voice[] Catalog =
    [
        new("de_DE-thorsten-medium", "Thorsten", "de", "male", 61, "de/de_DE/thorsten/medium/"),
        new("de_DE-kerstin-low", "Kerstin", "de", "female", 61, "de/de_DE/kerstin/low/"),
        new("de_DE-thorsten_emotional-medium", "Thorsten (expressive)", "de", "male", 74, "de/de_DE/thorsten_emotional/medium/"),
        new("en_US-amy-medium", "Amy", "en", "female", 61, "en/en_US/amy/medium/"),
        new("en_US-ryan-medium", "Ryan", "en", "male", 61, "en/en_US/ryan/medium/"),
        new("en_GB-alba-medium", "Alba (British)", "en", "female", 61, "en/en_GB/alba/medium/"),
    ];

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(20) };

    public static string Root => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Haulix", "voices");
    public static string EngineExe => System.IO.Path.Combine(Root, "piper", "piper.exe");
    public static bool EngineInstalled => File.Exists(EngineExe);
    public static string ModelPath(string id) => System.IO.Path.Combine(Root, id + ".onnx");
    public static bool Installed(string id) => EngineInstalled && File.Exists(ModelPath(id)) && File.Exists(ModelPath(id) + ".json");

    /// <summary>Default voice for a language: the first installed one, else the first in the catalogue.</summary>
    public static string DefaultFor(string lang) =>
        Catalog.FirstOrDefault(v => v.Lang == lang && Installed(v.Id))?.Id ?? Catalog.First(v => v.Lang == lang).Id;

    /// <summary>Downloads the engine (once) and the voice; progress 0–1.</summary>
    public static async Task Install(string id, Action<double, string> progress)
    {
        var voice = Catalog.FirstOrDefault(v => v.Id == id) ?? throw new ArgumentException("Unknown voice");
        Directory.CreateDirectory(Root);
        if (!EngineInstalled)
        {
            var zip = System.IO.Path.Combine(Root, "piper.zip");
            await Download(EngineUrl, zip, p => progress(p * 0.25, "engine"));
            ZipFile.ExtractToDirectory(zip, Root, overwriteFiles: true);
            File.Delete(zip);
        }
        await Download(VoiceBase + voice.Path + id + ".onnx.json", ModelPath(id) + ".json", _ => { });
        await Download(VoiceBase + voice.Path + id + ".onnx", ModelPath(id), p => progress(0.25 + p * 0.75, "voice"));
        progress(1, "done");
    }

    public static void Remove(string id)
    {
        foreach (var f in new[] { ModelPath(id), ModelPath(id) + ".json" })
            if (File.Exists(f)) File.Delete(f);
    }

    private static async Task Download(string url, string target, Action<double> progress)
    {
        var tmp = target + ".part";
        using (var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? 0;
            await using var src = await resp.Content.ReadAsStreamAsync();
            await using var dst = File.Create(tmp);
            var buf = new byte[1 << 16];
            long done = 0; int n;
            while ((n = await src.ReadAsync(buf)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n));
                done += n;
                if (total > 0) progress((double)done / total);
            }
        }
        File.Move(tmp, target, overwrite: true);
    }

    /// <summary>Synthesises text to WAV bytes (≈0.1–0.5 s). Rate &gt; 1 speaks faster.</summary>
    public static byte[] Synthesize(string id, string text, double rate)
    {
        var wav = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"haulix-voice-{Guid.NewGuid():N}.wav");
        var psi = new ProcessStartInfo(EngineExe)
        {
            ArgumentList = { "--model", ModelPath(id), "--output_file", wav, "--length_scale", (1 / Math.Clamp(rate, 0.6, 1.6)).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) },
            RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true,
            UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = System.IO.Path.GetDirectoryName(EngineExe)!,
        };
        using var p = Process.Start(psi)!;
        p.StandardInput.WriteLine(text.Replace('\n', ' '));
        p.StandardInput.Close();
        _ = p.StandardError.ReadToEndAsync();
        _ = p.StandardOutput.ReadToEndAsync();
        if (!p.WaitForExit(15000)) { try { p.Kill(); } catch (Exception) { } throw new TimeoutException("Voice engine did not answer"); }
        try { return File.ReadAllBytes(wav); }
        finally { try { File.Delete(wav); } catch (Exception) { } }
    }
}
