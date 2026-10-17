namespace MoonTTS.Infrastructure.OS.Linux.X11;

/* File Structure:
        - XOpenDisplay(string? displayName) : nint
        - XDefaultRootWindow(nint display) : ulong
        - XStringToKeysym(string stringName) : nint
        - XKeysymToKeycode(nint display, nint keysym) : uint
        - XGrabKey(
            nint display,
            int keycode,
            uint modifiers,
            ulong grabWindow,
            bool ownerEvents,
            int pointerMode,
            int keyboardMode) : int
        - XUngrabKey(nint display, int keycode, uint modifiers, ulong grabWindow) : int
        - XPending(nint display) : int
        - XNextEvent(nint display, ref LibX11.XEvent eventReturn) : int
        - XCloseDisplay(nint display) : int
*/

/* The libX11 calls made by X11ShortcutHook. X11Api forwards them to the native library, which keeps the hook
   independent from the X server it talks to (the real one, or a stand-in during tests) */
public interface IX11Api
{
    /* Opens the connection to the X server (null uses the DISPLAY environment variable) */
    nint XOpenDisplay(string? displayName);

    /* Returns the root window of the default screen */
    ulong XDefaultRootWindow(nint display);

    /* Converts a keysym name (e.g. "m", "space") into its keysym, or 0 (NoSymbol) when unknown */
    nint XStringToKeysym(string stringName);

    /* Converts a keysym into the hardware keycode of the current keyboard map, or 0 when unmapped */
    uint XKeysymToKeycode(nint display, nint keysym);

    /* Grabs a key + modifier combination on a window so its key events are delivered to this client */
    int XGrabKey(
        nint display,
        int keycode,
        uint modifiers,
        ulong grabWindow,
        bool ownerEvents,
        int pointerMode,
        int keyboardMode);

    /* Releases a key grab (keycode 0 = AnyKey, modifiers 0x8000 = AnyModifier) */
    int XUngrabKey(nint display, int keycode, uint modifiers, ulong grabWindow);

    /* Returns the number of events received from the X server that have not been read yet */
    int XPending(nint display);

    /* Removes the next event from the queue and copies it into eventReturn */
    int XNextEvent(nint display, ref LibX11.XEvent eventReturn);

    /* Closes the connection to the X server */
    int XCloseDisplay(nint display);
}
