using MoonTTS.Domain.Audio;

namespace MoonTTS.Domain.Tests.Audio;

/* Verifies the defaults of SpeechOptions: everything enabled, no overrides */
public sealed class SpeechOptionsTests
{
    /* History recording is enabled by default */
    [Fact]
    public void RecordHistory_Default_IsTrue()
    {
        Assert.True(new SpeechOptions().RecordHistory);
    }

    /* Dictionary replacements are applied by default */
    [Fact]
    public void ApplyDictionary_Default_IsTrue()
    {
        Assert.True(new SpeechOptions().ApplyDictionary);
    }

    /* Playback is enabled by default */
    [Fact]
    public void EnablePlayback_Default_IsTrue()
    {
        Assert.True(new SpeechOptions().EnablePlayback);
    }

    /* No speed, voice, volume or export overrides are set by default */
    [Fact]
    public void Overrides_Default_AreNull()
    {
        var options = new SpeechOptions();

        Assert.Null(options.Speed);
        Assert.Null(options.Voice);
        Assert.Null(options.PrimaryVolume);
        Assert.Null(options.MonitorVolume);
        Assert.Null(options.ExportPath);
    }

    /* Every property keeps the value it is set to */
    [Fact]
    public void Properties_WhenSet_KeepValues()
    {
        var options = new SpeechOptions
        {
            RecordHistory = false,
            ApplyDictionary = false,
            EnablePlayback = false,
            Speed = 1.5f,
            Voice = "af_heart",
            PrimaryVolume = 40,
            MonitorVolume = 60,
            ExportPath = "/tmp/out.wav"
        };

        Assert.False(options.RecordHistory);
        Assert.False(options.ApplyDictionary);
        Assert.False(options.EnablePlayback);
        Assert.Equal(1.5f, options.Speed);
        Assert.Equal("af_heart", options.Voice);
        Assert.Equal(40, options.PrimaryVolume);
        Assert.Equal(60, options.MonitorVolume);
        Assert.Equal("/tmp/out.wav", options.ExportPath);
    }
}
