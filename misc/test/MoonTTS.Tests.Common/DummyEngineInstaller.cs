using System.Text.Json;
using MoonTTS.Engine.Protocol;
using MoonTTS.Tests.DummyEngine;

namespace MoonTTS.Tests.Common;

/* File Structure:
        - Install(string enginesDirectory, string folderName, DummyEngineOptions options, params EngineModelManifest[] models) : string
        - WorkerAssemblyPath : string
*/

/* Installs the dummy worker as an engine folder (engine.json + dummy-engine.json + launcher), exactly like a
   real engine would be installed, so the host discovers and starts it through its normal code paths */
public static class DummyEngineInstaller
{
    /* The dummy worker assembly copied next to the test assembly by the project reference */
    public static string WorkerAssemblyPath => Path.Combine(AppContext.BaseDirectory, "MoonTTS.Tests.DummyEngine.dll");

    /* Creates "<enginesDirectory>/<folderName>/" and returns its path. Without models a single "default" model
       with no download files is declared */
    public static string Install(string enginesDirectory, string folderName, DummyEngineOptions options, params EngineModelManifest[] models)
    {
        string engineDir = Path.Combine(enginesDirectory, folderName);
        Directory.CreateDirectory(engineDir);

        string launcher;
        if (OperatingSystem.IsWindows())
        {
            launcher = "run.cmd";
            File.WriteAllText(Path.Combine(engineDir, launcher), $"@dotnet \"{WorkerAssemblyPath}\"\r\n");
        }
        else
        {
            launcher = "run.sh";
            string path = Path.Combine(engineDir, launcher);
            File.WriteAllText(path, $"#!/bin/sh\nexec dotnet \"{WorkerAssemblyPath}\"\n");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var manifest = new EngineManifest
        {
            Id = options.EngineId,
            DisplayName = "Dummy Engine",
            Executable = launcher,
            Models = models.Length > 0 ? models.ToList() : [new EngineModelManifest { Id = "default", DisplayName = "Default" }]
        };

        File.WriteAllText(Path.Combine(engineDir, EngineManifest.FileName),
            JsonSerializer.Serialize(manifest, EngineProtocolJsonContext.Default.EngineManifest));
        File.WriteAllText(Path.Combine(engineDir, DummyEngineOptions.FileName), JsonSerializer.Serialize(options));
        return engineDir;
    }
}
