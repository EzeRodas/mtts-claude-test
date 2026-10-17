using MoonTTS.Core.Helpers;
using MoonTTS.Core.Services;
using MoonTTS.Domain.ValueObjects;
using MoonTTS.Engine.Protocol;
using MoonTTS.Infrastructure.Tts;

namespace MoonTTS.Infrastructure.Tests.Tts;

/* TtsEngineFactory deciding which engine to build for a model id */
public sealed class TtsEngineFactoryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /* A factory over the harness's catalog and settings */
    private static TtsEngineFactory CreateFactory(DummyEngineHarness harness) =>
        new(harness.Catalog, harness.Settings, new DownloadManager(harness.Catalog, new FakeModelDownloader(), harness.Settings),
            harness.VoiceStore);

    /* A factory over an in-memory registry */
    private static TtsEngineFactory CreateFactory(FakeModelRegistry registry, TempDirectory temp)
    {
        var repository = new FakeConfigurationRepository();
        repository.AppConfig.CustomModelsDirectory = temp.CreateSubdirectory("models");
        var settings = new SettingsManager(repository);
        return new TtsEngineFactory(registry, settings, new DownloadManager(registry, new FakeModelDownloader(), settings), new EngineVoiceStore());
    }

    /* No selected model yields the empty null-object engine */
    [Fact]
    public async Task CreateAsync_Unselected_ReturnsEmptyEngine()
    {
        using var temp = new TempDirectory();
        var factory = CreateFactory(new FakeModelRegistry(), temp);

        var result = await factory.CreateAsync(ModelId.Unselected, Ct);

        Assert.True(result.IsSuccess);
        Assert.IsType<EmptyTtsEngine>(result.Value);
    }

    /* A model no installed engine provides yields a failure */
    [Fact]
    public async Task CreateAsync_UnknownModel_ReturnsFailure()
    {
        using var temp = new TempDirectory();
        var factory = CreateFactory(new FakeModelRegistry(), temp);

        var result = await factory.CreateAsync(new ModelId("ghost", "model"), Ct);

        Assert.True(result.IsFailure);
        Assert.Contains("not provided by any installed engine", result.ErrorMessage);
    }

    /* A model whose engine is not installed yields a failure */
    [Fact]
    public async Task CreateAsync_EngineMissing_ReturnsFailure()
    {
        using var temp = new TempDirectory();
        var registry = new FakeModelRegistry();
        registry.AddModel("orphan", "model");
        var factory = CreateFactory(registry, temp);

        var result = await factory.CreateAsync(new ModelId("orphan", "model"), Ct);

        Assert.True(result.IsFailure);
        Assert.Contains("Engine 'orphan' is not installed", result.ErrorMessage);
    }

    /* A model whose files are not downloaded yields a failure and starts no worker */
    [Fact]
    public async Task CreateAsync_ModelNotDownloaded_ReturnsFailureWithoutStartingWorker()
    {
        using var harness = new DummyEngineHarness(null, new EngineModelManifest
        {
            Id = "big",
            DownloadFiles = new() { ["weights.onnx"] = "https://example.invalid/weights.onnx" }
        });

        var result = await CreateFactory(harness).CreateAsync(new ModelId("dummy", "big"), Ct);

        Assert.True(result.IsFailure);
        Assert.Contains("not downloaded", result.ErrorMessage);
        Assert.Empty(harness.ReadRequests());
    }

    /* A downloaded model of an installed engine yields a started ExternalTtsEngine */
    [Fact]
    public async Task CreateAsync_DownloadedModel_ReturnsAvailableExternalEngine()
    {
        using var harness = new DummyEngineHarness(null, new EngineModelManifest
        {
            Id = "big",
            DownloadFiles = new() { ["weights.onnx"] = "https://example.invalid/weights.onnx" }
        });
        harness.Temp.WriteFile(Path.Combine("models", "dummy", "weights.onnx"), "weights");

        var result = await CreateFactory(harness).CreateAsync(new ModelId("dummy", "big"), Ct);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        using var engine = result.Value!;
        Assert.IsType<ExternalTtsEngine>(engine);
        Assert.True(engine.IsAvailable);
    }

    /* A worker that fails to start is reported as a failure */
    [Fact]
    public async Task CreateAsync_WorkerFailsHandshake_ReturnsFailure()
    {
        using var harness = new DummyEngineHarness(new MoonTTS.Tests.DummyEngine.DummyEngineOptions { FailLoadModel = true });

        var result = await CreateFactory(harness).CreateAsync(new ModelId("dummy", "default"), Ct);

        Assert.True(result.IsFailure);
        Assert.Contains("dummy model load failure", result.ErrorMessage);
    }
}
