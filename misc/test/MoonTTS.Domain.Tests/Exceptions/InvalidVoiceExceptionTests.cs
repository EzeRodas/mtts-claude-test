using MoonTTS.Domain.Exceptions;

namespace MoonTTS.Domain.Tests.Exceptions;

/* Verifies the message and type hierarchy of InvalidVoiceException */
public sealed class InvalidVoiceExceptionTests
{
    /* The constructor keeps the message */
    [Fact]
    public void Constructor_Message_KeepsMessage()
    {
        var exception = new InvalidVoiceException("Unknown voice 'x'.");

        Assert.Equal("Unknown voice 'x'.", exception.Message);
    }

    /* The exception is an InvalidOperationException so generic handlers still catch it */
    [Fact]
    public void Type_Always_IsInvalidOperationException()
    {
        Assert.IsAssignableFrom<InvalidOperationException>(new InvalidVoiceException("x"));
    }
}
