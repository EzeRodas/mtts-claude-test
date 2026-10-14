namespace MoonTTS.Domain.Entities;

public class HotkeyEntry
{
    public Guid Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public string Hotkey { get; set; } = string.Empty;
}