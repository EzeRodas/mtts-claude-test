using System.Runtime.InteropServices;
using MoonTTS.Domain.Interfaces;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.Infrastructure.OS.Linux.X11;

/* File Structure:
        - InitializeAsync() : Task
        - RegisterShortcutAsync(string toggleShortcut, Dictionary<string,object> phraseShortcuts) : Task
        - RegisterSingleShortcut(string id, string comboString) : void
        - EventLoop() : void
        - Dispose() : void
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

/* Shortcut hook for Linux X11 platform */

public class X11ShortcutHook : IGlobalShortcutHook
{
    private IntPtr _display;
    private ulong _rootWindow;
    private Thread? _eventThread;
    private bool _isRunning;
    private DateTime _lastTriggerTime = DateTime.MinValue;

    /* Maps the exact (ModifierState, KeyCode) -> Shortcut ID */
    private readonly Dictionary<(uint, uint), string> _registeredCombos = new();

    public event Action<string>? OnShortcutTriggered;

    /* Called once when the app starts */
    public Task InitializeAsync()
    {
        /* Establishes connection with the X11 server */
        _display = LibX11.XOpenDisplay(null);
        if (_display == IntPtr.Zero)
            throw new InvalidOperationException("Failed to open x11 Display");
        
        /* Get the desktop root background window to listen globally */
        _rootWindow = LibX11.XDefaultRootWindow(_display);
        
        /* Starts the background thread to listen for X11 events */
        _isRunning = true;
        _eventThread = new Thread(EventLoop) {IsBackground = true};
        _eventThread.Start();
        
        return Task.CompletedTask;
    }

    /* Called every time the user saves their settings. Cleans the registered keybinds and rebinds them one by one */
    public Task RegisterShortcutAsync(ShortcutCombo? toggleShortcut, Dictionary<string, ShortcutCombo> phraseShortcuts)
    {
        if (_display == IntPtr.Zero) return Task.CompletedTask;
        
        /* Ungrabs all previous keys in case the user changed them in settings */
        LibX11.XUngrabKey(_display, 0, LibX11.AnyModifier, _rootWindow);
        _registeredCombos.Clear();

        if (toggleShortcut != null) RegisterSingleShortcut("toggle_app", toggleShortcut);

        foreach (var kvp in phraseShortcuts)
        {
            RegisterSingleShortcut($"phrase_{kvp.Key}", kvp.Value);
        }
        
        return Task.CompletedTask;
    }

    /* Breaks apart the shortcut command and registers it with the X11 server */
    private void RegisterSingleShortcut(string id, ShortcutCombo combo)
    {
        uint modifiers = 0;
        
        /* Parses the modifiers in 4 bit binary flags */
        if (combo.HasCtrl) modifiers |= LibX11.ControlMask;
        if (combo.HasAlt) modifiers |= LibX11.Mod1Mask;
        if (combo.HasShift) modifiers |= LibX11.ShiftMask;
        if (combo.HasWin) modifiers |= LibX11.Mod4Mask;
        
        /* Asks X11 to convert the string into a harware KeyCode */
        /* As stated in ShortcutCombo, X11 demands lowercase */
        IntPtr keysym = LibX11.XStringToKeysym(combo.MainKey.ToLowerInvariant());
        if (keysym == IntPtr.Zero) return;
        
        uint keycode = LibX11.XKeysymToKeycode(_display, keysym);
        if (keycode == 0) return;
        
        /* Safety net in case user has NumLock disabled */
        uint[] ignoredModifiers =
        {
            0,
            LibX11.LockMask,
            LibX11.Mod2Mask,
            LibX11.LockMask | LibX11.Mod2Mask,
        };

        foreach (var ignore in ignoredModifiers)
        {
            uint finalModifiers = modifiers | ignore;
            
            /* XGrabKey asks the OS to send only to it said keystroke */
            LibX11.XGrabKey(
                _display,
                (int)keycode,
                finalModifiers,
                _rootWindow,
                true,
                LibX11.GrabModeAsync,
                LibX11.GrabModeAsync);

            _registeredCombos[(finalModifiers, keycode)] = id;
        }
    }

    /* Wakes up every 50 milliseconds to check if the X11 server tried to notify the app */
    private void EventLoop()
    {
        LibX11.XEvent xevent = new  LibX11.XEvent();

        while (_isRunning)
        {
            /* XPending checks if there are events waiting, if not, sleeps to avoid cpu overload */
            while (LibX11.XPending(_display) > 0)
            {
                LibX11.XNextEvent(_display, ref xevent);

                if (xevent.type == LibX11.KeyPress)
                {
                    uint keycode = xevent.xkey.keycode;
                    uint state = xevent.xkey.state;

                    /* If X11 returns one of our registered combos, invokes the associated action */
                    if (_registeredCombos.TryGetValue((state, keycode), out string? shortcutId))
                    {
                        /* Ignore auto-repeat spam if it fired less than 500ms ago */
                        /* Is it necessary? I'm not sure, but windows has a built-in feature to avoid this,
                           so it doesn't hurt to prevent */
                        if ((DateTime.UtcNow - _lastTriggerTime).TotalMilliseconds > 500)
                        {
                            _lastTriggerTime = DateTime.UtcNow;
                            OnShortcutTriggered?.Invoke(shortcutId);
                        }
                    } 
                }
            }
            /* 50 millisecond pause */
            Thread.Sleep(50);
        }
    }

    /* Disposes the process */
    public void Dispose()
    {
        _isRunning = false;

        if (_display != IntPtr.Zero)
        {
            LibX11.XUngrabKey(_display, 0, LibX11.AnyModifier, _rootWindow);
            LibX11.XCloseDisplay(_display);
            _display = IntPtr.Zero;
        }
    }

    /* Native C code that x11 expects to talk to */
    /* Here, https://xorg.freedesktop.org/releases/current/doc/libX11/libX11/libX11.html , Good luck :) */
    
    private static class LibX11
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
        public static extern IntPtr XOpenDisplay(string? display_name);

        [DllImport(LibName)]
        public static extern ulong XDefaultRootWindow(IntPtr display);

        [DllImport(LibName)]
        public static extern IntPtr XStringToKeysym(string string_name);
        
        [DllImport(LibName)]
        public static extern uint XKeysymToKeycode(IntPtr display, IntPtr keysym);

        [DllImport(LibName)]
        public static extern int XGrabKey(
            IntPtr display,
            int keycode,
            uint modifiers,
            ulong grab_window,
            bool owner_events,
            int pointer_mode,
            int keyboard_mode);
        
        [DllImport(LibName)]
        public static extern int XUngrabKey(
            IntPtr display, 
            int keycode, 
            uint modifiers, 
            ulong grab_window);

        [DllImport(LibName)]
        public static extern int XPending(IntPtr display);
        
        [DllImport(LibName)]
        public static extern int XNextEvent(IntPtr display, ref XEvent event_return);
        
        [DllImport(LibName)]
        public static extern int XCloseDisplay(IntPtr display);
        
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
}