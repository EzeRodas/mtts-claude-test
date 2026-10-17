using MoonTTS.Domain.ValueObjects;
using MoonTTS.Infrastructure.Tts;

namespace MoonTTS.Infrastructure.Tests.Tts;

/* EngineCatalog over hand-written engine folders: discovery, manifest validation, duplicate ids,
   executable resolution and model lookups. In the Environment collection because one test points the
   user's data folder at a temp directory */
[Collection(TestCollections.Environment)]
public sealed class EngineCatalogTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _engines;

    /* Every test gets its own engines folder */
    public EngineCatalogTests()
    {
        _engines = _temp.CreateSubdirectory("engines");
    }

    /* Removes the temp folder */
    public void Dispose() => _temp.Dispose();

    /* Writes "<engines>/<folder>/engine.json" and returns the engine folder */
    private string WriteManifest(string folder, string json)
    {
        _temp.WriteFile(Path.Combine("engines", folder, "engine.json"), json);
        return Path.Combine(_engines, folder);
    }

    /* A minimal valid manifest with the given id and model ids */
    private string WriteEngine(string folder, string id, params string[] models)
    {
        string modelsJson = string.Join(",", models.Select(m => $"{{\"id\":\"{m}\",\"displayName\":\"{m} display\"}}"));
        return WriteManifest(folder, $"{{\"id\":\"{id}\",\"displayName\":\"{id} engine\",\"executable\":\"worker\",\"models\":[{modelsJson}]}}");
    }

    /* A folder that does not exist yields no models */
    [Fact]
    public void GetAvailableModels_MissingDirectory_ReturnsEmpty()
    {
        var catalog = new EngineCatalog(_temp.Combine("does-not-exist"));

        Assert.Empty(catalog.GetAvailableModels());
    }

    /* Every model of a valid engine is exposed as "<engineId>/<modelId>" */
    [Fact]
    public void GetAvailableModels_ValidEngine_ReturnsModelsAddressedByEngineId()
    {
        WriteEngine("AnyFolderName", "piper", "lessac", "amy");

        var catalog = new EngineCatalog(_engines);

        Assert.Equal([new ModelId("piper", "lessac"), new ModelId("piper", "amy")], catalog.GetAvailableModels().Select(m => m.Id));
    }

    /* Models of every installed engine are listed together */
    [Fact]
    public void GetAvailableModels_SeveralEngines_ReturnsModelsOfAllEngines()
    {
        WriteEngine("A", "alpha", "one");
        WriteEngine("B", "beta", "two");

        var catalog = new EngineCatalog(_engines);

        Assert.Equal(2, catalog.GetAvailableModels().Count());
    }

    /* A model keeps its display name and its download files */
    [Fact]
    public void GetAvailableModels_ModelWithFiles_KeepsDisplayNameAndDownloadFiles()
    {
        WriteManifest("Piper", """
            {
              "id": "piper",
              "executable": "worker",
              "models": [ { "id": "lessac", "displayName": "Lessac", "downloadFiles": { "a.onnx": "https://example.invalid/a" } } ]
            }
            """);

        var model = Assert.Single(new EngineCatalog(_engines).GetAvailableModels());

        Assert.Equal("Lessac", model.DisplayName);
        Assert.Equal(new Dictionary<string, string> { ["a.onnx"] = "https://example.invalid/a" }, model.DownloadFiles);
    }

    /* A model without display name is shown by its id */
    [Fact]
    public void GetAvailableModels_ModelWithoutDisplayName_UsesModelId()
    {
        WriteManifest("Piper", """{ "id": "piper", "executable": "worker", "models": [ { "id": "lessac" } ] }""");

        var model = Assert.Single(new EngineCatalog(_engines).GetAvailableModels());

        Assert.Equal("lessac", model.DisplayName);
    }

    /* Models with a blank id are skipped */
    [Fact]
    public void GetAvailableModels_ModelWithBlankId_IsSkipped()
    {
        WriteManifest("Piper", """{ "id": "piper", "executable": "worker", "models": [ { "id": " " }, { "id": "kept" } ] }""");

        var model = Assert.Single(new EngineCatalog(_engines).GetAvailableModels());

        Assert.Equal("kept", model.Id.ModelName);
    }

    /* A sub folder without engine.json is not an engine */
    [Fact]
    public void GetAvailableModels_FolderWithoutManifest_IsIgnored()
    {
        _temp.CreateSubdirectory("engines", "NotAnEngine");
        WriteEngine("Piper", "piper", "lessac");

        Assert.Single(new EngineCatalog(_engines).GetAvailableModels());
    }

    /* Manifests that cannot be used are skipped while valid engines are still discovered */
    [Theory]
    [InlineData("this is not json")]
    [InlineData("null")]
    [InlineData("""{ "executable": "worker", "models": [ { "id": "m" } ] }""")]
    [InlineData("""{ "id": " ", "executable": "worker", "models": [ { "id": "m" } ] }""")]
    [InlineData("""{ "id": "bad/id", "executable": "worker", "models": [ { "id": "m" } ] }""")]
    [InlineData("""{ "id": "noexe", "models": [ { "id": "m" } ] }""")]
    [InlineData("""{ "id": "noexe", "executable": "  ", "models": [ { "id": "m" } ] }""")]
    public void GetAvailableModels_InvalidManifest_IsSkipped(string invalidManifest)
    {
        WriteManifest("Broken", invalidManifest);
        WriteEngine("Valid", "valid", "model");

        var model = Assert.Single(new EngineCatalog(_engines).GetAvailableModels());

        Assert.Equal(new ModelId("valid", "model"), model.Id);
    }

    /* Comments and trailing commas are accepted in engine.json */
    [Fact]
    public void GetAvailableModels_ManifestWithCommentsAndTrailingCommas_IsLoaded()
    {
        WriteManifest("Piper", """
            {
              // hand-written manifest
              "id": "piper",
              "executable": "worker",
              "models": [ { "id": "lessac", }, ],
            }
            """);

        Assert.Single(new EngineCatalog(_engines).GetAvailableModels());
    }

    /* When two folders declare the same engine id, only the first folder (ordinal order) is kept */
    [Fact]
    public void GetEngine_DuplicateEngineId_KeepsFirstFolderInOrdinalOrder()
    {
        string first = WriteEngine("A-first", "same", "from-a");
        WriteEngine("B-second", "same", "from-b");

        var catalog = new EngineCatalog(_engines);

        Assert.Equal(first, catalog.GetEngine("same")!.RootDirectory);
        Assert.Equal(["from-a"], catalog.GetAvailableModels().Select(m => m.Id.ModelName));
    }

    /* GetEngine returns the descriptor of an installed engine */
    [Fact]
    public void GetEngine_InstalledEngine_ReturnsDescriptor()
    {
        string folder = WriteEngine("Piper", "piper", "lessac");

        var engine = new EngineCatalog(_engines).GetEngine("piper");

        Assert.NotNull(engine);
        Assert.Equal("piper engine", engine.DisplayName);
        Assert.Equal(folder, engine.RootDirectory);
        Assert.Single(engine.Models);
    }

    /* An engine without display name is shown by its id */
    [Fact]
    public void GetEngine_ManifestWithoutDisplayName_UsesEngineId()
    {
        WriteManifest("Piper", """{ "id": "piper", "executable": "worker" }""");

        Assert.Equal("piper", new EngineCatalog(_engines).GetEngine("piper")!.DisplayName);
    }

    /* Engine ids are matched exactly (case sensitive) */
    [Fact]
    public void GetEngine_DifferentCase_ReturnsNull()
    {
        WriteEngine("Piper", "piper", "lessac");

        Assert.Null(new EngineCatalog(_engines).GetEngine("Piper"));
    }

    /* An unknown engine id yields null */
    [Fact]
    public void GetEngine_UnknownEngine_ReturnsNull()
    {
        WriteEngine("Piper", "piper", "lessac");

        Assert.Null(new EngineCatalog(_engines).GetEngine("kokoro"));
    }

    /* The executable is resolved relative to the engine folder as a full path */
    [Fact]
    public void GetEngine_RelativeExecutable_ResolvesInsideEngineFolder()
    {
        string folder = WriteManifest("Piper", """{ "id": "piper", "executable": "bin/../bin/worker" }""");

        var engine = new EngineCatalog(_engines).GetEngine("piper")!;

        Assert.Equal(Path.GetFullPath(Path.Combine(folder, "bin", "worker")), engine.ExecutablePath);
    }

    /* On Windows an extension-less executable resolves to its ".exe" when only that file exists */
    [Fact(Skip = "Windows only", SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void GetEngine_WindowsExecutableWithoutExtension_AppendsExe()
    {
        string folder = WriteManifest("Piper", """{ "id": "piper", "executable": "worker" }""");
        File.WriteAllText(Path.Combine(folder, "worker.exe"), string.Empty);

        Assert.Equal(Path.Combine(folder, "worker.exe"), new EngineCatalog(_engines).GetEngine("piper")!.ExecutablePath);
    }

    /* Outside Windows the executable path is used exactly as declared */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void GetEngine_PosixExecutableWithoutExtension_KeepsDeclaredName()
    {
        string folder = WriteManifest("Piper", """{ "id": "piper", "executable": "worker" }""");
        File.WriteAllText(Path.Combine(folder, "worker.exe"), string.Empty);

        Assert.Equal(Path.Combine(folder, "worker"), new EngineCatalog(_engines).GetEngine("piper")!.ExecutablePath);
    }

    /* Engines are scanned once, on first use: folders added afterwards are not picked up */
    [Fact]
    public void GetAvailableModels_EngineAddedAfterFirstUse_IsNotPickedUp()
    {
        WriteEngine("A", "alpha", "one");
        var catalog = new EngineCatalog(_engines);
        Assert.Single(catalog.GetAvailableModels());

        WriteEngine("B", "beta", "two");

        Assert.Single(catalog.GetAvailableModels());
    }

    /* The scan is deferred until first use: engines installed after construction are discovered */
    [Fact]
    public void GetAvailableModels_EngineAddedBeforeFirstUse_IsDiscovered()
    {
        var catalog = new EngineCatalog(_engines);

        WriteEngine("A", "alpha", "one");

        Assert.Single(catalog.GetAvailableModels());
    }

    /* GetModelMetadata finds a model by its exact id */
    [Fact]
    public void GetModelMetadata_KnownModel_ReturnsIt()
    {
        WriteEngine("Piper", "piper", "lessac", "amy");

        var model = new EngineCatalog(_engines).GetModelMetadata(new ModelId("piper", "amy"));

        Assert.Equal(new ModelId("piper", "amy"), model!.Id);
    }

    /* GetModelMetadata returns null for an unknown model */
    [Fact]
    public void GetModelMetadata_UnknownModel_ReturnsNull()
    {
        WriteEngine("Piper", "piper", "lessac");

        Assert.Null(new EngineCatalog(_engines).GetModelMetadata(new ModelId("piper", "other")));
    }

    /* GetModelMetadata returns null for Unselected */
    [Fact]
    public void GetModelMetadata_Unselected_ReturnsNull()
    {
        WriteEngine("Piper", "piper", "lessac");

        Assert.Null(new EngineCatalog(_engines).GetModelMetadata(ModelId.Unselected));
    }

    /* FindModel resolves the full "<engine>/<model>" id ignoring case and surrounding spaces */
    [Fact]
    public void FindModel_FullIdWithDifferentCaseAndSpaces_ReturnsModel()
    {
        WriteEngine("Piper", "piper", "lessac");

        var model = new EngineCatalog(_engines).FindModel("  PIPER/Lessac ");

        Assert.Equal(new ModelId("piper", "lessac"), model!.Id);
    }

    /* FindModel resolves a model name that only one engine offers */
    [Fact]
    public void FindModel_UniqueModelName_ReturnsModel()
    {
        WriteEngine("Piper", "piper", "lessac");
        WriteEngine("Kokoro", "kokoro", "v1");

        var model = new EngineCatalog(_engines).FindModel("V1");

        Assert.Equal(new ModelId("kokoro", "v1"), model!.Id);
    }

    /* FindModel refuses a model name offered by several engines */
    [Fact]
    public void FindModel_AmbiguousModelName_ReturnsNull()
    {
        WriteEngine("Piper", "piper", "shared");
        WriteEngine("Kokoro", "kokoro", "shared");

        Assert.Null(new EngineCatalog(_engines).FindModel("shared"));
    }

    /* FindModel still resolves an ambiguous name when the full id is given */
    [Fact]
    public void FindModel_AmbiguousNameGivenAsFullId_ReturnsModel()
    {
        WriteEngine("Piper", "piper", "shared");
        WriteEngine("Kokoro", "kokoro", "shared");

        Assert.Equal(new ModelId("kokoro", "shared"), new EngineCatalog(_engines).FindModel("kokoro/shared")!.Id);
    }

    /* FindModel returns null when nothing matches */
    [Fact]
    public void FindModel_UnknownQuery_ReturnsNull()
    {
        WriteEngine("Piper", "piper", "lessac");

        Assert.Null(new EngineCatalog(_engines).FindModel("piper/other"));
    }

    /* FindModel returns null for blank input */
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FindModel_BlankQuery_ReturnsNull(string query)
    {
        WriteEngine("Piper", "piper", "lessac");

        Assert.Null(new EngineCatalog(_engines).FindModel(query));
    }

    /* Without an explicit folder the catalog scans "$XDG_DATA_HOME/Moon-TTS/engines" */
    [Fact(Skip = "XDG_DATA_HOME is only honoured on Linux", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void Constructor_NoDirectory_ScansUserEnginesFolder()
    {
        using var temp = new TempDirectory();
        using var env = EnvironmentScope.IsolatedUserDirectories(temp);
        temp.WriteFile(Path.Combine("data", "Moon-TTS", "engines", "Piper", "engine.json"),
            """{ "id": "piper", "executable": "worker", "models": [ { "id": "lessac" } ] }""");

        var catalog = new EngineCatalog();

        Assert.Equal(new ModelId("piper", "lessac"), Assert.Single(catalog.GetAvailableModels()).Id);
    }
}
