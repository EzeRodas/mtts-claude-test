using System.Text.Json;
using MoonTTS.Domain.Common;
using MoonTTS.Domain.ValueObjects;
using MoonTTS.Engine.Protocol;

namespace MoonTTS.Infrastructure.Tts;

/* File Structure:
        - TryGetVoices(EngineDescriptor engine, string modelName) : IReadOnlyList<VoiceInfo>?
        - SaveVoices(EngineDescriptor engine, string modelName, IReadOnlyList<VoiceInfo> voices) : Result
        - Load(string path) : VoiceCatalogFile?
*/

/* Reads and writes the voices.json that sits next to each engine.json.
   Engine authors may ship it; when a model has no entry the host fills it from the worker's ListVoices reply */
public class EngineVoiceStore
{
    private readonly Lock _fileLock = new();

    /* Returns the cached voices of a model, or null when voices.json has no entry for it */
    public IReadOnlyList<VoiceInfo>? TryGetVoices(EngineDescriptor engine, string modelName)
    {
        string path = Path.Combine(engine.RootDirectory, VoiceCatalogFile.FileName);
        lock (_fileLock)
        {
            var catalog = Load(path);
            if (catalog == null || !catalog.Models.TryGetValue(modelName, out var entries) || entries.Count == 0)
                return null;

            return entries
                .Where(v => !string.IsNullOrWhiteSpace(v.Id))
                .Select(v => new VoiceInfo(v.Id, v.Name, v.Language))
                .ToList();
        }
    }

    /* Stores the voices of a model, keeping the entries of the other models (atomic .tmp + File.Move) */
    public Result SaveVoices(EngineDescriptor engine, string modelName, IReadOnlyList<VoiceInfo> voices)
    {
        string path = Path.Combine(engine.RootDirectory, VoiceCatalogFile.FileName);
        string tempPath = path + ".tmp";

        try
        {
            lock (_fileLock)
            {
                var catalog = Load(path) ?? new VoiceCatalogFile();
                catalog.Models[modelName] = voices
                    .Select(v => new VoiceEntry { Id = v.Id, Name = v.Name, Language = v.Language })
                    .ToList();

                using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, catalog, EngineProtocolJsonContext.Default.VoiceCatalogFile);
                }
                File.Move(tempPath, path, overwrite: true);
            }
            return Result.Success();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure($"Failed to write '{path}': {ex.Message}", ex);
        }
    }

    /* Parses voices.json. A missing or malformed file is treated as empty so it gets regenerated */
    private static VoiceCatalogFile? Load(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, EngineProtocolJsonContext.Default.VoiceCatalogFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine($"[EngineVoiceStore] Ignoring unreadable '{path}': {ex.Message}");
            return null;
        }
    }
}
