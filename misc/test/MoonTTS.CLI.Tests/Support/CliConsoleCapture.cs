namespace MoonTTS.CLI.Tests.Support;

/* File Structure:
        - CliConsoleCapture(string? stdin)
        - Out : TextWriter
        - Error : TextWriter
        - Output : string
        - ErrorOutput : string
        - Dispose() : void
*/

/* Redirects Console.Out, Console.Error and Console.In for one test, standing in for the user's terminal:
   everything the CLI prints is recorded and confirmation prompts read the scripted stdin (empty means EOF).
   The console is process wide: tests using this must be in [Collection(TestCollections.Environment)] */
public sealed class CliConsoleCapture : IDisposable
{
    private readonly TextWriter _originalOut = Console.Out;
    private readonly TextWriter _originalError = Console.Error;
    private readonly TextReader _originalIn = Console.In;
    private readonly StringWriter _out = new();
    private readonly StringWriter _error = new();

    /* Swaps the console streams; `stdin` is what the user would type (null or empty behaves like EOF) */
    public CliConsoleCapture(string? stdin = null)
    {
        Console.SetOut(_out);
        Console.SetError(_error);
        Console.SetIn(new StringReader(stdin ?? string.Empty));
    }

    /* The writer currently installed as Console.Out */
    public TextWriter Out => Console.Out;

    /* The writer currently installed as Console.Error */
    public TextWriter Error => Console.Error;

    /* Everything written to standard output so far */
    public string Output => _out.ToString();

    /* Everything written to standard error so far */
    public string ErrorOutput => _error.ToString();

    /* Restores the original console streams */
    public void Dispose()
    {
        Console.SetOut(_originalOut);
        Console.SetError(_originalError);
        Console.SetIn(_originalIn);
    }
}
