using MoonTTS.Domain.Entities;

namespace MoonTTS.Domain.Tests.Entities;

/* Verifies the defaults of DictionaryEntry */
public sealed class DictionaryEntryTests
{
    /* A new entry gets a non-empty id */
    [Fact]
    public void Id_Default_IsNotEmpty()
    {
        Assert.NotEqual(Guid.Empty, new DictionaryEntry().Id);
    }

    /* Every new entry gets its own id */
    [Fact]
    public void Id_Default_IsUniquePerInstance()
    {
        Assert.NotEqual(new DictionaryEntry().Id, new DictionaryEntry().Id);
    }

    /* The text fields start empty */
    [Fact]
    public void Texts_Default_AreEmpty()
    {
        var entry = new DictionaryEntry();

        Assert.Equal(string.Empty, entry.Original);
        Assert.Equal(string.Empty, entry.Spelling);
    }

    /* Replacements are case-insensitive by default */
    [Fact]
    public void CaseSensitive_Default_IsFalse()
    {
        Assert.False(new DictionaryEntry().CaseSensitive);
    }
}
