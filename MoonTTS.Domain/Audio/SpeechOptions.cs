namespace MoonTTS.Domain.Audio;

/* Options for configuring speech synthesis and playback */
public sealed class SpeechOptions
{
    public bool RecordHistory { get; set; } = true;
    public bool ApplyDictionary { get; set; } = true;
    public bool EnablePlayback { get; set; } = true;
    public float? Speed { get; set; } = null;
    public string? Voice { get; set; } = null;
    /* Volume override (0-100) for primary speakers; null uses AppConfig setting */
    public int? PrimaryVolume { get; set; } = null;
    /* Volume override (0-100) for virtual microphone; null uses AppConfig setting */
    public int? MonitorVolume { get; set; } = null;
    public string? ExportPath { get; set; } = null;
}