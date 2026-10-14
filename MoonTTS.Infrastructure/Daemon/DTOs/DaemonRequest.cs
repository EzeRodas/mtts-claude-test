namespace MoonTTS.Infrastructure.Daemon.DTOs;

/* Daemon to keep model loaded on RAM for 5 minutes before unloading it.
   ONLY USED ON CLI VERSION, NOT PRESENT ON GUI VERSION */

public class DaemonRequest
{
    public string Action { get; set; } = DaemonIpcProtocol.ActionSynthesize;
    public string Text { get; set; } = string.Empty;
    public float? Speed { get; set; }
}