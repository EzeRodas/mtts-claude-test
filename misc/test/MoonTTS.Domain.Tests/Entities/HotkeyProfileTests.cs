using MoonTTS.Domain.Entities;

namespace MoonTTS.Domain.Tests.Entities;

/* Verifies the required values and defaults of HotkeyProfile */
public sealed class HotkeyProfileTests
{
    /* The required name and folder keep the values given at construction */
    [Fact]
    public void RequiredProperties_WhenInitialized_KeepValues()
    {
        var profile = new HotkeyProfile { Name = "Gaming", FolderName = "gaming" };

        Assert.Equal("Gaming", profile.Name);
        Assert.Equal("gaming", profile.FolderName);
    }

    /* A new profile has no entries */
    [Fact]
    public void Entries_Default_IsEmpty()
    {
        var profile = new HotkeyProfile { Name = "Default", FolderName = "default" };

        Assert.Empty(profile.Entries);
    }

    /* Each profile owns its own entry list */
    [Fact]
    public void Entries_Default_IsNotSharedBetweenInstances()
    {
        var first = new HotkeyProfile { Name = "A", FolderName = "a" };
        var second = new HotkeyProfile { Name = "B", FolderName = "b" };

        first.Entries.Add(new HotkeyEntry());

        Assert.Empty(second.Entries);
    }
}
