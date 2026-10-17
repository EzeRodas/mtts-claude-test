using System.Globalization;
using System.Text.Json;
using MoonTTS.Core.Services;
using MoonTTS.Domain.Common;
using MoonTTS.Domain.Interfaces;
using MoonTTS.Domain.ValueObjects;
using MoonTTS.Engine.Protocol;
using MoonTTS.Infrastructure.Tts;
using MoonTTS.Tests.DummyEngine;

namespace MoonTTS.Infrastructure.Tests.Tts;

/* File Structure:
        - DummyEngineHarness(DummyEngineOptions? options, params EngineModelManifest[] models)
        - Temp : TempDirectory
        - EngineDirectory : string
        - ModelsDirectory : string
        - Options : DummyEngineOptions
        - Repository : FakeConfigurationRepository
        - Settings : SettingsManager
        - VoiceStore : EngineVoiceStore
        - Catalog : EngineCatalog
        - Engine : EngineDescriptor
        - GetModel(string modelName) : ModelMetadata
        - StartProcess() : EngineProcess
        - CreateEngineAsync(string modelName, CancellationToken cancellationToken) : Task<Result<ITtsEngine>>
        - ReadRequests() : IReadOnlyList<LoggedFrame>
        - ReadExitLog() : string
        - ReadWorkerPid() : int
        - UpdateOptions(Action<DummyEngineOptions> change) : void
        - Dispose() : void
*/

/* Installs the scriptable dummy worker as a real engine folder and wires the host-side collaborators
   (catalog, settings with an isolated models folder, voice store) so engine host tests stay short */
public sealed class DummyEngineHarness : IDisposable
{
    public TempDirectory Temp { get; } = new();
    public string EngineDirectory { get; }
    public string ModelsDirectory { get; }
    public DummyEngineOptions Options { get; }
    public FakeConfigurationRepository Repository { get; } = new();
    public SettingsManager Settings { get; }
    public EngineVoiceStore VoiceStore { get; } = new();
    public EngineCatalog Catalog { get; }

    /* Installs the worker with request, exit and pid logs pointed inside the temp folder */
    public DummyEngineHarness(DummyEngineOptions? options = null, params EngineModelManifest[] models)
    {
        Options = options ?? new DummyEngineOptions();
        Options.RequestLogPath ??= Temp.Combine("requests.log");
        Options.ExitLogPath ??= Temp.Combine("exit.log");
        Options.PidFilePath ??= Temp.Combine("worker.pid");

        string enginesDirectory = Temp.CreateSubdirectory("engines");
        ModelsDirectory = Temp.CreateSubdirectory("models");
        EngineDirectory = DummyEngineInstaller.Install(enginesDirectory, "Dummy", Options, models);

        Repository.AppConfig.CustomModelsDirectory = ModelsDirectory;
        Settings = new SettingsManager(Repository);
        Catalog = new EngineCatalog(enginesDirectory);
    }

    /* The installed engine as discovered by the catalog */
    public EngineDescriptor Engine => Catalog.GetEngine(Options.EngineId)
                                      ?? throw new InvalidOperationException("The dummy engine was not discovered.");

    /* One model of the installed engine */
    public ModelMetadata GetModel(string modelName = "default") =>
        Catalog.GetModelMetadata(new ModelId(Options.EngineId, modelName))
        ?? throw new InvalidOperationException($"Model '{modelName}' is not declared.");

    /* Starts the worker process directly */
    public EngineProcess StartProcess() => EngineProcess.Start(Engine);

    /* Starts an ExternalTtsEngine for one model */
    public Task<Result<ITtsEngine>> CreateEngineAsync(string modelName = "default", CancellationToken cancellationToken = default) =>
        ExternalTtsEngine.CreateAsync(Engine, GetModel(modelName), Settings, VoiceStore, cancellationToken);

    /* Every frame the worker received, oldest first. JSON payloads span several lines in the log */
    public IReadOnlyList<LoggedFrame> ReadRequests()
    {
        var frames = new List<LoggedFrame>();
        if (!File.Exists(Options.RequestLogPath))
            return frames;

        foreach (string line in File.ReadAllLines(Options.RequestLogPath!))
        {
            int space = line.IndexOf(' ');
            string head = space < 0 ? line : line[..space];
            if (head.Length > 0 && char.IsLetter(head[0]) && Enum.TryParse(head, out EngineOpcode opcode))
                frames.Add(new LoggedFrame(opcode, space < 0 ? string.Empty : line[(space + 1)..]));
            else if (frames.Count > 0)
                frames[^1] = frames[^1] with { Payload = frames[^1].Payload + "\n" + line };
        }
        return frames;
    }

    /* Why the worker's frame loop ended ("shutdown" / "stdin-closed"), empty while it runs or when killed */
    public string ReadExitLog() => File.Exists(Options.ExitLogPath) ? File.ReadAllText(Options.ExitLogPath!) : string.Empty;

    /* Process id of the most recently started worker */
    public int ReadWorkerPid() => int.Parse(File.ReadAllText(Options.PidFilePath!), CultureInfo.InvariantCulture);

    /* Rewrites dummy-engine.json; workers started afterwards (e.g. restarts) pick the new behaviour up */
    public void UpdateOptions(Action<DummyEngineOptions> change)
    {
        change(Options);
        File.WriteAllText(Path.Combine(EngineDirectory, DummyEngineOptions.FileName), JsonSerializer.Serialize(Options));
    }

    /* Removes the temp folder */
    public void Dispose() => Temp.Dispose();
}

/* One frame received by the dummy worker */
public sealed record LoggedFrame(EngineOpcode Opcode, string Payload)
{
    /* Parses the JSON payload */
    public JsonElement Json => JsonDocument.Parse(Payload).RootElement;
}
