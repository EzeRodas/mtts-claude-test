namespace MoonTTS.Core.Tests;

/* File Structure:
        - UserFolderSandbox()
        - Temp : TempDirectory
        - ConfigDirectory : string
        - Dispose() : void
*/

/* Points HOME / XDG_* / APPDATA at a fresh temp folder for one test, so services resolving user folders
   through PathResolver never touch the real user. Windows resolves AppData through the shell (not the APPDATA
   variable), so it cannot be isolated there and the test is skipped. Users must be in
   [Collection(TestCollections.Environment)] */
public sealed class UserFolderSandbox : IDisposable
{
    private readonly EnvironmentScope _scope;

    /* The temp folder holding the isolated home, config, data and appdata folders */
    public TempDirectory Temp { get; }

    /* The Moon-TTS config folder PathResolver resolves inside the sandbox */
    public string ConfigDirectory => Path.Combine(Temp.Combine("config"), "Moon-TTS");

    /* Creates the temp folder and redirects the user folder variables into it */
    public UserFolderSandbox()
    {
        Assert.SkipWhen(TestPlatform.IsWindows, "Windows resolves AppData through the shell; it cannot be isolated");
        Temp = new TempDirectory();
        _scope = EnvironmentScope.IsolatedUserDirectories(Temp);
    }

    /* Restores the environment and deletes the temp folder */
    public void Dispose()
    {
        _scope.Dispose();
        Temp.Dispose();
    }
}
