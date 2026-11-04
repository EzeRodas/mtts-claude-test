using System.Runtime.InteropServices;
using MoonTTS.Domain.Interfaces;
using MoonTTS.Infrastructure.OS.Linux.X11;
using MoonTTS.Infrastructure.OS.MacOS;
using MoonTTS.Infrastructure.OS.Windows;

namespace MoonTTS.Infrastructure.OS;

/* File Structure:
        - Create() : IWindowOverlayHelper
*/

/* Factory that instantiates the operating-system-specific IWindowOverlayHelper */
public static class WindowOverlayManager
{
    /* Detects the OS and returns the matching overlay helper (Linux always goes through X11 / XWayland,
       the backend Avalonia uses there) */
    public static IWindowOverlayHelper Create()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new WindowsWindowOverlayHelper();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return new MacWindowOverlayHelper();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return new X11WindowOverlayHelper();

        throw new PlatformNotSupportedException("OS not supported for window overlays");
    }
}
