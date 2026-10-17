using MoonTTS.Domain.Common;

namespace MoonTTS.Domain.Tests.Common;

/* Verifies the non-generic Result factories and their derived state */
public sealed class ResultTests
{
    /* Success() produces a successful result */
    [Fact]
    public void Success_Always_IsSuccess()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
    }

    /* Success() is not a failure */
    [Fact]
    public void Success_Always_IsNotFailure()
    {
        var result = Result.Success();

        Assert.False(result.IsFailure);
    }

    /* Success() carries neither an error message nor an exception */
    [Fact]
    public void Success_Always_HasNoErrorOrException()
    {
        var result = Result.Success();

        Assert.Null(result.ErrorMessage);
        Assert.Null(result.Exception);
    }

    /* Failure(string) produces a failed result */
    [Fact]
    public void FailureWithMessage_Always_IsFailure()
    {
        var result = Result.Failure("boom");

        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
    }

    /* Failure(string) keeps the error message */
    [Fact]
    public void FailureWithMessage_Always_KeepsMessage()
    {
        var result = Result.Failure("boom");

        Assert.Equal("boom", result.ErrorMessage);
    }

    /* Failure(string) without an exception leaves Exception null */
    [Fact]
    public void FailureWithMessage_NoException_HasNullException()
    {
        var result = Result.Failure("boom");

        Assert.Null(result.Exception);
    }

    /* Failure(string, Exception) keeps both the message and the exception */
    [Fact]
    public void FailureWithMessageAndException_Always_KeepsBoth()
    {
        var exception = new InvalidOperationException("inner");

        var result = Result.Failure("outer", exception);

        Assert.Equal("outer", result.ErrorMessage);
        Assert.Same(exception, result.Exception);
    }

    /* Failure(Exception) uses the exception's message as the error message */
    [Fact]
    public void FailureWithException_Always_UsesExceptionMessage()
    {
        var exception = new InvalidOperationException("inner");

        var result = Result.Failure(exception);

        Assert.Equal("inner", result.ErrorMessage);
    }

    /* Failure(Exception) keeps the exception instance */
    [Fact]
    public void FailureWithException_Always_KeepsException()
    {
        var exception = new InvalidOperationException("inner");

        var result = Result.Failure(exception);

        Assert.True(result.IsFailure);
        Assert.Same(exception, result.Exception);
    }
}
