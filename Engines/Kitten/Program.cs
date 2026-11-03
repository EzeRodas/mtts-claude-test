using MoonTTS.Engine.Kitten;
using MoonTTS.Engine.Protocol;

/* KittenTTS out-of-process engine worker entrypoint.
   Communicates with the Moon-TTS host over binary-framed stdin/stdout */

var input = Console.OpenStandardInput();
var output = Console.OpenStandardOutput();

/* Diagnostics go to stderr; stdout is reserved exclusively for binary protocol frames */
Console.SetOut(Console.Error);

using var worker = new KittenWorker(output);

try
{
    while (await EngineIpcProtocol.ReadFrameAsync(input) is { } frame)
    {
        bool keepRunning = await worker.HandleAsync(frame);
        if (!keepRunning)
            break;
    }
}
finally
{
    await worker.WaitForIdleAsync();
}
