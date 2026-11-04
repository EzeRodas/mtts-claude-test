namespace MoonTTS.Domain.Interfaces;

/* Contract for platform-specific overlay behavior of the HUD windows over fullscreen and borderless applications.
   Every method is a best-effort OS hint: failures are never fatal and a window handle of 0 is ignored. */
public interface IWindowOverlayHelper
{
    /* Applies the OS window hints that keep a window out of the taskbar / pager / switcher and eligible to
       overlay fullscreen applications (the on-top state itself is owned by the UI framework) */
    void ConfigureOverlayWindow(nint windowHandle, bool alwaysOnTop);

    /* Called right before the main window is shown. Captures the currently active application window and, when
       it is a fullscreen / borderless one, links the main window to it so the OS keeps the taskbar hidden
       once the main window takes focus */
    void PrepareBeforeShow(nint mainWindowHandle, bool alwaysOnTop);

    /* Links a secondary window (e.g. settings) to the main window so it shares the overlay state of the main window */
    void LinkChildOverlayWindow(nint childWindowHandle, nint ownerWindowHandle, bool alwaysOnTop);

    /* Raises and activates an already-mapped overlay window so its text input receives keyboard focus */
    void ActivateOverlayWindow(nint windowHandle);

    /* Called when the main window loses focus to an external application while remaining visible. Updates the
       tracked external window and, when alwaysOnTop is false, releases the transient link so the focused app can cover it */
    void OnMainWindowDeactivated(nint mainWindowHandle, bool alwaysOnTop);

    /* Called after the main window hides. Drops the link made in PrepareBeforeShow and hands focus back to the
       application that was active before the main window was shown */
    void OnAfterHide(nint mainWindowHandle);
}
