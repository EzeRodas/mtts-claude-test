using MoonTTS.Domain.Entities;

namespace MoonTTS.Domain.Tests.Entities;

/* Verifies the defaults of the per-model EngineConfig */
public sealed class EngineConfigTests
{
    /* No voice is chosen by default */
    [Fact]
    public void VoiceId_Default_IsEmpty()
    {
        Assert.Equal(string.Empty, new EngineConfig().VoiceId);
    }

    /* Speech runs at normal speed by default */
    [Fact]
    public void Speed_Default_IsOne()
    {
        Assert.Equal(1.0f, new EngineConfig().Speed);
    }

    /* Both properties keep the values they are set to */
    [Fact]
    public void Properties_WhenSet_KeepValues()
    {
        var config = new EngineConfig { VoiceId = "af_heart", Speed = 1.25f };

        Assert.Equal("af_heart", config.VoiceId);
        Assert.Equal(1.25f, config.Speed);
    }
}
