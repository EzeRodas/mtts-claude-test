namespace MoonTTS.Tests.Common;

/* File Structure:
        - TempDirectory()
        - Path : string
        - Combine(params string[] parts) : string
        - CreateSubdirectory(params string[] parts) : string
        - WriteFile(string relativePath, string content) : string
        - Dispose() : void
*/

/* A unique, empty directory that is deleted (recursively) when the test finishes */
public sealed class TempDirectory : IDisposable
{
    public string Path { get; }

    /* Creates a fresh directory under the system temp folder */
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "moontts-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    /* Builds a path inside the directory */
    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    /* Creates (and returns) a sub directory */
    public string CreateSubdirectory(params string[] parts)
    {
        string path = Combine(parts);
        Directory.CreateDirectory(path);
        return path;
    }

    /* Writes a text file, creating its parent folders, and returns its full path */
    public string WriteFile(string relativePath, string content)
    {
        string path = Combine(relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /* Removes the directory and everything inside it */
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            /* A process spawned by the test may still hold a file; the temp folder is cleaned by the OS */
        }
    }
}
