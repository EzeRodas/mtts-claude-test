using MoonTTS.CLI.commands.Output.Monitor;
using MoonTTS.CLI.Tests.Support;

namespace MoonTTS.CLI.Tests.Commands.Output.Monitor;

[Collection(TestCollections.Environment)]
public sealed class EnableMonitorOutputCommandTests : IDisposable
{
    private readonly CliTestHost _host = new();

    /* Releases the host */
    public void Dispose() => _host.Dispose();

    /* Build creates the "enable" command describing that it enables the monitor output */
    [Fact]
    public void Build_Always_CreatesEnableCommand()
    {
        var command = EnableMonitorOutputCommand.Build(_host.Services);

        Assert.Equal("enable", command.Name);
        Assert.Equal("Enables the monitor audio output.", command.Description);
    }

    /* A disabled monitor output is enabled and saved */
    [Fact]
    public async Task Enable_Disabled_EnablesAndSavesMonitorPlayback()
    {
        _host.Repository.AppConfig.EnableMonitorPlayback = false;

        var run = await _host.RunAsync(EnableMonitorOutputCommand.Build);

        Assert.Equal(0, run.ExitCode);
        Assert.True(_host.Repository.AppConfig.EnableMonitorPlayback);
        Assert.Equal(1, _host.Repository.AppConfigSaveCount);
    }

    /* The primary output is left as it was */
    [Fact]
    public async Task Enable_Always_KeepsPrimaryPlayback()
    {
        _host.Repository.AppConfig.EnablePrimaryPlayback = false;

        await _host.RunAsync(EnableMonitorOutputCommand.Build);

        Assert.False(_host.Repository.AppConfig.EnablePrimaryPlayback);
    }

    /* The user is told the monitor output is enabled */
    [Fact]
    public async Task Enable_Always_PrintsEnabled()
    {
        var run = await _host.RunAsync(EnableMonitorOutputCommand.Build);

        Assert.Contains("Monitor audio output enabled.", run.Output);
        Assert.DoesNotContain("disabled", run.Output);
    }
}
