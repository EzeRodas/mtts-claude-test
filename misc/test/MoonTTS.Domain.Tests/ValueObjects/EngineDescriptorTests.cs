using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.Domain.Tests.ValueObjects;

/* Verifies the stored values and record semantics of EngineDescriptor */
public sealed class EngineDescriptorTests
{
    private static readonly IReadOnlyList<ModelMetadata> Models =
        [new ModelMetadata(new ModelId("kokoro", "v1"), "Kokoro v1", [])];

    /* The constructor stores every component */
    [Fact]
    public void Constructor_Always_StoresValues()
    {
        var descriptor = new EngineDescriptor("kokoro", "Kokoro", "/engines/Kokoro", "/engines/Kokoro/kokoro-worker", Models);

        Assert.Equal("kokoro", descriptor.Id);
        Assert.Equal("Kokoro", descriptor.DisplayName);
        Assert.Equal("/engines/Kokoro", descriptor.RootDirectory);
        Assert.Equal("/engines/Kokoro/kokoro-worker", descriptor.ExecutablePath);
        Assert.Same(Models, descriptor.Models);
    }

    /* Descriptors with the same values (and the same model list) are equal */
    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        Assert.Equal(
            new EngineDescriptor("kokoro", "Kokoro", "/root", "/root/worker", Models),
            new EngineDescriptor("kokoro", "Kokoro", "/root", "/root/worker", Models));
    }

    /* Descriptors that differ in any scalar component are not equal */
    [Theory]
    [InlineData("piper", "Kokoro", "/root", "/root/worker")]
    [InlineData("kokoro", "Piper", "/root", "/root/worker")]
    [InlineData("kokoro", "Kokoro", "/other", "/root/worker")]
    [InlineData("kokoro", "Kokoro", "/root", "/other/worker")]
    public void Equality_DifferentValue_AreNotEqual(string id, string displayName, string root, string executable)
    {
        Assert.NotEqual(
            new EngineDescriptor("kokoro", "Kokoro", "/root", "/root/worker", Models),
            new EngineDescriptor(id, displayName, root, executable, Models));
    }

    /* A with-expression copies the descriptor and changes only the given component */
    [Fact]
    public void With_ChangedExecutable_KeepsOtherValues()
    {
        var original = new EngineDescriptor("kokoro", "Kokoro", "/root", "/root/worker", Models);

        var copy = original with { ExecutablePath = "/root/worker.exe" };

        Assert.Equal("/root/worker.exe", copy.ExecutablePath);
        Assert.Equal(original.Id, copy.Id);
        Assert.Equal(original.RootDirectory, copy.RootDirectory);
        Assert.Same(original.Models, copy.Models);
    }
}
