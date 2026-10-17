using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using MoonTTS.CLI.Tests.Support;
using MoonTTS.Core.Services;
using MoonTTS.Core.Utilities;
using MoonTTS.Domain.Interfaces;
using MoonTTS.Engine.Protocol;
using MoonTTS.Infrastructure.Daemon;
using MoonTTS.Infrastructure.Persistence;
using MoonTTS.Infrastructure.Tts;
using MoonTTS.Tests.DummyEngine;

namespace MoonTTS.CLI.Tests;

/* Program is internal; its members are reached through CliInternals (UnsafeAccessor).
   The Main tests run the real composition root against isolated user folders (HOME / XDG_*), with the dummy engine
   worker installed as a real engine. They are skipped on Windows, where APPDATA is not read from the environment */
[Collection(TestCollections.Environment)]
public sealed class ProgramTests
{
    /* Returns the names of a command's subcommands, in registration order */
    private static string[] SubcommandNames(Command command) => command.Subcommands.Select(c => c.Name).ToArray();

    /* Returns the subcommand with the given name */
    private static Command Subcommand(Command command, string name) => Assert.Single(command.Subcommands, c => c.Name == name);

    /* Builds the command tree over the fakes */
    private static RootCommand BuildTree(CliTestHost host) => CliInternals.BuildRootCommand(null, host.Services);

    /* Runs Program.Main with the console captured */
    private static async Task<CliRun> RunMainAsync(params string[] args)
    {
        using var console = new CliConsoleCapture();
        int exitCode = await CliInternals.RunMain(null, args);
        return new CliRun(exitCode, console.Output, console.ErrorOutput);
    }

    /* Points the user folders at `root` and installs the dummy engine with a ready "default" model (no files) and a
       "large" model whose file is not downloaded */
    private static EnvironmentScope IsolatedEnvironmentWithDummyEngine(TempDirectory root)
    {
        var scope = EnvironmentScope.IsolatedUserDirectories(root);
        DummyEngineInstaller.Install(PathResolver.GetEnginesDirectory(), "Dummy", new DummyEngineOptions(),
            new EngineModelManifest { Id = "default", DisplayName = "Dummy Default" },
            new EngineModelManifest
            {
                Id = "large", DisplayName = "Dummy Large",
                DownloadFiles = new() { ["large.bin"] = "https://example.invalid/large.bin" }
            });
        return scope;
    }

    /* IServiceProvider that counts resolutions and returns nothing */
    private sealed class CountingServiceProvider : IServiceProvider
    {
        public int Resolutions { get; private set; }

        /* Records the request */
        public object? GetService(Type serviceType)
        {
            Resolutions++;
            return null;
        }
    }

    /* The root exposes the playback commands and the model, voice, output, mic and daemon branches */
    [Fact]
    public void BuildRootCommand_Always_RegistersTopLevelCommands()
    {
        using var host = new CliTestHost();

        var root = BuildTree(host);

        Assert.Equal(["play", "pause", "stop", "model", "voice", "output", "mic", "daemon"], SubcommandNames(root));
    }

    /* The model branch has list, set, download and delete */
    [Fact]
    public void BuildRootCommand_ModelBranch_RegistersModelCommands()
    {
        using var host = new CliTestHost();

        var model = Subcommand(BuildTree(host), "model");

        Assert.Equal(["list", "set", "download", "delete"], SubcommandNames(model));
    }

    /* The voice branch has list and set */
    [Fact]
    public void BuildRootCommand_VoiceBranch_RegistersVoiceCommands()
    {
        using var host = new CliTestHost();

        var voice = Subcommand(BuildTree(host), "voice");

        Assert.Equal(["list", "set"], SubcommandNames(voice));
    }

    /* The output branch has list plus the main and monitor sub branches */
    [Fact]
    public void BuildRootCommand_OutputBranch_RegistersListMainAndMonitor()
    {
        using var host = new CliTestHost();

        var output = Subcommand(BuildTree(host), "output");

        Assert.Equal(["list", "main", "monitor"], SubcommandNames(output));
    }

    /* Both output sub branches have set, enable, disable and vol */
    [Theory]
    [InlineData("main")]
    [InlineData("monitor")]
    public void BuildRootCommand_OutputSubBranch_RegistersDeviceCommands(string branch)
    {
        using var host = new CliTestHost();

        var outputBranch = Subcommand(Subcommand(BuildTree(host), "output"), branch);

        Assert.Equal(["set", "enable", "disable", "vol"], SubcommandNames(outputBranch));
    }

    /* The mic branch has create and status */
    [Fact]
    public void BuildRootCommand_MicBranch_RegistersVirtualMicCommands()
    {
        using var host = new CliTestHost();

        var mic = Subcommand(BuildTree(host), "mic");

        Assert.Equal(["create", "status"], SubcommandNames(mic));
    }

    /* The daemon branch has start, kill, status and the hidden run loop */
    [Fact]
    public void BuildRootCommand_DaemonBranch_RegistersDaemonCommands()
    {
        using var host = new CliTestHost();

        var daemon = Subcommand(BuildTree(host), "daemon");

        Assert.Equal(["start", "kill", "status", "run"], SubcommandNames(daemon));
        Assert.True(Subcommand(daemon, "run").Hidden);
    }

    /* Building the tree resolves no service, so parsing and --help never start players, engines or daemons */
    [Fact]
    public void BuildRootCommand_Always_ResolvesNoServices()
    {
        var provider = new CountingServiceProvider();

        CliInternals.BuildRootCommand(null, provider);

        Assert.Equal(0, provider.Resolutions);
    }

    /* Every production service is registered once, as a singleton */
    [Fact]
    public void ConfigureServices_Always_RegistersEveryServiceAsSingleton()
    {
        var services = new ServiceCollection();

        CliInternals.ConfigureServices(null, services);

        Assert.Equal(15, services.Count);
        Assert.All(services, d => Assert.Equal(ServiceLifetime.Singleton, d.Lifetime));
        Assert.Equal(services.Count, services.Select(d => d.ServiceType).Distinct().Count());
    }

    /* Each service is bound to its production implementation */
    [Theory]
    [InlineData(typeof(IConfigurationRepository), typeof(JsonConfigurationRepository))]
    [InlineData(typeof(SettingsManager), typeof(SettingsManager))]
    [InlineData(typeof(EngineVoiceStore), typeof(EngineVoiceStore))]
    [InlineData(typeof(IModelDownloader), typeof(HttpModelDownloader))]
    [InlineData(typeof(DownloadManager), typeof(DownloadManager))]
    [InlineData(typeof(HistoryManager), typeof(HistoryManager))]
    [InlineData(typeof(DictionaryManager), typeof(DictionaryManager))]
    [InlineData(typeof(HttpClient), typeof(HttpClient))]
    [InlineData(typeof(DaemonClient), typeof(DaemonClient))]
    [InlineData(typeof(DaemonServer), typeof(DaemonServer))]
    [InlineData(typeof(ITtsEngineFactory), typeof(TtsEngineFactory))]
    [InlineData(typeof(ITtsEngineProvider), typeof(TtsEngineProvider))]
    [InlineData(typeof(ISpeechPipeline), typeof(SpeechPipeline))]
    public void ConfigureServices_Service_BoundToProductionImplementation(Type serviceType, Type implementationType)
    {
        var services = new ServiceCollection();

        CliInternals.ConfigureServices(null, services);

        Assert.Equal(implementationType, Assert.Single(services, d => d.ServiceType == serviceType).ImplementationType);
    }

    /* The audio player and the model registry are built by factories (platform player, engine catalog) */
    [Theory]
    [InlineData(typeof(IAudioPlayer))]
    [InlineData(typeof(IModelRegistry))]
    public void ConfigureServices_PlatformServices_UseFactories(Type serviceType)
    {
        var services = new ServiceCollection();

        CliInternals.ConfigureServices(null, services);

        Assert.NotNull(Assert.Single(services, d => d.ServiceType == serviceType).ImplementationFactory);
    }

    /* The model registry factory scans the user's engines folder through an EngineCatalog */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void ConfigureServices_ModelRegistryFactory_CreatesEngineCatalog()
    {
        using var root = new TempDirectory();
        using var scope = EnvironmentScope.IsolatedUserDirectories(root);
        var services = new ServiceCollection();
        CliInternals.ConfigureServices(null, services);

        var factory = Assert.Single(services, d => d.ServiceType == typeof(IModelRegistry)).ImplementationFactory!;

        Assert.IsType<EngineCatalog>(factory(new ServiceCollection().BuildServiceProvider()));
    }

    /* --help prints every top-level command and exits with 0 */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Main_Help_ListsTopLevelCommandsAndReturnsZero()
    {
        using var root = new TempDirectory();
        using var scope = EnvironmentScope.IsolatedUserDirectories(root);

        var run = await RunMainAsync("--help");

        Assert.Equal(0, run.ExitCode);
        Assert.All(["play", "pause", "stop", "model", "voice", "output", "mic", "daemon"],
            name => Assert.Contains(name, run.Output));
    }

    /* An unknown command is a parse error */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Main_UnknownCommand_ReturnsNonZero()
    {
        using var root = new TempDirectory();
        using var scope = EnvironmentScope.IsolatedUserDirectories(root);

        var run = await RunMainAsync("fly");

        Assert.NotEqual(0, run.ExitCode);
    }

    /* Main loads (and so creates) the configuration folder before running any command */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Main_AnyCommand_CreatesConfigurationDirectory()
    {
        using var root = new TempDirectory();
        using var scope = EnvironmentScope.IsolatedUserDirectories(root);

        await RunMainAsync("model", "list");

        Assert.True(Directory.Exists(PathResolver.GetConfigDirectory()));
        Assert.StartsWith(root.Path, PathResolver.GetConfigDirectory());
    }

    /* With no engine installed, "model list" says so */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Main_ModelListWithoutEngines_WarnsNoEnginesInstalled()
    {
        using var root = new TempDirectory();
        using var scope = EnvironmentScope.IsolatedUserDirectories(root);

        var run = await RunMainAsync("model", "list");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("No engines are installed.", run.Output);
    }

    /* "model list" shows the installed engine's models with their download state */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Main_ModelListWithDummyEngine_ListsEngineModels()
    {
        using var root = new TempDirectory();
        using var scope = IsolatedEnvironmentWithDummyEngine(root);

        var run = await RunMainAsync("model", "list");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Dummy Default", CliOutput.LineContaining(run.Output, "dummy/default"));
        Assert.Contains(" Yes ", CliOutput.LineContaining(run.Output, "dummy/default"));
        Assert.Contains(" No ", CliOutput.LineContaining(run.Output, "dummy/large"));
    }

    /* "model set" starts the dummy worker, and the selection is persisted to settings.json */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Main_ModelSetDownloadedModel_PersistsActiveModel()
    {
        using var root = new TempDirectory();
        using var scope = IsolatedEnvironmentWithDummyEngine(root);

        var run = await RunMainAsync("model", "set", "dummy/default");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Successfully switched active model to: dummy/default", run.Output);
        string settings = await File.ReadAllTextAsync(
            Path.Combine(PathResolver.GetConfigDirectory(), "settings.json"), TestContext.Current.CancellationToken);
        Assert.Contains("\"dummy/default\"", settings);
    }

    /* A model selected in one run is shown as ACTIVE by the next run */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Main_ModelListAfterModelSet_MarksModelActive()
    {
        using var root = new TempDirectory();
        using var scope = IsolatedEnvironmentWithDummyEngine(root);
        await RunMainAsync("model", "set", "dummy/default");

        var run = await RunMainAsync("model", "list");

        Assert.Contains("ACTIVE", CliOutput.LineContaining(run.Output, "dummy/default"));
    }

    /* A model whose files are missing cannot be selected */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Main_ModelSetNotDownloaded_ReturnsOne()
    {
        using var root = new TempDirectory();
        using var scope = IsolatedEnvironmentWithDummyEngine(root);

        var run = await RunMainAsync("model", "set", "dummy/large");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Error: Model 'dummy/large' is not installed.", run.Output);
    }

    /* Without a selected model "voice list" fails */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Main_VoiceListWithoutModel_ReturnsOne()
    {
        using var root = new TempDirectory();
        using var scope = IsolatedEnvironmentWithDummyEngine(root);

        var run = await RunMainAsync("voice", "list");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Error: No TTS model is currently loaded.", run.Output);
    }

    /* "voice list" starts the selected engine's worker and lists the voices it reports */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Main_VoiceListWithActiveDummyModel_ListsWorkerVoices()
    {
        using var root = new TempDirectory();
        using var scope = IsolatedEnvironmentWithDummyEngine(root);
        await RunMainAsync("model", "set", "dummy/default");

        var run = await RunMainAsync("voice", "list");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Available Voices for 'dummy/default'", run.Output);
        string v1 = CliOutput.LineContaining(run.Output, "v1 ");
        Assert.Contains("Voice One", v1);
        Assert.Contains("en-US", v1);
        Assert.Contains("Showing 2 of 2 voices.", run.Output);
    }

    /* A voice selected with "voice set" is persisted and shown as ACTIVE by the next "voice list" */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Main_VoiceSetThenList_MarksVoiceActive()
    {
        using var root = new TempDirectory();
        using var scope = IsolatedEnvironmentWithDummyEngine(root);
        await RunMainAsync("model", "set", "dummy/default");

        var set = await RunMainAsync("voice", "set", "v2");
        var list = await RunMainAsync("voice", "list");

        Assert.Equal(0, set.ExitCode);
        Assert.Contains("ACTIVE", CliOutput.LineContaining(list.Output, "v2 "));
        Assert.DoesNotContain("ACTIVE", CliOutput.LineContaining(list.Output, "v1 "));
    }
}
