using System.Globalization;
using System.Speech.Synthesis;
using Haulix.Core.Services;
using Haulix.Core.Settings;

namespace Haulix.App;

/// <summary>
/// Reads job notifications aloud with the Windows speech voices (offline). Picks a voice matching the UI
/// language when one is installed; queued so announcements never talk over each other.
/// </summary>
internal sealed class VoiceAnnouncer : IDisposable
{
    private SpeechSynthesizer? _synth;
    private string? _voiceFor;

    public void Speak(Notice n, GeneralSettings general)
    {
        try
        {
            var lang = general.LanguageChosen && general.Language is "en" or "de" ? general.Language : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            _synth ??= new SpeechSynthesizer { Volume = 90, Rate = 0 };
            _synth.SetOutputToDefaultAudioDevice();
            if (_voiceFor != lang)
            {
                _voiceFor = lang;
                var voice = _synth.GetInstalledVoices()
                    .Where(v => v.Enabled && v.VoiceInfo.Culture.TwoLetterISOLanguageName == lang)
                    .Select(v => v.VoiceInfo.Name)
                    .FirstOrDefault();
                if (voice is not null) _synth.SelectVoice(voice);
            }
            // Symbols read badly; keep the sentence natural.
            var text = $"{n.Title}. {n.Message}".Replace("→", lang == "de" ? " nach " : " to ").Replace("·", ",").Replace("≈", "");
            _synth.SpeakAsync(text);
        }
        catch (Exception)
        {
            // No speech engine/voice available: stay silent.
        }
    }

    public void Dispose() => _synth?.Dispose();
}
