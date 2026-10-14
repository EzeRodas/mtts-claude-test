namespace MoonTTS.Domain.Entities;

public class HotkeyProfile
{
    public required string Name { get; set; }
    public required string FolderName { get; set; }
    public List<HotkeyEntry> Entries { get; set; } = new();
}