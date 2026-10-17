using MoonTTS.Domain.Entities;

namespace MoonTTS.Domain.Tests.Entities;

/* Verifies that HistoryEntry keeps its required init-only values */
public sealed class HistoryEntryTests
{
    /* Every required property keeps the value given at construction */
    [Fact]
    public void Properties_WhenInitialized_KeepValues()
    {
        var id = Guid.NewGuid();
        var createdAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        var entry = new HistoryEntry { Id = id, Text = "Hello", CreatedAt = createdAt, WavFileName = "clip.wav" };

        Assert.Equal(id, entry.Id);
        Assert.Equal("Hello", entry.Text);
        Assert.Equal(createdAt, entry.CreatedAt);
        Assert.Equal("clip.wav", entry.WavFileName);
    }
}
