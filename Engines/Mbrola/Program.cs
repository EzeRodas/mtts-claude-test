using MoonTTS.Engine.Mbrola;
using MoonTTS.Engine.Protocol;

/* Entry point of the MBROLA engine worker. stdin/stdout carry the binary protocol, so anything a library
   prints is redirected to stderr (which the host forwards to its log) before the protocol starts */
var input = Console.OpenStandardInput();
var output = Console.OpenStandardOutput();
Console.SetOut(Console.Error);

using var worker = new MbrolaWorker(output);
try
{
    /* Runs until the host sends Shutdown or closes stdin */
    while (await EngineIpcProtocol.ReadFrameAsync(input) is { } frame)
    {
        if (!await worker.HandleAsync(frame))
            break;
    }
}
catch (Exception ex) when (ex is IOException or InvalidDataException)
{
    Console.Error.WriteLine($"Protocol channel closed: {ex.Message}");
}

await worker.WaitForIdleAsync();
return 0;
