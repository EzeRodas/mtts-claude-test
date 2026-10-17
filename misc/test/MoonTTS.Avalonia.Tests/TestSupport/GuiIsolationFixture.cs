namespace MoonTTS.Avalonia.Tests.TestSupport;

/* File Structure:
        - GuiIsolationFixture()
        - Root : TempDirectory
        - Dispose() : void
*/

/* Assembly fixture that isolates the whole test process from the real user session before the first test runs:
   - HOME and the XDG / APPDATA folders point into a temp directory, because HotkeyManager, HistoryManager,
     JsonConfigurationRepository and PathResolver resolve the user's config and data folders.
   - TMPDIR points into the same temp directory, because .NET creates named pipes as Unix sockets under
     Path.GetTempPath() and caches that folder the first time a pipe is used in the process. Isolating TMPDIR
     per test therefore cannot work; isolating it once, here, keeps every single-instance pipe of the run away
     from a real running Moon-TTS.
   The variables are set once, before any test starts, and never changed afterwards, so parallel tests can rely
   on them; tests that change other variables still use [Collection(TestCollections.Environment)] */
public sealed class GuiIsolationFixture : IDisposable
{
    private readonly EnvironmentScope _scope;

    /* Shared temp folder holding the isolated user directories and temp folder */
    public TempDirectory Root { get; }

    /* Creates the temp folder (before TMPDIR changes) and redirects the user directories and TMPDIR into it */
    public GuiIsolationFixture()
    {
        Root = new TempDirectory();
        _scope = EnvironmentScope.IsolatedUserDirectories(Root).Set("TMPDIR", Root.CreateSubdirectory("tmp"));
    }

    /* Restores the original variables and deletes the temp folder */
    public void Dispose()
    {
        _scope.Dispose();
        Root.Dispose();
    }
}
