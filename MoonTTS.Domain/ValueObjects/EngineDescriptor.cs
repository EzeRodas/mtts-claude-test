namespace MoonTTS.Domain.ValueObjects;

/* An engine installed in the engines folder: where it lives, how to launch its worker and which models it offers */
public sealed record EngineDescriptor(
    string Id,
    string DisplayName,
    string RootDirectory,
    string ExecutablePath,
    IReadOnlyList<ModelMetadata> Models
);
