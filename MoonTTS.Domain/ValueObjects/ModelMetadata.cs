namespace MoonTTS.Domain.ValueObjects;

/* Describes a downloadable model of an installed engine.
   The keys of DownloadFiles are the local file names, its values the remote URLs */
public record ModelMetadata(
    ModelId Id,
    string DisplayName,
    Dictionary<string, string> DownloadFiles
)
{
    /* Keeps the display name visible wherever the model is rendered through ToString */
    public override string ToString() => DisplayName;
}
