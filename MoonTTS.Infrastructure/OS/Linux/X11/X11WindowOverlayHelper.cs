using System.Runtime.InteropServices;
using MoonTTS.Domain.Interfaces;

namespace MoonTTS.Infrastructure.OS.Linux.X11;

/* File Structure:
        - X11WindowOverlayHelper()
        - ConfigureOverlayWindow(nint windowHandle, bool alwaysOnTop) : void
        - PrepareBeforeShow(nint mainWindowHandle, bool alwaysOnTop) : void
        - LinkChildOverlayWindow(nint childWindowHandle, nint ownerWindowHandle, bool alwaysOnTop) : void
        - ActivateOverlayWindow(nint windowHandle) : void
        - OnMainWindowDeactivated(nint mainWindowHandle, bool alwaysOnTop) : void
        - OnAfterHide(nint mainWindowHandle) : void
        - IsUsable(nint windowHandle) : bool
        - RunGuarded(Action action) : void
        - GetActiveWindow() : ulong
        - IsOverlayTarget(ulong window) : bool
        - ReadProperty(ulong window, string propertyName, ulong requestedType, int maxItems) : ulong[]
        - SendStateMessage(ulong window, bool add, string stateName) : void
        - SendActiveWindowRequest(ulong window) : void
        - SendRootClientMessage(ClientMessageEvent message) : void
        - IgnoreX11Error(nint display, ref XErrorEvent errorEvent) : int
        - Dispose() : void
        - LibX11Overlay
            - XOpenDisplay(string? displayName) : nint
            - XCloseDisplay(nint display) : int
            - XDefaultRootWindow(nint display) : ulong
            - XInternAtom(nint display, string atomName, bool onlyIfExists) : ulong
            - XGetWindowProperty(...) : int
            - XFree(nint data) : int
            - XSetTransientForHint(nint display, ulong window, ulong propertyWindow) : int
            - XDeleteProperty(nint display, ulong window, ulong property) : int
            - XRaiseWindow(nint display, ulong window) : int
            - XSetInputFocus(nint display, ulong focus, int revertTo, ulong time) : int
            - XSendEvent(nint display, ulong window, bool propagate, long eventMask, ref ClientMessageEvent message) : int
            - XFlush(nint display) : int
            - XSync(nint display, bool discard) : int
            - XSetErrorHandler(XErrorHandlerDelegate handler) : nint
            - XSetErrorHandler(nint handler) : nint
            - XErrorHandlerDelegate
            - XErrorEvent
            - ClientMessageEvent
*/

/* Overlay helper for Linux X11 (also used for XWayland sessions, where Avalonia runs on its X11 backend).
   Links the HUD windows as transients of the active fullscreen / borderless application so the window manager keeps
   that application in its active fullscreen layer (and the panel / taskbar hidden) while the HUD takes keyboard focus.
   Only standard EWMH / ICCCM window hints are used; nothing is injected into or read from other processes. */
public sealed class X11WindowOverlayHelper : IWindowOverlayHelper, IDisposable
{
    /* Static reference keeps the error callback alive across the process lifetime for Native AOT */
    private static readonly LibX11Overlay.XErrorHandlerDelegate GuardedErrorHandler = IgnoreX11Error;

    private readonly Lock _lock = new();
    private IntPtr _display;
    private ulong _rootWindow;

    /* Window that was active before the HUD was shown, focus is handed back to it when the HUD hides */
    private ulong _previousActiveWindow;

    /* Main window currently linked as a transient of _previousActiveWindow */
    private ulong _linkedMainWindow;

    /* Secondary HUD window (e.g. settings) linked as a transient of the main window */
    private ulong _linkedChildWindow;

    /* Opens a dedicated connection to the X server. If it fails every operation becomes a no-op */
    public X11WindowOverlayHelper()
    {
        _display = LibX11Overlay.XOpenDisplay(null);
        if (_display != IntPtr.Zero)
            _rootWindow = LibX11Overlay.XDefaultRootWindow(_display);
    }

    /* Keeps the window out of the taskbar and pager, and mirrors the always-on-top state. The window must be mapped */
    public void ConfigureOverlayWindow(nint windowHandle, bool alwaysOnTop)
    {
        if (!IsUsable(windowHandle)) return;

        lock (_lock)
        {
            RunGuarded(() =>
            {
                ulong window = (ulong)windowHandle;
                if (window == _linkedMainWindow && _previousActiveWindow != 0)
                    LibX11Overlay.XSetTransientForHint(_display, _linkedMainWindow, _previousActiveWindow);

                SendStateMessage(window, true, "_NET_WM_STATE_SKIP_TASKBAR");
                SendStateMessage(window, true, "_NET_WM_STATE_SKIP_PAGER");
                SendStateMessage(window, alwaysOnTop, "_NET_WM_STATE_ABOVE");
            });
        }
    }

    /* Captures the active application window and links the main window to it as a transient regardless of alwaysOnTop,
       so pulling the HUD over a borderless or fullscreen window never causes the taskbar to appear */
    public void PrepareBeforeShow(nint mainWindowHandle, bool alwaysOnTop)
    {
        if (!IsUsable(mainWindowHandle)) return;

        lock (_lock)
        {
            RunGuarded(() =>
            {
                ulong mainWindow = (ulong)mainWindowHandle;
                ulong active = GetActiveWindow();
                if (active != 0 && active != mainWindow && active != _linkedChildWindow && IsOverlayTarget(active))
                    _previousActiveWindow = active;

                if (_previousActiveWindow == 0) return;

                _linkedMainWindow = mainWindow;
                LibX11Overlay.XSetTransientForHint(_display, _linkedMainWindow, _previousActiveWindow);
            });
        }
    }

    /* Links a secondary window to the main window so both stay in the same fullscreen layer chain */
    public void LinkChildOverlayWindow(nint childWindowHandle, nint ownerWindowHandle, bool alwaysOnTop)
    {
        if (!IsUsable(childWindowHandle) || !IsUsable(ownerWindowHandle)) return;

        lock (_lock)
        {
            RunGuarded(() =>
            {
                _linkedChildWindow = (ulong)childWindowHandle;
                LibX11Overlay.XSetTransientForHint(_display, _linkedChildWindow, (ulong)ownerWindowHandle);
            });
        }
    }

    /* Raises a mapped overlay window and asks the window manager to give it input focus */
    public void ActivateOverlayWindow(nint windowHandle)
    {
        if (!IsUsable(windowHandle)) return;

        lock (_lock)
        {
            RunGuarded(() =>
            {
                ulong window = (ulong)windowHandle;
                LibX11Overlay.XRaiseWindow(_display, window);
                SendActiveWindowRequest(window);
                LibX11Overlay.XSetInputFocus(
                    _display,
                    window,
                    LibX11Overlay.RevertToPointerRoot,
                    LibX11Overlay.CurrentTime);
            });
        }
    }

    /* Updates the tracked external window when the HUD loses focus while visible, and drops the transient link
       when alwaysOnTop is false so the newly focused application can cover the HUD */
    public void OnMainWindowDeactivated(nint mainWindowHandle, bool alwaysOnTop)
    {
        if (!IsUsable(mainWindowHandle)) return;

        lock (_lock)
        {
            RunGuarded(() =>
            {
                ulong mainWindow = (ulong)mainWindowHandle;
                ulong active = GetActiveWindow();
                if (active != 0 && active != mainWindow && active != _linkedChildWindow && IsOverlayTarget(active))
                    _previousActiveWindow = active;

                if (!alwaysOnTop && _linkedMainWindow == mainWindow)
                {
                    LibX11Overlay.XDeleteProperty(_display, _linkedMainWindow, LibX11Overlay.XA_WM_TRANSIENT_FOR);
                    _linkedMainWindow = 0;

                    if (active != 0 && active != mainWindow && active != _linkedChildWindow)
                        LibX11Overlay.XRaiseWindow(_display, active);
                }
            });
        }
    }

    /* Removes the transient link and hands focus back to the application that was active before the HUD appeared */
    public void OnAfterHide(nint mainWindowHandle)
    {
        if (!IsUsable(mainWindowHandle)) return;

        lock (_lock)
        {
            RunGuarded(() =>
            {
                if (_linkedMainWindow == (ulong)mainWindowHandle)
                {
                    LibX11Overlay.XDeleteProperty(_display, _linkedMainWindow, LibX11Overlay.XA_WM_TRANSIENT_FOR);
                    _linkedMainWindow = 0;
                }

                ulong previous = _previousActiveWindow;
                _previousActiveWindow = 0;
                if (previous != 0)
                {
                    SendActiveWindowRequest(previous);
                    LibX11Overlay.XSetInputFocus(
                        _display,
                        previous,
                        LibX11Overlay.RevertToPointerRoot,
                        LibX11Overlay.CurrentTime);
                }
            });
        }
    }

    /* A handle is usable when it is non-zero and the X connection is open */
    private bool IsUsable(nint windowHandle) => windowHandle != 0 && _display != IntPtr.Zero;

    /* Runs X requests under a non-fatal error handler (a window may vanish between query and request) and flushes
       them synchronously so errors are reported inside the guard. The previous process-wide handler is restored */
    private void RunGuarded(Action action)
    {
        IntPtr previousHandler = LibX11Overlay.XSetErrorHandler(GuardedErrorHandler);
        try
        {
            action();
            LibX11Overlay.XSync(_display, false);
        }
        finally
        {
            LibX11Overlay.XSetErrorHandler(previousHandler);
        }
    }

    /* Reads _NET_ACTIVE_WINDOW from the root window, 0 when no X11 client is active */
    private ulong GetActiveWindow()
    {
        ulong[] values = ReadProperty(_rootWindow, "_NET_ACTIVE_WINDOW", LibX11Overlay.XA_WINDOW, 1);
        return values.Length > 0 ? values[0] : 0;
    }

    /* The desktop background and panels are never worth linking to, any other client window is */
    private bool IsOverlayTarget(ulong window)
    {
        ulong desktopType = LibX11Overlay.XInternAtom(_display, "_NET_WM_WINDOW_TYPE_DESKTOP", false);
        ulong dockType = LibX11Overlay.XInternAtom(_display, "_NET_WM_WINDOW_TYPE_DOCK", false);

        foreach (ulong type in ReadProperty(window, "_NET_WM_WINDOW_TYPE", LibX11Overlay.XA_ATOM, 16))
        {
            if (type == desktopType || type == dockType)
                return false;
        }
        return true;
    }

    /* Reads a 32-bit format property (windows or atoms) as a list of values, empty when missing */
    private ulong[] ReadProperty(ulong window, string propertyName, ulong requestedType, int maxItems)
    {
        ulong property = LibX11Overlay.XInternAtom(_display, propertyName, false);
        int status = LibX11Overlay.XGetWindowProperty(
            _display, window, property, 0, maxItems, false, requestedType,
            out ulong actualType, out int actualFormat, out ulong itemCount, out _, out IntPtr data);

        if (status != 0 || data == IntPtr.Zero)
            return [];

        try
        {
            if (actualType != requestedType || actualFormat != 32)
                return [];

            /* Format 32 properties are returned as an array of C longs (pointer sized on Linux) */
            var values = new ulong[(int)itemCount];
            for (int i = 0; i < values.Length; i++)
                values[i] = (ulong)Marshal.ReadIntPtr(data, i * IntPtr.Size);
            return values;
        }
        finally
        {
            LibX11Overlay.XFree(data);
        }
    }

    /* Asks the window manager to add or remove an EWMH _NET_WM_STATE flag on a mapped window */
    private void SendStateMessage(ulong window, bool add, string stateName)
    {
        SendRootClientMessage(new LibX11Overlay.ClientMessageEvent
        {
            type = LibX11Overlay.ClientMessage,
            window = window,
            message_type = LibX11Overlay.XInternAtom(_display, "_NET_WM_STATE", false),
            format = 32,
            data0 = add ? 1 : 0,
            data1 = (long)LibX11Overlay.XInternAtom(_display, stateName, false),
            data3 = 1, /* Source indication: normal application */
        });
    }

    /* Asks the window manager to activate a window using the user-switcher source indication (2) so focus-stealing
       prevention does not block focus restoration after an in-app key press such as Escape */
    private void SendActiveWindowRequest(ulong window)
    {
        SendRootClientMessage(new LibX11Overlay.ClientMessageEvent
        {
            type = LibX11Overlay.ClientMessage,
            window = window,
            message_type = LibX11Overlay.XInternAtom(_display, "_NET_ACTIVE_WINDOW", false),
            format = 32,
            data0 = 2, /* Source indication: pager / direct user action */
            data1 = (long)LibX11Overlay.CurrentTime,
        });
    }

    /* Delivers a client message to the root window, where window managers listen for EWMH requests */
    private void SendRootClientMessage(LibX11Overlay.ClientMessageEvent message)
    {
        LibX11Overlay.XSendEvent(
            _display,
            _rootWindow,
            false,
            LibX11Overlay.SubstructureRedirectMask | LibX11Overlay.SubstructureNotifyMask,
            ref message);
        LibX11Overlay.XFlush(_display);
    }

    /* Ignores X11 errors raised while a guarded request runs (e.g. BadWindow for a window that just closed) */
    private static int IgnoreX11Error(IntPtr display, ref LibX11Overlay.XErrorEvent errorEvent) => 0;

    /* Closes the connection to the X server */
    public void Dispose()
    {
        lock (_lock)
        {
            if (_display != IntPtr.Zero)
            {
                LibX11Overlay.XCloseDisplay(_display);
                _display = IntPtr.Zero;
            }
        }
    }

    /* Native Xlib entry points used by the overlay helper */
    private static class LibX11Overlay
    {
        private const string LibName = "libX11.so.6";

        public const int ClientMessage = 33;
        public const int RevertToPointerRoot = 1;
        public const ulong CurrentTime = 0;
        public const long SubstructureNotifyMask = 1L << 19;
        public const long SubstructureRedirectMask = 1L << 20;
        public const ulong XA_ATOM = 4;
        public const ulong XA_WINDOW = 33;
        public const ulong XA_WM_TRANSIENT_FOR = 68;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int XErrorHandlerDelegate(IntPtr display, ref XErrorEvent errorEvent);

        [DllImport(LibName)]
        public static extern IntPtr XOpenDisplay(string? displayName);

        [DllImport(LibName)]
        public static extern int XCloseDisplay(IntPtr display);

        [DllImport(LibName)]
        public static extern ulong XDefaultRootWindow(IntPtr display);

        [DllImport(LibName)]
        public static extern ulong XInternAtom(IntPtr display, string atomName, bool onlyIfExists);

        [DllImport(LibName)]
        public static extern int XGetWindowProperty(
            IntPtr display,
            ulong window,
            ulong property,
            long longOffset,
            long longLength,
            bool delete,
            ulong requestedType,
            out ulong actualType,
            out int actualFormat,
            out ulong itemCount,
            out ulong bytesAfter,
            out IntPtr data);

        [DllImport(LibName)]
        public static extern int XFree(IntPtr data);

        [DllImport(LibName)]
        public static extern int XSetTransientForHint(IntPtr display, ulong window, ulong propertyWindow);

        [DllImport(LibName)]
        public static extern int XDeleteProperty(IntPtr display, ulong window, ulong property);

        [DllImport(LibName)]
        public static extern int XRaiseWindow(IntPtr display, ulong window);

        [DllImport(LibName)]
        public static extern int XSetInputFocus(IntPtr display, ulong focus, int revertTo, ulong time);

        [DllImport(LibName)]
        public static extern int XSendEvent(
            IntPtr display,
            ulong window,
            bool propagate,
            long eventMask,
            ref ClientMessageEvent message);

        [DllImport(LibName)]
        public static extern int XFlush(IntPtr display);

        [DllImport(LibName)]
        public static extern int XSync(IntPtr display, bool discard);

        [DllImport(LibName)]
        public static extern IntPtr XSetErrorHandler(XErrorHandlerDelegate handler);

        /* Same entry point taking the raw previous handler pointer, used to restore it */
        [DllImport(LibName, EntryPoint = "XSetErrorHandler")]
        public static extern IntPtr XSetErrorHandler(IntPtr handler);

        [StructLayout(LayoutKind.Sequential)]
        public struct XErrorEvent
        {
            public int type;
            public IntPtr display;
            public ulong resourceid;
            public ulong serial;
            public byte error_code;
            public byte request_code;
            public byte minor_code;
        }

        /* Memory mapping of the C struct "XClientMessageEvent" from "/usr/include/X11/Xlib.h", padded to the
           192 bytes of the XEvent union so it can be passed to XSendEvent directly */
        [StructLayout(LayoutKind.Explicit, Size = 192)]
        public struct ClientMessageEvent
        {
            [FieldOffset(0)] public int type;
            [FieldOffset(8)] public ulong serial;
            [FieldOffset(16)] public int send_event;
            [FieldOffset(24)] public IntPtr display;
            [FieldOffset(32)] public ulong window;
            [FieldOffset(40)] public ulong message_type;
            [FieldOffset(48)] public int format;
            [FieldOffset(56)] public long data0;
            [FieldOffset(64)] public long data1;
            [FieldOffset(72)] public long data2;
            [FieldOffset(80)] public long data3;
            [FieldOffset(88)] public long data4;
        }
    }
}
