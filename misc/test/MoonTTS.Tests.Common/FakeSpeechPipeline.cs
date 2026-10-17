using MoonTTS.Domain.Audio;
using MoonTTS.Domain.Common;
using MoonTTS.Domain.Interfaces;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.Tests.Common;

/* File Structure:
        - SpeakAsync(string text, CancellationToken cancellationToken) : Task<Result<SpeechResult>>
        - SpeakAsync(string text, SpeechOptions options, CancellationToken cancellationToken) : Task<Result<SpeechResult>>
        - Pause() : void
        - Resume() : void
        - Stop() : void
*/

/* ISpeechPipeline that records requests and returns a configurable result */
public sealed class FakeSpeechPipeline : ISpeechPipeline
{
    public List<(string Text, SpeechOptions? Options)> Requests { get; } = new();
    public Result<SpeechResult> ResultToReturn { get; set; } = Result<SpeechResult>.Success(new SpeechResult());

    public bool IsSpeaking { get; set; }
    public bool IsPaused { get; set; }
    public int PauseCount { get; private set; }
    public int ResumeCount { get; private set; }
    public int StopCount { get; private set; }

    /* Records a request with default options */
    public Task<Result<SpeechResult>> SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        Requests.Add((text, null));
        return Task.FromResult(ResultToReturn);
    }

    /* Records a request with explicit options */
    public Task<Result<SpeechResult>> SpeakAsync(string text, SpeechOptions options, CancellationToken cancellationToken = default)
    {
        Requests.Add((text, options));
        return Task.FromResult(ResultToReturn);
    }

    /* Counts pauses and sets IsPaused */
    public void Pause() { PauseCount++; IsPaused = true; }

    /* Counts resumes and clears IsPaused */
    public void Resume() { ResumeCount++; IsPaused = false; }

    /* Counts stops and clears the speaking state */
    public void Stop() { StopCount++; IsSpeaking = false; IsPaused = false; }
}

/* File Structure:
        - InitializeAsync() : Task
        - RegisterShortcutAsync(ShortcutCombo? toggleShortcut, Dictionary<string, ShortcutCombo> phraseShortcuts) : Task
        - Trigger(string shortcutId) : void
        - Dispose() : void
*/

/* IGlobalShortcutHook that records registrations and lets tests fire shortcuts */
public sealed class FakeGlobalShortcutHook : IGlobalShortcutHook
{
    public event Action<string>? OnShortcutTriggered;

    public bool IsInitialized { get; private set; }
    public bool IsDisposed { get; private set; }
    public List<(ShortcutCombo? Toggle, Dictionary<string, ShortcutCombo> Phrases)> Registrations { get; } = new();

    /* Marks the hook as initialized */
    public Task InitializeAsync()
    {
        IsInitialized = true;
        return Task.CompletedTask;
    }

    /* Records the registered shortcuts */
    public Task RegisterShortcutAsync(ShortcutCombo? toggleShortcut, Dictionary<string, ShortcutCombo> phraseShortcuts)
    {
        Registrations.Add((toggleShortcut, new Dictionary<string, ShortcutCombo>(phraseShortcuts)));
        return Task.CompletedTask;
    }

    /* Simulates the OS reporting a pressed shortcut */
    public void Trigger(string shortcutId) => OnShortcutTriggered?.Invoke(shortcutId);

    /* Marks the hook as disposed */
    public void Dispose() => IsDisposed = true;
}
