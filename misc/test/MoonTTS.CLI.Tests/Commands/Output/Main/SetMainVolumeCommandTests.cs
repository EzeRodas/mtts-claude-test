using MoonTTS.CLI.commands.Output.Main;
using MoonTTS.CLI.Tests.Support;

namespace MoonTTS.CLI.Tests.Commands.Output.Main;

[Collection(TestCollections.Environment)]
public sealed class SetMainVolumeCommandTests : IDisposable
{
    private readonly CliTestHost _host = new();

    /* Releases the host */
    public void Dispose() => _host.Dispose();

    /* Build creates the "vol" command with an optional "level" argument */
    [Fact]
    public void Build_Always_CreatesVolCommandWithOptionalLevel()
    {
        var command = SetMainVolumeCommand.Build(_host.Services);

        Assert.Equal("vol", command.Name);
        var level = Assert.Single(command.Arguments);
        Assert.Equal("level", level.Name);
        Assert.Equal(0, level.Arity.MinimumNumberOfValues);
    }

    /* A level inside 0-100 is saved as the primary volume and reported */
    [Fact]
    public async Task Vol_LevelInRange_SavesPrimaryVolume()
    {
        var run = await _host.RunAsync(SetMainVolumeCommand.Build, "42");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(42, _host.Repository.AppConfig.PrimaryVolume);
        Assert.Equal(1, _host.Repository.AppConfigSaveCount);
        Assert.Contains("Main audio volume set to: 42%", run.Output);
    }

    /* A level above 100 is clamped to 100 */
    [Fact]
    public async Task Vol_LevelAboveHundred_ClampsToHundred()
    {
        _host.Repository.AppConfig.PrimaryVolume = 10;

        await _host.RunAsync(SetMainVolumeCommand.Build, "250");

        Assert.Equal(100, _host.Repository.AppConfig.PrimaryVolume);
    }

    /* A negative level is clamped to 0 */
    [Fact]
    public async Task Vol_NegativeLevel_ClampsToZero()
    {
        await _host.RunAsync(SetMainVolumeCommand.Build, "-5");

        Assert.Equal(0, _host.Repository.AppConfig.PrimaryVolume);
    }

    /* The monitor volume is left as it was */
    [Fact]
    public async Task Vol_Level_KeepsMonitorVolume()
    {
        _host.Repository.AppConfig.MonitorVolume = 33;

        await _host.RunAsync(SetMainVolumeCommand.Build, "42");

        Assert.Equal(33, _host.Repository.AppConfig.MonitorVolume);
    }

    /* Without a level the current volume is shown and nothing is saved */
    [Fact]
    public async Task Vol_NoLevel_PrintsCurrentVolumeWithoutSaving()
    {
        _host.Repository.AppConfig.PrimaryVolume = 64;

        var run = await _host.RunAsync(SetMainVolumeCommand.Build);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Main audio volume is currently: 64%", run.Output);
        Assert.Equal(0, _host.Repository.AppConfigSaveCount);
    }

    /* A level that is not a number is a parse error and nothing is saved */
    [Fact]
    public async Task Vol_NotANumber_ReturnsParseError()
    {
        var run = await _host.RunAsync(SetMainVolumeCommand.Build, "loud");

        Assert.NotEqual(0, run.ExitCode);
        Assert.Equal(0, _host.Repository.AppConfigSaveCount);
    }
}
