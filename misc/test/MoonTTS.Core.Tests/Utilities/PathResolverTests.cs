using MoonTTS.Core.Utilities;

namespace MoonTTS.Core.Tests.Utilities;

/* Tests for PathResolver. Each OS branch runs on its own OS; HOME / XDG_* point at a temp folder */
[Collection(TestCollections.Environment)]
public sealed class PathResolverTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly EnvironmentScope _env;

    /* Isolates the user folders of every test */
    public PathResolverTests()
    {
        _env = EnvironmentScope.IsolatedUserDirectories(_temp);
    }

    /* Restores the environment and removes the temp folder */
    public void Dispose()
    {
        _env.Dispose();
        _temp.Dispose();
    }

    private string Home => _temp.Combine("home");

    /* On Linux the config folder lives under XDG_CONFIG_HOME */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void GetConfigDirectory_LinuxWithXdgConfigHome_ReturnsXdgSubfolder()
    {
        string result = PathResolver.GetConfigDirectory();

        Assert.Equal(Path.Combine(_temp.Combine("config"), "Moon-TTS"), result);
    }

    /* On Linux without XDG_CONFIG_HOME the config folder falls back to ~/.config */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void GetConfigDirectory_LinuxWithoutXdgConfigHome_FallsBackToHomeConfig()
    {
        _env.Set("XDG_CONFIG_HOME", null);

        string result = PathResolver.GetConfigDirectory();

        Assert.Equal(Path.Combine(Home, ".config", "Moon-TTS"), result);
    }

    /* On Linux an empty XDG_CONFIG_HOME is treated as unset */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void GetConfigDirectory_LinuxWithEmptyXdgConfigHome_FallsBackToHomeConfig()
    {
        _env.Set("XDG_CONFIG_HOME", string.Empty);

        string result = PathResolver.GetConfigDirectory();

        Assert.Equal(Path.Combine(Home, ".config", "Moon-TTS"), result);
    }

    /* On macOS the config folder is ~/Library/Application Support/Moon-TTS */
    [Fact(Skip = "macOS only", SkipUnless = nameof(TestPlatform.IsMacOS), SkipType = typeof(TestPlatform))]
    public void GetConfigDirectory_MacOS_ReturnsApplicationSupportFolder()
    {
        string result = PathResolver.GetConfigDirectory();

        Assert.Equal(Path.Combine(Home, "Library", "Application Support", "Moon-TTS"), result);
    }

    /* On Windows the config folder is %APPDATA%\Moon-TTS (resolved only, nothing is created) */
    [Fact(Skip = "Windows only", SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void GetConfigDirectory_Windows_ReturnsAppDataFolder()
    {
        string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Moon-TTS");

        string result = PathResolver.GetConfigDirectory();

        Assert.Equal(expected, result);
    }

    /* The config folder is only resolved, never created */
    [Fact(Skip = "Isolated user folders are not available on Windows", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void GetConfigDirectory_Always_DoesNotCreateFolder()
    {
        string result = PathResolver.GetConfigDirectory();

        Assert.False(Directory.Exists(result));
    }

    /* On Linux models live under ~/.local/share/Moon-TTS/models */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void GetModelsDirectory_Linux_ReturnsLocalShareModelsFolder()
    {
        string result = PathResolver.GetModelsDirectory();

        Assert.Equal(Path.Combine(Home, ".local", "share", "Moon-TTS", "models"), result);
    }

    /* On macOS models live under ~/Library/Application Support/Moon-TTS/models */
    [Fact(Skip = "macOS only", SkipUnless = nameof(TestPlatform.IsMacOS), SkipType = typeof(TestPlatform))]
    public void GetModelsDirectory_MacOS_ReturnsApplicationSupportModelsFolder()
    {
        string result = PathResolver.GetModelsDirectory();

        Assert.Equal(Path.Combine(Home, "Library", "Application Support", "Moon-TTS", "models"), result);
    }

    /* The models folder is created when missing */
    [Fact(Skip = "Isolated user folders are not available on Windows", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void GetModelsDirectory_MissingFolder_CreatesIt()
    {
        string result = PathResolver.GetModelsDirectory();

        Assert.True(Directory.Exists(result));
    }

    /* An already existing models folder is returned as is, keeping its contents */
    [Fact(Skip = "Isolated user folders are not available on Windows", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void GetModelsDirectory_ExistingFolder_KeepsContents()
    {
        string first = PathResolver.GetModelsDirectory();
        string marker = Path.Combine(first, "model.onnx");
        File.WriteAllText(marker, "x");

        string second = PathResolver.GetModelsDirectory();

        Assert.Equal(first, second);
        Assert.True(File.Exists(marker));
    }

    /* On Linux engines live under XDG_DATA_HOME */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void GetEnginesDirectory_LinuxWithXdgDataHome_ReturnsXdgSubfolder()
    {
        string result = PathResolver.GetEnginesDirectory();

        Assert.Equal(Path.Combine(_temp.Combine("data"), "Moon-TTS", "engines"), result);
    }

    /* On Linux without XDG_DATA_HOME engines fall back to ~/.local/share */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void GetEnginesDirectory_LinuxWithoutXdgDataHome_FallsBackToLocalShare()
    {
        _env.Set("XDG_DATA_HOME", null);

        string result = PathResolver.GetEnginesDirectory();

        Assert.Equal(Path.Combine(Home, ".local", "share", "Moon-TTS", "engines"), result);
    }

    /* On Linux a relative XDG_DATA_HOME is invalid per the XDG spec and is ignored */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void GetEnginesDirectory_LinuxWithRelativeXdgDataHome_FallsBackToLocalShare()
    {
        _env.Set("XDG_DATA_HOME", "relative/data");

        string result = PathResolver.GetEnginesDirectory();

        Assert.Equal(Path.Combine(Home, ".local", "share", "Moon-TTS", "engines"), result);
    }

    /* On macOS engines live under ~/Library/Application Support/Moon-TTS/engines */
    [Fact(Skip = "macOS only", SkipUnless = nameof(TestPlatform.IsMacOS), SkipType = typeof(TestPlatform))]
    public void GetEnginesDirectory_MacOS_ReturnsApplicationSupportEnginesFolder()
    {
        string result = PathResolver.GetEnginesDirectory();

        Assert.Equal(Path.Combine(Home, "Library", "Application Support", "Moon-TTS", "engines"), result);
    }

    /* The engines folder is created when missing */
    [Fact(Skip = "Isolated user folders are not available on Windows", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void GetEnginesDirectory_MissingFolder_CreatesIt()
    {
        string result = PathResolver.GetEnginesDirectory();

        Assert.True(Directory.Exists(result));
    }
}
