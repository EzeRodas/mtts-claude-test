using MoonTTS.Avalonia.ViewModels;
using MoonTTS.Core.Services;
using MoonTTS.Core.Utilities;
using MoonTTS.Domain.Audio;
using MoonTTS.Domain.Entities;
using MoonTTS.Domain.Interfaces;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.Avalonia.Tests.TestSupport;

/* File Structure:
        - GuiServices(IModelDownloader? downloader)
        - Config : AppConfig
        - AddModel(string engineId, string modelName, bool installed, params VoiceInfo[] voices) : ModelMetadata
        - Install(ModelMetadata model) : void
        - CreateViewModel() : SettingsViewModel
        - AddHistoryEntryAsync(string text, byte[] pcm, AudioFormat format) : Task<HistoryEntry>
        - HotkeyWavPath(Guid entryId) : string
        - Dispose() : void
*/

/* The real Core services the GUI receives in production (SettingsManager, DownloadManager, TtsEngineProvider,
   DictionaryManager, HotkeyManager, HistoryManager), built over the shared fakes so every OS and engine boundary
   answers from memory. Models live in a private temp folder (AppConfig.CustomModelsDirectory) */
public sealed class GuiServices : IDisposable
{
    /* Hotkey audio folder used by this instance, unique so parallel tests never share WAV files */
    private readonly string _profileFolder = "profile-" + Guid.NewGuid().ToString("N");

    public TempDirectory Temp { get; } = new();
    public FakeConfigurationRepository Repo { get; } = new();
    public FakeAudioPlayer Player { get; } = new();
    public FakeModelRegistry Registry { get; } = new();
    public FakeModelDownloader Downloader { get; } = new();
    public FakeTtsEngineFactory Factory { get; } = new();
    public FakeSpeechPipeline Pipeline { get; } = new();
    public FakeWindowService Windows { get; } = new();

    /* Engine instance handed out by the factory for each registered model */
    public Dictionary<ModelId, FakeTtsEngine> Engines { get; } = new();

    public SettingsManager Settings { get; }
    public DownloadManager Downloads { get; }
    public TtsEngineProvider Provider { get; }
    public DictionaryManager Dictionary { get; }
    public HotkeyManager Hotkeys { get; }
    public HistoryManager History { get; }

    /* Seeds a config whose models folder is private to this instance and a default hotkey profile, then builds
       the services. `downloader` replaces the default FakeModelDownloader */
    public GuiServices(IModelDownloader? downloader = null)
    {
        Repo.SaveAppConfig(new AppConfig { CustomModelsDirectory = Temp.CreateSubdirectory("models") });
        Repo.SaveProfileData(new ProfileDataStore
        {
            ActiveProfileName = "Default",
            Profiles = [new HotkeyProfile { Name = "Default", FolderName = _profileFolder }]
        });

        Settings = new SettingsManager(Repo);
        Downloads = new DownloadManager(Registry, downloader ?? Downloader, Settings);
        Provider = new TtsEngineProvider(Factory, Settings, Downloads, Registry);
        Dictionary = new DictionaryManager(Repo);
        Hotkeys = new HotkeyManager(Pipeline, Player, Repo);
        History = new HistoryManager(Player, Repo);
    }

    /* The live app config (mutating it does not count as a save) */
    public AppConfig Config => Repo.AppConfig;

    /* Registers a model with one download file and its own engine offering `voices`; `installed` writes the file */
    public ModelMetadata AddModel(string engineId, string modelName, bool installed, params VoiceInfo[] voices)
    {
        var model = Registry.AddModel(engineId, modelName, $"{modelName}.onnx");
        var engine = new FakeTtsEngine();
        engine.Voices.AddRange(voices);
        Engines[model.Id] = engine;
        Factory.Engines[model.Id] = () => engine;

        if (installed)
            Install(model);
        return model;
    }

    /* Writes every download file of the model where DownloadManager looks for it */
    public void Install(ModelMetadata model)
    {
        string directory = Settings.GetModelDirectory(model.Id);
        Directory.CreateDirectory(directory);
        foreach (string file in model.DownloadFiles.Keys)
            File.WriteAllText(Path.Combine(directory, file), "dummy model");
    }

    /* Builds the view model exactly as WindowService does */
    public SettingsViewModel CreateViewModel() =>
        new(Settings, Player, Windows, Downloads, Dictionary, Hotkeys, History, Provider);

    /* Records a history entry with a real WAV file (written in the isolated config folder) */
    public async Task<HistoryEntry> AddHistoryEntryAsync(string text, byte[] pcm, AudioFormat format)
    {
        await History.AddEntry(text, WavWriter.CreateWav(pcm, format));
        return History.GetHistory()[0];
    }

    /* Where HotkeyManager stores the pre-rendered audio of a phrase */
    public string HotkeyWavPath(Guid entryId) =>
        Path.Combine(PathResolver.GetConfigDirectory(), "audio", "hotkeys", _profileFolder, $"{entryId}.wav");

    /* Shuts down the engine provider and removes the temp folder */
    public void Dispose()
    {
        Provider.Dispose();
        Temp.Dispose();
    }
}
