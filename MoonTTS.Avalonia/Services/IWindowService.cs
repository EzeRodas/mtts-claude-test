namespace MoonTTS.Avalonia.Services;

public interface IWindowService
{
    /* Shows and activates the main application window */
    void ShowMainWindow();

    /* Shows the main application window only if it is hidden (never toggles it away) */
    void RevealMainWindow();

    /* Hides the main application window to tray */
    void HideMainWindow();

    /* Automatically hides the window when speech synthesis begins if hide on synth is enabled */
    void HideOnSynth();

    /* Shuts down and exits the application */
    void ExitApplication();
    
    /* Opens the settings menu */
    void ToggleSettingsWindow();
    
    /* Opens the settings menu */
    void HideSettingsWindow();
}