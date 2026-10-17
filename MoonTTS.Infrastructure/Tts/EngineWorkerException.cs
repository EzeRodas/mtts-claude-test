namespace MoonTTS.Infrastructure.Tts;

/* Raised when an engine worker replies with an Error frame, crashes, or breaks the protocol */
public sealed class EngineWorkerException : Exception
{
    /* Error code reported by the worker, empty when the failure was detected by the host */
    public string Code { get; }

    /* Creates the exception with an optional worker error code */
    public EngineWorkerException(string message, string code = "", Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }
}
