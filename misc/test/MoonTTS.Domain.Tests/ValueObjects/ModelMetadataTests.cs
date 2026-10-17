using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.Domain.Tests.ValueObjects;

/* Verifies the stored values, display text and record equality of ModelMetadata */
public sealed class ModelMetadataTests
{
    private static readonly ModelId Id = new("kokoro", "v1.0-fp16");

    /* The constructor stores id, display name and download files */
    [Fact]
    public void Constructor_Always_StoresValues()
    {
        var files = new Dictionary<string, string> { ["model.onnx"] = "https://example.org/model.onnx" };

        var metadata = new ModelMetadata(Id, "Kokoro FP16", files);

        Assert.Equal(Id, metadata.Id);
        Assert.Equal("Kokoro FP16", metadata.DisplayName);
        Assert.Same(files, metadata.DownloadFiles);
    }

    /* ToString renders the display name */
    [Fact]
    public void ToString_Always_ReturnsDisplayName()
    {
        var metadata = new ModelMetadata(Id, "Kokoro FP16", []);

        Assert.Equal("Kokoro FP16", metadata.ToString());
    }

    /* Metadata built from the same values (and the same file map) is equal */
    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        var files = new Dictionary<string, string>();

        Assert.Equal(new ModelMetadata(Id, "Kokoro FP16", files), new ModelMetadata(Id, "Kokoro FP16", files));
    }

    /* Metadata with a different id is not equal */
    [Fact]
    public void Equality_DifferentId_AreNotEqual()
    {
        var files = new Dictionary<string, string>();

        Assert.NotEqual(
            new ModelMetadata(Id, "Kokoro FP16", files),
            new ModelMetadata(new ModelId("kokoro", "v1.0-fp32"), "Kokoro FP16", files));
    }

    /* Metadata with a different display name is not equal */
    [Fact]
    public void Equality_DifferentDisplayName_AreNotEqual()
    {
        var files = new Dictionary<string, string>();

        Assert.NotEqual(new ModelMetadata(Id, "Kokoro FP16", files), new ModelMetadata(Id, "Kokoro", files));
    }
}
