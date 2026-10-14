namespace MoonTTS.Domain.Entities;

public class HistoryEntry
{
    public required Guid Id { get; init;  }
    public required string Text { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required string WavFileName { get; init; }
}