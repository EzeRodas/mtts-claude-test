using System.Diagnostics;
using System.Runtime.Versioning;
using MoonTTS.Domain.Audio;
using MoonTTS.Infrastructure.Audio.Linux;

namespace MoonTTS.Infrastructure.Tests.Audio.Linux;

/* Verifies how playback processes are launched: paplay first, pw-play as fallback, raw PCM on stdin */
[Collection(TestCollections.Environment)]
[SupportedOSPlatform("linux")]
public sealed class LinuxAudioStreamerTests
{
    private static readonly AudioFormat Format = new(24000, 1, 16);

    /* Closes stdin so the dummy player exits, then releases the process */
    private static void Finish(LinuxProcessRunner.SinkStreamTarget target)
    {
        target.StandardInput.Close();
        target.Process.WaitForExit(5000);
        if (!target.Process.HasExited)
            target.Process.Kill(entireProcessTree: true);
        target.Process.Dispose();
    }

    /* Default sink: paplay plays raw PCM from stdin in the engine's format */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void StartSinkSession_NoSink_StartsPaplayWithRawFormatArguments()
    {
        using var os = new LinuxDummyAudioSystem().AddPlayer("paplay", "-d");

        var session = LinuxAudioStreamer.StartSinkSession(null, Format, () => 1.0f);

        Assert.True(session.IsSuccess, session.ErrorMessage);
        Finish(session.Value!);
        Assert.Equal(
            ["--raw --rate=24000 --channels=1 --latency-msec=50 --format=s16le /dev/stdin"],
            os.Tools.GetInvocations("paplay"));
    }

    /* The format comes from the engine, never hardcoded */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void StartSinkSession_StereoFormat_PassesEngineRateAndChannels()
    {
        using var os = new LinuxDummyAudioSystem().AddPlayer("paplay", "-d");

        var session = LinuxAudioStreamer.StartSinkSession(null, new AudioFormat(44100, 2, 16), () => 1.0f);

        Finish(session.Value!);
        Assert.Contains("--rate=44100 --channels=2", Assert.Single(os.Tools.GetInvocations("paplay")));
    }

    /* A named sink is passed to paplay with -d */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void StartSinkSession_WithSink_PassesSinkToPaplay()
    {
        using var os = new LinuxDummyAudioSystem().AddPlayer("paplay", "-d");

        var session = LinuxAudioStreamer.StartSinkSession("Moon-TTS_Sink", Format, () => 1.0f);

        Finish(session.Value!);
        Assert.Equal(
            ["--raw --rate=24000 --channels=1 --latency-msec=50 --format=s16le -d Moon-TTS_Sink /dev/stdin"],
            os.Tools.GetInvocations("paplay"));
    }

    /* An empty sink name means the default sink */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void StartSinkSession_EmptySink_UsesDefaultSink()
    {
        using var os = new LinuxDummyAudioSystem().AddPlayer("paplay", "-d");

        var session = LinuxAudioStreamer.StartSinkSession("", Format, () => 1.0f);

        Finish(session.Value!);
        Assert.DoesNotContain("-d", Assert.Single(os.Tools.GetInvocations("paplay")).Split(' '));
    }

    /* The returned target pipes into the player's stdin and carries the volume provider */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void StartSinkSession_PaplayStarts_ReturnsTargetWritingToPlayerStdin()
    {
        using var os = new LinuxDummyAudioSystem().AddPlayer("paplay", "-d");
        Func<float> volume = () => 0.25f;

        var session = LinuxAudioStreamer.StartSinkSession(null, Format, volume);

        var target = session.Value!;
        target.StandardInput.Write([1, 2, 3, 4]);
        Finish(target);
        Assert.Equal([1, 2, 3, 4], os.GetPlayedBytes("paplay"));
        Assert.Same(volume, target.VolumeProvider);
    }

    /* The started process is reported to the caller so it can be paused/stopped later */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void StartSinkSession_PaplayStarts_ReportsStartedProcess()
    {
        using var os = new LinuxDummyAudioSystem().AddPlayer("paplay", "-d");
        Process? reported = null;

        var session = LinuxAudioStreamer.StartSinkSession(null, Format, () => 1.0f, p => reported = p);

        Assert.Same(session.Value!.Process, reported);
        Finish(session.Value!);
    }

    /* Without paplay, pw-play plays the raw PCM from stdin */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void StartSinkSession_PaplayMissing_FallsBackToPwPlay()
    {
        using var os = new LinuxDummyAudioSystem().AddPlayer("pw-play", "--target");

        var session = LinuxAudioStreamer.StartSinkSession(null, Format, () => 1.0f);

        Assert.True(session.IsSuccess, session.ErrorMessage);
        Finish(session.Value!);
        Assert.Equal(["--raw --rate 24000 --channels 1 --format s16 -"], os.Tools.GetInvocations("pw-play"));
    }

    /* pw-play receives a named sink with --target */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void StartSinkSession_PaplayMissingWithSink_PassesTargetToPwPlay()
    {
        using var os = new LinuxDummyAudioSystem().AddPlayer("pw-play", "--target");

        var session = LinuxAudioStreamer.StartSinkSession("48", Format, () => 1.0f);

        Finish(session.Value!);
        Assert.Equal(["--raw --rate 24000 --channels 1 --format s16 --target 48 -"], os.Tools.GetInvocations("pw-play"));
    }

    /* The pw-play process is reported to the caller too */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void StartSinkSession_PwPlayStarts_ReportsStartedProcess()
    {
        using var os = new LinuxDummyAudioSystem().AddPlayer("pw-play", "--target");
        Process? reported = null;

        var session = LinuxAudioStreamer.StartSinkSession(null, Format, () => 1.0f, p => reported = p);

        Assert.Same(session.Value!.Process, reported);
        Finish(session.Value!);
    }

    /* paplay starting means pw-play is never launched */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void StartSinkSession_PaplayStarts_DoesNotStartPwPlay()
    {
        using var os = new LinuxDummyAudioSystem().AddPlayer("paplay", "-d").AddPlayer("pw-play", "--target");

        var session = LinuxAudioStreamer.StartSinkSession(null, Format, () => 1.0f);

        Finish(session.Value!);
        Assert.False(os.Tools.WasInvoked("pw-play"));
    }

    /* Neither player available is a failure naming both errors, and nothing is reported */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void StartSinkSession_BothPlayersMissing_ReturnsFailureWithBothErrors()
    {
        using var os = new LinuxDummyAudioSystem();
        bool reported = false;

        var session = LinuxAudioStreamer.StartSinkSession(null, Format, () => 1.0f, _ => reported = true);

        Assert.True(session.IsFailure);
        Assert.StartsWith("Failed to start audio playback (paplay: Failed to execute 'paplay'", session.ErrorMessage);
        Assert.Contains("| pw-play: Failed to execute 'pw-play'", session.ErrorMessage);
        Assert.False(reported);
    }

    /* StartProcess launches the tool with stdin redirected */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void StartProcess_ToolExists_ReturnsProcessWithWritableStdin()
    {
        using var os = new LinuxDummyAudioSystem().AddPlayer("paplay", "-d");

        var started = LinuxAudioStreamer.StartProcess("paplay", "-d speakers");

        Assert.True(started.IsSuccess, started.ErrorMessage);
        Assert.True(started.Value.StandardInput.CanWrite);
        Finish(new LinuxProcessRunner.SinkStreamTarget(started.Value.Process, started.Value.StandardInput, () => 1.0f));
        Assert.Equal(["-d speakers"], os.Tools.GetInvocations("paplay"));
    }

    /* A missing tool is a failure carrying the exception */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void StartProcess_ToolMissing_ReturnsFailureWithException()
    {
        using var os = new LinuxDummyAudioSystem();

        var started = LinuxAudioStreamer.StartProcess("paplay", "");

        Assert.True(started.IsFailure);
        Assert.StartsWith("Failed to execute 'paplay': ", started.ErrorMessage);
        Assert.NotNull(started.Exception);
    }
}
