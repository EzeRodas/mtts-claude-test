using MoonTTS.Domain.Common;
using MoonTTS.Domain.Interfaces;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.Tests.Common;

/* File Structure:
        - AddModel(string engineId, string modelName, params string[] files) : ModelMetadata
        - GetAvailableModels() : IEnumerable<ModelMetadata>
        - GetModelMetadata(ModelId modelId) : ModelMetadata?
        - FindModel(string query) : ModelMetadata?
        - GetEngine(string engineId) : EngineDescriptor?
*/

/* In-memory IModelRegistry */
public sealed class FakeModelRegistry : IModelRegistry
{
    public List<ModelMetadata> Models { get; } = new();
    public Dictionary<string, EngineDescriptor> Engines { get; } = new();

    /* Registers a model whose download files are `files` (each with a dummy URL) */
    public ModelMetadata AddModel(string engineId, string modelName, params string[] files)
    {
        var model = new ModelMetadata(
            new ModelId(engineId, modelName),
            $"{engineId} {modelName}",
            files.ToDictionary(f => f, f => $"https://example.invalid/{engineId}/{f}"));
        Models.Add(model);
        return model;
    }

    /* Returns every registered model */
    public IEnumerable<ModelMetadata> GetAvailableModels() => Models;

    /* Looks up a model by id */
    public ModelMetadata? GetModelMetadata(ModelId modelId) => Models.FirstOrDefault(m => m.Id == modelId);

    /* Matches the full id or the model name, ignoring case */
    public ModelMetadata? FindModel(string query) =>
        Models.FirstOrDefault(m => string.Equals(m.Id.ToString(), query, StringComparison.OrdinalIgnoreCase))
        ?? Models.FirstOrDefault(m => string.Equals(m.Id.ModelName, query, StringComparison.OrdinalIgnoreCase));

    /* Looks up a registered engine */
    public EngineDescriptor? GetEngine(string engineId) => Engines.GetValueOrDefault(engineId);
}

/* File Structure:
        - DownloadModelAsync(ModelMetadata model, string destinationFolder, IProgress<double>? progress, CancellationToken cancellationToken) : Task<Result>
*/

/* IModelDownloader that "downloads" by writing small placeholder files */
public sealed class FakeModelDownloader : IModelDownloader
{
    public Result ResultToReturn { get; set; } = Result.Success();
    public List<(ModelMetadata Model, string Destination)> Downloads { get; } = new();

    /* Writes every file of the model into the destination (unless configured to fail) and reports 100% */
    public Task<Result> DownloadModelAsync(ModelMetadata model, string destinationFolder, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        Downloads.Add((model, destinationFolder));
        if (ResultToReturn.IsSuccess)
        {
            Directory.CreateDirectory(destinationFolder);
            foreach (string file in model.DownloadFiles.Keys)
                File.WriteAllText(Path.Combine(destinationFolder, file), "dummy model");
            progress?.Report(100);
        }
        return Task.FromResult(ResultToReturn);
    }
}
