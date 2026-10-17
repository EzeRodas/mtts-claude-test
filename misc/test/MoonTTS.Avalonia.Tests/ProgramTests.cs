using System.IO.Pipes;
using System.Runtime.Versioning;
using Avalonia;
using MoonTTS.Avalonia.Services;
using MoonTTS.Avalonia.Tests.TestSupport;

namespace MoonTTS.Avalonia.Tests;

/* Verifies the desktop entry point. Program is internal, so it is reached through GuiPrivate */
[Collection(TestCollections.Environment)]
public sealed class ProgramTests
{
    private static readonly Type Program = typeof(App).Assembly.GetType("MoonTTS.Avalonia.Program", throwOnError: true)!;

    /* The Avalonia builder is configured for the Moon-TTS App */
    [Fact]
    public void BuildAvaloniaApp_Called_ConfiguresApp()
    {
        var builder = Assert.IsType<AppBuilder>(GuiPrivate.InvokeStatic(Program, "BuildAvaloniaApp"));

        Assert.Equal(typeof(App), builder.ApplicationType);
    }

    /* A second launch while Moon-TTS runs only toggles the running instance and returns without starting a GUI.
       The pipe lives in the isolated TMPDIR (GuiIsolationFixture); DISPLAY / WAYLAND_DISPLAY are cleared so a
       regression fails instead of opening a real window */
    [Fact(Skip = "Unix socket pipes only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    [UnsupportedOSPlatform("windows")]
    public async Task Main_InstanceAlreadyRunning_TogglesItAndReturns()
    {
        using var environment = new EnvironmentScope().Set("DISPLAY", null).Set("WAYLAND_DISPLAY", null);
        string pipeName = (string)GuiPrivate.GetStaticField(typeof(SingleInstanceService), "PipeName")!;
        using var runningInstance = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        GuiPrivate.InvokeStatic(Program, "Main", new object?[] { Array.Empty<string>() });

        await runningInstance.WaitForConnectionAsync(TestContext.Current.CancellationToken);
        using var reader = new StreamReader(runningInstance);
        Assert.Equal("toggle", await reader.ReadLineAsync(TestContext.Current.CancellationToken));
    }
}
