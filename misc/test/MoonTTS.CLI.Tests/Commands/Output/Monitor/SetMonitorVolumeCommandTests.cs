using MoonTTS.CLI.commands.Output.Monitor;
using MoonTTS.CLI.Tests.Support;

namespace MoonTTS.CLI.Tests.Commands.Output.Monitor;

[Collection(TestCollections.Environment)]
public sealed class SetMonitorVolumeCommandTests : IDisposable
{
    private readonly CliTestHost _host = new();

    /* Releases the host */
    public void Dispose() => _host.Dispose();

    /* Build creates the "vol" command with an optional "level" argument */
    [Fact]
    public void Build_Always_CreatesVolCommandWithOptionalLevel()
    {
        var command = SetMonitorVolumeCommand.Build(_host.Services);

        Assert.Equal("vol", command.Name);
        var level = Assert.Single(command.Arguments);
        Assert.Equal("level", level.Name);
        Assert.Equal(0, level.Arity.MinimumNumberOfValues);
    }

    /* A level inside 0-100 is saved as the monitor volume and reported */
    [Fact]
    public async Task Vol_LevelInRange_SavesMonitorVolume()
    {
        var run = await _host.RunAsync(SetMonitorVolumeCommand.Build, "42");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(42, _host.Repository.AppConfig.MonitorVolume);
        Assert.Equal(1, _host.Repository.AppConfigSaveCount);
        Assert.Contains("Monitor audio volume set to: 42%", run.Output);
    }

    /* A level above 100 is clamped to 100 */
    [Fact]
    public async Task Vol_LevelAboveHundred_ClampsToHundred()
    {
        _host.Repository.AppConfig.MonitorVolume = 10;

        await _host.RunAsync(SetMonitorVolumeCommand.Build, "250");

        Assert.Equal(100, _host.Repository.AppConfig.MonitorVolume);
    }

    /* A negative level is clamped to 0 */
    [Fact]
    public async Task Vol_NegativeLevel_ClampsToZero()
    {
        await _host.RunAsync(SetMonitorVolumeCommand.Build, "-5");

        Assert.Equal(0, _host.Repository.AppConfig.MonitorVolume);
    }

    /* The primary volume is left as it was */
    [Fact]
    public async Task Vol_Level_KeepsPrimaryVolume()
    {
        _host.Repository.AppConfig.PrimaryVolume = 33;

        await _host.RunAsync(SetMonitorVolumeCommand.Build, "42");

        Assert.Equal(33, _host.Repository.AppConfig.PrimaryVolume);
    }

    /* Without a level the current volume is shown and nothing is saved */
    [Fact]
    public async Task Vol_NoLevel_PrintsCurrentVolumeWithoutSaving()
    {
        _host.Repository.AppConfig.MonitorVolume = 64;

        var run = await _host.RunAsync(SetMonitorVolumeCommand.Build);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Monitor audio volume is currently: 64%", run.Output);
        Assert.Equal(0, _host.Repository.AppConfigSaveCount);
    }

    /* A level that is not a number is a parse error and nothing is saved */
    [Fact]
    public async Task Vol_NotANumber_ReturnsParseError()
    {
        var run = await _host.RunAsync(SetMonitorVolumeCommand.Build, "loud");

        Assert.NotEqual(0, run.ExitCode);
        Assert.Equal(0, _host.Repository.AppConfigSaveCount);
    }
}
