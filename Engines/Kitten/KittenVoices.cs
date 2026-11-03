using MoonTTS.Engine.Protocol;

namespace MoonTTS.Engine.Kitten;

/* File Structure:
        - VoiceEntryMapping (Record)
        - Voices (Property)
        - Describe() : List<VoiceEntry>
        - ResolveVoiceKey(string voiceId) : string
        - GetSpeedMultiplier(string modelId, string voiceKey) : float
*/

/* Presets, aliases and speed prior multipliers for KittenTTS voices */
public static class KittenVoices
{
    public sealed record VoiceEntryMapping(string Name, string Key, string Gender, string Language);

    private static readonly VoiceEntryMapping[] s_voices =
    [
        new("Bella", "expr-voice-2-f", "Female", "en-US"),
        new("Jasper", "expr-voice-2-m", "Male", "en-US"),
        new("Luna", "expr-voice-3-f", "Female", "en-US"),
        new("Bruno", "expr-voice-3-m", "Male", "en-US"),
        new("Rosie", "expr-voice-4-f", "Female", "en-US"),
        new("Hugo", "expr-voice-4-m", "Male", "en-US"),
        new("Kiki", "expr-voice-5-f", "Female", "en-US"),
        new("Leo", "expr-voice-5-m", "Male", "en-US")
    ];

    /* Returns the list of public VoiceEntry descriptors for ListVoices protocol responses */
    public static List<VoiceEntry> Describe()
    {
        return s_voices.Select(v => new VoiceEntry
        {
            Id = v.Name.ToLowerInvariant(),
            Name = $"{v.Name} ({v.Gender})",
            Language = v.Language
        }).ToList();
    }

    /* Resolves user input (e.g. 'bella' or 'expr-voice-2-f') to the internal npz key */
    public static string ResolveVoiceKey(string? voiceId)
    {
        if (string.IsNullOrWhiteSpace(voiceId))
            return s_voices[0].Key;

        string normalized = voiceId.Trim().ToLowerInvariant();
        var match = s_voices.FirstOrDefault(v =>
            string.Equals(v.Name, normalized, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(v.Key, normalized, StringComparison.OrdinalIgnoreCase));

        return match != null ? match.Key : s_voices[0].Key;
    }

    /* Returns the model-specific speed multiplier (speed_priors for nano models) */
    public static float GetSpeedMultiplier(string modelId, string voiceKey)
    {
        if (modelId.Contains("nano", StringComparison.OrdinalIgnoreCase))
        {
            return voiceKey == "expr-voice-4-m" ? 0.9f : 0.8f;
        }
        return 1.0f;
    }
}
