using MoonTTS.CLI.commands;
using MoonTTS.CLI.Tests.Support;
using MoonTTS.Domain.Common;

namespace MoonTTS.CLI.Tests.Commands.Models;

[Collection(TestCollections.Environment)]
public sealed class DownloadModelCommandTests : IDisposable
{
    private readonly CliTestHost _host = new();

    /* Releases the host */
    public void Dispose() => _host.Dispose();

    /* Build creates the "download" command with a required "model" argument */
    [Fact]
    public void Build_Always_CreatesDownloadCommandWithModelArgument()
    {
        var command = DownloadModelCommand.Build(_host.Services);

        Assert.Equal("download", command.Name);
        Assert.Equal("model", Assert.Single(command.Arguments).Name);
    }

    /* An unknown model fails and lists the supported models */
    [Fact]
    public async Task Download_UnknownModel_ReturnsErrorAndListsModels()
    {
        _host.AddModel("kokoro", "v1", "model.onnx");

        var run = await _host.RunAsync(DownloadModelCommand.Build, "missing");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Error: Unknown model 'missing'.", run.Output);
        Assert.Contains("Supported models: kokoro/v1", run.Output);
        Assert.Empty(_host.Downloader.Downloads);
    }

    /* A model whose files are already present is not downloaded again */
    [Fact]
    public async Task Download_AlreadyDownloaded_WarnsAndSkipsDownload()
    {
        _host.AddDownloadedModel("kokoro", "v1", "model.onnx");

        var run = await _host.RunAsync(DownloadModelCommand.Build, "kokoro/v1");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Model 'kokoro/v1' is already downloaded.", run.Output);
        Assert.Empty(_host.Downloader.Downloads);
    }

    /* A new model is downloaded into its engine's folder under the models directory */
    [Fact]
    public async Task Download_NewModel_DownloadsIntoEngineFolder()
    {
        var model = _host.AddModel("kokoro", "v1", "model.onnx", "voices.bin");

        var run = await _host.RunAsync(DownloadModelCommand.Build, "kokoro/v1");

        Assert.Equal(0, run.ExitCode);
        var download = Assert.Single(_host.Downloader.Downloads);
        Assert.Equal(model, download.Model);
        Assert.Equal(Path.Combine(_host.ModelsDirectory, "kokoro"), download.Destination);
        Assert.True(File.Exists(_host.ModelFilePath(model, "voices.bin")));
    }

    /* A successful download announces the start and the result */
    [Fact]
    public async Task Download_NewModel_PrintsProgressAndSuccess()
    {
        _host.AddModel("kokoro", "v1", "model.onnx");

        var run = await _host.RunAsync(DownloadModelCommand.Build, "kokoro/v1");

        Assert.Contains("Downloading model files for 'kokoro/v1'...", run.Output);
        Assert.Contains("Successfully downloaded model: kokoro/v1", run.Output);
    }

    /* A failing download reports the downloader's error and exits with 1 */
    [Fact]
    public async Task Download_DownloaderFails_ReturnsErrorWithMessage()
    {
        _host.AddModel("kokoro", "v1", "model.onnx");
        _host.Downloader.ResultToReturn = Result.Failure("network unreachable");

        var run = await _host.RunAsync(DownloadModelCommand.Build, "kokoro/v1");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Download failed: network unreachable", run.Output);
        Assert.DoesNotContain("Successfully downloaded", run.Output);
    }

    /* The downloader reports 0-100 (IModelDownloader contract), so 50 draws a half-filled bar at 50% */
    [Fact]
    public async Task Download_DownloaderReportsHalfway_RendersHalfFilledBar()
    {
        using var host = new CliTestHost(new CliProgressDownloader(50));
        host.AddModel("kokoro", "v1", "model.onnx");

        var run = await host.RunAsync(DownloadModelCommand.Build, "kokoro/v1");

        Assert.Contains($"\rDownloading kokoro/v1 [{new string('#', 15)}{new string('-', 15)}]  50%", run.Output);
    }

    /* A finished download (100) draws a full bar and ends the progress line */
    [Fact]
    public async Task Download_DownloaderReportsComplete_RendersFullBar()
    {
        using var host = new CliTestHost(new CliProgressDownloader(100));
        host.AddModel("kokoro", "v1", "model.onnx");

        var run = await host.RunAsync(DownloadModelCommand.Build, "kokoro/v1");

        Assert.Contains($"\rDownloading kokoro/v1 [{new string('#', 30)}] 100%" + Environment.NewLine, run.Output);
    }
}
