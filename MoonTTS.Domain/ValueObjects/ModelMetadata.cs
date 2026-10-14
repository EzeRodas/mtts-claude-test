using MoonTTS.Domain.Enums;

namespace MoonTTS.Domain.ValueObjects;

public record ModelMetadata(
    TtsModelType ModelType,
    string DisplayName,
    Dictionary<string, string> DownloadFiles
);