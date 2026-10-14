namespace MoonTTS.Domain.Entities;

public class DictionaryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Original { get; set; } = string.Empty;
    public string Spelling { get; set; } = string.Empty;
    public bool CaseSensitive { get; set; } = false;
}