using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using MoonTTS.CLI.Utilities;
using MoonTTS.Core.Services;
using MoonTTS.Domain.Enums;

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

            if (activeModel != TtsModelType.Unselected && !downloadManager.IsModelDownloaded(activeModel))
            {
                var config = settingsManager.CurrentAppConfig;
                config.ActiveModel = TtsModelType.Unselected;
                settingsManager.SaveAppConfig(config);
                activeModel = TtsModelType.Unselected;
            }
            
            var availableModels = downloadManager.GetAvailableModels().ToList();

            var headers = new[] { "Model Key", "Display Name", "Downloaded", "Status" };

            var rows = availableModels.Select(m =>
            {
                bool isDownloaded = downloadManager.IsModelDownloaded(m.ModelType);
                bool isActive = m.ModelType == activeModel;

                return new[]
                {
                    m.ModelType.ToString(),
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
