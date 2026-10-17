using MoonTTS.Infrastructure.Audio;

namespace MoonTTS.Infrastructure.Tests.Audio;

/* AudioPlayerManager choosing the IAudioPlayer of the current platform */
public sealed class AudioPlayerManagerTests
{
    /* On Linux the PipeWire/PulseAudio player is created */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void Create_Linux_ReturnsLinuxAudioPlayer()
    {
        Assert.IsType<LinuxAudioPlayer>(AudioPlayerManager.Create());
    }

    /* Each call creates a new player */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void Create_CalledTwice_ReturnsDistinctPlayers()
    {
        Assert.NotSame(AudioPlayerManager.Create(), AudioPlayerManager.Create());
    }
}
