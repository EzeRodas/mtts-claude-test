namespace MoonTTS.Domain.Entities;

/* Per-model user preferences, persisted for each model id */
public class EngineConfig
{
    public string VoiceId { get; set; } = string.Empty;
    public float Speed { get; set; } = 1.0f;
}
