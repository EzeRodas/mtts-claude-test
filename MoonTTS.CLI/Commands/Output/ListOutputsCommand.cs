using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using MoonTTS.CLI.Utilities;
using MoonTTS.Core.Services;
using MoonTTS.Domain.Interfaces;

namespace MoonTTS.CLI.commands.Output;

/* Lists all available system audio devices and their primary / monitor assignments */

public static class ListOutputsCommand
{
    public static Command Build(IServiceProvider services)
    {
        var command = new Command("list", "Lists available audio output devices.");
        command.SetAction((_) =>
        {
            var audioPlayer = services.GetRequiredService<IAudioPlayer>();
            var settingsManager = services.GetRequiredService<SettingsManager>();

            var result = audioPlayer.GetAvailableDevices();
            if (result.IsFailure)
            {
                Terminal.Error($"Error retrieving audio devices: {result.ErrorMessage}");
                return 1;
            }
            
            var config = settingsManager.CurrentAppConfig;
            var devices = result.Value ?? new();

            var headers = new[] { "Device Name", "Device ID", "Status" };

            var rows = devices.Select(d =>
            {
                bool isMain = config.EnablePrimaryPlayback && string.Equals(config.PrimaryAudioDeviceId, d.DeviceId, StringComparison.OrdinalIgnoreCase);
                bool isMonitor = config.EnableMonitorPlayback && string.Equals(config.MonitorAudioDeviceId, d.DeviceId, StringComparison.OrdinalIgnoreCase);

                string status = (isMain, isMonitor) switch
                {
                    (true, true) => "main, monitor",
                    (true, false) => "main",
                    (false, true) => "monitor",
                    _ => "-"
                };

                return new[] { d.DeviceName, d.DeviceId, status };
            });

            Terminal.Table("Audio Output Sinks", headers, rows);

            Terminal.Info($"Main output: {(config.EnablePrimaryPlayback ? "Enabled" : "Disabled")} | Volume: {config.PrimaryVolume}%");
            Terminal.Info($"Monitor output: {(config.EnableMonitorPlayback ? "Enabled" : "Disabled")} | Volume: {config.MonitorVolume}%");
            Terminal.Muted("Change outputs with: mtts output main/monitor set <DEVICE ID / NAME>");
            return 0;
        });

        return command;
    }
}