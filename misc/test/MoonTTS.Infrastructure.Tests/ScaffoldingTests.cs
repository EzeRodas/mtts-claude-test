using MoonTTS.Core.Services;
using MoonTTS.Domain.ValueObjects;
#if !WINDOWS
using MoonTTS.Infrastructure.Audio.Linux;
#endif
using MoonTTS.Infrastructure.Tts;
using MoonTTS.Tests.DummyEngine;

namespace MoonTTS.Infrastructure.Tests;

/* Verifies the shared dummy-OS helpers themselves: the dummy engine worker and dummy command-line tools */
[Collection(TestCollections.Environment)]
public sealed class ScaffoldingTests
{
    /* The dummy worker installs like a real engine and completes the full host handshake */
    [Fact]
    public async Task DummyEngine_IsDiscoveredAndStreamsChunks()
    {
        using var temp = new TempDirectory();
        using var env = EnvironmentScope.IsolatedUserDirectories(temp);
        string engines = temp.CreateSubdirectory("engines");
        DummyEngineInstaller.Install(engines, "Dummy", new DummyEngineOptions { ChunksPerRequest = 2, ChunkSize = 10 });

        var catalog = new EngineCatalog(engines);
        var settings = new SettingsManager(new FakeConfigurationRepository());
        var downloads = new DownloadManager(catalog, new FakeModelDownloader(), settings);
        var factory = new TtsEngineFactory(catalog, settings, downloads, new EngineVoiceStore());

        var result = await factory.CreateAsync(new ModelId("dummy", "default"), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        using var engine = result.Value!;

        var chunks = new List<byte[]>();
        await foreach (var chunk in engine.SynthesizeStreamAsync("hello", cancellationToken: TestContext.Current.CancellationToken))
            chunks.Add(chunk.ToArray());

        Assert.Equal(2, chunks.Count);
        Assert.All(chunks, c => Assert.Equal(10, c.Length));
    }

#if !WINDOWS
    /* Linux audio types only exist outside the Windows build */
    /* A dummy tool on PATH answers in place of the real OS program and records its arguments */
    [Fact(Skip = "Dummy tools are POSIX shell scripts", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void DummyExecutables_ShadowOsToolsOnPath()
    {
        using var temp = new TempDirectory();
        var tools = new DummyExecutables(temp).Add("pactl", stdout: "Sink #1\n\tName: speakers\n\tDescription: Speakers\n");
        using var env = tools.Activate(new EnvironmentScope());

        var sinks = LinuxDeviceEnumerator.GetSinks();

        Assert.Equal([new LinuxSink("speakers", "Speakers")], sinks);
        Assert.Equal(["list sinks"], tools.GetInvocations("pactl"));
    }
#endif
}
