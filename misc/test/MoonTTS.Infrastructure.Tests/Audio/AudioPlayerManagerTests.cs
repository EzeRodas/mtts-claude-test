using MoonTTS.Infrastructure.Audio;

namespace MoonTTS.Infrastructure.Tests.Audio;

/* AudioPlayerManager choosing the IAudioPlayer of the current platform */
public sealed class AudioPlayerManagerTests
{
#if WINDOWS
    /* The Windows build always creates the waveOut player */
    [Fact]
    public void Create_WindowsBuild_ReturnsWindowsAudioPlayer()
    {
        Assert.IsType<WindowsAudioPlayer>(AudioPlayerManager.Create());
    }
#else
    /* On Linux the PipeWire/PulseAudio player is created (LinuxAudioPlayer only exists outside the Windows build) */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void Create_Linux_ReturnsLinuxAudioPlayer()
    {
        Assert.IsType<LinuxAudioPlayer>(AudioPlayerManager.Create());
    }
#endif

    /* Each call creates a new player */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void Create_CalledTwice_ReturnsDistinctPlayers()
    {
        Assert.NotSame(AudioPlayerManager.Create(), AudioPlayerManager.Create());
    }
}
