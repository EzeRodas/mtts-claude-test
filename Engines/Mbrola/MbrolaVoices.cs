using MoonTTS.Engine.Protocol;

namespace MoonTTS.Engine.Mbrola;

/* File Structure:
        - Entries : VoiceEntry[]
        - Ids : HashSet<string>
        - Describe() : List<VoiceEntry>
        - Voice(string id, string nativeLanguageName, string gender, string bcp47) : VoiceEntry
*/

/* Curated, pre-sorted voice presets for the MBROLA diphone synthesizer (1 Male + 1 Female where available
   across all 36 supported MBROLA languages, ordered alphabetically by native language name) */
public static class MbrolaVoices
{
    private static readonly VoiceEntry[] Entries =
    [
        Voice("mb-af1", "Afrikaans", "Male", "af-ZA"),
        Voice("mb-ma1", "Bahasa Melayu", "Female", "ms-MY"),
        Voice("mb-cz2", "Čeština", "Male", "cs-CZ"),
        Voice("mb-cz1", "Čeština", "Female", "cs-CZ"),
        Voice("mb-de2", "Deutsch", "Male", "de-DE"),
        Voice("mb-de1", "Deutsch", "Female", "de-DE"),
        Voice("mb-ee1", "Eesti", "Male", "et-EE"),
        Voice("mb-en1", "English, United Kingdom", "Male", "en-GB"),
        Voice("mb-us2", "English, United States", "Male", "en-US"),
        Voice("mb-us1", "English, United States", "Female", "en-US"),
        Voice("mb-es1", "Español, España", "Male", "es-ES"),
        Voice("mb-es3", "Español, España", "Female", "es-ES"),
        Voice("mb-mx1", "Español, México", "Male", "es-MX"),
        Voice("mb-vz1", "Español, Venezuela", "Male", "es-VE"),
        Voice("mb-fr5", "Français, Belgique", "Male", "fr-BE"),
        Voice("mb-ca2", "Français, Canada", "Male", "fr-CA"),
        Voice("mb-fr1", "Français, France", "Male", "fr-FR"),
        Voice("mb-fr4", "Français, France", "Female", "fr-FR"),
        Voice("mb-cr1", "Hrvatski", "Male", "hr-HR"),
        Voice("mb-id1", "Indonesia", "Male", "id-ID"),
        Voice("mb-ic1", "Íslenska", "Male", "is-IS"),
        Voice("mb-it3", "Italiano", "Male", "it-IT"),
        Voice("mb-it4", "Italiano", "Female", "it-IT"),
        Voice("mb-la1", "Latina", "Male", "la-VA"),
        Voice("mb-lt2", "Lietuvių", "Male", "lt-LT"),
        Voice("mb-hu1", "Magyar", "Female", "hu-HU"),
        Voice("mb-nz1", "Māori", "Male", "mi-NZ"),
        Voice("mb-nl1", "Nederlands", "Male", "nl-NL"),
        Voice("mb-nl3", "Nederlands", "Female", "nl-NL"),
        Voice("mb-pl1", "Polski", "Female", "pl-PL"),
        Voice("mb-br1", "Português, Brasil", "Male", "pt-BR"),
        Voice("mb-br4", "Português, Brasil", "Female", "pt-BR"),
        Voice("mb-pt1", "Português, Portugal", "Female", "pt-PT"),
        Voice("mb-ro1", "Română", "Male", "ro-RO"),
        Voice("mb-sw1", "Svenska", "Male", "sv-SE"),
        Voice("mb-sw2", "Svenska", "Female", "sv-SE"),
        Voice("mb-tr1", "Türkçe", "Male", "tr-TR"),
        Voice("mb-tr2", "Türkçe", "Female", "tr-TR"),
        Voice("mb-gr1", "Ελληνικά", "Male", "el-GR"),
        Voice("mb-hb1", "עברית", "Male", "he-IL"),
        Voice("mb-hb2", "עברית", "Female", "he-IL"),
        Voice("mb-ar1", "العربية", "Male", "ar-SA"),
        Voice("mb-ir1", "فارسی", "Male", "fa-IR"),
        Voice("mb-in1", "हिन्दी", "Male", "hi-IN"),
        Voice("mb-in2", "हिन्दी", "Female", "hi-IN"),
        Voice("mb-tl1", "తెలుగు", "Female", "te-IN"),
        Voice("mb-jp1", "日本語", "Male", "ja-JP"),
        Voice("mb-jp3", "日本語", "Female", "ja-JP"),
        Voice("mb-cn1", "中文 普通话", "Female", "zh-CN")
    ];

    public static readonly HashSet<string> Ids =
        new(Entries.Select(e => e.Id), StringComparer.OrdinalIgnoreCase);

    /* Builds the ListVoices reply */
    public static List<VoiceEntry> Describe() => Entries.ToList();

    /* Constructs a VoiceEntry formatted with its native language name and gender */
    private static VoiceEntry Voice(string id, string nativeLanguageName, string gender, string bcp47) =>
        new()
        {
            Id = id,
            Name = $"{nativeLanguageName} · {gender}",
            Language = bcp47
        };
}
