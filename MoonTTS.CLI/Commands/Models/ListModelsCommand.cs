using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using MoonTTS.CLI.Utilities;
using MoonTTS.Core.Services;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.CLI.commands;

/* Lists all supported models and download status */

public static class ListModelsCommand
{
    public static Command Build(IServiceProvider services)
    {
        var command = new Command("list", "Lists all supported models and download status.");
        command.SetAction((_) =>
        {
            var downloadManager = services.GetRequiredService<DownloadManager>();
            var settingsManager = services.GetRequiredService<SettingsManager>();

            var activeModel = settingsManager.CurrentAppConfig.ActiveModel;

            if (!activeModel.IsUnselected && !downloadManager.IsModelDownloaded(activeModel))
            {
                var config = settingsManager.CurrentAppConfig;
                config.ActiveModel = ModelId.Unselected;
                settingsManager.SaveAppConfig(config);
                activeModel = ModelId.Unselected;
            }
            
            var availableModels = downloadManager.GetAvailableModels().ToList();
            if (availableModels.Count == 0)
            {
                Terminal.Warning("No engines are installed.");
                Terminal.Muted("Install an engine by placing its folder (with an engine.json) in the Moon-TTS engines folder.");
                return 0;
            }

            var headers = new[] { "Model Key", "Display Name", "Downloaded", "Status" };

            var rows = availableModels.Select(m =>
            {
                bool isDownloaded = downloadManager.IsModelDownloaded(m.Id);
                bool isActive = m.Id == activeModel;

                return new[]
                {
                    m.Id.ToString(),
                    m.DisplayName,
                    isDownloaded ? "Yes" : "No",
                    isActive ? "ACTIVE" : "-"
                };
            });

            Terminal.Table("Supported TTS Models", headers, rows);
            return 0;
        });
        return command;
    }
}
