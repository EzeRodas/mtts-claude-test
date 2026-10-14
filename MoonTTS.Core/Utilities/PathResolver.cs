using System.Runtime.InteropServices;

namespace MoonTTS.Core.Utilities;

public class PathResolver
{
    public static string GetConfigDirectory()
    {
        /* On windows retunds AppData/Moon-TTS */
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Moon-TTS");
        }

        /* On MacOS it saves to Library/Application Support/Moon-TTS, I'm not really sure if that's where it goes on Mac but I don't use mac so no idea feel free to fix it if it's wrong */
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library",
                "Application Support", "Moon-TTS");
        }

        /* On Linux / POSIX it checks that the XDG_CONFIG_HOME is set, otherwise falls back to using / making the .config folder on home */
        string? configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(configHome))
        {
            configHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }
        return Path.Combine(configHome, "Moon-TTS");
    }
    
    public static string GetModelsDirectory()
    {
        /* Otherwise, gets the default */
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string defaultPath;
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            defaultPath = Path.Combine(appData, "Moon-TTS", "models");
        } 
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            defaultPath = Path.Combine(userProfile, "Library", "Application Support", "Moon-TTS", "models");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            defaultPath = Path.Combine(userProfile, ".local", "share", "Moon-TTS", "models");
        }
        else
        {
            defaultPath = Path.Combine(userProfile, "Moon-TTS", "models"); 
        }
        
        /* Ensures the folders actually exist */
        if (!Directory.Exists(defaultPath))
        {
            Directory.CreateDirectory(defaultPath);
        }
        return defaultPath;
    }
}