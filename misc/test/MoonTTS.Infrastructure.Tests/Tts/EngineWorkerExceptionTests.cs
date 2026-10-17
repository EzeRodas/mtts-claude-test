using MoonTTS.Infrastructure.Tts;

namespace MoonTTS.Infrastructure.Tests.Tts;

/* EngineWorkerException carrying the worker's error code */
public sealed class EngineWorkerExceptionTests
{
    /* A host-detected failure has a message and an empty code */
    [Fact]
    public void Constructor_MessageOnly_HasEmptyCode()
    {
        var ex = new EngineWorkerException("worker crashed");

        Assert.Equal("worker crashed", ex.Message);
        Assert.Equal(string.Empty, ex.Code);
        Assert.Null(ex.InnerException);
    }

    /* A worker-reported failure keeps the worker's code */
    [Fact]
    public void Constructor_WithCode_KeepsCode()
    {
        var ex = new EngineWorkerException("model missing", "model_not_found");

        Assert.Equal("model_not_found", ex.Code);
    }

    /* The underlying exception is kept as inner exception */
    [Fact]
    public void Constructor_WithInnerException_KeepsInnerException()
    {
        var inner = new IOException("broken pipe");

        var ex = new EngineWorkerException("not accepting requests", innerException: inner);

        Assert.Same(inner, ex.InnerException);
    }
}
