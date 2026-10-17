using MoonTTS.CLI.commands.Output;
using MoonTTS.CLI.Tests.Support;
using MoonTTS.Domain.Common;
using MoonTTS.Domain.Entities;

namespace MoonTTS.CLI.Tests.Commands.Output;

[Collection(TestCollections.Environment)]
public sealed class ListOutputsCommandTests : IDisposable
{
    private readonly CliTestHost _host = new();

    /* Adds the dummy sinks the OS reports: built-in speakers, a USB headset and the virtual mic */
    public ListOutputsCommandTests()
    {
        _host.AudioPlayer.Devices.Add(new AudioDeviceInfo { DeviceId = "alsa.speakers", DeviceName = "Built-in Speakers" });
        _host.AudioPlayer.Devices.Add(new AudioDeviceInfo { DeviceId = "usb.headset", DeviceName = "USB Headset" });
        _host.AudioPlayer.Devices.Add(new AudioDeviceInfo { DeviceId = "mtts_mic", DeviceName = "Moon-TTS Virtual Sink" });
    }

    /* Releases the host */
    public void Dispose() => _host.Dispose();

    /* Returns the Status cell of a device row (the text after its id) */
    private static string StatusOf(string output, string deviceId)
    {
        string line = CliOutput.LineContaining(output, deviceId);
        return line[(line.IndexOf(deviceId, StringComparison.Ordinal) + deviceId.Length)..].Trim();
    }

    /* Build creates the "list" command without arguments */
    [Fact]
    public void Build_Always_CreatesListCommand()
    {
        var command = ListOutputsCommand.Build(_host.Services);

        Assert.Equal("list", command.Name);
        Assert.Empty(command.Arguments);
    }

    /* A failing device query is reported and exits with 1 */
    [Fact]
    public async Task List_DeviceQueryFails_ReturnsErrorWithMessage()
    {
        _host.AudioPlayer.DevicesResultOverride = Result<List<AudioDeviceInfo>>.Failure("pactl not found");

        var run = await _host.RunAsync(ListOutputsCommand.Build);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Error retrieving audio devices: pactl not found", run.Output);
    }

    /* Every device is listed with its name and id */
    [Fact]
    public async Task List_Devices_PrintsEveryDevice()
    {
        var run = await _host.RunAsync(ListOutputsCommand.Build);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Audio Output Sinks", run.Output);
        Assert.Contains("Built-in Speakers", CliOutput.LineContaining(run.Output, "alsa.speakers"));
        Assert.Contains("USB Headset", CliOutput.LineContaining(run.Output, "usb.headset"));
        Assert.Contains("Moon-TTS Virtual Sink", CliOutput.LineContaining(run.Output, "mtts_mic"));
    }

    /* The enabled main device is tagged "main" and unassigned devices show "-" */
    [Fact]
    public async Task List_EnabledMainDevice_TaggedMain()
    {
        _host.Repository.AppConfig.PrimaryAudioDeviceId = "alsa.speakers";

        var run = await _host.RunAsync(ListOutputsCommand.Build);

        Assert.Equal("main", StatusOf(run.Output, "alsa.speakers"));
        Assert.Equal("-", StatusOf(run.Output, "usb.headset"));
    }

    /* The enabled monitor device is tagged "monitor" */
    [Fact]
    public async Task List_EnabledMonitorDevice_TaggedMonitor()
    {
        _host.Repository.AppConfig.EnableMonitorPlayback = true;
        _host.Repository.AppConfig.MonitorAudioDeviceId = "mtts_mic";

        var run = await _host.RunAsync(ListOutputsCommand.Build);

        Assert.Equal("monitor", StatusOf(run.Output, "mtts_mic"));
    }

    /* A device used by both outputs is tagged "main, monitor" */
    [Fact]
    public async Task List_DeviceUsedByBothOutputs_TaggedMainAndMonitor()
    {
        _host.Repository.AppConfig.PrimaryAudioDeviceId = "usb.headset";
        _host.Repository.AppConfig.EnableMonitorPlayback = true;
        _host.Repository.AppConfig.MonitorAudioDeviceId = "usb.headset";

        var run = await _host.RunAsync(ListOutputsCommand.Build);

        Assert.Equal("main, monitor", StatusOf(run.Output, "usb.headset"));
    }

    /* A disabled output does not tag its configured device */
    [Fact]
    public async Task List_DisabledOutputs_DoNotTagDevices()
    {
        _host.Repository.AppConfig.EnablePrimaryPlayback = false;
        _host.Repository.AppConfig.PrimaryAudioDeviceId = "alsa.speakers";
        _host.Repository.AppConfig.EnableMonitorPlayback = false;
        _host.Repository.AppConfig.MonitorAudioDeviceId = "mtts_mic";

        var run = await _host.RunAsync(ListOutputsCommand.Build);

        Assert.Equal("-", StatusOf(run.Output, "alsa.speakers"));
        Assert.Equal("-", StatusOf(run.Output, "mtts_mic"));
    }

    /* Configured ids match devices ignoring case */
    [Fact]
    public async Task List_ConfiguredIdWithDifferentCase_StillTagsDevice()
    {
        _host.Repository.AppConfig.PrimaryAudioDeviceId = "USB.HEADSET";

        var run = await _host.RunAsync(ListOutputsCommand.Build);

        Assert.Equal("main", StatusOf(run.Output, "usb.headset"));
    }

    /* The summary shows each output's state and volume, plus how to change them */
    [Fact]
    public async Task List_Always_PrintsOutputSummary()
    {
        _host.Repository.AppConfig.PrimaryVolume = 80;
        _host.Repository.AppConfig.EnableMonitorPlayback = false;
        _host.Repository.AppConfig.MonitorVolume = 35;

        var run = await _host.RunAsync(ListOutputsCommand.Build);

        Assert.Contains("Main output: Enabled | Volume: 80%", run.Output);
        Assert.Contains("Monitor output: Disabled | Volume: 35%", run.Output);
        Assert.Contains("mtts output main/monitor set <DEVICE ID / NAME>", run.Output);
    }

    /* A successful query without a list still prints the (empty) table and summary */
    [Fact]
    public async Task List_NullDeviceList_PrintsEmptyTable()
    {
        _host.AudioPlayer.DevicesResultOverride = Result<List<AudioDeviceInfo>>.Success(null!);

        var run = await _host.RunAsync(ListOutputsCommand.Build);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Audio Output Sinks", run.Output);
        Assert.Contains("Main output:", run.Output);
    }
}
