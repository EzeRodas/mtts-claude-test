using MoonTTS.CLI.commands.Output.Main;
using MoonTTS.CLI.Tests.Support;

namespace MoonTTS.CLI.Tests.Commands.Output.Main;

[Collection(TestCollections.Environment)]
public sealed class EnableMainOutputCommandTests : IDisposable
{
    private readonly CliTestHost _host = new();

    /* Releases the host */
    public void Dispose() => _host.Dispose();

    /* Build creates the "enable" command describing that it enables the primary output */
    [Fact]
    public void Build_Always_CreatesEnableCommand()
    {
        var command = EnableMainOutputCommand.Build(_host.Services);

        Assert.Equal("enable", command.Name);
        Assert.Equal("Enables the primary audio output.", command.Description);
    }

    /* A disabled primary output is enabled and saved */
    [Fact]
    public async Task Enable_Disabled_EnablesAndSavesPrimaryPlayback()
    {
        _host.Repository.AppConfig.EnablePrimaryPlayback = false;

        var run = await _host.RunAsync(EnableMainOutputCommand.Build);

        Assert.Equal(0, run.ExitCode);
        Assert.True(_host.Repository.AppConfig.EnablePrimaryPlayback);
        Assert.Equal(1, _host.Repository.AppConfigSaveCount);
    }

    /* The monitor output is left as it was */
    [Fact]
    public async Task Enable_Always_KeepsMonitorPlayback()
    {
        _host.Repository.AppConfig.EnableMonitorPlayback = false;

        await _host.RunAsync(EnableMainOutputCommand.Build);

        Assert.False(_host.Repository.AppConfig.EnableMonitorPlayback);
    }

    /* The user is told the main output is enabled */
    [Fact]
    public async Task Enable_Always_PrintsEnabled()
    {
        var run = await _host.RunAsync(EnableMainOutputCommand.Build);

        Assert.Contains("Main audio output enabled.", run.Output);
    }
}
