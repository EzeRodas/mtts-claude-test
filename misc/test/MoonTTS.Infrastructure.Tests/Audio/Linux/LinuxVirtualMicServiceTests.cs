using System.Runtime.Versioning;
using MoonTTS.Infrastructure.Audio.Linux;

namespace MoonTTS.Infrastructure.Tests.Audio.Linux;

/* Verifies the virtual microphone lifecycle against dummy pactl, wpctl and pw-cli tools */
[Collection(TestCollections.Environment)]
[SupportedOSPlatform("linux")]
public sealed class LinuxVirtualMicServiceTests
{
    /* pactl script answering "list sinks short" with the given listing and "load-module" with a module index,
       like a running pipewire-pulse server */
    private static LinuxDummyAudioSystem WithPactl(string sinksShort, int nullSinkExit = 0, int remapExit = 0)
    {
        var os = new LinuxDummyAudioSystem();
        string listing = os.Temp.WriteFile("sinks-short.txt", sinksShort);
        os.Tools.AddScript("pactl",
            "case \"$1 $2\" in\n" +
            $"  'list sinks') cat '{listing}' ;;\n" +
            $"  'load-module module-null-sink') [ {nullSinkExit} -eq 0 ] && echo 536870913 || {{ echo 'Failure: Module initialization failed' >&2; exit {nullSinkExit}; }} ;;\n" +
            $"  'load-module module-remap-source') [ {remapExit} -eq 0 ] && echo 536870914 || {{ echo 'Failure: No such entity' >&2; exit {remapExit}; }} ;;\n" +
            "esac\n");
        return os;
    }

    /* The Moon-TTS sink in `pactl list sinks short` means the microphone exists */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void Exists_PactlListsVirtualSink_ReturnsTrue()
    {
        using var os = WithPactl(LinuxToolOutputs.PactlListSinksShortWithVirtualMic);

        Assert.True(LinuxVirtualMicService.Exists());
    }

    /* Exists queries the short sink listing */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void Exists_PactlAvailable_RunsPactlListSinksShort()
    {
        using var os = WithPactl(LinuxToolOutputs.PactlListSinksShortWithVirtualMic);

        LinuxVirtualMicService.Exists();

        Assert.Equal(["list sinks short"], os.Tools.GetInvocations("pactl"));
    }

    /* The sink name is matched case-insensitively */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void Exists_PactlListsSinkInDifferentCase_ReturnsTrue()
    {
        using var os = WithPactl("68\tmoon-tts_sink\tPipeWire\tfloat32le 2ch 48000Hz\tIDLE\n");

        Assert.True(LinuxVirtualMicService.Exists());
    }

    /* Found by pactl, so wpctl is not consulted */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void Exists_PactlListsVirtualSink_DoesNotRunWpctl()
    {
        using var os = WithPactl(LinuxToolOutputs.PactlListSinksShortWithVirtualMic)
            .AddTool("wpctl", LinuxToolOutputs.WpctlStatusWithVirtualMic);

        LinuxVirtualMicService.Exists();

        Assert.False(os.Tools.WasInvoked("wpctl"));
    }

    /* Not in pactl but present in wpctl status means it exists */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void Exists_OnlyWpctlListsVirtualSink_ReturnsTrue()
    {
        using var os = WithPactl(LinuxToolOutputs.PactlListSinksShort)
            .AddTool("wpctl", LinuxToolOutputs.WpctlStatusWithVirtualMic);

        Assert.True(LinuxVirtualMicService.Exists());
    }

    /* pactl failing (no PulseAudio) falls back to wpctl status */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void Exists_PactlFails_ChecksWpctlStatus()
    {
        using var os = new LinuxDummyAudioSystem()
            .AddFailingTool("pactl")
            .AddTool("wpctl", LinuxToolOutputs.WpctlStatusWithVirtualMic);

        Assert.True(LinuxVirtualMicService.Exists());
        Assert.Equal(["status"], os.Tools.GetInvocations("wpctl"));
    }

    /* Neither tool lists the sink */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void Exists_NoToolListsVirtualSink_ReturnsFalse()
    {
        using var os = WithPactl(LinuxToolOutputs.PactlListSinksShort)
            .AddTool("wpctl", LinuxToolOutputs.WpctlStatus);

        Assert.False(LinuxVirtualMicService.Exists());
    }

    /* Without any sound tool nothing can exist */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void Exists_NoToolsInstalled_ReturnsFalse()
    {
        using var os = new LinuxDummyAudioSystem();

        Assert.False(LinuxVirtualMicService.Exists());
    }

    /* An existing microphone is reused: success without loading any module */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void EnsureCreated_AlreadyExists_SucceedsWithoutLoadingModules()
    {
        using var os = WithPactl(LinuxToolOutputs.PactlListSinksShortWithVirtualMic);

        var result = LinuxVirtualMicService.EnsureCreated();

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.DoesNotContain(os.Tools.GetInvocations("pactl"), call => call.StartsWith("load-module"));
    }

    /* Missing microphone: pactl loads a null sink and remaps its monitor as a microphone source */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void EnsureCreated_PactlAvailable_LoadsNullSinkThenRemapsMonitor()
    {
        using var os = WithPactl(LinuxToolOutputs.PactlListSinksShort);

        var result = LinuxVirtualMicService.EnsureCreated();

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var loads = os.Tools.GetInvocations("pactl").Where(call => call.StartsWith("load-module")).ToList();
        Assert.Equal(2, loads.Count);
        Assert.StartsWith("load-module module-null-sink sink_name=Moon-TTS_Sink sink_properties=device.description=", loads[0]);
        Assert.StartsWith("load-module module-remap-source master=Moon-TTS_Sink.monitor source_name=Moon-TTS_Mic source_properties=device.description=", loads[1]);
    }

    /* pactl creating the microphone means pw-cli is not needed */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void EnsureCreated_PactlCreatesSink_DoesNotRunPwCli()
    {
        using var os = WithPactl(LinuxToolOutputs.PactlListSinksShort).AddTool("pw-cli");

        LinuxVirtualMicService.EnsureCreated();

        Assert.False(os.Tools.WasInvoked("pw-cli"));
    }

    /* A failing remap reports the remap error */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void EnsureCreated_RemapFails_ReturnsRemapFailure()
    {
        using var os = WithPactl(LinuxToolOutputs.PactlListSinksShort, remapExit: 1);

        var result = LinuxVirtualMicService.EnsureCreated();

        Assert.True(result.IsFailure);
        Assert.StartsWith("Failed to remap virtual microphone source: ", result.ErrorMessage);
        Assert.Contains("Failure: No such entity", result.ErrorMessage);
    }

    /* A failing null sink falls back to creating a PipeWire node with pw-cli */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void EnsureCreated_NullSinkFails_CreatesNodeWithPwCli()
    {
        using var os = WithPactl(LinuxToolOutputs.PactlListSinksShort, nullSinkExit: 1).AddTool("pw-cli", "69\n");

        var result = LinuxVirtualMicService.EnsureCreated();

        Assert.True(result.IsSuccess, result.ErrorMessage);
        string call = Assert.Single(os.Tools.GetInvocations("pw-cli"));
        Assert.StartsWith("create-node adapter ", call);
        Assert.Contains("factory.name=support.null-audio-sink", call);
        Assert.Contains("node.name=Moon-TTS_Sink", call);
        Assert.Contains("media.class=Audio/Sink", call);
        Assert.Contains("object.linger=true", call);
    }

    /* pactl missing (pure PipeWire) also uses pw-cli */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void EnsureCreated_PactlMissing_CreatesNodeWithPwCli()
    {
        using var os = new LinuxDummyAudioSystem()
            .AddTool("wpctl", LinuxToolOutputs.WpctlStatus)
            .AddTool("pw-cli", "69\n");

        var result = LinuxVirtualMicService.EnsureCreated();

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(os.Tools.WasInvoked("pw-cli"));
    }

    /* Both creation paths failing reports the pw-cli error */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void EnsureCreated_PactlAndPwCliFail_ReturnsCombinedFailure()
    {
        using var os = WithPactl(LinuxToolOutputs.PactlListSinksShort, nullSinkExit: 1)
            .AddFailingTool("pw-cli", "Error: \"create-node\" failed");

        var result = LinuxVirtualMicService.EnsureCreated();

        Assert.True(result.IsFailure);
        Assert.StartsWith("Failed to create virtual sink via pactl and pw-cli: ", result.ErrorMessage);
        Assert.Contains("create-node", result.ErrorMessage);
    }

    /* No sound tools at all is a failure, not an exception */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void EnsureCreated_NoToolsInstalled_ReturnsFailure()
    {
        using var os = new LinuxDummyAudioSystem();

        var result = LinuxVirtualMicService.EnsureCreated();

        Assert.True(result.IsFailure);
        Assert.StartsWith("Failed to create virtual sink via pactl and pw-cli: Failed to execute 'pw-cli'", result.ErrorMessage);
    }
}
