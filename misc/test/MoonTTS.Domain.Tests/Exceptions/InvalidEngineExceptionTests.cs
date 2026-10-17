using MoonTTS.Domain.Exceptions;

namespace MoonTTS.Domain.Tests.Exceptions;

/* Verifies the message and type hierarchy of InvalidEngineException */
public sealed class InvalidEngineExceptionTests
{
    /* The constructor keeps the message */
    [Fact]
    public void Constructor_Message_KeepsMessage()
    {
        var exception = new InvalidEngineException("Unknown engine 'x'.");

        Assert.Equal("Unknown engine 'x'.", exception.Message);
    }

    /* The exception is an InvalidOperationException so generic handlers still catch it */
    [Fact]
    public void Type_Always_IsInvalidOperationException()
    {
        Assert.IsAssignableFrom<InvalidOperationException>(new InvalidEngineException("x"));
    }
}
