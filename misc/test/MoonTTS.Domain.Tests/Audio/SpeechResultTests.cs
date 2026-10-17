using MoonTTS.Domain.Audio;

namespace MoonTTS.Domain.Tests.Audio;

/* Verifies the defaults and init-only values of SpeechResult */
public sealed class SpeechResultTests
{
    /* A default result reports zero durations and zero bytes */
    [Fact]
    public void Properties_Default_AreZero()
    {
        var result = new SpeechResult();

        Assert.Equal(TimeSpan.Zero, result.AudioDuration);
        Assert.Equal(TimeSpan.Zero, result.TimeToFirstSound);
        Assert.Equal(0, result.TotalPcmBytes);
    }

    /* Init-only properties keep the values given at construction */
    [Fact]
    public void Properties_WhenInitialized_KeepValues()
    {
        var result = new SpeechResult
        {
            AudioDuration = TimeSpan.FromSeconds(2),
            TimeToFirstSound = TimeSpan.FromMilliseconds(150),
            TotalPcmBytes = 96000
        };

        Assert.Equal(TimeSpan.FromSeconds(2), result.AudioDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(150), result.TimeToFirstSound);
        Assert.Equal(96000, result.TotalPcmBytes);
    }
}
