using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using MoonTTS.Core.Services;
using MoonTTS.Domain.Interfaces;
using MoonTTS.Domain.ValueObjects;
using MoonTTS.Infrastructure.Daemon;

namespace MoonTTS.CLI.Tests.Support;

/* File Structure:
        - CliTestHost(IModelDownloader? downloader)
        - Services : ServiceProvider
        - ModelsDirectory : string
        - Temp : TempDirectory
        - Settings : SettingsManager
        - AddModel(string engineId, string modelName, params string[] files) : ModelMetadata
        - AddDownloadedModel(string engineId, string modelName, params string[] files) : ModelMetadata
        - ModelFilePath(ModelMetadata model, string file) : string
        - AddEngine(ModelMetadata model) : FakeTtsEngine
        - ActivateModel(ModelMetadata model) : FakeTtsEngine
        - RunAsync(Func<IServiceProvider, Command> build, params string[] args) : Task<CliRun>
        - RunWithInputAsync(Func<IServiceProvider, Command> build, string stdin, params string[] args) : Task<CliRun>
        - Dispose() : void
*/

/* The CLI's DI container rebuilt over the shared fakes: real Core services (SettingsManager, DownloadManager,
   TtsEngineProvider, DictionaryManager) and real daemon client/server on top of in-memory configuration, model
   registry, downloader, engine factory, audio player and speech pipeline. Models live in a temp folder */
public sealed class CliTestHost : IDisposable
{
    private readonly TempDirectory _temp = new();

    public FakeConfigurationRepository Repository { get; } = new();
    public FakeModelRegistry Registry { get; } = new();
    public FakeModelDownloader Downloader { get; } = new();
    public FakeTtsEngineFactory EngineFactory { get; } = new();
    public FakeAudioPlayer AudioPlayer { get; } = new();
    public FakeSpeechPipeline Pipeline { get; } = new();

    /* The provider handed to every command's Build */
    public ServiceProvider Services { get; }

    /* Folder models are "downloaded" into (AppConfig.CustomModelsDirectory), never the real user folder */
    public string ModelsDirectory { get; }

    /* Scratch folder for files a test needs (exports, etc.) */
    public TempDirectory Temp => _temp;

    /* The real settings manager over the fake repository */
    public SettingsManager Settings => Services.GetRequiredService<SettingsManager>();

    /* Registers the services exactly as Program.ConfigureServices does, with fakes at the OS boundaries.
       `downloader` replaces the FakeModelDownloader when a test needs a scripted one */
    public CliTestHost(IModelDownloader? downloader = null)
    {
        ModelsDirectory = _temp.CreateSubdirectory("models");
        Repository.AppConfig.CustomModelsDirectory = ModelsDirectory;

        var services = new ServiceCollection();
        services.AddSingleton<IConfigurationRepository>(Repository);
        services.AddSingleton<IModelRegistry>(Registry);
        services.AddSingleton(downloader ?? Downloader);
        services.AddSingleton<ITtsEngineFactory>(EngineFactory);
        services.AddSingleton<IAudioPlayer>(AudioPlayer);
        services.AddSingleton<ISpeechPipeline>(Pipeline);
        services.AddSingleton<SettingsManager>();
        services.AddSingleton<DownloadManager>();
        services.AddSingleton<DictionaryManager>();
        services.AddSingleton<ITtsEngineProvider>(sp => new TtsEngineProvider(
            sp.GetRequiredService<ITtsEngineFactory>(),
            sp.GetRequiredService<SettingsManager>(),
            sp.GetRequiredService<DownloadManager>(),
            sp.GetRequiredService<IModelRegistry>()));
        services.AddSingleton<DaemonClient>();
        services.AddSingleton<DaemonServer>();
        Services = services.BuildServiceProvider();
    }

    /* Registers a model that is not downloaded */
    public ModelMetadata AddModel(string engineId, string modelName, params string[] files) =>
        Registry.AddModel(engineId, modelName, files);

    /* Registers a model and writes its files where DownloadManager looks for them */
    public ModelMetadata AddDownloadedModel(string engineId, string modelName, params string[] files)
    {
        var model = Registry.AddModel(engineId, modelName, files);
        foreach (string file in files)
        {
            string path = ModelFilePath(model, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "dummy model");
        }
        return model;
    }

    /* Path a model file is stored at */
    public string ModelFilePath(ModelMetadata model, string file) =>
        Path.Combine(Settings.GetModelDirectory(model.Id), file);

    /* Makes the engine factory return the returned engine for the model */
    public FakeTtsEngine AddEngine(ModelMetadata model)
    {
        var engine = new FakeTtsEngine();
        EngineFactory.Engines[model.Id] = () => engine;
        return engine;
    }

    /* Selects the model in settings and returns the engine the provider will load for it */
    public FakeTtsEngine ActivateModel(ModelMetadata model)
    {
        var engine = AddEngine(model);
        Repository.AppConfig.ActiveModel = model.Id;
        return engine;
    }

    /* Builds the command, mounts it under a root command (as Program does) and runs "<name> args..." with the
       console captured and no stdin */
    public Task<CliRun> RunAsync(Func<IServiceProvider, Command> build, params string[] args) =>
        RunWithInputAsync(build, string.Empty, args);

    /* Same as RunAsync, with `stdin` as what the user types at prompts */
    public async Task<CliRun> RunWithInputAsync(Func<IServiceProvider, Command> build, string stdin, params string[] args)
    {
        var command = build(Services);
        var root = new RootCommand { command };
        var parseResult = root.Parse([command.Name, .. args]);

        using var console = new CliConsoleCapture(stdin);
        Task<int> invocation;
        using (CliInlineSynchronizationContext.Install())
            invocation = parseResult.InvokeAsync(CliRun.Configuration(console), TestContext.Current.CancellationToken);
        int exitCode = await invocation;
        return new CliRun(exitCode, console.Output, console.ErrorOutput);
    }

    /* Shuts the engines down and removes the temp folder */
    public void Dispose()
    {
        Services.Dispose();
        _temp.Dispose();
    }
}

/* File Structure:
        - Configuration(CliConsoleCapture console) : InvocationConfiguration
*/

/* Outcome of one CLI invocation */
public sealed record CliRun(int ExitCode, string Output, string Error)
{
    /* Invocation settings for tests: captured streams and no process-termination (Ctrl+C / SIGTERM) handlers */
    public static InvocationConfiguration Configuration(CliConsoleCapture console) => new()
    {
        Output = console.Out,
        Error = console.Error,
        ProcessTerminationTimeout = null
    };
}
