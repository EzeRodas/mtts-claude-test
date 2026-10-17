using MoonTTS.Core.Services;
using MoonTTS.Domain.Entities;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.Core.Tests.Services;

/* Tests for SettingsManager. The default models folder resolves through PathResolver, so user folders are isolated */
[Collection(TestCollections.Environment)]
public sealed class SettingsManagerTests : IDisposable
{
    private static readonly ModelId Kokoro = new("kokoro", "v1");

    private readonly UserFolderSandbox _sandbox = new();
    private readonly FakeConfigurationRepository _repo = new();

    /* Restores the environment */
    public void Dispose() => _sandbox.Dispose();

    /* CurrentAppConfig always reflects the repository's current config */
    [Fact]
    public void CurrentAppConfig_AfterRepositoryChange_ReturnsRepositoryConfig()
    {
        var settings = new SettingsManager(_repo);
        var config = new AppConfig { PrimaryVolume = 42 };

        _repo.SaveAppConfig(config);

        Assert.Same(config, settings.CurrentAppConfig);
    }

    /* A repository app-config change is forwarded as SettingsChanged */
    [Fact]
    public void Constructor_RepositoryAppConfigChanged_RaisesSettingsChanged()
    {
        var settings = new SettingsManager(_repo);
        int raised = 0;
        settings.SettingsChanged += () => raised++;

        _repo.SaveAppConfig(new AppConfig());

        Assert.Equal(1, raised);
    }

    /* A repository engine-config change is forwarded as SettingsChanged */
    [Fact]
    public void Constructor_RepositoryEngineConfigChanged_RaisesSettingsChanged()
    {
        var settings = new SettingsManager(_repo);
        int raised = 0;
        settings.SettingsChanged += () => raised++;

        _repo.SaveEngineConfig(Kokoro, new EngineConfig());

        Assert.Equal(1, raised);
    }

    /* Engine configs are read from the repository */
    [Fact]
    public void GetEngineConfig_KnownModel_ReturnsRepositoryConfig()
    {
        var config = new EngineConfig { VoiceId = "af_heart", Speed = 1.2f };
        _repo.SaveEngineConfig(Kokoro, config);
        var settings = new SettingsManager(_repo);

        var result = settings.GetEngineConfig(Kokoro);

        Assert.Same(config, result);
    }

    /* Saving the app config persists it and succeeds */
    [Fact]
    public void SaveAppConfig_NewConfig_PersistsAndSucceeds()
    {
        var settings = new SettingsManager(_repo);
        var config = new AppConfig { SaveHistory = false };

        var result = settings.SaveAppConfig(config);

        Assert.True(result.IsSuccess);
        Assert.Same(config, _repo.AppConfig);
        Assert.Equal(1, _repo.AppConfigSaveCount);
    }

    /* Saving the app config notifies SettingsChanged subscribers */
    [Fact]
    public void SaveAppConfig_NewConfig_RaisesSettingsChanged()
    {
        var settings = new SettingsManager(_repo);
        int raised = 0;
        settings.SettingsChanged += () => raised++;

        settings.SaveAppConfig(new AppConfig());

        Assert.Equal(1, raised);
    }

    /* Saving an engine config persists it for that model and succeeds */
    [Fact]
    public void SaveEngineConfig_NewConfig_PersistsAndSucceeds()
    {
        var settings = new SettingsManager(_repo);
        var config = new EngineConfig { Speed = 0.8f };

        var result = settings.SaveEngineConfig(Kokoro, config);

        Assert.True(result.IsSuccess);
        Assert.Same(config, _repo.GetEngineConfig(Kokoro));
        Assert.Equal(1, _repo.EngineConfigSaveCount);
    }

    /* Saving an engine config notifies SettingsChanged subscribers */
    [Fact]
    public void SaveEngineConfig_NewConfig_RaisesSettingsChanged()
    {
        var settings = new SettingsManager(_repo);
        int raised = 0;
        settings.SettingsChanged += () => raised++;

        settings.SaveEngineConfig(Kokoro, new EngineConfig());

        Assert.Equal(1, raised);
    }

    /* A custom models folder overrides the default one */
    [Fact]
    public void GetModelsDirectory_CustomDirectory_ReturnsCustomDirectory()
    {
        string custom = _sandbox.Temp.Combine("my-models");
        _repo.SaveAppConfig(new AppConfig { CustomModelsDirectory = custom });
        var settings = new SettingsManager(_repo);

        string result = settings.GetModelsDirectory();

        Assert.Equal(custom, result);
    }

    /* Without a custom folder the OS default from PathResolver is used */
    [Fact]
    public void GetModelsDirectory_NoCustomDirectory_ReturnsDefaultDirectory()
    {
        var settings = new SettingsManager(_repo);

        string result = settings.GetModelsDirectory();

        Assert.Equal(Core.Utilities.PathResolver.GetModelsDirectory(), result);
        Assert.StartsWith(_sandbox.Temp.Path, result);
    }

    /* A whitespace-only custom folder is treated as unset */
    [Fact]
    public void GetModelsDirectory_WhitespaceCustomDirectory_ReturnsDefaultDirectory()
    {
        _repo.SaveAppConfig(new AppConfig { CustomModelsDirectory = "   " });
        var settings = new SettingsManager(_repo);

        string result = settings.GetModelsDirectory();

        Assert.Equal(Core.Utilities.PathResolver.GetModelsDirectory(), result);
    }

    /* Each engine gets its own sub folder of the models folder */
    [Fact]
    public void GetModelDirectory_Model_ReturnsEngineSubfolder()
    {
        string custom = _sandbox.Temp.Combine("models");
        _repo.SaveAppConfig(new AppConfig { CustomModelsDirectory = custom });
        var settings = new SettingsManager(_repo);

        string result = settings.GetModelDirectory(Kokoro);

        Assert.Equal(Path.Combine(custom, "kokoro"), result);
    }

    /* Models of the same engine share the engine folder */
    [Fact]
    public void GetModelDirectory_TwoModelsOfSameEngine_ShareFolder()
    {
        _repo.SaveAppConfig(new AppConfig { CustomModelsDirectory = _sandbox.Temp.Combine("models") });
        var settings = new SettingsManager(_repo);

        Assert.Equal(settings.GetModelDirectory(new ModelId("piper", "a")), settings.GetModelDirectory(new ModelId("piper", "b")));
    }

    /* The engine id is sanitized before becoming a folder name */
    [Fact]
    public void GetModelDirectory_EngineIdWithInvalidCharacters_SanitizesFolderName()
    {
        string custom = _sandbox.Temp.Combine("models");
        _repo.SaveAppConfig(new AppConfig { CustomModelsDirectory = custom });
        var settings = new SettingsManager(_repo);

        string result = settings.GetModelDirectory(new ModelId("my:engine", "v1"));

        Assert.Equal(Path.Combine(custom, "my_engine"), result);
    }

    /* Reloading asks the repository to load everything again */
    [Fact]
    public void ReloadAppConfig_Always_ReloadsRepository()
    {
        var settings = new SettingsManager(_repo);

        settings.ReloadAppConfig();

        Assert.Equal(1, _repo.LoadAllCount);
    }

    /* Reloading notifies SettingsChanged subscribers */
    [Fact]
    public void ReloadAppConfig_Always_RaisesSettingsChanged()
    {
        var settings = new SettingsManager(_repo);
        int raised = 0;
        settings.SettingsChanged += () => raised++;

        settings.ReloadAppConfig();

        Assert.Equal(1, raised);
    }
}
