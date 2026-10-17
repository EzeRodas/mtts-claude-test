using MoonTTS.Avalonia.Services;

namespace MoonTTS.Avalonia.Tests.TestSupport;

/* File Structure:
        - ShowMainWindow() : void
        - HideMainWindow() : void
        - HideOnSynth() : void
        - ExitApplication() : void
        - ToggleSettingsWindow() : void
        - HideSettingsWindow() : void
*/

/* IWindowService that only counts calls, so view models can be tested without any window */
public sealed class FakeWindowService : IWindowService
{
    public int ShowMainWindowCount { get; private set; }
    public int HideMainWindowCount { get; private set; }
    public int HideOnSynthCount { get; private set; }
    public int ExitApplicationCount { get; private set; }
    public int ToggleSettingsWindowCount { get; private set; }
    public int HideSettingsWindowCount { get; private set; }

    /* Counts requests to show the HUD */
    public void ShowMainWindow() => ShowMainWindowCount++;

    /* Counts requests to hide the HUD */
    public void HideMainWindow() => HideMainWindowCount++;

    /* Counts hide-on-synthesis notifications */
    public void HideOnSynth() => HideOnSynthCount++;

    /* Counts exit requests */
    public void ExitApplication() => ExitApplicationCount++;

    /* Counts settings toggles */
    public void ToggleSettingsWindow() => ToggleSettingsWindowCount++;

    /* Counts requests to hide the settings window */
    public void HideSettingsWindow() => HideSettingsWindowCount++;
}
