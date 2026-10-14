namespace MoonTTS.Domain.ValueObjects;

/* File Structure:
        - ModelId(string engineId, string modelName)
        - Unselected : ModelId
        - IsUnselected : bool
        - TryParse(string? value, out ModelId modelId) : bool
        - ToString() : string
*/

/* Identifies a model of an installed engine as "<engineId>/<modelName>".
   The default value represents "no model selected" */
public readonly record struct ModelId
{
    public const char Separator = '/';

    private readonly string? _engineId;
    private readonly string? _modelName;

    /* Represents the absence of an active model */
    public static ModelId Unselected => default;

    public string EngineId => _engineId ?? string.Empty;
    public string ModelName => _modelName ?? string.Empty;
    public bool IsUnselected => _engineId is null;

    /* Creates an id from its two parts. The engine id cannot contain the separator */
    public ModelId(string engineId, string modelName)
    {
        if (string.IsNullOrWhiteSpace(engineId) || engineId.Contains(Separator))
            throw new ArgumentException($"Invalid engine id '{engineId}'.", nameof(engineId));
        if (string.IsNullOrWhiteSpace(modelName))
            throw new ArgumentException("Model name cannot be empty.", nameof(modelName));

        _engineId = engineId;
        _modelName = modelName;
    }

    /* Parses "<engineId>/<modelName>". Null or empty input yields Unselected and returns true */
    public static bool TryParse(string? value, out ModelId modelId)
    {
        modelId = Unselected;
        if (string.IsNullOrWhiteSpace(value))
            return true;

        int separatorIndex = value.IndexOf(Separator);
        if (separatorIndex <= 0 || separatorIndex == value.Length - 1)
            return false;

        string engineId = value[..separatorIndex];
        string modelName = value[(separatorIndex + 1)..];
        if (string.IsNullOrWhiteSpace(engineId) || string.IsNullOrWhiteSpace(modelName))
            return false;

        modelId = new ModelId(engineId, modelName);
        return true;
    }

    /* Returns the canonical "<engineId>/<modelName>" form, or an empty string when unselected */
    public override string ToString() => IsUnselected ? string.Empty : $"{EngineId}{Separator}{ModelName}";
}
