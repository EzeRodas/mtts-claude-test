namespace MoonTTS.Domain.Exceptions;

public class InvalidVoiceException : InvalidOperationException
{
    public InvalidVoiceException(string message) : base(message) { }
}