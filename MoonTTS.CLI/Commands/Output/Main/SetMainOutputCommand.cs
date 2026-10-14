using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using MoonTTS.CLI.Utilities;
using MoonTTS.Core.Services;
using MoonTTS.Domain.Interfaces;

namespace MoonTTS.CLI.commands.Output.Main;

/* Sets the device output for primary playback */

public static class SetMainOutputCommand
{
    public static Command Build(IServiceProvider services)
    {
        var command = new Command("set", "Sets the device output for primary playback.");
        var deviceArg = new Argument<string>("device") { Description = "Device ID or device name." };
        command.Add(deviceArg);

        command.SetAction((parseResult) =>
        {
            string device = parseResult.GetValue(deviceArg) ?? string.Empty;
            var audioPlayer = services.GetRequiredService<IAudioPlayer>();
            var settingsManager = services.GetRequiredService<SettingsManager>();

            var devicesResult = audioPlayer.GetAvailableDevices();
            if (devicesResult.IsFailure || devicesResult.Value == null)
            {
                Terminal.Error($"Error retrieving audio devices: {devicesResult.ErrorMessage}");
                return 1;
            }

            var matched = DeviceFinder.FindDevice(devicesResult.Value, device);
            if (matched == null)
            {
                Terminal.Error($"Error: Unknown audio device '{device}'.");
                Terminal.Muted("Run 'mtts output list' to view available devices.");
                return 1;
            }

            var config = settingsManager.CurrentAppConfig;
            config.PrimaryAudioDeviceId = matched.DeviceId;

            var saveResult = settingsManager.SaveAppConfig(config);
            if (saveResult.IsFailure)
            {
                Terminal.Error($"Failed to save settings: {saveResult.ErrorMessage}");
                return 1;
            }

            Terminal.Success($"Main audio output set to: {matched.DeviceName} ({matched.DeviceId})");
            return 0;
        });

        return command;
    }
}
