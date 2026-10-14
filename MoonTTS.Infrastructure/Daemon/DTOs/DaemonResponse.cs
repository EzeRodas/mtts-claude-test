using MoonTTS.Domain.Audio;

namespace MoonTTS.Infrastructure.Daemon.DTOs;

/* Daemon to keep model loaded on RAM for 5 minutes before unloading it.
   ONLY USED ON CLI VERSION, NOT PRESENT ON GUI VERSION */

public class DaemonResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ActiveModel { get; set; }
    public int IdleSecondsRemaining { get; set; }
    public AudioFormat? Format { get; set; }
}