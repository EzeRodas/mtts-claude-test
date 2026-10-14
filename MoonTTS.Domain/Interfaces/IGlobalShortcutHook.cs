using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.Domain.Interfaces;

public interface IGlobalShortcutHook : IDisposable
{
    event Action<string>? OnShortcutTriggered;

    Task InitializeAsync();
    Task RegisterShortcutAsync(ShortcutCombo? toggleShortcut, Dictionary<string, ShortcutCombo> phraseShortcuts);
}