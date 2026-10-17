using System.Text.Json;
using MoonTTS.Domain.ValueObjects;
using MoonTTS.Engine.Protocol;
using MoonTTS.Infrastructure.Tts;

namespace MoonTTS.Infrastructure.Tests.Tts;

/* EngineVoiceStore reading and atomically writing the voices.json next to an engine.json */
public sealed class EngineVoiceStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly EngineDescriptor _engine;
    private readonly EngineVoiceStore _store = new();

    /* Every test gets its own engine folder */
    public EngineVoiceStoreTests()
    {
        string root = _temp.CreateSubdirectory("Engine");
        _engine = new EngineDescriptor("dummy", "Dummy", root, Path.Combine(root, "worker"), []);
    }

    /* Removes the temp folder */
    public void Dispose() => _temp.Dispose();

    private string VoicesPath => Path.Combine(_engine.RootDirectory, VoiceCatalogFile.FileName);

    /* Writes voices.json by hand */
    private void WriteVoicesFile(string json) => File.WriteAllText(VoicesPath, json);

    /* Reads voices.json back */
    private VoiceCatalogFile ReadVoicesFile() =>
        JsonSerializer.Deserialize(File.ReadAllText(VoicesPath), EngineProtocolJsonContext.Default.VoiceCatalogFile)!;

    /* Without voices.json there are no cached voices */
    [Fact]
    public void TryGetVoices_NoFile_ReturnsNull()
    {
        Assert.Null(_store.TryGetVoices(_engine, "model"));
    }

    /* The entry of the requested model is returned with names and languages */
    [Fact]
    public void TryGetVoices_EntryExists_ReturnsVoices()
    {
        WriteVoicesFile("""
            { "models": { "model": [ { "id": "a", "name": "Alpha", "language": "en-US" }, { "id": "b" } ] } }
            """);

        Assert.Equal([new VoiceInfo("a", "Alpha", "en-US"), new VoiceInfo("b")], _store.TryGetVoices(_engine, "model"));
    }

    /* A model without an entry has no cached voices */
    [Fact]
    public void TryGetVoices_OtherModelOnly_ReturnsNull()
    {
        WriteVoicesFile("""{ "models": { "other": [ { "id": "a" } ] } }""");

        Assert.Null(_store.TryGetVoices(_engine, "model"));
    }

    /* An empty entry counts as missing so it gets regenerated */
    [Fact]
    public void TryGetVoices_EmptyEntry_ReturnsNull()
    {
        WriteVoicesFile("""{ "models": { "model": [] } }""");

        Assert.Null(_store.TryGetVoices(_engine, "model"));
    }

    /* Voices with a blank id are dropped */
    [Fact]
    public void TryGetVoices_VoiceWithBlankId_IsDropped()
    {
        WriteVoicesFile("""{ "models": { "model": [ { "id": "" }, { "id": "kept" } ] } }""");

        Assert.Equal([new VoiceInfo("kept")], _store.TryGetVoices(_engine, "model"));
    }

    /* A malformed voices.json is treated as missing */
    [Fact]
    public void TryGetVoices_MalformedFile_ReturnsNull()
    {
        WriteVoicesFile("{ not json");

        Assert.Null(_store.TryGetVoices(_engine, "model"));
    }

    /* SaveVoices creates voices.json with the model's voices */
    [Fact]
    public void SaveVoices_NoFile_CreatesFileWithModelEntry()
    {
        var result = _store.SaveVoices(_engine, "model", [new VoiceInfo("a", "Alpha", "en-US")]);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var entry = Assert.Single(ReadVoicesFile().Models["model"]);
        Assert.Equal(("a", "Alpha", "en-US"), (entry.Id, entry.Name, entry.Language));
    }

    /* Saved voices are read back by TryGetVoices */
    [Fact]
    public void SaveVoices_ThenTryGetVoices_RoundTrips()
    {
        IReadOnlyList<VoiceInfo> voices = [new VoiceInfo("a", "Alpha", "en-US"), new VoiceInfo("b")];

        _store.SaveVoices(_engine, "model", voices);

        Assert.Equal(voices, _store.TryGetVoices(_engine, "model"));
    }

    /* Saving one model keeps the entries of the other models */
    [Fact]
    public void SaveVoices_ExistingOtherModel_KeepsItsEntry()
    {
        WriteVoicesFile("""{ "models": { "other": [ { "id": "x", "name": "Shipped" } ] } }""");

        _store.SaveVoices(_engine, "model", [new VoiceInfo("a")]);

        Assert.Equal([new VoiceInfo("x", "Shipped")], _store.TryGetVoices(_engine, "other"));
        Assert.Equal([new VoiceInfo("a")], _store.TryGetVoices(_engine, "model"));
    }

    /* Saving a model again replaces its previous entry */
    [Fact]
    public void SaveVoices_ExistingEntry_ReplacesIt()
    {
        _store.SaveVoices(_engine, "model", [new VoiceInfo("old")]);

        _store.SaveVoices(_engine, "model", [new VoiceInfo("new")]);

        Assert.Equal([new VoiceInfo("new")], _store.TryGetVoices(_engine, "model"));
    }

    /* A malformed voices.json is replaced by a valid one holding the saved model */
    [Fact]
    public void SaveVoices_MalformedFile_RewritesValidFile()
    {
        WriteVoicesFile("{ not json");

        var result = _store.SaveVoices(_engine, "model", [new VoiceInfo("a")]);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(["model"], ReadVoicesFile().Models.Keys);
    }

    /* The write goes through a temporary file that does not remain afterwards */
    [Fact]
    public void SaveVoices_Success_LeavesNoTemporaryFile()
    {
        File.WriteAllText(VoicesPath + ".tmp", "stale");

        _store.SaveVoices(_engine, "model", [new VoiceInfo("a")]);

        Assert.False(File.Exists(VoicesPath + ".tmp"));
    }

    /* A folder that cannot be written yields a failure instead of an exception */
    [Fact]
    public void SaveVoices_EngineFolderMissing_ReturnsFailure()
    {
        var missing = _engine with { RootDirectory = _temp.Combine("missing") };

        var result = _store.SaveVoices(missing, "model", [new VoiceInfo("a")]);

        Assert.True(result.IsFailure);
        Assert.Contains(VoiceCatalogFile.FileName, result.ErrorMessage);
        Assert.NotNull(result.Exception);
    }
}
