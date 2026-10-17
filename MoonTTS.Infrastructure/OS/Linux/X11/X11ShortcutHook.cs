using MoonTTS.Domain.Interfaces;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.Infrastructure.OS.Linux.X11;

/* File Structure:
        - X11ShortcutHook()
        - X11ShortcutHook(IX11Api x11)
        - InitializeAsync() : Task
        - RegisterShortcutAsync(ShortcutCombo? toggleShortcut, Dictionary<string,ShortcutCombo> phraseShortcuts) : Task
        - RegisterSingleShortcut(string id, ShortcutCombo combo) : void
        - EventLoop() : void
        - Dispose() : void
*/

/* Shortcut hook for Linux X11 platform */

public class X11ShortcutHook : IGlobalShortcutHook
{
    private readonly IX11Api _x11;
    private IntPtr _display;
    private ulong _rootWindow;
    private Thread? _eventThread;
    private volatile bool _isRunning;
    private DateTime _lastTriggerTime = DateTime.MinValue;

    /* Maps the exact (ModifierState, KeyCode) -> Shortcut ID */
    private readonly Dictionary<(uint, uint), string> _registeredCombos = new();

    public event Action<string>? OnShortcutTriggered;

    /* Creates the hook over the native libX11 */
    public X11ShortcutHook() : this(new X11Api())
    {
    }

    /* Creates the hook over the given X11 API (the native libX11, or a stand-in X server) */
    public X11ShortcutHook(IX11Api x11)
    {
        _x11 = x11;
    }

    /* Called once when the app starts */
    public Task InitializeAsync()
    {
        /* Establishes connection with the X11 server */
        _display = _x11.XOpenDisplay(null);
        if (_display == IntPtr.Zero)
            throw new InvalidOperationException("Failed to open x11 Display");
        
        /* Get the desktop root background window to listen globally */
        _rootWindow = _x11.XDefaultRootWindow(_display);
        
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
        _x11.XUngrabKey(_display, 0, LibX11.AnyModifier, _rootWindow);
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
        IntPtr keysym = _x11.XStringToKeysym(combo.MainKey.ToLowerInvariant());
        if (keysym == IntPtr.Zero) return;
        
        uint keycode = _x11.XKeysymToKeycode(_display, keysym);
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
            _x11.XGrabKey(
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
            while (_x11.XPending(_display) > 0)
            {
                _x11.XNextEvent(_display, ref xevent);

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

        /* Waits for the event loop to stop using the display before closing it (skipped when a shortcut handler
           disposes the hook from the event thread itself) */
        if (_eventThread != null && _eventThread != Thread.CurrentThread)
            _eventThread.Join();
        _eventThread = null;

        if (_display != IntPtr.Zero)
        {
            _x11.XUngrabKey(_display, 0, LibX11.AnyModifier, _rootWindow);
            _x11.XCloseDisplay(_display);
            _display = IntPtr.Zero;
        }
    }
}