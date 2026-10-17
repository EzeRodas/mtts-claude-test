namespace MoonTTS.Tests.Common;

/* File Structure:
        - Set(string name, string? value) : EnvironmentScope
        - PrependPath(string directory) : EnvironmentScope
        - IsolatedUserDirectories(TempDirectory root) : EnvironmentScope
        - Dispose() : void
*/

/* Changes process environment variables for one test and restores them afterwards.
   Environment variables are process wide: tests using this must be in [Collection(TestCollections.Environment)] */
public sealed class EnvironmentScope : IDisposable
{
    private readonly Dictionary<string, string?> _originalValues = new();

    /* Sets a variable (null removes it), remembering its original value */
    public EnvironmentScope Set(string name, string? value)
    {
        if (!_originalValues.ContainsKey(name))
            _originalValues[name] = Environment.GetEnvironmentVariable(name);

        Environment.SetEnvironmentVariable(name, value);
        return this;
    }

    /* Puts a directory first on PATH so its dummy executables shadow the real OS tools */
    public EnvironmentScope PrependPath(string directory)
    {
        string current = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return Set("PATH", directory + System.IO.Path.PathSeparator + current);
    }

    /* Points HOME and the XDG folders at a temp directory so config, data and models never touch the real user */
    public static EnvironmentScope IsolatedUserDirectories(TempDirectory root)
    {
        return new EnvironmentScope()
            .Set("HOME", root.CreateSubdirectory("home"))
            .Set("XDG_CONFIG_HOME", root.CreateSubdirectory("config"))
            .Set("XDG_DATA_HOME", root.CreateSubdirectory("data"))
            .Set("APPDATA", root.CreateSubdirectory("appdata"));
    }

    /* Restores every variable this scope changed */
    public void Dispose()
    {
        foreach (var (name, value) in _originalValues)
            Environment.SetEnvironmentVariable(name, value);
        _originalValues.Clear();
    }
}

/* Names of the xUnit collections shared by all test projects */
public static class TestCollections
{
    /* Tests that change environment variables, PATH or the working directory. Runs without parallelism */
    public const string Environment = "Environment";
}
