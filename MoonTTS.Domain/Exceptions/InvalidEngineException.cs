namespace MoonTTS.Domain.Exceptions;

public class InvalidEngineException : InvalidOperationException
{
    public InvalidEngineException(string message) : base(message) { }
}