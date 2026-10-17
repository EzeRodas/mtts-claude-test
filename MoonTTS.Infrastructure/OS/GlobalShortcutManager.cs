using System.Runtime.InteropServices;
using MoonTTS.Domain.Interfaces;
using MoonTTS.Domain.ValueObjects;
using MoonTTS.Infrastructure.OS.Linux.Wayland;
using MoonTTS.Infrastructure.OS.Linux.X11;
using MoonTTS.Infrastructure.OS.MacOS;
using MoonTTS.Infrastructure.OS.Windows;

namespace MoonTTS.Infrastructure.OS;

/* File Structure:
        - GlobalShortcutManager()
        - GlobalShortcutManager(IGlobalShortcutHook hook)
        - InitializeAsync() : Task
        - CreatePlatformHook() : IGlobalShortcutHook
        - UpdateShortcutAsync(ShortcutCombo? toggleShortcut, Dictionary<string, ShortcutCombo> phraseShortcuts) : Task
        - Dispose() : void
*/

public class GlobalShortcutManager :IDisposable
{
    /* Hook chosen by the caller; when null the OS hook is detected on InitializeAsync */
    private readonly IGlobalShortcutHook? _providedHook;
    private IGlobalShortcutHook? _osHook;

    public event Action<string>? OnShortcutTriggered;

    /* Uses the hook of the current OS */
    public GlobalShortcutManager()
    {
    }

    /* Uses the given hook instead of detecting the OS one */
    public GlobalShortcutManager(IGlobalShortcutHook hook)
    {
        _providedHook = hook;
    }

    /* Selects the hook, forwards its shortcut events and starts it */
    public async Task InitializeAsync()
    {
        _osHook = _providedHook ?? CreatePlatformHook();

        /* Wires the listener hook */
        _osHook.OnShortcutTriggered += (shortcutId) =>
        {
            OnShortcutTriggered?.Invoke(shortcutId);
        };

        /* Starts the correct connections (bg threads for win, x11 and mac; or DBus for wayland) */
        await _osHook.InitializeAsync();
    }

    /* Builds the hook matching the current OS and display server, without starting it */
    public static IGlobalShortcutHook CreatePlatformHook()
    {
        /* Detects the OS and instantiates the correct hook */
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsShortcutHook();
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new MacShortcutHook();
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            /* Sub-check to see if it's an instance of X11 or Wayland */
            string? waylandDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
            if (!string.IsNullOrEmpty(waylandDisplay))
            {
                return new WaylandShortcutHook();
            }
            return new X11ShortcutHook();
        }

        /* If none of the above, throws an exception */
        throw new PlatformNotSupportedException("OS not supported for global shortcuts");
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
