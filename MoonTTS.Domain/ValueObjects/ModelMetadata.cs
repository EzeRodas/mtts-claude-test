namespace MoonTTS.Domain.ValueObjects;

/* File Structure:
        - Unselected : ModelMetadata
        - IsUnselected : bool
        - ToString() : string
*/

/* Describes a downloadable model of an installed engine.
   The keys of DownloadFiles are the local file names, its values the remote URLs */
public record ModelMetadata(
    ModelId Id,
    string DisplayName,
    Dictionary<string, string> DownloadFiles
)
{
    /* Null-object entry representing "no model selected", so selectors always have a concrete item to show */
    public static ModelMetadata Unselected { get; } = new(ModelId.Unselected, "Unselected", new Dictionary<string, string>());

    /* True when this entry stands for "no model selected" */
    public bool IsUnselected => Id.IsUnselected;

    /* Keeps the display name visible wherever the model is rendered through ToString */
    public override string ToString() => DisplayName;
}
