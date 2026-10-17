using MoonTTS.CLI.commands;
using MoonTTS.CLI.Tests.Support;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.CLI.Tests.Commands.Models;

[Collection(TestCollections.Environment)]
public sealed class DeleteModelCommandTests : IDisposable
{
    private readonly CliTestHost _host = new();

    /* Releases the host */
    public void Dispose() => _host.Dispose();

    /* Registers a downloaded model "kokoro/v1" with one file */
    private ModelMetadata AddDownloaded() => _host.AddDownloadedModel("kokoro", "v1", "model.onnx");

    /* Registers a downloaded model and makes it the active one */
    private ModelMetadata AddActive()
    {
        var model = AddDownloaded();
        _host.ActivateModel(model);
        return model;
    }

    /* True while the model file is still on disk */
    private bool FileExists(ModelMetadata model) => File.Exists(_host.ModelFilePath(model, "model.onnx"));

    /* Build creates the "delete" command with a "model" argument and a -s/--skip option */
    [Fact]
    public void Build_Always_CreatesDeleteCommandWithModelArgumentAndSkipOption()
    {
        var command = DeleteModelCommand.Build(_host.Services);

        Assert.Equal("delete", command.Name);
        Assert.Equal("model", Assert.Single(command.Arguments).Name);
        var skip = Assert.Single(command.Options);
        Assert.Contains("--skip", skip.Aliases.Append(skip.Name));
        Assert.Contains("-s", skip.Aliases.Append(skip.Name));
    }

    /* An unknown model fails and lists the supported models */
    [Fact]
    public async Task Delete_UnknownModel_ReturnsErrorAndListsModels()
    {
        AddDownloaded();

        var run = await _host.RunAsync(DeleteModelCommand.Build, "missing", "--skip");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Error: Unknown model 'missing'.", run.Output);
        Assert.Contains("Supported models: kokoro/v1", run.Output);
    }

    /* A model that is not downloaded only gets a warning */
    [Fact]
    public async Task Delete_NotDownloaded_WarnsAndReturnsZero()
    {
        _host.AddModel("kokoro", "v1", "model.onnx");

        var run = await _host.RunAsync(DeleteModelCommand.Build, "kokoro/v1", "--skip");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Model 'kokoro/v1' is not downloaded.", run.Output);
    }

    /* --skip deletes an inactive model without asking */
    [Fact]
    public async Task Delete_InactiveModelWithSkip_DeletesFilesWithoutPrompt()
    {
        var model = AddDownloaded();

        var run = await _host.RunAsync(DeleteModelCommand.Build, "kokoro/v1", "--skip");

        Assert.Equal(0, run.ExitCode);
        Assert.False(FileExists(model));
        Assert.DoesNotContain("Are you sure", run.Output);
        Assert.Contains("Successfully deleted model: kokoro/v1", run.Output);
    }

    /* The short -s alias behaves like --skip */
    [Fact]
    public async Task Delete_InactiveModelWithShortSkip_DeletesFiles()
    {
        var model = AddDownloaded();

        var run = await _host.RunAsync(DeleteModelCommand.Build, "kokoro/v1", "-s");

        Assert.Equal(0, run.ExitCode);
        Assert.False(FileExists(model));
    }

    /* Without --skip the user is asked, and "y" deletes the model */
    [Fact]
    public async Task Delete_InactiveModelConfirmed_DeletesFiles()
    {
        var model = AddDownloaded();

        var run = await _host.RunWithInputAsync(DeleteModelCommand.Build, "y\n", "kokoro/v1");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Are you sure you want to delete model 'kokoro/v1'? [y/N]: ", run.Output);
        Assert.False(FileExists(model));
    }

    /* Answering "n" cancels the deletion and keeps the files */
    [Fact]
    public async Task Delete_InactiveModelDeclined_KeepsFilesAndPrintsCancelled()
    {
        var model = AddDownloaded();

        var run = await _host.RunWithInputAsync(DeleteModelCommand.Build, "n\n", "kokoro/v1");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Deletion cancelled.", run.Output);
        Assert.True(FileExists(model));
    }

    /* Pressing Enter takes the default answer (No) and keeps the files */
    [Fact]
    public async Task Delete_InactiveModelEmptyAnswer_KeepsFiles()
    {
        var model = AddDownloaded();

        var run = await _host.RunWithInputAsync(DeleteModelCommand.Build, "\n", "kokoro/v1");

        Assert.Contains("Deletion cancelled.", run.Output);
        Assert.True(FileExists(model));
    }

    /* Deleting the active model warns that it is active before anything else */
    [Fact]
    public async Task Delete_ActiveModel_WarnsItIsActive()
    {
        AddActive();

        var run = await _host.RunAsync(DeleteModelCommand.Build, "kokoro/v1", "--skip");

        Assert.Contains("Warning: 'kokoro/v1' is currently the active model.", run.Output);
    }

    /* Confirming the deletion of the active model unselects it and deletes the files */
    [Fact]
    public async Task Delete_ActiveModelConfirmed_UnselectsActiveModelAndDeletesFiles()
    {
        var model = AddActive();

        var run = await _host.RunWithInputAsync(DeleteModelCommand.Build, "yes\n", "kokoro/v1");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Deleting it will set active model to Unselected. Proceed? [y/N]: ", run.Output);
        Assert.Equal(ModelId.Unselected, _host.Repository.AppConfig.ActiveModel);
        Assert.False(FileExists(model));
    }

    /* Declining the deletion of the active model keeps it active and keeps its files */
    [Fact]
    public async Task Delete_ActiveModelDeclined_KeepsActiveModelAndFiles()
    {
        var model = AddActive();

        var run = await _host.RunWithInputAsync(DeleteModelCommand.Build, "n\n", "kokoro/v1");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Deletion cancelled.", run.Output);
        Assert.Equal(model.Id, _host.Repository.AppConfig.ActiveModel);
        Assert.True(FileExists(model));
    }

    /* --skip deletes the active model without asking and unselects it */
    [Fact]
    public async Task Delete_ActiveModelWithSkip_UnselectsWithoutPrompt()
    {
        var model = AddActive();

        var run = await _host.RunAsync(DeleteModelCommand.Build, "kokoro/v1", "--skip");

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("Proceed?", run.Output);
        Assert.Equal(ModelId.Unselected, _host.Repository.AppConfig.ActiveModel);
        Assert.False(FileExists(model));
    }

    /* A model with nothing to delete (no files) reports the DownloadManager failure and exits with 1 */
    [Fact]
    public async Task Delete_NoFilesToDelete_ReturnsErrorWithMessage()
    {
        _host.AddDownloadedModel("kokoro", "empty");

        var run = await _host.RunAsync(DeleteModelCommand.Build, "kokoro/empty", "--skip");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Failed to delete model files: No files found for model 'kokoro/empty'.", run.Output);
    }
}
