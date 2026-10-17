using MoonTTS.CLI.commands;
using MoonTTS.CLI.Tests.Support;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.CLI.Tests.Commands.Models;

[Collection(TestCollections.Environment)]
public sealed class ListModelsCommandTests : IDisposable
{
    private readonly CliTestHost _host = new();

    /* Releases the host */
    public void Dispose() => _host.Dispose();

    /* Build creates the "list" command without arguments */
    [Fact]
    public void Build_Always_CreatesListCommand()
    {
        var command = ListModelsCommand.Build(_host.Services);

        Assert.Equal("list", command.Name);
        Assert.Empty(command.Arguments);
    }

    /* With no engine installed the user is told how to install one, and the command still succeeds */
    [Fact]
    public async Task List_NoModels_WarnsNoEnginesInstalledAndReturnsZero()
    {
        var run = await _host.RunAsync(ListModelsCommand.Build);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("No engines are installed.", run.Output);
        Assert.Contains("engines folder", run.Output);
    }

    /* Every model of every engine is listed with its id and display name */
    [Fact]
    public async Task List_Models_PrintsEveryModelWithDisplayName()
    {
        _host.AddModel("kokoro", "v1", "model.onnx");
        _host.AddModel("piper", "amy", "amy.onnx");

        var run = await _host.RunAsync(ListModelsCommand.Build);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Supported TTS Models", run.Output);
        Assert.Contains("kokoro v1", CliOutput.LineContaining(run.Output, "kokoro/v1"));
        Assert.Contains("piper amy", CliOutput.LineContaining(run.Output, "piper/amy"));
    }

    /* A model whose files are all present shows "Yes" in the Downloaded column */
    [Fact]
    public async Task List_DownloadedModel_ShowsYes()
    {
        _host.AddDownloadedModel("kokoro", "v1", "model.onnx");

        var run = await _host.RunAsync(ListModelsCommand.Build);

        Assert.Contains(" Yes ", CliOutput.LineContaining(run.Output, "kokoro/v1"));
    }

    /* A model with missing files shows "No" in the Downloaded column */
    [Fact]
    public async Task List_ModelNotDownloaded_ShowsNo()
    {
        _host.AddModel("kokoro", "v1", "model.onnx");

        var run = await _host.RunAsync(ListModelsCommand.Build);

        Assert.Contains(" No ", CliOutput.LineContaining(run.Output, "kokoro/v1"));
    }

    /* The active (downloaded) model is marked ACTIVE and the others are not */
    [Fact]
    public async Task List_ActiveDownloadedModel_MarksOnlyItActive()
    {
        var active = _host.AddDownloadedModel("kokoro", "v1", "model.onnx");
        _host.AddDownloadedModel("kokoro", "v2", "model2.onnx");
        _host.Repository.AppConfig.ActiveModel = active.Id;

        var run = await _host.RunAsync(ListModelsCommand.Build);

        Assert.Contains("ACTIVE", CliOutput.LineContaining(run.Output, "kokoro/v1"));
        Assert.DoesNotContain("ACTIVE", CliOutput.LineContaining(run.Output, "kokoro/v2"));
    }

    /* An active model whose files disappeared is reset to Unselected and the change is saved */
    [Fact]
    public async Task List_ActiveModelNotDownloaded_ResetsActiveModelToUnselected()
    {
        var model = _host.AddModel("kokoro", "v1", "model.onnx");
        _host.Repository.AppConfig.ActiveModel = model.Id;

        var run = await _host.RunAsync(ListModelsCommand.Build);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(ModelId.Unselected, _host.Repository.AppConfig.ActiveModel);
        Assert.Equal(1, _host.Repository.AppConfigSaveCount);
        Assert.DoesNotContain("ACTIVE", CliOutput.LineContaining(run.Output, "kokoro/v1"));
    }

    /* A downloaded active model is kept and the configuration is not rewritten */
    [Fact]
    public async Task List_ActiveModelDownloaded_DoesNotSaveConfiguration()
    {
        var model = _host.AddDownloadedModel("kokoro", "v1", "model.onnx");
        _host.Repository.AppConfig.ActiveModel = model.Id;

        await _host.RunAsync(ListModelsCommand.Build);

        Assert.Equal(model.Id, _host.Repository.AppConfig.ActiveModel);
        Assert.Equal(0, _host.Repository.AppConfigSaveCount);
    }

    /* With no model selected nothing is reset or saved */
    [Fact]
    public async Task List_NoActiveModel_DoesNotSaveConfiguration()
    {
        _host.AddModel("kokoro", "v1", "model.onnx");

        await _host.RunAsync(ListModelsCommand.Build);

        Assert.Equal(0, _host.Repository.AppConfigSaveCount);
    }
}
