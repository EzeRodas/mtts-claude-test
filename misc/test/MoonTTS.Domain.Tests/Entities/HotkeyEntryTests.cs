using MoonTTS.Domain.Entities;

namespace MoonTTS.Domain.Tests.Entities;

/* Verifies the defaults of HotkeyEntry */
public sealed class HotkeyEntryTests
{
    /* The id is not generated automatically */
    [Fact]
    public void Id_Default_IsEmptyGuid()
    {
        Assert.Equal(Guid.Empty, new HotkeyEntry().Id);
    }

    /* The phrase and hotkey start empty */
    [Fact]
    public void Texts_Default_AreEmpty()
    {
        var entry = new HotkeyEntry();

        Assert.Equal(string.Empty, entry.Text);
        Assert.Equal(string.Empty, entry.Hotkey);
    }

    /* Every property keeps the value it is set to */
    [Fact]
    public void Properties_WhenSet_KeepValues()
    {
        var id = Guid.NewGuid();

        var entry = new HotkeyEntry { Id = id, Text = "Hello", Hotkey = "Ctrl+1" };

        Assert.Equal(id, entry.Id);
        Assert.Equal("Hello", entry.Text);
        Assert.Equal("Ctrl+1", entry.Hotkey);
    }
}
