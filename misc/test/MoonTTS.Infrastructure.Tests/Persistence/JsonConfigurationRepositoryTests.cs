using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MoonTTS.Core.Utilities;
using MoonTTS.Domain.Entities;
using MoonTTS.Domain.ValueObjects;
using MoonTTS.Infrastructure.Persistence;

namespace MoonTTS.Infrastructure.Tests.Persistence;

/* JsonConfigurationRepository over an isolated config folder: loading, defaults for missing or corrupt files,
   atomic saves, per-model engine configurations, profiles and change events.
   The config folder comes from the user folders, which can only be redirected on POSIX systems */
[Collection(TestCollections.Environment)]
public sealed class JsonConfigurationRepositoryTests : IDisposable
{
    private const string PosixOnly = "The user config folder cannot be redirected on Windows";

    private readonly TempDirectory _temp = new();
    private readonly EnvironmentScope _environment;

    /* Points the user folders at a temp directory for the duration of the test */
    public JsonConfigurationRepositoryTests()
    {
        _environment = EnvironmentScope.IsolatedUserDirectories(_temp);
    }

    /* Restores the environment and removes the temp folder */
    public void Dispose()
    {
        _environment.Dispose();
        _temp.Dispose();
    }

    private static string ConfigDirectory => PathResolver.GetConfigDirectory();

    /* Full path of a file inside the config folder */
    private static string ConfigPath(params string[] parts) => Path.Combine([ConfigDirectory, .. parts]);

    /* Writes a config file with the app's own serializer metadata */
    private static void WriteConfig<T>(T value, JsonTypeInfo<T> typeInfo, params string[] parts)
    {
        string path = ConfigPath(parts);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, typeInfo));
    }

    /* Writes raw text into a config file */
    private static void WriteRaw(string content, params string[] parts)
    {
        string path = ConfigPath(parts);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    /* Reads a config file back with the app's own serializer metadata */
    private static T ReadConfig<T>(JsonTypeInfo<T> typeInfo, params string[] parts) =>
        JsonSerializer.Deserialize(File.ReadAllText(ConfigPath(parts)), typeInfo)!;

    /* Two profiles ("Main" active, folder "main_folder"; "Work", folder "work_folder") */
    private static ProfileDataStore TwoProfiles(string active = "Main") => new()
    {
        ActiveProfileName = active,
        Profiles =
        [
            new HotkeyProfile { Name = "Main", FolderName = "main_folder" },
            new HotkeyProfile { Name = "Work", FolderName = "work_folder" }
        ]
    };

    /* A hotkey entry */
    private static HotkeyEntry Hotkey(string text) => new() { Id = Guid.NewGuid(), Text = text, Hotkey = "Ctrl+Alt+H" };

    /* A history entry */
    private static HistoryEntry History(string text) =>
        new() { Id = Guid.NewGuid(), Text = text, CreatedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), WavFileName = $"{text}.wav" };

    /* The constructor creates the config folder */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void Constructor_NoConfigFolder_CreatesIt()
    {
        _ = new JsonConfigurationRepository();

        Assert.True(Directory.Exists(ConfigDirectory));
    }

    /* Without any file every part uses its defaults */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void Constructor_NoFiles_UsesDefaults()
    {
        var repo = new JsonConfigurationRepository();

        Assert.Equal(JsonSerializer.Serialize(new AppConfig(), AppJsonContext.Default.AppConfig),
            JsonSerializer.Serialize(repo.AppConfig, AppJsonContext.Default.AppConfig));
        Assert.Empty(repo.DictionaryEntries);
        Assert.Empty(repo.Hotkeys);
        Assert.Empty(repo.HistoryEntries);
    }

    /* Without profiles.json a "Default" profile is created, activated and saved */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void Constructor_NoProfilesFile_CreatesAndSavesDefaultProfile()
    {
        var repo = new JsonConfigurationRepository();

        var profile = Assert.Single(repo.ProfileData.Profiles);
        Assert.Equal(("Default", "Default"), (profile.Name, profile.FolderName));
        Assert.Equal("Default", repo.ProfileData.ActiveProfileName);
        Assert.Equal("Default", ReadConfig(AppJsonContext.Default.ProfileDataStore, "audio", "hotkeys", "profiles.json").Profiles.Single().Name);
    }

    /* settings.json is loaded into AppConfig */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void LoadAll_SettingsFile_LoadsAppConfig()
    {
        WriteConfig(new AppConfig { ActiveModel = new ModelId("kokoro", "v1"), PrimaryVolume = 42 }, AppJsonContext.Default.AppConfig, "settings.json");

        var repo = new JsonConfigurationRepository();

        Assert.Equal(new ModelId("kokoro", "v1"), repo.AppConfig.ActiveModel);
        Assert.Equal(42, repo.AppConfig.PrimaryVolume);
    }

    /* A corrupt settings.json falls back to the default AppConfig */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void LoadAll_CorruptSettings_UsesDefaultAppConfig()
    {
        WriteRaw("{ this is not json", "settings.json");

        var repo = new JsonConfigurationRepository();

        Assert.Equal(100, repo.AppConfig.PrimaryVolume);
        Assert.True(repo.AppConfig.ActiveModel.IsUnselected);
    }

    /* dictionary.json is loaded into DictionaryEntries */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void LoadAll_DictionaryFile_LoadsEntries()
    {
        WriteConfig(new List<DictionaryEntry> { new() { Original = "lol", Spelling = "laughing out loud" } },
            AppJsonContext.Default.ListDictionaryEntry, "dictionary.json");

        var repo = new JsonConfigurationRepository();

        Assert.Equal("laughing out loud", Assert.Single(repo.DictionaryEntries).Spelling);
    }

    /* A corrupt dictionary.json falls back to an empty dictionary */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void LoadAll_CorruptDictionary_UsesEmptyDictionary()
    {
        WriteRaw("[ { broken", "dictionary.json");

        Assert.Empty(new JsonConfigurationRepository().DictionaryEntries);
    }

    /* profiles.json is loaded into ProfileData */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void LoadAll_ProfilesFile_LoadsProfiles()
    {
        WriteConfig(TwoProfiles(active: "Work"), AppJsonContext.Default.ProfileDataStore, "audio", "hotkeys", "profiles.json");

        var repo = new JsonConfigurationRepository();

        Assert.Equal(["Main", "Work"], repo.ProfileData.Profiles.Select(p => p.Name));
        Assert.Equal("Work", repo.ProfileData.ActiveProfileName);
    }

    /* A corrupt profiles.json is replaced by a valid file with the Default profile */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void LoadAll_CorruptProfiles_RewritesDefaultProfile()
    {
        WriteRaw("not json at all", "audio", "hotkeys", "profiles.json");

        var repo = new JsonConfigurationRepository();

        Assert.Equal("Default", Assert.Single(repo.ProfileData.Profiles).Name);
        Assert.Equal("Default", ReadConfig(AppJsonContext.Default.ProfileDataStore, "audio", "hotkeys", "profiles.json").Profiles.Single().Name);
    }

    /* A profiles.json without profiles gets the Default profile */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void LoadAll_ProfilesFileWithoutProfiles_AddsDefaultProfile()
    {
        WriteRaw("""{ "ActiveProfileName": "Gone", "Profiles": [] }""", "audio", "hotkeys", "profiles.json");

        var repo = new JsonConfigurationRepository();

        Assert.Equal("Default", Assert.Single(repo.ProfileData.Profiles).Name);
        Assert.Equal("Default", repo.ProfileData.ActiveProfileName);
    }

    /* The hotkeys of the active profile are loaded from its folder */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void LoadAll_ActiveProfile_LoadsHotkeysFromItsFolder()
    {
        WriteConfig(TwoProfiles(active: "Work"), AppJsonContext.Default.ProfileDataStore, "audio", "hotkeys", "profiles.json");
        WriteConfig(new List<HotkeyEntry> { Hotkey("main phrase") }, AppJsonContext.Default.ListHotkeyEntry, "audio", "hotkeys", "main_folder", "hotkeys.json");
        WriteConfig(new List<HotkeyEntry> { Hotkey("work phrase") }, AppJsonContext.Default.ListHotkeyEntry, "audio", "hotkeys", "work_folder", "hotkeys.json");

        var repo = new JsonConfigurationRepository();

        Assert.Equal("work phrase", Assert.Single(repo.Hotkeys).Text);
    }

    /* When the active profile name matches no profile, the first profile's hotkeys are loaded */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void LoadAll_UnknownActiveProfile_LoadsHotkeysOfFirstProfile()
    {
        WriteConfig(TwoProfiles(active: "Missing"), AppJsonContext.Default.ProfileDataStore, "audio", "hotkeys", "profiles.json");
        WriteConfig(new List<HotkeyEntry> { Hotkey("main phrase") }, AppJsonContext.Default.ListHotkeyEntry, "audio", "hotkeys", "main_folder", "hotkeys.json");

        var repo = new JsonConfigurationRepository();

        Assert.Equal("main phrase", Assert.Single(repo.Hotkeys).Text);
    }

    /* A corrupt hotkeys.json falls back to no hotkeys */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void LoadAll_CorruptHotkeys_UsesEmptyList()
    {
        WriteRaw("{{{", "audio", "hotkeys", "Default", "hotkeys.json");

        Assert.Empty(new JsonConfigurationRepository().Hotkeys);
    }

    /* history.json is loaded into HistoryEntries */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void LoadAll_HistoryFile_LoadsEntries()
    {
        WriteConfig(new List<HistoryEntry> { History("hello") }, AppJsonContext.Default.ListHistoryEntry, "audio", "history", "history.json");

        var entry = Assert.Single(new JsonConfigurationRepository().HistoryEntries);

        Assert.Equal(("hello", "hello.wav"), (entry.Text, entry.WavFileName));
    }

    /* A corrupt history.json falls back to an empty history */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void LoadAll_CorruptHistory_UsesEmptyHistory()
    {
        WriteRaw("[1, 2,", "audio", "history", "history.json");

        Assert.Empty(new JsonConfigurationRepository().HistoryEntries);
    }

    /* LoadAll picks up files changed on disk after construction */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void LoadAll_SettingsChangedOnDisk_ReloadsAppConfig()
    {
        var repo = new JsonConfigurationRepository();
        WriteConfig(new AppConfig { PrimaryVolume = 7 }, AppJsonContext.Default.AppConfig, "settings.json");

        repo.LoadAll();

        Assert.Equal(7, repo.AppConfig.PrimaryVolume);
    }

    /* LoadAll drops cached engine configurations so they are read from disk again */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void LoadAll_EngineConfigChangedOnDisk_ReloadsEngineConfig()
    {
        var model = new ModelId("kokoro", "v1");
        var repo = new JsonConfigurationRepository();
        _ = repo.GetEngineConfig(model);
        WriteConfig(new EngineConfig { VoiceId = "edited" }, AppJsonContext.Default.EngineConfig, "engine configurations", "kokoro", "v1.json");

        repo.LoadAll();

        Assert.Equal("edited", repo.GetEngineConfig(model).VoiceId);
    }

    /* SaveAppConfig replaces the in-memory AppConfig */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void SaveAppConfig_Config_UpdatesAppConfig()
    {
        var repo = new JsonConfigurationRepository();
        var config = new AppConfig { MonitorVolume = 12 };

        repo.SaveAppConfig(config);

        Assert.Same(config, repo.AppConfig);
    }

    /* SaveAppConfig persists the config for the next start */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void SaveAppConfig_Config_IsLoadedByNewInstance()
    {
        new JsonConfigurationRepository().SaveAppConfig(new AppConfig { ActiveModel = new ModelId("piper", "amy"), MonitorVolume = 12 });

        var reloaded = new JsonConfigurationRepository();

        Assert.Equal(new ModelId("piper", "amy"), reloaded.AppConfig.ActiveModel);
        Assert.Equal(12, reloaded.AppConfig.MonitorVolume);
    }

    /* SaveAppConfig raises AppConfigChanged */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void SaveAppConfig_Config_RaisesAppConfigChanged()
    {
        var repo = new JsonConfigurationRepository();
        int raised = 0;
        repo.AppConfigChanged += () => raised++;

        repo.SaveAppConfig(new AppConfig());

        Assert.Equal(1, raised);
    }

    /* Saves write a temporary file and move it over the target, leaving no temporary file behind */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void SaveAppConfig_StaleTemporaryFile_IsReplacedAndRemoved()
    {
        var repo = new JsonConfigurationRepository();
        WriteRaw("stale", "settings.json.tmp");

        repo.SaveAppConfig(new AppConfig { PrimaryVolume = 3 });

        Assert.False(File.Exists(ConfigPath("settings.json.tmp")));
        Assert.Equal(3, ReadConfig(AppJsonContext.Default.AppConfig, "settings.json").PrimaryVolume);
    }

    /* SaveDictionary persists the entries for the next start */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void SaveDictionary_Entries_AreLoadedByNewInstance()
    {
        new JsonConfigurationRepository().SaveDictionary([new DictionaryEntry { Original = "brb", Spelling = "be right back", CaseSensitive = true }]);

        var entry = Assert.Single(new JsonConfigurationRepository().DictionaryEntries);

        Assert.Equal(("brb", "be right back", true), (entry.Original, entry.Spelling, entry.CaseSensitive));
    }

    /* SaveDictionary keeps its own copy of the list */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void SaveDictionary_InputListChangedLater_DoesNotAffectEntries()
    {
        var repo = new JsonConfigurationRepository();
        var entries = new List<DictionaryEntry> { new() { Original = "a", Spelling = "b" } };

        repo.SaveDictionary(entries);
        entries.Clear();

        Assert.Single(repo.DictionaryEntries);
    }

    /* SaveDictionary raises DictionaryChanged */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void SaveDictionary_Entries_RaisesDictionaryChanged()
    {
        var repo = new JsonConfigurationRepository();
        int raised = 0;
        repo.DictionaryChanged += () => raised++;

        repo.SaveDictionary([]);

        Assert.Equal(1, raised);
    }

    /* SaveProfileData replaces the profiles in memory and on disk */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void SaveProfileData_Store_IsLoadedByNewInstance()
    {
        var repo = new JsonConfigurationRepository();
        var store = TwoProfiles(active: "Work");

        repo.SaveProfileData(store);

        Assert.Same(store, repo.ProfileData);
        var reloaded = new JsonConfigurationRepository();
        Assert.Equal(["Main", "Work"], reloaded.ProfileData.Profiles.Select(p => p.Name));
        Assert.Equal("Work", reloaded.ProfileData.ActiveProfileName);
    }

    /* SaveHotkeys writes the hotkeys into the active profile's folder */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void SaveHotkeys_Entries_AreWrittenToActiveProfileFolder()
    {
        WriteConfig(TwoProfiles(active: "Work"), AppJsonContext.Default.ProfileDataStore, "audio", "hotkeys", "profiles.json");
        var repo = new JsonConfigurationRepository();

        repo.SaveHotkeys([Hotkey("work phrase")]);

        Assert.Equal("work phrase", ReadConfig(AppJsonContext.Default.ListHotkeyEntry, "audio", "hotkeys", "work_folder", "hotkeys.json").Single().Text);
        Assert.False(File.Exists(ConfigPath("audio", "hotkeys", "main_folder", "hotkeys.json")));
    }

    /* SaveHotkeys keeps its own copy of the list */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void SaveHotkeys_InputListChangedLater_DoesNotAffectHotkeys()
    {
        var repo = new JsonConfigurationRepository();
        var entries = new List<HotkeyEntry> { Hotkey("phrase") };

        repo.SaveHotkeys(entries);
        entries.Clear();

        Assert.Single(repo.Hotkeys);
    }

    /* SaveHotkeys raises HotkeysChanged */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void SaveHotkeys_Entries_RaisesHotkeysChanged()
    {
        var repo = new JsonConfigurationRepository();
        int raised = 0;
        repo.HotkeysChanged += () => raised++;

        repo.SaveHotkeys([]);

        Assert.Equal(1, raised);
    }

    /* SaveHistory persists the entries for the next start */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void SaveHistory_Entries_AreLoadedByNewInstance()
    {
        var entry = History("saved");
        new JsonConfigurationRepository().SaveHistory([entry]);

        var loaded = Assert.Single(new JsonConfigurationRepository().HistoryEntries);

        Assert.Equal((entry.Id, entry.Text, entry.CreatedAt, entry.WavFileName), (loaded.Id, loaded.Text, loaded.CreatedAt, loaded.WavFileName));
    }

    /* SaveHistory keeps its own copy of the list */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void SaveHistory_InputListChangedLater_DoesNotAffectHistory()
    {
        var repo = new JsonConfigurationRepository();
        var entries = new List<HistoryEntry> { History("a") };

        repo.SaveHistory(entries);
        entries.Clear();

        Assert.Single(repo.HistoryEntries);
    }

    /* SaveHistory raises HistoryChanged */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void SaveHistory_Entries_RaisesHistoryChanged()
    {
        var repo = new JsonConfigurationRepository();
        int raised = 0;
        repo.HistoryChanged += () => raised++;

        repo.SaveHistory([]);

        Assert.Equal(1, raised);
    }

    /* A model without a stored configuration gets the defaults */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void GetEngineConfig_NoFile_ReturnsDefaults()
    {
        var config = new JsonConfigurationRepository().GetEngineConfig(new ModelId("kokoro", "v1"));

        Assert.Equal((string.Empty, 1.0f), (config.VoiceId, config.Speed));
    }

    /* A model's configuration is read from "engine configurations/<engine>/<model>.json" */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void GetEngineConfig_FileExists_LoadsIt()
    {
        WriteConfig(new EngineConfig { VoiceId = "af_heart", Speed = 1.3f }, AppJsonContext.Default.EngineConfig, "engine configurations", "kokoro", "v1.json");

        var config = new JsonConfigurationRepository().GetEngineConfig(new ModelId("kokoro", "v1"));

        Assert.Equal(("af_heart", 1.3f), (config.VoiceId, config.Speed));
    }

    /* A corrupt engine configuration falls back to the defaults */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void GetEngineConfig_CorruptFile_ReturnsDefaults()
    {
        WriteRaw("garbage", "engine configurations", "kokoro", "v1.json");

        var config = new JsonConfigurationRepository().GetEngineConfig(new ModelId("kokoro", "v1"));

        Assert.Equal(string.Empty, config.VoiceId);
    }

    /* Repeated lookups of a model return the same cached instance */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void GetEngineConfig_CalledTwice_ReturnsCachedInstance()
    {
        var repo = new JsonConfigurationRepository();
        var model = new ModelId("kokoro", "v1");

        Assert.Same(repo.GetEngineConfig(model), repo.GetEngineConfig(model));
    }

    /* SaveEngineConfig writes "engine configurations/<engine>/<model>.json" */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void SaveEngineConfig_Config_WritesPerModelFile()
    {
        new JsonConfigurationRepository().SaveEngineConfig(new ModelId("kokoro", "v1"), new EngineConfig { VoiceId = "bm_george", Speed = 0.8f });

        var stored = ReadConfig(AppJsonContext.Default.EngineConfig, "engine configurations", "kokoro", "v1.json");

        Assert.Equal(("bm_george", 0.8f), (stored.VoiceId, stored.Speed));
    }

    /* The saved configuration is what GetEngineConfig returns afterwards */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void SaveEngineConfig_Config_IsReturnedByGetEngineConfig()
    {
        var repo = new JsonConfigurationRepository();
        var model = new ModelId("kokoro", "v1");
        var config = new EngineConfig { VoiceId = "x" };

        repo.SaveEngineConfig(model, config);

        Assert.Same(config, repo.GetEngineConfig(model));
    }

    /* Each model keeps its own configuration */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void SaveEngineConfig_TwoModels_AreKeptSeparately()
    {
        new JsonConfigurationRepository().SaveEngineConfig(new ModelId("kokoro", "v1"), new EngineConfig { VoiceId = "one" });
        var repo = new JsonConfigurationRepository();

        repo.SaveEngineConfig(new ModelId("kokoro", "v2"), new EngineConfig { VoiceId = "two" });

        var reloaded = new JsonConfigurationRepository();
        Assert.Equal("one", reloaded.GetEngineConfig(new ModelId("kokoro", "v1")).VoiceId);
        Assert.Equal("two", reloaded.GetEngineConfig(new ModelId("kokoro", "v2")).VoiceId);
    }

    /* SaveEngineConfig raises EngineConfigChanged */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void SaveEngineConfig_Config_RaisesEngineConfigChanged()
    {
        var repo = new JsonConfigurationRepository();
        int raised = 0;
        repo.EngineConfigChanged += () => raised++;

        repo.SaveEngineConfig(new ModelId("kokoro", "v1"), new EngineConfig());

        Assert.Equal(1, raised);
    }

    /* Engine and model names are made safe for the file system (no nested folders, no reserved characters) */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void SaveEngineConfig_NamesWithReservedCharacters_AreSanitized()
    {
        new JsonConfigurationRepository().SaveEngineConfig(new ModelId("my:engine", "voices/en"), new EngineConfig { VoiceId = "safe" });

        Assert.Equal("safe", ReadConfig(AppJsonContext.Default.EngineConfig, "engine configurations", "my_engine", "voices_en.json").VoiceId);
    }

    /* The Unselected model is stored as "engine configurations/unselected/unselected.json" */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void SaveEngineConfig_Unselected_WritesUnselectedFile()
    {
        new JsonConfigurationRepository().SaveEngineConfig(ModelId.Unselected, new EngineConfig { VoiceId = "none" });

        Assert.Equal("none", ReadConfig(AppJsonContext.Default.EngineConfig, "engine configurations", "unselected", "unselected.json").VoiceId);
    }

    /* SetActiveProfile changes the active profile name */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void SetActiveProfile_Name_UpdatesActiveProfileName()
    {
        WriteConfig(TwoProfiles(), AppJsonContext.Default.ProfileDataStore, "audio", "hotkeys", "profiles.json");
        var repo = new JsonConfigurationRepository();

        repo.SetActiveProfile("Work");

        Assert.Equal("Work", repo.ProfileData.ActiveProfileName);
    }

    /* SetActiveProfile swaps the in-memory hotkeys to the new profile's folder */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void SetActiveProfile_Name_LoadsHotkeysOfThatProfile()
    {
        WriteConfig(TwoProfiles(), AppJsonContext.Default.ProfileDataStore, "audio", "hotkeys", "profiles.json");
        WriteConfig(new List<HotkeyEntry> { Hotkey("main phrase") }, AppJsonContext.Default.ListHotkeyEntry, "audio", "hotkeys", "main_folder", "hotkeys.json");
        WriteConfig(new List<HotkeyEntry> { Hotkey("work phrase") }, AppJsonContext.Default.ListHotkeyEntry, "audio", "hotkeys", "work_folder", "hotkeys.json");
        var repo = new JsonConfigurationRepository();

        repo.SetActiveProfile("Work");

        Assert.Equal("work phrase", Assert.Single(repo.Hotkeys).Text);
    }

    /* Switching to a profile without hotkeys.json leaves no hotkeys */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void SetActiveProfile_ProfileWithoutHotkeysFile_HasNoHotkeys()
    {
        WriteConfig(TwoProfiles(), AppJsonContext.Default.ProfileDataStore, "audio", "hotkeys", "profiles.json");
        WriteConfig(new List<HotkeyEntry> { Hotkey("main phrase") }, AppJsonContext.Default.ListHotkeyEntry, "audio", "hotkeys", "main_folder", "hotkeys.json");
        var repo = new JsonConfigurationRepository();

        repo.SetActiveProfile("Work");

        Assert.Empty(repo.Hotkeys);
    }

    /* SetActiveProfile raises ProfilesChanged and HotkeysChanged */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void SetActiveProfile_Name_RaisesProfilesAndHotkeysChanged()
    {
        WriteConfig(TwoProfiles(), AppJsonContext.Default.ProfileDataStore, "audio", "hotkeys", "profiles.json");
        var repo = new JsonConfigurationRepository();
        var raised = new List<string>();
        repo.ProfilesChanged += () => raised.Add("profiles");
        repo.HotkeysChanged += () => raised.Add("hotkeys");

        repo.SetActiveProfile("Work");

        Assert.Equal(["profiles", "hotkeys"], raised);
    }

    /* The newly active profile is remembered across restarts */
    [Fact(Skip = PosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void SetActiveProfile_Name_IsRememberedByNewInstance()
    {
        WriteConfig(TwoProfiles(), AppJsonContext.Default.ProfileDataStore, "audio", "hotkeys", "profiles.json");
        new JsonConfigurationRepository().SetActiveProfile("Work");

        var reloaded = new JsonConfigurationRepository();

        Assert.Equal("Work", reloaded.ProfileData.ActiveProfileName);
    }
}
