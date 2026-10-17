using MoonTTS.Infrastructure.OS.MacOS;

namespace MoonTTS.Infrastructure.Tests.OS.MacOS;

/* File Structure:
        - FourCC(string code) : uint
        - InstalledHandler(nint Ref, EventHandlerProcPtr Handler, EventTypeSpec[] Types, nint UserData)
        - RegisteredHotKey(nint Ref, uint KeyCode, uint Modifiers, EventHotKeyID HotKeyId, nint Target, uint Options)
        - InstallApplicationEventHandler(...) : int
        - RemoveEventHandler(nint inHandlerRef) : int
        - GetApplicationEventTarget() : nint
        - RegisterEventHotKey(...) : int
        - UnregisterEventHotKey(nint inHotKeyRef) : int
        - GetEventParameter(...) : int
        - ReserveForSystem(uint keyCode, uint modifiers) : void
        - Press(uint keyCode, uint modifiers) : int?
        - DeliverHotKeyEvent(EventHotKeyID hotKeyId) : int?
*/

/* IMacCarbonHotKeyApi that behaves like the Carbon Event Manager:
   - RegisterEventHotKey rejects a keycode + modifiers pair that is already registered (eventHotKeyExistsErr)
   - pressing a registered combo creates a kEventHotKeyPressed event carrying the hotkey's EventHotKeyID and calls
     every application handler installed for (kEventClassKeyboard, kEventHotKeyPressed)
   - GetEventParameter only answers for a live event, the direct-object parameter and the 'ehki' type */
public sealed class DummyMacCarbonHotKeyApi : IMacCarbonHotKeyApi
{
    /* Carbon OSStatus codes */
    public const int NoErr = 0;
    public const int ParamErr = -50;
    public const int EventParameterNotFoundErr = -9870;
    public const int EventHotKeyExistsErr = -9878;
    public const int EventHotKeyInvalidErr = -9879;

    /* The application event target returned by GetApplicationEventTarget */
    public static readonly IntPtr ApplicationTarget = 0xA770;

    /* A handler installed with InstallApplicationEventHandler */
    public sealed record InstalledHandler(
        IntPtr Ref, IMacCarbonHotKeyApi.EventHandlerProcPtr Handler, IMacCarbonHotKeyApi.EventTypeSpec[] Types, IntPtr UserData);

    /* A hotkey registered with RegisterEventHotKey */
    public sealed record RegisteredHotKey(
        IntPtr Ref, uint KeyCode, uint Modifiers, IMacCarbonHotKeyApi.EventHotKeyID HotKeyId, IntPtr Target, uint Options);

    private readonly List<InstalledHandler> _handlers = new();
    private readonly List<RegisteredHotKey> _hotKeys = new();
    private readonly HashSet<(uint KeyCode, uint Modifiers)> _systemHotKeys = new();
    private readonly Dictionary<IntPtr, IMacCarbonHotKeyApi.EventHotKeyID> _liveEvents = new();
    private long _nextRef = 0x1000;

    /* Every RegisterEventHotKey call with its result, oldest first */
    public List<(uint KeyCode, uint Modifiers, IMacCarbonHotKeyApi.EventHotKeyID HotKeyId, IntPtr Target, uint Options, int Status)> RegisterCalls { get; } = new();

    /* Every hotkey ref passed to UnregisterEventHotKey, oldest first */
    public List<IntPtr> UnregisteredRefs { get; } = new();

    /* Every handler ref passed to RemoveEventHandler, oldest first */
    public List<IntPtr> RemovedHandlerRefs { get; } = new();

    /* When true GetEventParameter fails, as for a malformed event */
    public bool FailGetEventParameter { get; set; }

    /* Handlers currently installed */
    public IReadOnlyList<InstalledHandler> Handlers => _handlers;

    /* Hotkeys currently registered */
    public IReadOnlyList<RegisteredHotKey> HotKeys => _hotKeys;

    /* Builds a Carbon four-character code ("keyb" -> 0x6B657962) */
    public static uint FourCC(string code) =>
        (uint)code[0] << 24 | (uint)code[1] << 16 | (uint)code[2] << 8 | code[3];

    /* Installs a handler on the application target and returns its ref */
    public int InstallApplicationEventHandler(
        IMacCarbonHotKeyApi.EventHandlerProcPtr inHandler,
        uint inNumTypes,
        IMacCarbonHotKeyApi.EventTypeSpec[] inList,
        IntPtr inUserData,
        out IntPtr outRef)
    {
        outRef = NewRef();
        _handlers.Add(new InstalledHandler(outRef, inHandler, inList.Take((int)inNumTypes).ToArray(), inUserData));
        return NoErr;
    }

    /* Removes an installed handler */
    public int RemoveEventHandler(IntPtr inHandlerRef)
    {
        RemovedHandlerRefs.Add(inHandlerRef);
        return _handlers.RemoveAll(h => h.Ref == inHandlerRef) > 0 ? NoErr : ParamErr;
    }

    /* Returns the application's event target */
    public IntPtr GetApplicationEventTarget() => ApplicationTarget;

    /* Registers a hotkey unless the keycode + modifiers pair is already taken */
    public int RegisterEventHotKey(
        uint inHotKeyCode,
        uint inHotKeyModifiers,
        IMacCarbonHotKeyApi.EventHotKeyID inHotKeyID,
        IntPtr inTarget,
        uint inOptions,
        out IntPtr otRef)
    {
        int status;
        otRef = IntPtr.Zero;
        if (inTarget != ApplicationTarget)
            status = ParamErr;
        else if (_systemHotKeys.Contains((inHotKeyCode, inHotKeyModifiers)) ||
                 _hotKeys.Any(h => h.KeyCode == inHotKeyCode && h.Modifiers == inHotKeyModifiers))
            status = EventHotKeyExistsErr;
        else
        {
            otRef = NewRef();
            _hotKeys.Add(new RegisteredHotKey(otRef, inHotKeyCode, inHotKeyModifiers, inHotKeyID, inTarget, inOptions));
            status = NoErr;
        }

        RegisterCalls.Add((inHotKeyCode, inHotKeyModifiers, inHotKeyID, inTarget, inOptions, status));
        return status;
    }

    /* Releases a registered hotkey */
    public int UnregisterEventHotKey(IntPtr inHotKeyRef)
    {
        UnregisteredRefs.Add(inHotKeyRef);
        return _hotKeys.RemoveAll(h => h.Ref == inHotKeyRef) > 0 ? NoErr : EventHotKeyInvalidErr;
    }

    /* Reads the EventHotKeyID of a live hotkey event */
    public int GetEventParameter(
        IntPtr inEvent,
        uint inName,
        uint inDesiredType,
        IntPtr outActualType,
        uint inBufferSize,
        IntPtr outActualSize,
        out IMacCarbonHotKeyApi.EventHotKeyID outData)
    {
        outData = default;
        if (FailGetEventParameter || !_liveEvents.TryGetValue(inEvent, out var hotKeyId))
            return ParamErr;
        if (inName != FourCC("----") || inDesiredType != FourCC("ehki"))
            return EventParameterNotFoundErr;
        if (inBufferSize < 8)
            return ParamErr;

        outData = hotKeyId;
        return NoErr;
    }

    /* Simulates a combo owned by the system or another app (e.g. Spotlight) */
    public void ReserveForSystem(uint keyCode, uint modifiers) => _systemHotKeys.Add((keyCode, modifiers));

    /* Simulates the user pressing a combo. Returns the handler's status, or null when nothing was delivered */
    public int? Press(uint keyCode, uint modifiers)
    {
        var hotKey = _hotKeys.FirstOrDefault(h => h.KeyCode == keyCode && h.Modifiers == modifiers);
        return hotKey == null ? null : DeliverHotKeyEvent(hotKey.HotKeyId);
    }

    /* Dispatches a kEventHotKeyPressed event carrying `hotKeyId` to the matching application handlers */
    public int? DeliverHotKeyEvent(IMacCarbonHotKeyApi.EventHotKeyID hotKeyId)
    {
        var handlers = _handlers
            .Where(h => h.Types.Any(t => t.eventClass == FourCC("keyb") && t.eventKind == 5))
            .ToList();
        if (handlers.Count == 0)
            return null;

        IntPtr theEvent = NewRef();
        _liveEvents[theEvent] = hotKeyId;
        try
        {
            int status = NoErr;
            foreach (var handler in handlers)
                status = handler.Handler(0xCA11, theEvent, handler.UserData);
            return status;
        }
        finally
        {
            _liveEvents.Remove(theEvent);
        }
    }

    /* Returns a fresh opaque ref */
    private IntPtr NewRef() => (IntPtr)Interlocked.Increment(ref _nextRef);
}
