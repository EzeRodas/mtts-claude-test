using MoonTTS.Domain.Entities;
using MoonTTS.Domain.Enums;

namespace MoonTTS.Domain.Interfaces;

public interface IConfigurationRepository
{
    void LoadAll();
    
    /* App Config */
    AppConfig AppConfig { get; }
    void SaveAppConfig(AppConfig config);
    event Action AppConfigChanged;
    
    /* Engine Config */
    T GetEngineConfig<T>(TtsModelType engine) where T : IEngineConfig, new();
    void SaveEngineConfig<T>(TtsModelType engine, T config) where T : IEngineConfig;
    event Action? EngineConfigChanged;
    
    /* Dictionary */
    IReadOnlyList<DictionaryEntry> DictionaryEntries { get; }
    void SaveDictionary(List<DictionaryEntry> entries);
    event Action? DictionaryChanged;
    
    /* Profiles */
    ProfileDataStore ProfileData { get; }
    void SaveProfileData(ProfileDataStore profile);
    void SetActiveProfile(string profileName);
    event Action? ProfilesChanged;
    
    /* Hotkeys */
    IReadOnlyList<HotkeyEntry>  Hotkeys { get; }
    void SaveHotkeys(List<HotkeyEntry> entries);
    event Action? HotkeysChanged;
    
    /* History */
    IReadOnlyList<HistoryEntry> HistoryEntries { get; }
    void SaveHistory(List<HistoryEntry> entires);
    event Action? HistoryChanged;
}