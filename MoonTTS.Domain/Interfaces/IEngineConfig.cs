namespace MoonTTS.Domain.Interfaces;

public interface IEngineConfig
{
    public string VoiceId { get; set; }
    public float Speed { get; set; }
}