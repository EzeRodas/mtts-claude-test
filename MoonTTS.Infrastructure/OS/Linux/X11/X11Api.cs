using System.Runtime.InteropServices;

namespace MoonTTS.Infrastructure.OS.Linux.X11;

/* File Structure:
        - X11Api : IX11Api
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
        - LibX11
            - XOpenDisplay(string? display_name) : nint
            - XDefaultRootWindow(nint display) : ulong
            - XStringToKeysym(string string_name) : nint
            - XKeysymToKeycode(nint display, nint keysym) : uint
            - XGrabKey(
                nint display,
                int keycode,
                uint modifiers,
                ulong grab_window,
                bool owner_events,
                int pointer_mode,
                int keyboard_mode) : int
            - XUngrabKey(nint display, int keycode, uint modifiers, ulong grab_window) : int
            - XPending(nint display) : int
            - XNextEvent(nint display, ref XEvent event_return) : int
            - XCloseDisplay(nint display) : int
            - XKeyEvent
            - XEvent
*/

/* Default IX11Api used by the app: every call goes straight to the native libX11 */
public sealed class X11Api : IX11Api
{
    /* Opens the connection to the X server through libX11 */
    public nint XOpenDisplay(string? displayName) => LibX11.XOpenDisplay(displayName);

    /* Returns the root window of the default screen through libX11 */
    public ulong XDefaultRootWindow(nint display) => LibX11.XDefaultRootWindow(display);

    /* Converts a keysym name into its keysym through libX11 */
    public nint XStringToKeysym(string stringName) => LibX11.XStringToKeysym(stringName);

    /* Converts a keysym into a hardware keycode through libX11 */
    public uint XKeysymToKeycode(nint display, nint keysym) => LibX11.XKeysymToKeycode(display, keysym);

    /* Grabs a key combination through libX11 */
    public int XGrabKey(
        nint display,
        int keycode,
        uint modifiers,
        ulong grabWindow,
        bool ownerEvents,
        int pointerMode,
        int keyboardMode) =>
        LibX11.XGrabKey(display, keycode, modifiers, grabWindow, ownerEvents, pointerMode, keyboardMode);

    /* Releases a key grab through libX11 */
    public int XUngrabKey(nint display, int keycode, uint modifiers, ulong grabWindow) =>
        LibX11.XUngrabKey(display, keycode, modifiers, grabWindow);

    /* Returns the number of pending events through libX11 */
    public int XPending(nint display) => LibX11.XPending(display);

    /* Reads the next event through libX11 */
    public int XNextEvent(nint display, ref LibX11.XEvent eventReturn) => LibX11.XNextEvent(display, ref eventReturn);

    /* Closes the connection to the X server through libX11 */
    public int XCloseDisplay(nint display) => LibX11.XCloseDisplay(display);
}

/* Native C code that x11 expects to talk to */
/* Here, https://xorg.freedesktop.org/releases/current/doc/libX11/libX11/libX11.html , Good luck :) */

public static class LibX11
{
    private const string LibName = "libX11.so.6";

    public const int KeyPress = 2;
    public const int GrabModeAsync = 1;
    public const uint AnyModifier = 0x8000;
    public const uint ShiftMask = 1 << 0;
    public const uint LockMask = 1 << 1;
    public const uint ControlMask = 1 << 2;
    public const uint Mod1Mask = 1 << 3; /* Alt */
    public const uint Mod2Mask = 1 << 4; /* NumLock */
    public const uint Mod4Mask = 1 << 6; /* Super/Windows/Command */

    [DllImport(LibName)]
    internal static extern IntPtr XOpenDisplay(string? display_name);

    [DllImport(LibName)]
    internal static extern ulong XDefaultRootWindow(IntPtr display);

    [DllImport(LibName)]
    internal static extern IntPtr XStringToKeysym(string string_name);

    [DllImport(LibName)]
    internal static extern uint XKeysymToKeycode(IntPtr display, IntPtr keysym);

    [DllImport(LibName)]
    internal static extern int XGrabKey(
        IntPtr display,
        int keycode,
        uint modifiers,
        ulong grab_window,
        bool owner_events,
        int pointer_mode,
        int keyboard_mode);

    [DllImport(LibName)]
    internal static extern int XUngrabKey(
        IntPtr display,
        int keycode,
        uint modifiers,
        ulong grab_window);

    [DllImport(LibName)]
    internal static extern int XPending(IntPtr display);

    [DllImport(LibName)]
    internal static extern int XNextEvent(IntPtr display, ref XEvent event_return);

    [DllImport(LibName)]
    internal static extern int XCloseDisplay(IntPtr display);

    /* Memory mapping of the C struct "XKeyEvent" from "/usr/include/X11/Xlib.h" */

    [StructLayout(LayoutKind.Sequential)]
    public struct XKeyEvent
    {
        public int type;
        public ulong serial;
        public int send_event; /* Boolean in C is 4 bytes */
        public IntPtr display; /* Pointers in C are 8 bytes */
        public ulong window;   /* "Window" (XID) is 8 bytes */
        public ulong root;
        public ulong subwindow;
        public ulong time;     /* "Time" is 8 bytes */
        public int x, y;
        public int x_root, y_root;
        public uint state;     /* Modifiers Mask */
        public uint keycode;   /* HArdware keycode */
        public int same_screen;
    }

    [StructLayout(LayoutKind.Explicit, Size = 192)]
    public struct XEvent
    {
        [FieldOffset(0)] public int type;
        [FieldOffset(0)] public XKeyEvent xkey;
    }
}
