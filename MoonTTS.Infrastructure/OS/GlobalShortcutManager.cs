using System.Runtime.InteropServices;
using MoonTTS.Domain.Interfaces;
using MoonTTS.Domain.ValueObjects;
using MoonTTS.Infrastructure.OS.Linux.Wayland;
using MoonTTS.Infrastructure.OS.Linux.X11;
using MoonTTS.Infrastructure.OS.MacOS;
using MoonTTS.Infrastructure.OS.Windows;

namespace MoonTTS.Infrastructure.OS;

public class GlobalShortcutManager :IDisposable
{
    private IGlobalShortcutHook? _osHook;

    public event Action<string>? OnShortcutTriggered;

    public async Task InitializeAsync()
    {
        /* Detects the OS and instantiates the correct hook */
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _osHook = new WindowsShortcutHook();
        } 
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            _osHook = new MacShortcutHook();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            /* Sub-check to see if it's an instance of X11 or Wayland */
            string? waylandDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
            if (!string.IsNullOrEmpty(waylandDisplay))
            {
                _osHook = new WaylandShortcutHook();
            }
            else
            {
                _osHook = new X11ShortcutHook();
            }
        }
        else
        /* If none of the above, throws an exception */
        {
            throw new PlatformNotSupportedException("OS not supported for global shortcuts");
        }

        /* Wires the listener hook */
        _osHook.OnShortcutTriggered += (shortcutId) =>
        {
            OnShortcutTriggered?.Invoke(shortcutId);
        };
        
        /* Starts the correct connections (bg threads for win, x11 and mac; or DBus for wayland) */
        await _osHook.InitializeAsync();
    }

    public async Task UpdateShortcutAsync(
        ShortcutCombo? toggleShortcut,
        Dictionary<string, ShortcutCombo> phraseShortcuts)
    {
        if (_osHook == null)
        {
            throw new InvalidOperationException("OS not initialized");
        }
        
        await _osHook.RegisterShortcutAsync(toggleShortcut, phraseShortcuts);
    }

    public void Dispose()
    {
        /* Closes the connection for the OS' that need it */
        _osHook?.Dispose();
    }
}