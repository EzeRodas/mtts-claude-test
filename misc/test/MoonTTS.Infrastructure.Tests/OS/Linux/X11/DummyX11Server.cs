using MoonTTS.Infrastructure.OS.Linux.X11;

namespace MoonTTS.Infrastructure.Tests.OS.Linux.X11;

/* File Structure:
        - KeyGrab(int Keycode, uint Modifiers, ulong Window, bool OwnerEvents, int PointerMode, int KeyboardMode)
        - KeyUngrab(int Keycode, uint Modifiers, ulong Window)
        - DummyX11Server : IX11Api
            - Display / RootWindow / KeyPress / KeyRelease : constants
            - RefuseConnection : bool
            - OpenedDisplayNames / KeysymLookups / Grabs / Ungrabs : recordings
            - IsClosed / UsedAfterClose / CallCount : state
            - MapKey(string name, nint keysym, uint keycode) : DummyX11Server
            - SendEvents(params (int Type, uint Keycode, uint State)[] events) : void
            - WaitUntilEventsProcessedAsync(CancellationToken cancellationToken) : Task
            - HoldEventLoop() : void
            - WaitUntilEventLoopHeld() : bool
            - ReleaseEventLoop() : bool
            - Closed : Task
            - IX11Api members
*/

/* One XGrabKey call */
internal sealed record KeyGrab(int Keycode, uint Modifiers, ulong Window, bool OwnerEvents, int PointerMode, int KeyboardMode);

/* One XUngrabKey call */
internal sealed record KeyUngrab(int Keycode, uint Modifiers, ulong Window);

/* A stand-in X server: hands out a display and root window, resolves keysym names to keycodes with a US keyboard
   map (case-sensitive like XStringToKeysym), keeps the active key grabs (XUngrabKey with AnyKey + AnyModifier
   releases all of them) and queues key events that the hook's event loop reads through XPending / XNextEvent */
internal sealed class DummyX11Server : IX11Api
{
    public const nint Display = 0x5EED;
    public const ulong RootWindow = 0x1E3;
    public const int KeyPress = 2;
    public const int KeyRelease = 3;
    private const int AnyKey = 0;
    private const uint AnyModifier = 0x8000;
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    private readonly Lock _lock = new();
    private readonly Dictionary<string, nint> _keysyms = new(StringComparer.Ordinal)
    {
        ["m"] = 0x006d,
        ["a"] = 0x0061,
        ["space"] = 0x0020,
        ["1"] = 0x0031,
        ["F1"] = 0xffbe,
    };
    private readonly Dictionary<nint, uint> _keycodes = new()
    {
        [0x006d] = 58,
        [0x0061] = 38,
        [0x0020] = 65,
        [0x0031] = 10,
        [0xffbe] = 67,
    };
    private readonly List<string?> _openedDisplayNames = new();
    private readonly List<string> _keysymLookups = new();
    private readonly List<KeyGrab> _grabs = new();
    private readonly List<KeyUngrab> _ungrabs = new();
    private readonly Queue<LibX11.XEvent> _events = new();
    private readonly ManualResetEventSlim _heldInPending = new(false);
    private readonly ManualResetEventSlim _leftHeldPending = new(false);
    private readonly TaskCompletionSource _closedSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ManualResetEventSlim? _pendingGate;
    private ManualResetEventSlim? _releaseGate;
    private TaskCompletionSource _eventsProcessed = Completed();
    private int _callCount;

    /* XOpenDisplay fails, as with no X server or a wrong DISPLAY */
    public bool RefuseConnection { get; set; }

    /* Display names passed to XOpenDisplay */
    public IReadOnlyList<string?> OpenedDisplayNames { get { lock (_lock) return _openedDisplayNames.ToList(); } }

    /* Keysym names looked up with XStringToKeysym */
    public IReadOnlyList<string> KeysymLookups { get { lock (_lock) return _keysymLookups.ToList(); } }

    /* Key grabs currently active */
    public IReadOnlyList<KeyGrab> Grabs { get { lock (_lock) return _grabs.ToList(); } }

    /* Every XUngrabKey call */
    public IReadOnlyList<KeyUngrab> Ungrabs { get { lock (_lock) return _ungrabs.ToList(); } }

    /* True after XCloseDisplay */
    public bool IsClosed { get { lock (_lock) return _closed; } }
    private bool _closed;

    /* True when anything used the display after it was closed (a use-after-free on a real Xlib Display) */
    public bool UsedAfterClose { get { lock (_lock) return _usedAfterClose; } }
    private bool _usedAfterClose;

    /* Number of X11 calls received so far */
    public int CallCount { get { lock (_lock) return _callCount; } }

    /* Adds a key to the keyboard map */
    public DummyX11Server MapKey(string name, nint keysym, uint keycode)
    {
        lock (_lock)
        {
            _keysyms[name] = keysym;
            _keycodes[keysym] = keycode;
        }
        return this;
    }

    /* Queues key events at once, as the X server would deliver them for grabbed keys */
    public void SendEvents(params (int Type, uint Keycode, uint State)[] events)
    {
        lock (_lock)
        {
            foreach (var (type, keycode, state) in events)
            {
                var xevent = new LibX11.XEvent
                {
                    xkey = new LibX11.XKeyEvent
                    {
                        type = type,
                        display = Display,
                        window = RootWindow,
                        root = RootWindow,
                        keycode = keycode,
                        state = state,
                        same_screen = 1,
                    },
                };
                _events.Enqueue(xevent);
            }
            _eventsProcessed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    /* Completes once the event loop drained the queue and asked for more (so every event has been handled) */
    public async Task WaitUntilEventsProcessedAsync(CancellationToken cancellationToken)
    {
        Task processed;
        lock (_lock)
            processed = _eventsProcessed.Task;
        await processed.WaitAsync(WaitTimeout, cancellationToken);
    }

    /* Opens the dummy display unless the connection is refused */
    public nint XOpenDisplay(string? displayName)
    {
        lock (_lock)
        {
            _callCount++;
            _openedDisplayNames.Add(displayName);
            return RefuseConnection ? 0 : Display;
        }
    }

    /* Returns the root window of the dummy screen */
    public ulong XDefaultRootWindow(nint display)
    {
        Touch(display);
        return RootWindow;
    }

    /* Case-sensitive keysym name lookup; 0 (NoSymbol) for unknown names */
    public nint XStringToKeysym(string stringName)
    {
        lock (_lock)
        {
            _callCount++;
            _keysymLookups.Add(stringName);
            return _keysyms.TryGetValue(stringName, out nint keysym) ? keysym : 0;
        }
    }

    /* Keycode of a keysym in the keyboard map; 0 when the keyboard has no such key */
    public uint XKeysymToKeycode(nint display, nint keysym)
    {
        Touch(display);
        lock (_lock)
            return _keycodes.TryGetValue(keysym, out uint keycode) ? keycode : 0;
    }

    /* Activates a grab */
    public int XGrabKey(nint display, int keycode, uint modifiers, ulong grabWindow, bool ownerEvents, int pointerMode, int keyboardMode)
    {
        Touch(display);
        lock (_lock)
            _grabs.Add(new KeyGrab(keycode, modifiers, grabWindow, ownerEvents, pointerMode, keyboardMode));
        return 1;
    }

    /* Releases a grab; AnyKey + AnyModifier releases every grab on the window */
    public int XUngrabKey(nint display, int keycode, uint modifiers, ulong grabWindow)
    {
        Touch(display);
        lock (_lock)
        {
            _ungrabs.Add(new KeyUngrab(keycode, modifiers, grabWindow));
            _grabs.RemoveAll(g => g.Window == grabWindow
                                  && (keycode == AnyKey || g.Keycode == keycode)
                                  && (modifiers == AnyModifier || g.Modifiers == modifiers));
        }
        return 1;
    }

    /* Makes the next XPending call block until ReleaseEventLoop, holding the hook's event loop inside Xlib */
    public void HoldEventLoop()
    {
        lock (_lock)
        {
            _heldInPending.Reset();
            _leftHeldPending.Reset();
            _pendingGate = new ManualResetEventSlim(false);
        }
    }

    /* Waits until the event loop is blocked inside the held XPending call */
    public bool WaitUntilEventLoopHeld() => _heldInPending.Wait(WaitTimeout);

    /* Lets the held XPending call return and waits until it did */
    public bool ReleaseEventLoop()
    {
        lock (_lock)
            _releaseGate?.Set();
        return _leftHeldPending.Wait(WaitTimeout);
    }

    /* Completes when XCloseDisplay is called */
    public Task Closed => _closedSignal.Task;

    /* Number of queued events; an empty queue means every delivered event has been handled */
    public int XPending(nint display)
    {
        Touch(display);

        ManualResetEventSlim? gate;
        lock (_lock)
        {
            gate = _pendingGate;
            _pendingGate = null;
            _releaseGate = gate;
        }
        if (gate != null)
        {
            _heldInPending.Set();
            gate.Wait(WaitTimeout);
            /* Returning into a Display that was closed meanwhile is a use-after-free on a real Xlib */
            lock (_lock)
                _usedAfterClose |= _closed;
            _leftHeldPending.Set();
        }

        lock (_lock)
        {
            if (_events.Count == 0)
                _eventsProcessed.TrySetResult();
            return _events.Count;
        }
    }

    /* Dequeues the next event */
    public int XNextEvent(nint display, ref LibX11.XEvent eventReturn)
    {
        Touch(display);
        lock (_lock)
        {
            if (_events.Count > 0)
                eventReturn = _events.Dequeue();
        }
        return 0;
    }

    /* Closes the display */
    public int XCloseDisplay(nint display)
    {
        Touch(display);
        lock (_lock)
            _closed = true;
        _closedSignal.TrySetResult();
        return 0;
    }

    /* Counts a call and flags any use of a closed or invalid display */
    private void Touch(nint display)
    {
        lock (_lock)
        {
            _callCount++;
            if (_closed || display != Display)
                _usedAfterClose = true;
        }
    }

    /* A completed task (no events pending yet) */
    private static TaskCompletionSource Completed()
    {
        var done = new TaskCompletionSource();
        done.SetResult();
        return done;
    }
}
