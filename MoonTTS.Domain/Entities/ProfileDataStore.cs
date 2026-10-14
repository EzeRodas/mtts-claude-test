namespace MoonTTS.Domain.Entities;

public class ProfileDataStore
{
    public string ActiveProfileName { get; set; } = "Default";
    public List<HotkeyProfile> Profiles { get; set; } = new();
}