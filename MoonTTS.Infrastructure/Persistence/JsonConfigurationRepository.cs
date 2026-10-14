using System.Collections.Concurrent;
using System.Text.Json;
using MoonTTS.Core.Utilities;
using MoonTTS.Domain.Entities;
using MoonTTS.Domain.Enums;
using MoonTTS.Domain.Interfaces;

namespace MoonTTS.Infrastructure.Persistence;

/* File Structure:
        - JsonConfigurationRepository()
        - LoadAll() : void
        - SaveAppConfig(AppConfig config) : void
        - SaveDictionary(List<DictionaryEntry> entries) : void
        - SaveProfileData(ProfileDataStore profile) : void
        - SaveHotkeys(List<HotkeyEntry> entries) : void
        - SaveHistory(List<HistoryEntry> entires) : void
        - GetEngineConfig<T>(TtsModelType engine) : T
        - SaveEngineConfig<T>(TtsModelType engine, T config) : void
        - LoadInternal<T>(string relativePath, JsonTypeInfo<T> typeInfo) : T?
        - SaveInternal<T>(string relativePath, T data, JsonTypeInfo<T> typeInfo) : void
        - GetActiveProfileFolder() : string
        - SetActiveProfile(string profileName) : void
*/

public class JsonConfigurationRepository : IConfigurationRepository
{
    private readonly string _configDirectory;
    private readonly Lock _processLock = new();

    public AppConfig AppConfig { get; private set; } = new();
    public IReadOnlyList<DictionaryEntry> DictionaryEntries { get; private set; } =  new List<DictionaryEntry>();
    public ProfileDataStore ProfileData { get; private set; } = new();
    public IReadOnlyList<HotkeyEntry> Hotkeys { get; private set; } = new List<HotkeyEntry>();
    public IReadOnlyList<HistoryEntry> HistoryEntries { get; private set; } = new List<HistoryEntry>();

    private readonly ConcurrentDictionary<TtsModelType, IEngineConfig> _engineConfigs = new();
    
    public event Action? AppConfigChanged;
    public event Action? EngineConfigChanged;
    public event Action? DictionaryChanged;
    public event Action? ProfilesChanged;
    public event Action? HotkeysChanged;
    public event Action? HistoryChanged;

    public JsonConfigurationRepository()
    {
        _configDirectory = PathResolver.GetConfigDirectory();
        Directory.CreateDirectory(_configDirectory);
        LoadAll();
    }

    public void LoadAll()
    {
        _engineConfigs.Clear();
        AppConfig = LoadInternal("settings.json", AppJsonContext.Default.AppConfig) ?? new AppConfig();
        DictionaryEntries = LoadInternal("dictionary.json", AppJsonContext.Default.ListDictionaryEntry) 
                            ?? new List<DictionaryEntry>();
        
        string profilesPath = Path.Combine("audio", "hotkeys", "profiles.json");
        ProfileData = LoadInternal(profilesPath, AppJsonContext.Default.ProfileDataStore) 
                            ?? new ProfileDataStore();

        if (ProfileData.Profiles == null || ProfileData.Profiles.Count == 0)
        {
            ProfileData.Profiles = new List<HotkeyProfile>
            {
                new HotkeyProfile
                {
                    Name = "Default",
                    FolderName = "Default"
                }
            };
            ProfileData.ActiveProfileName = "Default";
            SaveProfileData(ProfileData);
        }
        
        string activeFolder = GetActiveProfileFolder();
        string hotkeysPath = Path.Combine("audio", "hotkeys", activeFolder, "hotkeys.json");
        Hotkeys = LoadInternal(hotkeysPath, AppJsonContext.Default.ListHotkeyEntry) 
                      ?? new List<HotkeyEntry>();

        string historyPath = Path.Combine("audio", "history", "history.json");
        HistoryEntries = LoadInternal(historyPath, AppJsonContext.Default.ListHistoryEntry) ?? new List<HistoryEntry>();
    }

    public void SaveAppConfig(AppConfig config)
    {
        AppConfig = config;
        SaveInternal("settings.json", config, AppJsonContext.Default.AppConfig);
        AppConfigChanged?.Invoke();
    }

    public void SaveDictionary(List<DictionaryEntry> entries)
    {
        List<DictionaryEntry> entriesList = entries.ToList();
        DictionaryEntries = entriesList;
        SaveInternal("dictionary.json", entriesList, AppJsonContext.Default.ListDictionaryEntry);
        DictionaryChanged?.Invoke();
    }

    public void SaveProfileData(ProfileDataStore profile)
    {
        ProfileData = profile;
        string path = Path.Combine("audio", "hotkeys", "profiles.json");
        SaveInternal(path, ProfileData, AppJsonContext.Default.ProfileDataStore);
        
    }

    public void SaveHotkeys(List<HotkeyEntry> entries)
    {
        List<HotkeyEntry> entriesList =  entries.ToList();
        Hotkeys = entriesList;

        /* Saves to the currently active subfolder */
        string activeFolder = GetActiveProfileFolder();
        string path = Path.Combine("audio", "hotkeys", activeFolder, "hotkeys.json");

        SaveInternal(path, entriesList, AppJsonContext.Default.ListHotkeyEntry);
        HotkeysChanged?.Invoke();
    }

    public void SaveHistory(List<HistoryEntry> entires)
    {
        List<HistoryEntry> entriesList = entires.ToList();
        HistoryEntries = entriesList;

        string path = Path.Combine("audio", "history", "history.json");
        SaveInternal(path, entriesList, AppJsonContext.Default.ListHistoryEntry);
        
        HistoryChanged?.Invoke();
    }

    public T GetEngineConfig<T>(TtsModelType engine) where T : IEngineConfig, new()
    {
        return (T)_engineConfigs.GetOrAdd(engine, e =>
        {
            string path = Path.Combine("engine configurations", $"{e}.json");
            
            /* Explicit type check to route to correct source gen metadata */
            if (typeof(T) == typeof(KokoroEngineConfig))
            {
                return (T)(object)(LoadInternal(path, AppJsonContext.Default.KokoroEngineConfig) ?? new KokoroEngineConfig());
            }

            return new T();
        });
    }

    public void SaveEngineConfig<T>(TtsModelType engine, T config) where T : IEngineConfig
    {
        _engineConfigs[engine] = config;
        string path = Path.Combine("engine configurations", $"{engine}.json");
        
        if (typeof(T) == typeof(KokoroEngineConfig))
        {
            SaveInternal(path, (KokoroEngineConfig)(object)config, AppJsonContext.Default.KokoroEngineConfig);
        }
        EngineConfigChanged?.Invoke();
    }

    private T? LoadInternal<T>(string relativePath, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        string fullPath = Path.Combine(_configDirectory, relativePath);
        if (!File.Exists(fullPath)) return default;
        try
        {
            lock (_processLock)
            {
                using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return JsonSerializer.Deserialize(fs, typeInfo);
            }
        }
        catch
        {
            return default;
        }
    }

    private void SaveInternal<T>(string relativePath, T data, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        string fullPath = Path.Combine(_configDirectory, relativePath);
        string tempPath = fullPath + ".tmp";
        
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        lock (_processLock)
        {
            using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(fs, data, typeInfo);
            }
            File.Move(tempPath, fullPath, overwrite:true);
        }
    }

    private string GetActiveProfileFolder()
    {
        /* Ifno profiles available returns default */
        if (ProfileData.Profiles == null || ProfileData.Profiles.Count == 0)
            return "Default";

        /* Retrieves the profile with the matching name */
        var activeProfile = ProfileData.Profiles.FirstOrDefault(p => p.Name == ProfileData.ActiveProfileName);

        /* Returns it folder name, or fallbacks to the first profile's folder. */
        return activeProfile?.FolderName ?? ProfileData.Profiles[0].FolderName;
    }

    public void SetActiveProfile(string profileName)
    {
        ProfileData.ActiveProfileName = profileName;
        
        /* Saves the profiles.json to remember the newly active profile */
        string profilesPath = Path.Combine("audio", "profiles", "profiles.json");
        SaveInternal(profilesPath, ProfileData, AppJsonContext.Default.ProfileDataStore);
        ProfilesChanged?.Invoke();
        
        /* Swaps the hotkeys in memory to match the new profile */
        string activeFolder = GetActiveProfileFolder();
        string hotkeysPath = Path.Combine("audio", "hotkeys", activeFolder, "hotkeys.json");
        Hotkeys = LoadInternal(hotkeysPath, AppJsonContext.Default.ListHotkeyEntry) ?? new List<HotkeyEntry>();
        
        HotkeysChanged?.Invoke();
    }
}