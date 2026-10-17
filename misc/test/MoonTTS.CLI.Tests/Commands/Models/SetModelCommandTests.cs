using MoonTTS.CLI.commands;
using MoonTTS.CLI.Tests.Support;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.CLI.Tests.Commands.Models;

[Collection(TestCollections.Environment)]
public sealed class SetModelCommandTests : IDisposable
{
    private readonly CliTestHost _host = new();

    /* Releases the host */
    public void Dispose() => _host.Dispose();

    /* Build creates the "set" command with a required "model" argument */
    [Fact]
    public void Build_Always_CreatesSetCommandWithModelArgument()
    {
        var command = SetModelCommand.Build(_host.Services);

        Assert.Equal("set", command.Name);
        Assert.Equal("model", Assert.Single(command.Arguments).Name);
    }

    /* An unknown model fails and lists the models that do exist */
    [Fact]
    public async Task Set_UnknownModel_ReturnsErrorAndListsModels()
    {
        _host.AddDownloadedModel("kokoro", "v1");
        _host.AddModel("piper", "amy", "amy.onnx");

        var run = await _host.RunAsync(SetModelCommand.Build, "missing");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Error: Unknown model 'missing'.", run.Output);
        Assert.Contains("Available models: kokoro/v1, piper/amy", run.Output);
    }

    /* An unknown model does not touch the engine or the configuration */
    [Fact]
    public async Task Set_UnknownModel_DoesNotSwitchEngine()
    {
        await _host.RunAsync(SetModelCommand.Build, "missing");

        Assert.Empty(_host.EngineFactory.CreatedModels);
        Assert.Equal(0, _host.Repository.AppConfigSaveCount);
    }

    /* A model that is installed but not downloaded fails with the download hint */
    [Fact]
    public async Task Set_ModelNotDownloaded_ReturnsErrorWithDownloadHint()
    {
        _host.AddModel("kokoro", "v1", "model.onnx");

        var run = await _host.RunAsync(SetModelCommand.Build, "kokoro/v1");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Error: Model 'kokoro/v1' is not installed.", run.Output);
        Assert.Contains("mtts model download kokoro/v1", run.Output);
        Assert.Empty(_host.EngineFactory.CreatedModels);
    }

    /* A downloaded model is loaded, saved as the active model and reported */
    [Fact]
    public async Task Set_DownloadedModel_SwitchesActiveModel()
    {
        var model = _host.AddDownloadedModel("kokoro", "v1", "model.onnx");
        _host.AddEngine(model);

        var run = await _host.RunAsync(SetModelCommand.Build, "kokoro/v1");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(model.Id, _host.Repository.AppConfig.ActiveModel);
        Assert.Contains("Successfully switched active model to: kokoro/v1", run.Output);
    }

    /* The model can be named without its engine prefix */
    [Fact]
    public async Task Set_ModelNameOnly_ResolvesFullModelId()
    {
        var model = _host.AddDownloadedModel("kokoro", "v1", "model.onnx");
        _host.AddEngine(model);

        var run = await _host.RunAsync(SetModelCommand.Build, "v1");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(new ModelId("kokoro", "v1"), _host.Repository.AppConfig.ActiveModel);
    }

    /* An engine that fails to start reports the failure and keeps the previous active model */
    [Fact]
    public async Task Set_EngineFailsToLoad_ReturnsErrorAndKeepsActiveModel()
    {
        var model = _host.AddDownloadedModel("kokoro", "v1", "model.onnx");
        _host.EngineFactory.Failures[model.Id] = "worker crashed";

        var run = await _host.RunAsync(SetModelCommand.Build, "kokoro/v1");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Failed: ", run.Output);
        Assert.Contains("worker crashed", run.Output);
        Assert.Equal(ModelId.Unselected, _host.Repository.AppConfig.ActiveModel);
    }

    /* An engine that starts but reports itself unavailable is rejected */
    [Fact]
    public async Task Set_EngineUnavailable_ReturnsError()
    {
        _host.AddDownloadedModel("kokoro", "v1", "model.onnx");

        var run = await _host.RunAsync(SetModelCommand.Build, "kokoro/v1");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("not available or loaded", run.Output);
        Assert.Equal(ModelId.Unselected, _host.Repository.AppConfig.ActiveModel);
    }

    /* Omitting the model is a parse error and nothing runs */
    [Fact]
    public async Task Set_MissingModelArgument_ReturnsParseError()
    {
        var run = await _host.RunAsync(SetModelCommand.Build);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Empty(_host.EngineFactory.CreatedModels);
    }
}
