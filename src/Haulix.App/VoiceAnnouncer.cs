using System.Collections.Concurrent;
using System.Globalization;
using System.Media;
using System.Speech.Synthesis;
using Haulix.Core.Services;
using Haulix.Core.Settings;

namespace Haulix.App;

/// <summary>
/// Reads notifications aloud. Two engines: a natural AI voice (Piper, offline neural voices downloaded on
/// request) or a Windows voice. Announcements are queued on one background thread so they never overlap;
/// if the chosen natural voice is not installed, the Windows voice of the UI language is used instead.
/// </summary>
internal sealed class VoiceAnnouncer : IDisposable
{
    private readonly BlockingCollection<(string? Text, NotificationSettings Cfg, string Lang, byte[]? Wav)> _queue = new();
    private readonly Thread _worker;
    private SpeechSynthesizer? _synth;

    public VoiceAnnouncer()
    {
        _worker = new Thread(Run) { IsBackground = true, Name = "HAULIX voice" };
        _worker.Start();
    }

    public static string Language(GeneralSettings g) =>
        g.LanguageChosen && g.Language is "en" or "de" ? g.Language : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "de" : "en";

    public void Speak(Notice n, GeneralSettings general, NotificationSettings cfg)
    {
        var lang = Language(general);
        // Symbols read badly; keep the sentence natural.
        var text = $"{n.Title}. {n.Message}".Replace("→", lang == "de" ? " nach " : " to ").Replace("·", ",").Replace("≈", "").Replace("%", lang == "de" ? " Prozent" : " percent");
        Say(text, cfg, lang);
    }

    public void Say(string text, NotificationSettings cfg, string lang)
    {
        if (_queue.Count < 6) _queue.Add((text, cfg, lang, null));
    }

    /// <summary>Queues a notification sound (played before any announcement that follows it).</summary>
    public void Chime(string sound, NotificationSettings cfg)
    {
        if (_queue.Count < 6) _queue.Add((null, cfg, "", NotificationSounds.Wav(sound, cfg.SoundStyle)));
    }

    /// <summary>Installed Windows voices (for the settings list).</summary>
    public static List<string> WindowsVoices()
    {
        try
        {
            using var s = new SpeechSynthesizer();
            return s.GetInstalledVoices().Where(v => v.Enabled).Select(v => v.VoiceInfo.Name).ToList();
        }
        catch (Exception) { return new List<string>(); }
    }

    private void Run()
    {
        foreach (var (text, cfg, lang, wav) in _queue.GetConsumingEnumerable())
        {
            try
            {
                if (wav is not null) { PlayWav(wav, cfg.SoundVolume); continue; }
                if (text is null) continue;
                var voice = string.IsNullOrEmpty(cfg.VoiceId) ? PiperVoices.DefaultFor(lang) : cfg.VoiceId;
                if (cfg.VoiceEngine != "windows" && PiperVoices.Installed(voice)) PlayWav(PiperVoices.Synthesize(voice, text, cfg.VoiceRate), cfg.VoiceVolume);
                else SpeakWindows(text, cfg, lang);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Voice failed: {ex.Message}");
            }
        }
    }

    private void SpeakWindows(string text, NotificationSettings cfg, string lang)
    {
        _synth ??= new SpeechSynthesizer();
        _synth.SetOutputToDefaultAudioDevice();
        _synth.Volume = Math.Clamp(cfg.VoiceVolume, 0, 100);
        _synth.Rate = (int)Math.Round(Math.Clamp((cfg.VoiceRate - 1) * 10, -5, 5));
        var name = cfg.WindowsVoice;
        var voices = _synth.GetInstalledVoices().Where(v => v.Enabled).Select(v => v.VoiceInfo).ToList();
        var pick = voices.FirstOrDefault(v => v.Name == name) ?? voices.FirstOrDefault(v => v.Culture.TwoLetterISOLanguageName == lang);
        if (pick is not null) _synth.SelectVoice(pick.Name);
        _synth.Speak(text);
    }

    /// <summary>Plays 16-bit PCM WAV bytes at the given volume (0–100) and waits until done.</summary>
    private static void PlayWav(byte[] wav, int volume)
    {
        var v = Math.Clamp(volume, 0, 100) / 100.0;
        if (v < 0.999 && wav.Length > 44)
        {
            for (var i = 44; i + 1 < wav.Length; i += 2)
            {
                var sample = (short)(wav[i] | (wav[i + 1] << 8));
                var scaled = (short)Math.Clamp(sample * v, short.MinValue, short.MaxValue);
                wav[i] = (byte)(scaled & 0xFF);
                wav[i + 1] = (byte)((scaled >> 8) & 0xFF);
            }
        }
        using var ms = new MemoryStream(wav);
        using var player = new SoundPlayer(ms);
        player.PlaySync();
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _synth?.Dispose();
    }
}
