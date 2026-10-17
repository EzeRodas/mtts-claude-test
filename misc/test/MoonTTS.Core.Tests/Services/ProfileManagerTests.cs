using MoonTTS.Core.Services;
using MoonTTS.Domain.Entities;

namespace MoonTTS.Core.Tests.Services;

/* Tests for ProfileManager. Profile folders live in the config folder, so user folders are isolated */
[Collection(TestCollections.Environment)]
public sealed class ProfileManagerTests : IDisposable
{
    private readonly UserFolderSandbox _sandbox = new();
    private readonly FakeConfigurationRepository _repo = new();

    /* Restores the environment */
    public void Dispose() => _sandbox.Dispose();

    private string HotkeysDirectory => Path.Combine(_sandbox.ConfigDirectory, "audio", "hotkeys");

    /* Seeds the repository with profiles (folder name = name) and the active profile name */
    private void SeedProfiles(string active, params string[] names) =>
        _repo.SaveProfileData(new ProfileDataStore
        {
            ActiveProfileName = active,
            Profiles = names.Select(n => new HotkeyProfile { Name = n, FolderName = n }).ToList()
        });

    /* Creates the on-disk folder of a profile */
    private string CreateProfileFolder(string folderName) =>
        Directory.CreateDirectory(Path.Combine(HotkeysDirectory, folderName)).FullName;

    /* The constructor creates the hotkeys folder */
    [Fact]
    public void Constructor_Always_CreatesHotkeysDirectory()
    {
        _ = new ProfileManager(_repo);

        Assert.True(Directory.Exists(HotkeysDirectory));
    }

    /* A repository profiles change is forwarded as ProfilesListChanged */
    [Fact]
    public void Constructor_RepositoryProfilesChanged_RaisesProfilesListChanged()
    {
        var manager = new ProfileManager(_repo);
        int raised = 0;
        manager.ProfilesListChanged += () => raised++;

        _repo.SaveProfileData(new ProfileDataStore());

        Assert.Equal(1, raised);
    }

    /* GetProfiles lists every stored profile */
    [Fact]
    public void GetProfiles_SeededProfiles_ReturnsProfiles()
    {
        SeedProfiles("Default", "Default", "Work");
        var manager = new ProfileManager(_repo);

        Assert.Equal(["Default", "Work"], manager.GetProfiles().Select(p => p.Name));
    }

    /* The active profile is found by name, ignoring case */
    [Fact]
    public void GetActiveProfiles_ActiveNameDifferentCase_ReturnsActiveProfile()
    {
        SeedProfiles("work", "Default", "Work");
        var manager = new ProfileManager(_repo);

        Assert.Equal("Work", manager.GetActiveProfiles().Name);
    }

    /* A matching active profile is returned without changing the stored active name */
    [Fact]
    public void GetActiveProfiles_ActiveNameMatches_DoesNotChangeActiveProfile()
    {
        SeedProfiles("Work", "Default", "Work");
        var manager = new ProfileManager(_repo);

        manager.GetActiveProfiles();

        Assert.Null(_repo.LastActiveProfile);
    }

    /* An active name that matches no profile falls back to the first profile and persists that choice */
    [Fact]
    public void GetActiveProfiles_UnknownActiveName_FallsBackToFirstProfile()
    {
        SeedProfiles("Missing", "Default", "Work");
        var manager = new ProfileManager(_repo);

        var profile = manager.GetActiveProfiles();

        Assert.Equal("Default", profile.Name);
        Assert.Equal("Default", _repo.LastActiveProfile);
    }

    /* A blank name is rejected without saving */
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void CreateProfile_BlankName_ReturnsFailure(string name)
    {
        SeedProfiles("Default", "Default");
        int savesBefore = _repo.ProfileSaveCount;
        var manager = new ProfileManager(_repo);

        var result = manager.CreateProfile(name);

        Assert.True(result.IsFailure);
        Assert.Equal(savesBefore, _repo.ProfileSaveCount);
    }

    /* A name already taken (trimmed, ignoring case) is rejected */
    [Fact]
    public void CreateProfile_DuplicateName_ReturnsFailure()
    {
        SeedProfiles("Default", "Default", "Work");
        var manager = new ProfileManager(_repo);

        var result = manager.CreateProfile("  work ");

        Assert.True(result.IsFailure);
        Assert.Equal(2, _repo.ProfileData.Profiles.Count);
    }

    /* A new profile is saved with its trimmed name and the active profile is kept */
    [Fact]
    public void CreateProfile_NewName_SavesProfileKeepingActive()
    {
        SeedProfiles("Default", "Default");
        var manager = new ProfileManager(_repo);

        var result = manager.CreateProfile("  Gaming ");

        Assert.True(result.IsSuccess);
        Assert.Equal(["Default", "Gaming"], _repo.ProfileData.Profiles.Select(p => p.Name));
        Assert.Equal("Default", _repo.ProfileData.ActiveProfileName);
    }

    /* A new profile gets a sanitized folder that is created on disk */
    [Fact]
    public void CreateProfile_NameWithInvalidCharacters_CreatesSanitizedFolder()
    {
        SeedProfiles("Default", "Default");
        var manager = new ProfileManager(_repo);

        manager.CreateProfile("Work:Stream");

        var profile = _repo.ProfileData.Profiles.Single(p => p.Name == "Work:Stream");
        Assert.Equal("Work_Stream", profile.FolderName);
        Assert.True(Directory.Exists(Path.Combine(HotkeysDirectory, "Work_Stream")));
    }

    /* A folder name already used by another profile gets a "(1)" suffix */
    [Fact]
    public void CreateProfile_FolderUsedByOtherProfile_AppendsCounter()
    {
        SeedProfiles("Default", "Default", "Work_Stream");
        var manager = new ProfileManager(_repo);

        manager.CreateProfile("Work:Stream");

        Assert.Equal("Work_Stream(1)", _repo.ProfileData.Profiles.Single(p => p.Name == "Work:Stream").FolderName);
    }

    /* A folder name already present on disk gets a "(1)" suffix */
    [Fact]
    public void CreateProfile_FolderExistsOnDisk_AppendsCounter()
    {
        SeedProfiles("Default", "Default");
        CreateProfileFolder("Gaming");
        var manager = new ProfileManager(_repo);

        manager.CreateProfile("Gaming");

        Assert.Equal("Gaming(1)", _repo.ProfileData.Profiles.Single(p => p.Name == "Gaming").FolderName);
    }

    /* The counter keeps increasing until the folder name is free */
    [Fact]
    public void CreateProfile_SeveralCollisions_UsesFirstFreeCounter()
    {
        SeedProfiles("Default", "Default");
        CreateProfileFolder("Gaming");
        CreateProfileFolder("Gaming(1)");
        var manager = new ProfileManager(_repo);

        manager.CreateProfile("Gaming");

        Assert.Equal("Gaming(2)", _repo.ProfileData.Profiles.Single(p => p.Name == "Gaming").FolderName);
    }

    /* A blank name is rejected */
    [Fact]
    public void SetActiveProfile_BlankName_ReturnsFailure()
    {
        SeedProfiles("Default", "Default");
        var manager = new ProfileManager(_repo);

        var result = manager.SetActiveProfile(" ");

        Assert.True(result.IsFailure);
        Assert.Null(_repo.LastActiveProfile);
    }

    /* An unknown profile is rejected */
    [Fact]
    public void SetActiveProfile_UnknownName_ReturnsFailure()
    {
        SeedProfiles("Default", "Default");
        var manager = new ProfileManager(_repo);

        var result = manager.SetActiveProfile("Nope");

        Assert.True(result.IsFailure);
        Assert.Null(_repo.LastActiveProfile);
    }

    /* Selecting the already active profile succeeds without persisting or raising events */
    [Fact]
    public void SetActiveProfile_AlreadyActive_SucceedsWithoutChange()
    {
        SeedProfiles("Default", "Default", "Work");
        var manager = new ProfileManager(_repo);
        int raised = 0;
        manager.ActiveProfileChanged += _ => raised++;

        var result = manager.SetActiveProfile("default");

        Assert.True(result.IsSuccess);
        Assert.Null(_repo.LastActiveProfile);
        Assert.Equal(0, raised);
    }

    /* Selecting another profile (trimmed, ignoring case) persists its stored name */
    [Fact]
    public void SetActiveProfile_OtherProfile_PersistsStoredName()
    {
        SeedProfiles("Default", "Default", "Work");
        var manager = new ProfileManager(_repo);

        var result = manager.SetActiveProfile("  work ");

        Assert.True(result.IsSuccess);
        Assert.Equal("Work", _repo.LastActiveProfile);
    }

    /* Selecting another profile raises ActiveProfileChanged with that profile */
    [Fact]
    public void SetActiveProfile_OtherProfile_RaisesActiveProfileChanged()
    {
        SeedProfiles("Default", "Default", "Work");
        var manager = new ProfileManager(_repo);
        HotkeyProfile? raised = null;
        manager.ActiveProfileChanged += p => raised = p;

        manager.SetActiveProfile("Work");

        Assert.Equal("Work", raised?.Name);
    }

    /* A blank name is rejected */
    [Fact]
    public void DeleteProfile_BlankName_ReturnsFailure()
    {
        SeedProfiles("Default", "Default");
        var manager = new ProfileManager(_repo);

        var result = manager.DeleteProfile("");

        Assert.True(result.IsFailure);
    }

    /* An unknown profile is rejected and nothing is removed */
    [Fact]
    public void DeleteProfile_UnknownName_ReturnsFailure()
    {
        SeedProfiles("Default", "Default", "Work");
        var manager = new ProfileManager(_repo);

        var result = manager.DeleteProfile("Nope");

        Assert.True(result.IsFailure);
        Assert.Equal(2, _repo.ProfileData.Profiles.Count);
    }

    /* Deleting an inactive profile removes it and keeps the active profile */
    [Fact]
    public void DeleteProfile_InactiveProfile_RemovesItKeepingActive()
    {
        SeedProfiles("Default", "Default", "Work");
        var manager = new ProfileManager(_repo);

        var result = manager.DeleteProfile("work");

        Assert.True(result.IsSuccess);
        Assert.Equal(["Default"], _repo.ProfileData.Profiles.Select(p => p.Name));
        Assert.Equal("Default", _repo.ProfileData.ActiveProfileName);
        Assert.Null(_repo.LastActiveProfile);
    }

    /* Deleting an inactive profile raises ProfileDeleted but not ActiveProfileChanged */
    [Fact]
    public void DeleteProfile_InactiveProfile_RaisesOnlyProfileDeleted()
    {
        SeedProfiles("Default", "Default", "Work");
        var manager = new ProfileManager(_repo);
        HotkeyProfile? deleted = null;
        int activeChanged = 0;
        manager.ProfileDeleted += p => deleted = p;
        manager.ActiveProfileChanged += _ => activeChanged++;

        manager.DeleteProfile("Work");

        Assert.Equal("Work", deleted?.Name);
        Assert.Equal(0, activeChanged);
    }

    /* Deleting a profile removes its (empty) folder from disk */
    [Fact]
    public void DeleteProfile_ProfileWithEmptyFolder_DeletesFolder()
    {
        SeedProfiles("Default", "Default", "Work");
        string folder = CreateProfileFolder("Work");
        var manager = new ProfileManager(_repo);

        manager.DeleteProfile("Work");

        Assert.False(Directory.Exists(folder));
    }

    /* Deleting the active profile falls back to "Default" when it exists */
    [Fact]
    public void DeleteProfile_ActiveProfileWithDefaultPresent_FallsBackToDefault()
    {
        SeedProfiles("Work", "Gaming", "Default", "Work");
        var manager = new ProfileManager(_repo);
        HotkeyProfile? activated = null;
        manager.ActiveProfileChanged += p => activated = p;

        manager.DeleteProfile("Work");

        Assert.Equal("Default", _repo.ProfileData.ActiveProfileName);
        Assert.Equal("Default", _repo.LastActiveProfile);
        Assert.Equal("Default", activated?.Name);
    }

    /* Deleting the active profile without a "Default" falls back to the first remaining profile */
    [Fact]
    public void DeleteProfile_ActiveProfileWithoutDefault_FallsBackToFirstProfile()
    {
        SeedProfiles("Work", "Gaming", "Work", "Music");
        var manager = new ProfileManager(_repo);

        manager.DeleteProfile("Work");

        Assert.Equal(["Gaming", "Music"], _repo.ProfileData.Profiles.Select(p => p.Name));
        Assert.Equal("Gaming", _repo.LastActiveProfile);
    }

    /* Deleting the active profile also raises ProfileDeleted */
    [Fact]
    public void DeleteProfile_ActiveProfile_RaisesProfileDeleted()
    {
        SeedProfiles("Work", "Default", "Work");
        var manager = new ProfileManager(_repo);
        HotkeyProfile? deleted = null;
        manager.ProfileDeleted += p => deleted = p;

        manager.DeleteProfile("Work");

        Assert.Equal("Work", deleted?.Name);
    }

    /* Deleting the last profile replaces it with a fresh active "Default" profile */
    [Fact]
    public void DeleteProfile_LastProfile_RecreatesDefaultProfile()
    {
        SeedProfiles("Solo", "Solo");
        var manager = new ProfileManager(_repo);

        var result = manager.DeleteProfile("Solo");

        Assert.True(result.IsSuccess);
        var profile = Assert.Single(_repo.ProfileData.Profiles);
        Assert.Equal(ProfileManager.DefaultProfileName, profile.Name);
        Assert.Equal(ProfileManager.DefaultProfileName, profile.FolderName);
        Assert.Equal(ProfileManager.DefaultProfileName, _repo.ProfileData.ActiveProfileName);
        Assert.Equal(ProfileManager.DefaultProfileName, _repo.LastActiveProfile);
    }

    /* Deleting the last profile raises ProfileDeleted for it and ActiveProfileChanged for the new Default */
    [Fact]
    public void DeleteProfile_LastProfile_RaisesDeletedAndActiveChanged()
    {
        SeedProfiles("Solo", "Solo");
        var manager = new ProfileManager(_repo);
        HotkeyProfile? deleted = null;
        HotkeyProfile? activated = null;
        manager.ProfileDeleted += p => deleted = p;
        manager.ActiveProfileChanged += p => activated = p;

        manager.DeleteProfile("Solo");

        Assert.Equal("Solo", deleted?.Name);
        Assert.Equal(ProfileManager.DefaultProfileName, activated?.Name);
    }

    /* Deleting the last profile removes its folder */
    [Fact]
    public void DeleteProfile_LastProfile_DeletesFolder()
    {
        SeedProfiles("Solo", "Solo");
        string folder = CreateProfileFolder("Solo");
        var manager = new ProfileManager(_repo);

        manager.DeleteProfile("Solo");

        Assert.False(Directory.Exists(folder));
    }
}
