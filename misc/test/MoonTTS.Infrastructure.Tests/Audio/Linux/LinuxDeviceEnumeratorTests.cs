#if !WINDOWS
/* Linux audio types only exist outside the Windows build */
using System.Runtime.Versioning;
using MoonTTS.Infrastructure.Audio.Linux;

namespace MoonTTS.Infrastructure.Tests.Audio.Linux;

/* Verifies sink discovery against dummy pactl (PulseAudio / pipewire-pulse) and wpctl (WirePlumber) tools */
[Collection(TestCollections.Environment)]
[SupportedOSPlatform("linux")]
public sealed class LinuxDeviceEnumeratorTests
{
    private static readonly LinuxSink BuiltIn =
        new("alsa_output.pci-0000_00_1f.3.analog-stereo", "Built-in Audio Analog Stereo");
    private static readonly LinuxSink Headset =
        new("alsa_output.usb-Logitech_G435-00.analog-stereo", "G435 Wireless Gaming Headset Analog Stereo");

    /* pactl output is parsed into one sink per "Sink #" block, using Name and Description */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void GetSinks_PactlListsSinks_ReturnsNameAndDescriptionOfEachSink()
    {
        using var os = new LinuxDummyAudioSystem().AddTool("pactl", LinuxToolOutputs.PactlListSinks);

        var sinks = LinuxDeviceEnumerator.GetSinks();

        Assert.Equal([BuiltIn, Headset], sinks);
    }

    /* pactl is asked for the long sink listing */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void GetSinks_PactlAvailable_RunsPactlListSinks()
    {
        using var os = new LinuxDummyAudioSystem().AddTool("pactl", LinuxToolOutputs.PactlListSinks);

        LinuxDeviceEnumerator.GetSinks();

        Assert.Equal(["list sinks"], os.Tools.GetInvocations("pactl"));
    }

    /* When pactl finds sinks, wpctl is never consulted */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void GetSinks_PactlFindsSinks_DoesNotRunWpctl()
    {
        using var os = new LinuxDummyAudioSystem()
            .AddTool("pactl", LinuxToolOutputs.PactlListSinks)
            .AddTool("wpctl", LinuxToolOutputs.WpctlStatus);

        LinuxDeviceEnumerator.GetSinks();

        Assert.False(os.Tools.WasInvoked("wpctl"));
    }

    /* A sink without a Description line is shown with its name */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void GetSinks_PactlSinkWithoutDescription_UsesNameAsDisplayName()
    {
        using var os = new LinuxDummyAudioSystem().AddTool("pactl", LinuxToolOutputs.PactlListSinkWithoutDescription);

        var sinks = LinuxDeviceEnumerator.GetSinks();

        Assert.Equal([new LinuxSink("bluez_output.00_1B_66_A1_B2_C3.1", "bluez_output.00_1B_66_A1_B2_C3.1")], sinks);
    }

    /* When pactl fails (no PulseAudio server), wpctl status is parsed into node id + description */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void GetSinks_PactlFails_ParsesWpctlAudioSinks()
    {
        using var os = new LinuxDummyAudioSystem()
            .AddFailingTool("pactl")
            .AddTool("wpctl", LinuxToolOutputs.WpctlStatus);

        var sinks = LinuxDeviceEnumerator.GetSinks();

        Assert.Equal(
            [
                new LinuxSink("48", "Built-in Audio Analog Stereo"),
                new LinuxSink("57", "G435 Wireless Gaming Headset Analog Stereo"),
            ],
            sinks);
    }

    /* wpctl is asked for its status overview */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void GetSinks_PactlFails_RunsWpctlStatus()
    {
        using var os = new LinuxDummyAudioSystem()
            .AddFailingTool("pactl")
            .AddTool("wpctl", LinuxToolOutputs.WpctlStatus);

        LinuxDeviceEnumerator.GetSinks();

        Assert.Equal(["status"], os.Tools.GetInvocations("wpctl"));
    }

    /* pactl not installed (pure PipeWire) also falls back to wpctl */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void GetSinks_PactlMissing_FallsBackToWpctl()
    {
        using var os = new LinuxDummyAudioSystem().AddTool("wpctl", LinuxToolOutputs.WpctlStatus);

        var sinks = LinuxDeviceEnumerator.GetSinks();

        Assert.Equal(["48", "57"], sinks.Select(s => s.RealName));
    }

    /* pactl succeeding with no sinks still falls back to wpctl */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void GetSinks_PactlListsNoSinks_FallsBackToWpctl()
    {
        using var os = new LinuxDummyAudioSystem()
            .AddTool("pactl", "")
            .AddTool("wpctl", LinuxToolOutputs.WpctlStatus);

        var sinks = LinuxDeviceEnumerator.GetSinks();

        Assert.Equal(["48", "57"], sinks.Select(s => s.RealName));
    }

    /* Video sinks/sources and the Settings section of wpctl status are not mistaken for audio sinks */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void GetSinks_WpctlStatus_IgnoresNodesOutsideAudioSinks()
    {
        using var os = new LinuxDummyAudioSystem().AddTool("wpctl", LinuxToolOutputs.WpctlStatus);

        var sinks = LinuxDeviceEnumerator.GetSinks();

        Assert.DoesNotContain(sinks, s => s.RealName is "49" or "61" or "63" or "0" or "33");
    }

    /* wpctl output without a Sinks section yields no sinks */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void GetSinks_WpctlHasNoSinksSection_ReturnsEmpty()
    {
        using var os = new LinuxDummyAudioSystem()
            .AddFailingTool("pactl")
            .AddTool("wpctl", LinuxToolOutputs.WpctlStatusWithoutSinks);

        var sinks = LinuxDeviceEnumerator.GetSinks();

        Assert.Empty(sinks);
    }

    /* Both tools failing yields an empty list instead of an error */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void GetSinks_BothToolsFail_ReturnsEmpty()
    {
        using var os = new LinuxDummyAudioSystem()
            .AddFailingTool("pactl")
            .AddFailingTool("wpctl", "Could not connect to PipeWire");

        var sinks = LinuxDeviceEnumerator.GetSinks();

        Assert.Empty(sinks);
    }

    /* No sound tools installed at all yields an empty list */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void GetSinks_NoToolsInstalled_ReturnsEmpty()
    {
        using var os = new LinuxDummyAudioSystem();

        var sinks = LinuxDeviceEnumerator.GetSinks();

        Assert.Empty(sinks);
    }
}

#endif
