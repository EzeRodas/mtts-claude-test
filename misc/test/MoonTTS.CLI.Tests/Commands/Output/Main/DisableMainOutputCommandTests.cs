using MoonTTS.CLI.commands.Output.Main;
using MoonTTS.CLI.Tests.Support;

namespace MoonTTS.CLI.Tests.Commands.Output.Main;

[Collection(TestCollections.Environment)]
public sealed class DisableMainOutputCommandTests : IDisposable
{
    private readonly CliTestHost _host = new();

    /* Releases the host */
    public void Dispose() => _host.Dispose();

    /* Build creates the "disable" command describing that it disables the primary output */
    [Fact]
    public void Build_Always_CreatesDisableCommand()
    {
        var command = DisableMainOutputCommand.Build(_host.Services);

        Assert.Equal("disable", command.Name);
        Assert.Equal("Disables the primary audio output.", command.Description);
    }

    /* An enabled primary output is disabled and saved */
    [Fact]
    public async Task Disable_Enabled_DisablesAndSavesPrimaryPlayback()
    {
        _host.Repository.AppConfig.EnablePrimaryPlayback = true;

        var run = await _host.RunAsync(DisableMainOutputCommand.Build);

        Assert.Equal(0, run.ExitCode);
        Assert.False(_host.Repository.AppConfig.EnablePrimaryPlayback);
        Assert.Equal(1, _host.Repository.AppConfigSaveCount);
    }

    /* The monitor output is left as it was */
    [Fact]
    public async Task Disable_Always_KeepsMonitorPlayback()
    {
        _host.Repository.AppConfig.EnableMonitorPlayback = true;

        await _host.RunAsync(DisableMainOutputCommand.Build);

        Assert.True(_host.Repository.AppConfig.EnableMonitorPlayback);
    }

    /* The user is told the main output is disabled */
    [Fact]
    public async Task Disable_Always_PrintsDisabled()
    {
        var run = await _host.RunAsync(DisableMainOutputCommand.Build);

        Assert.Contains("Main audio output disabled.", run.Output);
    }
}
