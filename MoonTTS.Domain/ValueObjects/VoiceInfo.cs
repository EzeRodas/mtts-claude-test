namespace MoonTTS.Domain.ValueObjects;

/* A voice offered by a model. Name and Language are optional, engines may only report ids */
public sealed record VoiceInfo(string Id, string? Name = null, string? Language = null)
{
    /* Human readable label for selection lists */
    public string DisplayName => string.IsNullOrWhiteSpace(Name)
        ? Id
        : string.IsNullOrWhiteSpace(Language) ? $"{Name} ({Id})" : $"{Name} ({Id}, {Language})";

    /* Keeps the id visible wherever the voice is rendered through ToString */
    public override string ToString() => DisplayName;
}
