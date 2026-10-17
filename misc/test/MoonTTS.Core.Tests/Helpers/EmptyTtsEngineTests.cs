using MoonTTS.Core.Helpers;

namespace MoonTTS.Core.Tests.Helpers;

/* Tests for the EmptyTtsEngine null object */
public sealed class EmptyTtsEngineTests
{
    /* The empty engine is never available */
    [Fact]
    public void IsAvailable_Always_IsFalse()
    {
        var engine = new EmptyTtsEngine();

        Assert.False(engine.IsAvailable);
    }

    /* The placeholder output format is valid (no zero fields), so consumers can compute with it */
    [Fact]
    public void OutputFormat_Always_IsUsablePlaceholder()
    {
        var format = new EmptyTtsEngine().OutputFormat;

        Assert.True(format.SampleRate > 0);
        Assert.True(format.Channels > 0);
        Assert.True(format.BitDepth > 0);
    }

    /* Speed starts at the neutral 1.0 */
    [Fact]
    public void Speed_Default_IsOne()
    {
        var engine = new EmptyTtsEngine();

        Assert.Equal(1.0f, engine.Speed);
    }

    /* Speed keeps the value it is given */
    [Fact]
    public void Speed_Set_KeepsValue()
    {
        var engine = new EmptyTtsEngine { Speed = 1.5f };

        Assert.Equal(1.5f, engine.Speed);
    }

    /* The active voice starts empty */
    [Fact]
    public void ActiveVoice_Default_IsEmpty()
    {
        var engine = new EmptyTtsEngine();

        Assert.Equal(string.Empty, engine.ActiveVoice);
    }

    /* The active voice keeps the value it is given */
    [Fact]
    public void ActiveVoice_Set_KeepsValue()
    {
        var engine = new EmptyTtsEngine { ActiveVoice = "af_heart" };

        Assert.Equal("af_heart", engine.ActiveVoice);
    }

    /* There are no voices to list */
    [Fact]
    public void ListVoices_Always_ReturnsEmpty()
    {
        Assert.Empty(new EmptyTtsEngine().ListVoices());
    }

    /* Streaming yields no chunks and completes */
    [Fact]
    public async Task SynthesizeStreamAsync_AnyText_YieldsNoChunks()
    {
        var engine = new EmptyTtsEngine();
        int count = 0;

        await foreach (var _ in engine.SynthesizeStreamAsync("hello", cancellationToken: TestContext.Current.CancellationToken))
            count++;

        Assert.Equal(0, count);
    }

    /* Whole-buffer synthesis fails, telling the caller to select a model */
    [Fact]
    public async Task SynthesizeAsync_AnyText_ReturnsNoEngineFailure()
    {
        var engine = new EmptyTtsEngine();

        var result = await engine.SynthesizeAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Contains("No TTS engine is selected", result.ErrorMessage);
    }

    /* Disposing is a harmless no-op, even twice */
    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var engine = new EmptyTtsEngine();

        var exception = Record.Exception(() =>
        {
            engine.Dispose();
            engine.Dispose();
        });

        Assert.Null(exception);
    }
}
