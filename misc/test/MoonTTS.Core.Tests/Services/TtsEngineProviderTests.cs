using MoonTTS.Core.Helpers;
using MoonTTS.Core.Services;
using MoonTTS.Core.Tests.Doubles;
using MoonTTS.Domain.Entities;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.Core.Tests.Services;

/* Tests for TtsEngineProvider. A custom models folder inside a TempDirectory keeps user folders untouched */
public sealed class TtsEngineProviderTests : IDisposable
{
    private static readonly ModelId Kokoro = new("kokoro", "v1");
    private static readonly ModelId Piper = new("piper", "amy");

    private readonly TempDirectory _temp = new();
    private readonly FakeConfigurationRepository _repo = new();
    private readonly FakeTtsEngineFactory _factory = new();
    private readonly FakeModelRegistry _registry = new();
    private readonly SettingsManager _settings;
    private readonly DownloadManager _downloads;

    /* Points the models folder at the temp directory */
    public TtsEngineProviderTests()
    {
        _repo.SaveAppConfig(new AppConfig { CustomModelsDirectory = _temp.Combine("models") });
        _settings = new SettingsManager(_repo);
        _downloads = new DownloadManager(_registry, new FakeModelDownloader(), _settings);
    }

    /* Removes the temp folder */
    public void Dispose() => _temp.Dispose();

    /* Registers an available engine for the model and returns it */
    private FakeTtsEngine RegisterEngine(ModelId model)
    {
        var engine = new FakeTtsEngine();
        _factory.Engines[model] = () => engine;
        return engine;
    }

    /* Registers the model in the registry and writes its single file so it counts as downloaded */
    private void RegisterDownloadedModel(ModelId model)
    {
        _registry.AddModel(model.EngineId, model.ModelName, "model.onnx");
        _temp.WriteFile(Path.Combine("models", model.EngineId, "model.onnx"), "data");
    }

    /* Selects the model in the app config */
    private void SelectInSettings(ModelId model)
    {
        var config = _repo.AppConfig;
        config.ActiveModel = model;
        _repo.SaveAppConfig(config);
    }

    /* Builds the provider without download/registry validation */
    private TtsEngineProvider CreateProvider() => new(_factory, _settings);

    /* Builds the provider with download/registry validation */
    private TtsEngineProvider CreateValidatingProvider() => new(_factory, _settings, _downloads, _registry);

    /* Before anything is loaded the current engine is an empty, unavailable engine */
    [Fact]
    public void CurrentEngine_NothingLoaded_IsEmptyEngine()
    {
        using var provider = CreateProvider();

        Assert.IsType<EmptyTtsEngine>(provider.CurrentEngine);
    }

    /* Before anything is loaded no model is current */
    [Fact]
    public void CurrentModel_NothingLoaded_IsUnselected()
    {
        using var provider = CreateProvider();

        Assert.True(provider.CurrentModel.IsUnselected);
    }

    /* The first request loads the model selected in settings */
    [Fact]
    public async Task GetEngineAsync_FirstCall_LoadsModelFromSettings()
    {
        var engine = RegisterEngine(Kokoro);
        SelectInSettings(Kokoro);
        using var provider = CreateProvider();

        var result = await provider.GetEngineAsync(TestContext.Current.CancellationToken);

        Assert.Same(engine, result);
        Assert.Equal(Kokoro, provider.CurrentModel);
        Assert.Equal([Kokoro], _factory.CreatedModels);
    }

    /* Later requests reuse the loaded engine instead of creating another */
    [Fact]
    public async Task GetEngineAsync_SecondCall_ReusesLoadedEngine()
    {
        RegisterEngine(Kokoro);
        SelectInSettings(Kokoro);
        using var provider = CreateProvider();

        var first = await provider.GetEngineAsync(TestContext.Current.CancellationToken);
        var second = await provider.GetEngineAsync(TestContext.Current.CancellationToken);

        Assert.Same(first, second);
        Assert.Single(_factory.CreatedModels);
    }

    /* With no model selected the factory's (unavailable) placeholder engine is accepted */
    [Fact]
    public async Task GetEngineAsync_NoModelSelected_ReturnsUnavailableEngine()
    {
        using var provider = CreateProvider();

        var engine = await provider.GetEngineAsync(TestContext.Current.CancellationToken);

        Assert.False(engine.IsAvailable);
        Assert.Equal([ModelId.Unselected], _factory.CreatedModels);
    }

    /* A failed load leaves an empty engine in place */
    [Fact]
    public async Task GetEngineAsync_FactoryFails_ReturnsEmptyEngine()
    {
        _factory.Failures[Kokoro] = "worker crashed";
        SelectInSettings(Kokoro);
        using var provider = CreateProvider();

        var engine = await provider.GetEngineAsync(TestContext.Current.CancellationToken);

        Assert.IsType<EmptyTtsEngine>(engine);
        Assert.True(provider.CurrentModel.IsUnselected);
    }

    /* After a failed load the empty engine is kept, so the factory is not retried on every request */
    [Fact]
    public async Task GetEngineAsync_AfterFailedLoad_DoesNotRetry()
    {
        _factory.Failures[Kokoro] = "worker crashed";
        SelectInSettings(Kokoro);
        using var provider = CreateProvider();

        await provider.GetEngineAsync(TestContext.Current.CancellationToken);
        await provider.GetEngineAsync(TestContext.Current.CancellationToken);

        Assert.Single(_factory.CreatedModels);
    }

    /* A failed lazy load does not announce an engine change */
    [Fact]
    public async Task GetEngineAsync_FactoryFails_DoesNotRaiseOnEngineChanged()
    {
        _factory.Failures[Kokoro] = "worker crashed";
        SelectInSettings(Kokoro);
        using var provider = CreateProvider();
        int raised = 0;
        provider.OnEngineChanged += () => raised++;

        await provider.GetEngineAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, raised);
    }

    /* Concurrent first requests share a single load */
    [Fact]
    public async Task GetEngineAsync_ConcurrentFirstCalls_LoadOnce()
    {
        var factory = new GatedTtsEngineFactory();
        SelectInSettings(Kokoro);
        using var provider = new TtsEngineProvider(factory, _settings);

        var first = provider.GetEngineAsync(TestContext.Current.CancellationToken);
        await factory.WaitForCallAsync(TestContext.Current.CancellationToken);
        var second = provider.GetEngineAsync(TestContext.Current.CancellationToken);
        factory.Open();
        var engines = await Task.WhenAll(first, second);

        Assert.Equal(1, factory.CallCount);
        Assert.Same(engines[0], engines[1]);
    }

    /* A request cancelled while another load holds the lock stops waiting */
    [Fact]
    public async Task GetEngineAsync_CancelledWhileWaitingForLoad_ThrowsOperationCanceled()
    {
        var factory = new GatedTtsEngineFactory();
        SelectInSettings(Kokoro);
        using var provider = new TtsEngineProvider(factory, _settings);
        var first = provider.GetEngineAsync(TestContext.Current.CancellationToken);
        await factory.WaitForCallAsync(TestContext.Current.CancellationToken);
        using var cts = new CancellationTokenSource();

        var waiting = provider.GetEngineAsync(cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        factory.Open();
        await first;
    }

    /* Setting an engine makes it current and remembers its model */
    [Fact]
    public async Task SetEngineAsync_AvailableEngine_BecomesCurrent()
    {
        var engine = RegisterEngine(Kokoro);
        using var provider = CreateProvider();

        var result = await provider.SetEngineAsync(Kokoro, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Same(engine, provider.CurrentEngine);
        Assert.Equal(Kokoro, provider.CurrentModel);
    }

    /* Setting an engine announces the change */
    [Fact]
    public async Task SetEngineAsync_AvailableEngine_RaisesOnEngineChanged()
    {
        RegisterEngine(Kokoro);
        using var provider = CreateProvider();
        int raised = 0;
        provider.OnEngineChanged += () => raised++;

        await provider.SetEngineAsync(Kokoro, TestContext.Current.CancellationToken);

        Assert.Equal(1, raised);
    }

    /* Replacing an engine disposes the previous one */
    [Fact]
    public async Task SetEngineAsync_ReplacingEngine_DisposesPreviousEngine()
    {
        var previous = RegisterEngine(Kokoro);
        var next = RegisterEngine(Piper);
        using var provider = CreateProvider();
        await provider.SetEngineAsync(Kokoro, TestContext.Current.CancellationToken);

        await provider.SetEngineAsync(Piper, TestContext.Current.CancellationToken);

        Assert.True(previous.IsDisposed);
        Assert.False(next.IsDisposed);
    }

    /* A factory failure is returned and the current engine is kept */
    [Fact]
    public async Task SetEngineAsync_FactoryFails_KeepsCurrentEngine()
    {
        var current = RegisterEngine(Kokoro);
        _factory.Failures[Piper] = "worker crashed";
        using var provider = CreateProvider();
        await provider.SetEngineAsync(Kokoro, TestContext.Current.CancellationToken);

        var result = await provider.SetEngineAsync(Piper, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Contains("worker crashed", result.ErrorMessage);
        Assert.Same(current, provider.CurrentEngine);
        Assert.Equal(Kokoro, provider.CurrentModel);
        Assert.False(current.IsDisposed);
    }

    /* An unavailable engine for a selected model is rejected and disposed */
    [Fact]
    public async Task SetEngineAsync_UnavailableEngineForSelectedModel_FailsAndDisposesCandidate()
    {
        var candidate = new FakeTtsEngine { IsAvailable = false };
        _factory.Engines[Kokoro] = () => candidate;
        using var provider = CreateProvider();

        var result = await provider.SetEngineAsync(Kokoro, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.True(candidate.IsDisposed);
        Assert.IsType<EmptyTtsEngine>(provider.CurrentEngine);
    }

    /* Selecting "no model" accepts the factory's unavailable placeholder */
    [Fact]
    public async Task SetEngineAsync_Unselected_AcceptsUnavailableEngine()
    {
        RegisterEngine(Kokoro);
        using var provider = CreateProvider();
        await provider.SetEngineAsync(Kokoro, TestContext.Current.CancellationToken);

        var result = await provider.SetEngineAsync(ModelId.Unselected, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.True(provider.CurrentModel.IsUnselected);
        Assert.False(provider.CurrentEngine.IsAvailable);
    }

    /* Setting an engine never persists the selection */
    [Fact]
    public async Task SetEngineAsync_AvailableEngine_DoesNotSaveSettings()
    {
        RegisterEngine(Kokoro);
        using var provider = CreateProvider();
        int savesBefore = _repo.AppConfigSaveCount;

        await provider.SetEngineAsync(Kokoro, TestContext.Current.CancellationToken);

        Assert.Equal(savesBefore, _repo.AppConfigSaveCount);
    }

    /* Concurrent swaps are serialized: the factory never builds two engines at once */
    [Fact]
    public async Task SetEngineAsync_ConcurrentCalls_AreSerialized()
    {
        var factory = new GatedTtsEngineFactory();
        using var provider = new TtsEngineProvider(factory, _settings);

        var first = provider.SetEngineAsync(Kokoro, TestContext.Current.CancellationToken);
        await factory.WaitForCallAsync(TestContext.Current.CancellationToken);
        var second = provider.SetEngineAsync(Piper, TestContext.Current.CancellationToken);
        int callsWhileFirstRunning = factory.CallCount;
        factory.Open();
        await Task.WhenAll(first, second);

        Assert.Equal(1, callsWhileFirstRunning);
        Assert.Equal(2, factory.CallCount);
        Assert.Equal(1, factory.MaxConcurrent);
    }

    /* A model no installed engine provides is rejected before the factory runs */
    [Fact]
    public async Task SwitchModelAsync_ModelNotInRegistry_ReturnsFailure()
    {
        RegisterEngine(Kokoro);
        using var provider = CreateValidatingProvider();

        var result = await provider.SwitchModelAsync(Kokoro, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Empty(_factory.CreatedModels);
    }

    /* A model whose files are not downloaded is rejected before the factory runs */
    [Fact]
    public async Task SwitchModelAsync_ModelNotDownloaded_ReturnsFailure()
    {
        _registry.AddModel(Kokoro.EngineId, Kokoro.ModelName, "model.onnx");
        RegisterEngine(Kokoro);
        using var provider = CreateValidatingProvider();

        var result = await provider.SwitchModelAsync(Kokoro, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Contains("not downloaded", result.ErrorMessage);
        Assert.Empty(_factory.CreatedModels);
    }

    /* A valid switch loads the engine and saves the model as the selected one */
    [Fact]
    public async Task SwitchModelAsync_DownloadedModel_LoadsAndSavesSelection()
    {
        RegisterDownloadedModel(Kokoro);
        var engine = RegisterEngine(Kokoro);
        using var provider = CreateValidatingProvider();
        int savesBefore = _repo.AppConfigSaveCount;

        var result = await provider.SwitchModelAsync(Kokoro, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Same(engine, provider.CurrentEngine);
        Assert.Equal(Kokoro, _repo.AppConfig.ActiveModel);
        Assert.Equal(savesBefore + 1, _repo.AppConfigSaveCount);
    }

    /* A failed engine load is returned and the selection is not saved */
    [Fact]
    public async Task SwitchModelAsync_EngineLoadFails_DoesNotSaveSelection()
    {
        RegisterDownloadedModel(Kokoro);
        _factory.Failures[Kokoro] = "worker crashed";
        using var provider = CreateValidatingProvider();
        int savesBefore = _repo.AppConfigSaveCount;

        var result = await provider.SwitchModelAsync(Kokoro, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.True(_repo.AppConfig.ActiveModel.IsUnselected);
        Assert.Equal(savesBefore, _repo.AppConfigSaveCount);
    }

    /* Switching to "no model" skips registry/download validation and saves the empty selection */
    [Fact]
    public async Task SwitchModelAsync_Unselected_SkipsValidationAndSaves()
    {
        SelectInSettings(Kokoro);
        using var provider = CreateValidatingProvider();

        var result = await provider.SwitchModelAsync(ModelId.Unselected, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.True(_repo.AppConfig.ActiveModel.IsUnselected);
        Assert.Equal([ModelId.Unselected], _factory.CreatedModels);
    }

    /* Without a registry and download manager no validation happens */
    [Fact]
    public async Task SwitchModelAsync_WithoutValidators_LoadsAnyModel()
    {
        RegisterEngine(Kokoro);
        using var provider = CreateProvider();

        var result = await provider.SwitchModelAsync(Kokoro, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(Kokoro, _repo.AppConfig.ActiveModel);
    }

    /* Reloading loads whatever model the settings currently select */
    [Fact]
    public async Task ReloadFromSettingsAsync_SettingsChanged_LoadsSelectedModel()
    {
        RegisterEngine(Kokoro);
        var piper = RegisterEngine(Piper);
        SelectInSettings(Kokoro);
        using var provider = CreateProvider();
        await provider.GetEngineAsync(TestContext.Current.CancellationToken);
        SelectInSettings(Piper);

        var result = await provider.ReloadFromSettingsAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Same(piper, provider.CurrentEngine);
        Assert.Equal(Piper, provider.CurrentModel);
    }

    /* A failing reload is returned to the caller */
    [Fact]
    public async Task ReloadFromSettingsAsync_FactoryFails_ReturnsFailure()
    {
        _factory.Failures[Kokoro] = "worker crashed";
        SelectInSettings(Kokoro);
        using var provider = CreateProvider();

        var result = await provider.ReloadFromSettingsAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
    }

    /* Disposing shuts down the loaded engine */
    [Fact]
    public async Task Dispose_LoadedEngine_DisposesEngine()
    {
        var engine = RegisterEngine(Kokoro);
        var provider = CreateProvider();
        await provider.SetEngineAsync(Kokoro, TestContext.Current.CancellationToken);

        provider.Dispose();

        Assert.True(engine.IsDisposed);
        Assert.IsType<EmptyTtsEngine>(provider.CurrentEngine);
    }

    /* Disposing twice is harmless */
    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var provider = CreateProvider();

        var exception = Record.Exception(() =>
        {
            provider.Dispose();
            provider.Dispose();
        });

        Assert.Null(exception);
    }
}
