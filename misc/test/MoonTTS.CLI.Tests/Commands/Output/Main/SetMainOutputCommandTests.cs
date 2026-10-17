using MoonTTS.CLI.commands.Output.Main;
using MoonTTS.CLI.Tests.Support;
using MoonTTS.Domain.Common;
using MoonTTS.Domain.Entities;

namespace MoonTTS.CLI.Tests.Commands.Output.Main;

[Collection(TestCollections.Environment)]
public sealed class SetMainOutputCommandTests : IDisposable
{
    private readonly CliTestHost _host = new();

    /* Adds the dummy sinks the OS reports */
    public SetMainOutputCommandTests()
    {
        _host.AudioPlayer.Devices.Add(new AudioDeviceInfo { DeviceId = "alsa.speakers", DeviceName = "Built-in Speakers" });
        _host.AudioPlayer.Devices.Add(new AudioDeviceInfo { DeviceId = "usb.headset", DeviceName = "USB Headset" });
    }

    /* Releases the host */
    public void Dispose() => _host.Dispose();

    /* Build creates the "set" command with a required "device" argument */
    [Fact]
    public void Build_Always_CreatesSetCommandWithDeviceArgument()
    {
        var command = SetMainOutputCommand.Build(_host.Services);

        Assert.Equal("set", command.Name);
        Assert.Equal("device", Assert.Single(command.Arguments).Name);
    }

    /* A failing device query is reported and nothing is saved */
    [Fact]
    public async Task Set_DeviceQueryFails_ReturnsErrorAndDoesNotSave()
    {
        _host.AudioPlayer.DevicesResultOverride = Result<List<AudioDeviceInfo>>.Failure("no audio server");

        var run = await _host.RunAsync(SetMainOutputCommand.Build, "usb.headset");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Error retrieving audio devices: no audio server", run.Output);
        Assert.Equal(0, _host.Repository.AppConfigSaveCount);
    }

    /* A successful query without a device list is treated as a failure */
    [Fact]
    public async Task Set_NullDeviceList_ReturnsError()
    {
        _host.AudioPlayer.DevicesResultOverride = Result<List<AudioDeviceInfo>>.Success(null!);

        var run = await _host.RunAsync(SetMainOutputCommand.Build, "usb.headset");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Error retrieving audio devices", run.Output);
    }

    /* A device that does not exist fails with a hint and nothing is saved */
    [Fact]
    public async Task Set_UnknownDevice_ReturnsErrorWithHint()
    {
        var run = await _host.RunAsync(SetMainOutputCommand.Build, "bluetooth");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Error: Unknown audio device 'bluetooth'.", run.Output);
        Assert.Contains("mtts output list", run.Output);
        Assert.Equal(0, _host.Repository.AppConfigSaveCount);
    }

    /* A device given by id is saved as the primary device */
    [Fact]
    public async Task Set_DeviceById_SavesPrimaryDeviceId()
    {
        var run = await _host.RunAsync(SetMainOutputCommand.Build, "usb.headset");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("usb.headset", _host.Repository.AppConfig.PrimaryAudioDeviceId);
        Assert.Equal(1, _host.Repository.AppConfigSaveCount);
    }

    /* A device given by (part of) its name is saved by its id */
    [Fact]
    public async Task Set_DeviceByPartialName_SavesPrimaryDeviceId()
    {
        await _host.RunAsync(SetMainOutputCommand.Build, "speakers");

        Assert.Equal("alsa.speakers", _host.Repository.AppConfig.PrimaryAudioDeviceId);
    }

    /* The monitor device is left untouched */
    [Fact]
    public async Task Set_Device_KeepsMonitorDevice()
    {
        _host.Repository.AppConfig.MonitorAudioDeviceId = "alsa.speakers";

        await _host.RunAsync(SetMainOutputCommand.Build, "usb.headset");

        Assert.Equal("alsa.speakers", _host.Repository.AppConfig.MonitorAudioDeviceId);
    }

    /* The confirmation shows the device name and id */
    [Fact]
    public async Task Set_Device_PrintsDeviceNameAndId()
    {
        var run = await _host.RunAsync(SetMainOutputCommand.Build, "USB Headset");

        Assert.Contains("Main audio output set to: USB Headset (usb.headset)", run.Output);
    }
}
