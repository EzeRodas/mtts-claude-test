using MoonTTS.CLI.commands;
using MoonTTS.CLI.Tests.Support;

namespace MoonTTS.CLI.Tests.Commands.Playback;

[Collection(TestCollections.Environment)]
public sealed class PauseCommandTests : IDisposable
{
    private readonly CliTestHost _host = new();

    /* Releases the host */
    public void Dispose() => _host.Dispose();

    /* Build creates the "pause" command without arguments */
    [Fact]
    public void Build_Always_CreatesPauseCommand()
    {
        var command = PauseCommand.Build(_host.Services);

        Assert.Equal("pause", command.Name);
        Assert.Empty(command.Arguments);
    }

    /* Pause pauses the speech pipeline once and exits with 0 */
    [Fact]
    public async Task Pause_Always_PausesPipeline()
    {
        var run = await _host.RunAsync(PauseCommand.Build);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(1, _host.Pipeline.PauseCount);
        Assert.Equal(0, _host.Pipeline.StopCount);
    }

    /* The user is told playback is paused */
    [Fact]
    public async Task Pause_Always_PrintsPaused()
    {
        var run = await _host.RunAsync(PauseCommand.Build);

        Assert.Contains("Playback paused.", run.Output);
    }
}
