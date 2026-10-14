using MoonTTS.Domain.Enums;
using MoonTTS.Domain.Interfaces;

namespace MoonTTS.Domain.Entities;

public class KokoroEngineConfig : IEngineConfig
{
    public string VoiceId { get; set; } = KokoroVoiceId.Unselected.ToString();
    public float Speed { get; set; } = 1.0f;
}