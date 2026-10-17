using MoonTTS.Domain.Entities;

namespace MoonTTS.Domain.Tests.Entities;

/* Verifies the defaults of ProfileDataStore */
public sealed class ProfileDataStoreTests
{
    /* The active profile is called "Default" */
    [Fact]
    public void ActiveProfileName_Default_IsDefault()
    {
        Assert.Equal("Default", new ProfileDataStore().ActiveProfileName);
    }

    /* A new store holds no profiles */
    [Fact]
    public void Profiles_Default_IsEmpty()
    {
        Assert.Empty(new ProfileDataStore().Profiles);
    }

    /* Each store owns its own profile list */
    [Fact]
    public void Profiles_Default_IsNotSharedBetweenInstances()
    {
        var first = new ProfileDataStore();
        var second = new ProfileDataStore();

        first.Profiles.Add(new HotkeyProfile { Name = "A", FolderName = "a" });

        Assert.Empty(second.Profiles);
    }
}
