using MoonTTS.Domain.Common;

namespace MoonTTS.Domain.Tests.Common;

/* Verifies the generic Result<T> factories, derived state and implicit conversion */
public sealed class ResultOfTTests
{
    /* Success(T) produces a successful result */
    [Fact]
    public void Success_Always_IsSuccess()
    {
        var result = Result<int>.Success(42);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
    }

    /* Success(T) holds the given value */
    [Fact]
    public void Success_Always_HoldsValue()
    {
        var result = Result<string>.Success("value");

        Assert.Equal("value", result.Value);
    }

    /* Success(T) carries neither an error message nor an exception */
    [Fact]
    public void Success_Always_HasNoErrorOrException()
    {
        var result = Result<int>.Success(42);

        Assert.Null(result.ErrorMessage);
        Assert.Null(result.Exception);
    }

    /* Failure(string) produces a failed result */
    [Fact]
    public void FailureWithMessage_Always_IsFailure()
    {
        var result = Result<int>.Failure("boom");

        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
    }

    /* Failure(string) keeps the error message and leaves Exception null */
    [Fact]
    public void FailureWithMessage_NoException_KeepsMessageOnly()
    {
        var result = Result<int>.Failure("boom");

        Assert.Equal("boom", result.ErrorMessage);
        Assert.Null(result.Exception);
    }

    /* Failure(string) of a value type leaves Value at its default */
    [Fact]
    public void FailureWithMessage_ValueType_HasDefaultValue()
    {
        var result = Result<int>.Failure("boom");

        Assert.Equal(0, result.Value);
    }

    /* Failure(string) of a reference type leaves Value null */
    [Fact]
    public void FailureWithMessage_ReferenceType_HasNullValue()
    {
        var result = Result<string>.Failure("boom");

        Assert.Null(result.Value);
    }

    /* Failure(string, Exception) keeps both the message and the exception */
    [Fact]
    public void FailureWithMessageAndException_Always_KeepsBoth()
    {
        var exception = new InvalidOperationException("inner");

        var result = Result<int>.Failure("outer", exception);

        Assert.Equal("outer", result.ErrorMessage);
        Assert.Same(exception, result.Exception);
    }

    /* Failure(Exception) uses the exception's message as the error message */
    [Fact]
    public void FailureWithException_Always_UsesExceptionMessage()
    {
        var exception = new InvalidOperationException("inner");

        var result = Result<int>.Failure(exception);

        Assert.Equal("inner", result.ErrorMessage);
    }

    /* Failure(Exception) keeps the exception instance and produces a failure */
    [Fact]
    public void FailureWithException_Always_KeepsException()
    {
        var exception = new InvalidOperationException("inner");

        var result = Result<int>.Failure(exception);

        Assert.True(result.IsFailure);
        Assert.Same(exception, result.Exception);
    }

    /* The implicit conversion from T produces a successful result */
    [Fact]
    public void ImplicitOperator_FromValue_IsSuccess()
    {
        Result<int> result = 7;

        Assert.True(result.IsSuccess);
    }

    /* The implicit conversion from T holds the converted value */
    [Fact]
    public void ImplicitOperator_FromValue_HoldsValue()
    {
        Result<string> result = "converted";

        Assert.Equal("converted", result.Value);
        Assert.Null(result.ErrorMessage);
    }
}
