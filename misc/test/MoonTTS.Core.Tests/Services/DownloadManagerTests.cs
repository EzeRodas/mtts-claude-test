using MoonTTS.Core.Services;
using MoonTTS.Domain.Common;
using MoonTTS.Domain.Entities;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.Core.Tests.Services;

/* Tests for DownloadManager. A custom models folder inside a TempDirectory keeps user folders untouched */
public sealed class DownloadManagerTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeModelRegistry _registry = new();
    private readonly FakeModelDownloader _downloader = new();
    private readonly SettingsManager _settings;
    private readonly DownloadManager _manager;

    /* Points the models folder at the temp directory */
    public DownloadManagerTests()
    {
        var repo = new FakeConfigurationRepository();
        repo.SaveAppConfig(new AppConfig { CustomModelsDirectory = _temp.Combine("models") });
        _settings = new SettingsManager(repo);
        _manager = new DownloadManager(_registry, _downloader, _settings);
    }

    /* Removes the temp folder */
    public void Dispose() => _temp.Dispose();

    /* The folder holding a given engine's model files */
    private string EngineDirectory(string engineId) => Path.Combine(_temp.Combine("models"), engineId);

    /* Writes model files into their engine folder */
    private void WriteFiles(string engineId, params string[] files)
    {
        Directory.CreateDirectory(EngineDirectory(engineId));
        foreach (string file in files)
            File.WriteAllText(Path.Combine(EngineDirectory(engineId), file), "data");
    }

    /* Every model of the registry is listed */
    [Fact]
    public void GetAvailableModels_RegisteredModels_ReturnsRegistryModels()
    {
        var a = _registry.AddModel("kokoro", "v1", "model.onnx");
        var b = _registry.AddModel("piper", "amy", "amy.onnx");

        Assert.Equal([a, b], _manager.GetAvailableModels());
    }

    /* A model unknown to the registry is never downloaded */
    [Fact]
    public void IsModelDownloaded_UnknownModel_ReturnsFalse()
    {
        Assert.False(_manager.IsModelDownloaded(new ModelId("ghost", "v1")));
    }

    /* A model with every file present in its engine folder is downloaded */
    [Fact]
    public void IsModelDownloaded_AllFilesPresent_ReturnsTrue()
    {
        var model = _registry.AddModel("kokoro", "v1", "model.onnx", "voices.bin");
        WriteFiles("kokoro", "model.onnx", "voices.bin");

        Assert.True(_manager.IsModelDownloaded(model.Id));
    }

    /* A model missing any file is not downloaded */
    [Fact]
    public void IsModelDownloaded_OneFileMissing_ReturnsFalse()
    {
        var model = _registry.AddModel("kokoro", "v1", "model.onnx", "voices.bin");
        WriteFiles("kokoro", "model.onnx");

        Assert.False(_manager.IsModelDownloaded(model.Id));
    }

    /* A model that needs no download files (e.g. bundled with its engine) counts as downloaded */
    [Fact]
    public void IsModelDownloaded_ModelWithoutFiles_ReturnsTrue()
    {
        var model = _registry.AddModel("system", "default");

        Assert.True(_manager.IsModelDownloaded(model.Id));
    }

    /* Files of the same name in another engine's folder do not count */
    [Fact]
    public void IsModelDownloaded_FilesInOtherEngineFolder_ReturnsFalse()
    {
        var model = _registry.AddModel("kokoro", "v1", "model.onnx");
        WriteFiles("piper", "model.onnx");

        Assert.False(_manager.IsModelDownloaded(model.Id));
    }

    /* Downloading a model unknown to the registry fails without calling the downloader */
    [Fact]
    public async Task DownloadModelAsync_UnknownModel_ReturnsFailure()
    {
        var result = await _manager.DownloadModelAsync(new ModelId("ghost", "v1"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Empty(_downloader.Downloads);
    }

    /* The download targets the engine's own folder, which is created first */
    [Fact]
    public async Task DownloadModelAsync_KnownModel_DownloadsIntoEngineFolder()
    {
        var model = _registry.AddModel("kokoro", "v1", "model.onnx");
        _downloader.ResultToReturn = Result.Failure("network down");

        await _manager.DownloadModelAsync(model.Id, cancellationToken: TestContext.Current.CancellationToken);

        var (downloaded, destination) = Assert.Single(_downloader.Downloads);
        Assert.Equal(model, downloaded);
        Assert.Equal(EngineDirectory("kokoro"), destination);
        Assert.True(Directory.Exists(destination));
    }

    /* A successful download succeeds and leaves the model downloaded */
    [Fact]
    public async Task DownloadModelAsync_DownloaderSucceeds_ModelIsDownloaded()
    {
        var model = _registry.AddModel("kokoro", "v1", "model.onnx");

        var result = await _manager.DownloadModelAsync(model.Id, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.True(_manager.IsModelDownloaded(model.Id));
    }

    /* The downloader's failure is returned unchanged */
    [Fact]
    public async Task DownloadModelAsync_DownloaderFails_ReturnsDownloaderFailure()
    {
        var model = _registry.AddModel("kokoro", "v1", "model.onnx");
        _downloader.ResultToReturn = Result.Failure("network down");

        var result = await _manager.DownloadModelAsync(model.Id, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("network down", result.ErrorMessage);
    }

    /* Progress reports reach the caller's progress handler */
    [Fact]
    public async Task DownloadModelAsync_WithProgress_ForwardsProgress()
    {
        var model = _registry.AddModel("kokoro", "v1", "model.onnx");
        var progress = new RecordingProgress();

        await _manager.DownloadModelAsync(model.Id, progress, TestContext.Current.CancellationToken);

        Assert.Equal([100d], progress.Values);
    }

    /* Deleting a model unknown to the registry fails */
    [Fact]
    public void DeleteModel_UnknownModel_ReturnsFailure()
    {
        var result = _manager.DeleteModel(new ModelId("ghost", "v1"));

        Assert.True(result.IsFailure);
    }

    /* Deleting a model with no files on disk fails */
    [Fact]
    public void DeleteModel_NoFilesOnDisk_ReturnsFailure()
    {
        var model = _registry.AddModel("kokoro", "v1", "model.onnx");

        var result = _manager.DeleteModel(model.Id);

        Assert.True(result.IsFailure);
    }

    /* Deleting a model removes all of its files */
    [Fact]
    public void DeleteModel_DownloadedModel_DeletesItsFiles()
    {
        var model = _registry.AddModel("kokoro", "v1", "model.onnx", "voices.bin");
        WriteFiles("kokoro", "model.onnx", "voices.bin");

        var result = _manager.DeleteModel(model.Id);

        Assert.True(result.IsSuccess);
        Assert.False(File.Exists(Path.Combine(EngineDirectory("kokoro"), "model.onnx")));
        Assert.False(File.Exists(Path.Combine(EngineDirectory("kokoro"), "voices.bin")));
        Assert.False(_manager.IsModelDownloaded(model.Id));
    }

    /* Deleting a partially downloaded model removes the files that exist */
    [Fact]
    public void DeleteModel_PartiallyDownloadedModel_DeletesExistingFiles()
    {
        var model = _registry.AddModel("kokoro", "v1", "model.onnx", "voices.bin");
        WriteFiles("kokoro", "model.onnx");

        var result = _manager.DeleteModel(model.Id);

        Assert.True(result.IsSuccess);
        Assert.False(File.Exists(Path.Combine(EngineDirectory("kokoro"), "model.onnx")));
    }

    /* A file shared with another downloaded model of the same engine is kept */
    [Fact]
    public void DeleteModel_FileSharedWithDownloadedModel_KeepsSharedFile()
    {
        var target = _registry.AddModel("kokoro", "v1", "v1.onnx", "voices.bin");
        var other = _registry.AddModel("kokoro", "v2", "v2.onnx", "voices.bin");
        WriteFiles("kokoro", "v1.onnx", "v2.onnx", "voices.bin");

        var result = _manager.DeleteModel(target.Id);

        Assert.True(result.IsSuccess);
        Assert.False(File.Exists(Path.Combine(EngineDirectory("kokoro"), "v1.onnx")));
        Assert.True(File.Exists(Path.Combine(EngineDirectory("kokoro"), "voices.bin")));
        Assert.True(_manager.IsModelDownloaded(other.Id));
    }

    /* A file shared only with a model that is not downloaded is deleted */
    [Fact]
    public void DeleteModel_FileSharedWithNotDownloadedModel_DeletesSharedFile()
    {
        var target = _registry.AddModel("kokoro", "v1", "v1.onnx", "voices.bin");
        _registry.AddModel("kokoro", "v2", "v2.onnx", "voices.bin");
        WriteFiles("kokoro", "v1.onnx", "voices.bin");

        _manager.DeleteModel(target.Id);

        Assert.False(File.Exists(Path.Combine(EngineDirectory("kokoro"), "voices.bin")));
    }

    /* A downloaded model of another engine with the same file name does not protect the file (separate folders) */
    [Fact]
    public void DeleteModel_SameFileNameInOtherEngine_DeletesFileAndKeepsOtherEngineFile()
    {
        var target = _registry.AddModel("kokoro", "v1", "model.onnx");
        var other = _registry.AddModel("piper", "amy", "model.onnx");
        WriteFiles("kokoro", "model.onnx");
        WriteFiles("piper", "model.onnx");

        var result = _manager.DeleteModel(target.Id);

        Assert.True(result.IsSuccess);
        Assert.False(File.Exists(Path.Combine(EngineDirectory("kokoro"), "model.onnx")));
        Assert.True(_manager.IsModelDownloaded(other.Id));
    }

    /* A file that cannot be deleted turns into a failure instead of an exception */
    [Fact(Skip = "Directory permissions are POSIX specific", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void DeleteModel_FileCannotBeDeleted_ReturnsFailure()
    {
        Assert.SkipWhen(Environment.UserName == "root", "root ignores directory permissions");
        var model = _registry.AddModel("kokoro", "v1", "model.onnx");
        WriteFiles("kokoro", "model.onnx");
        string folder = EngineDirectory("kokoro");
        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var result = _manager.DeleteModel(model.Id);

            Assert.True(result.IsFailure);
            Assert.NotNull(result.Exception);
        }
        finally
        {
            File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /* IProgress that records reported values synchronously (Progress<T> posts them asynchronously) */
    private sealed class RecordingProgress : IProgress<double>
    {
        public List<double> Values { get; } = new();

        /* Records the value */
        public void Report(double value) => Values.Add(value);
    }
}
