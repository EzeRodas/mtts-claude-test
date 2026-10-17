using MoonTTS.Domain.Entities;
using MoonTTS.Domain.Interfaces;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.Tests.Common;

/* File Structure:
        - LoadAll() : void
        - SaveAppConfig(AppConfig config) : void
        - GetEngineConfig(ModelId model) : EngineConfig
        - SaveEngineConfig(ModelId model, EngineConfig config) : void
        - SaveDictionary(List<DictionaryEntry> entries) : void
        - SaveProfileData(ProfileDataStore profile) : void
        - SetActiveProfile(string profileName) : void
        - SaveHotkeys(List<HotkeyEntry> entries) : void
        - SaveHistory(List<HistoryEntry> entries) : void
*/

/* In-memory IConfigurationRepository that records how often each part was saved */
public sealed class FakeConfigurationRepository : IConfigurationRepository
{
    private readonly Dictionary<ModelId, EngineConfig> _engineConfigs = new();

    public AppConfig AppConfig { get; private set; } = new();
    public IReadOnlyList<DictionaryEntry> DictionaryEntries { get; private set; } = new List<DictionaryEntry>();
    public ProfileDataStore ProfileData { get; private set; } = new();
    public IReadOnlyList<HotkeyEntry> Hotkeys { get; private set; } = new List<HotkeyEntry>();
    public IReadOnlyList<HistoryEntry> HistoryEntries { get; private set; } = new List<HistoryEntry>();

    public int LoadAllCount { get; private set; }
    public int AppConfigSaveCount { get; private set; }
    public int EngineConfigSaveCount { get; private set; }
    public int DictionarySaveCount { get; private set; }
    public int ProfileSaveCount { get; private set; }
    public int HotkeysSaveCount { get; private set; }
    public int HistorySaveCount { get; private set; }
    public string? LastActiveProfile { get; private set; }

    public event Action AppConfigChanged = delegate { };
    public event Action? EngineConfigChanged;
    public event Action? DictionaryChanged;
    public event Action? ProfilesChanged;
    public event Action? HotkeysChanged;
    public event Action? HistoryChanged;

    /* Counts reloads; the in-memory state is already current */
    public void LoadAll() => LoadAllCount++;

    /* Stores the app config and raises AppConfigChanged */
    public void SaveAppConfig(AppConfig config)
    {
        AppConfig = config;
        AppConfigSaveCount++;
        AppConfigChanged();
    }

    /* Returns (creating on first use) the preferences of a model */
    public EngineConfig GetEngineConfig(ModelId model)
    {
        if (!_engineConfigs.TryGetValue(model, out var config))
        {
            config = new EngineConfig();
            _engineConfigs[model] = config;
        }
        return config;
    }

    /* Stores the preferences of a model and raises EngineConfigChanged */
    public void SaveEngineConfig(ModelId model, EngineConfig config)
    {
        _engineConfigs[model] = config;
        EngineConfigSaveCount++;
        EngineConfigChanged?.Invoke();
    }

    /* Stores the dictionary and raises DictionaryChanged */
    public void SaveDictionary(List<DictionaryEntry> entries)
    {
        DictionaryEntries = entries.ToList();
        DictionarySaveCount++;
        DictionaryChanged?.Invoke();
    }

    /* Stores the profiles and raises ProfilesChanged */
    public void SaveProfileData(ProfileDataStore profile)
    {
        ProfileData = profile;
        ProfileSaveCount++;
        ProfilesChanged?.Invoke();
    }

    /* Records the active profile and raises ProfilesChanged */
    public void SetActiveProfile(string profileName)
    {
        LastActiveProfile = profileName;
        ProfileData.ActiveProfileName = profileName;
        ProfilesChanged?.Invoke();
    }

    /* Stores the hotkeys and raises HotkeysChanged */
    public void SaveHotkeys(List<HotkeyEntry> entries)
    {
        Hotkeys = entries.ToList();
        HotkeysSaveCount++;
        HotkeysChanged?.Invoke();
    }

    /* Stores the history and raises HistoryChanged */
    public void SaveHistory(List<HistoryEntry> entries)
    {
        HistoryEntries = entries.ToList();
        HistorySaveCount++;
        HistoryChanged?.Invoke();
    }
}
