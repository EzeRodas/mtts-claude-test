using MoonTTS.CLI.commands.Output.Monitor;
using MoonTTS.CLI.Tests.Support;

namespace MoonTTS.CLI.Tests.Commands.Output.Monitor;

[Collection(TestCollections.Environment)]
public sealed class DisableMonitorOutputCommandTests : IDisposable
{
    private readonly CliTestHost _host = new();

    /* Releases the host */
    public void Dispose() => _host.Dispose();

    /* Build creates the "disable" command describing that it disables the monitor output */
    [Fact]
    public void Build_Always_CreatesDisableCommand()
    {
        var command = DisableMonitorOutputCommand.Build(_host.Services);

        Assert.Equal("disable", command.Name);
        Assert.Equal("Disables the monitor audio output.", command.Description);
    }

    /* An enabled monitor output is disabled and saved */
    [Fact]
    public async Task Disable_Enabled_DisablesAndSavesMonitorPlayback()
    {
        _host.Repository.AppConfig.EnableMonitorPlayback = true;

        var run = await _host.RunAsync(DisableMonitorOutputCommand.Build);

        Assert.Equal(0, run.ExitCode);
        Assert.False(_host.Repository.AppConfig.EnableMonitorPlayback);
        Assert.Equal(1, _host.Repository.AppConfigSaveCount);
    }

    /* The primary output is left as it was */
    [Fact]
    public async Task Disable_Always_KeepsPrimaryPlayback()
    {
        _host.Repository.AppConfig.EnablePrimaryPlayback = true;

        await _host.RunAsync(DisableMonitorOutputCommand.Build);

        Assert.True(_host.Repository.AppConfig.EnablePrimaryPlayback);
    }

    /* The user is told the monitor output is disabled */
    [Fact]
    public async Task Disable_Always_PrintsDisabled()
    {
        var run = await _host.RunAsync(DisableMonitorOutputCommand.Build);

        Assert.Contains("Monitor audio output disabled.", run.Output);
    }
}
