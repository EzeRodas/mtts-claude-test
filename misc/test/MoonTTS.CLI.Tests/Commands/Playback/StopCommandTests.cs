using MoonTTS.CLI.commands;
using MoonTTS.CLI.Tests.Support;

namespace MoonTTS.CLI.Tests.Commands.Playback;

[Collection(TestCollections.Environment)]
public sealed class StopCommandTests : IDisposable
{
    private readonly CliTestHost _host = new();

    /* Releases the host */
    public void Dispose() => _host.Dispose();

    /* Build creates the "stop" command without arguments */
    [Fact]
    public void Build_Always_CreatesStopCommand()
    {
        var command = StopCommand.Build(_host.Services);

        Assert.Equal("stop", command.Name);
        Assert.Empty(command.Arguments);
    }

    /* Stop stops the speech pipeline once and exits with 0 */
    [Fact]
    public async Task Stop_Always_StopsPipeline()
    {
        var run = await _host.RunAsync(StopCommand.Build);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(1, _host.Pipeline.StopCount);
        Assert.Equal(0, _host.Pipeline.PauseCount);
    }

    /* The user is told playback is stopped */
    [Fact]
    public async Task Stop_Always_PrintsStopped()
    {
        var run = await _host.RunAsync(StopCommand.Build);

        Assert.Contains("Playback stopped.", run.Output);
    }
}
