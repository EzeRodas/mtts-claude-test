using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MoonTTS.Domain.Entities;
using MoonTTS.Domain.Interfaces;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.Infrastructure.OS.MacOS;

/* File Structure:
		- MacShortcutHook()
		- MacShortcutHook(IMacCarbonHotKeyApi carbon, IMacKeyboardLayoutApi keyboardLayout)
		- MoonTtsSignature : uint
		- InitializeAsync() : Task
		- RegisterShortcutAsync(ShortcutCombo? toggleShortcut, Dictionary<string,ShortcutCombo> phraseShortcuts) : Task
		- RegisterSingleShortcut(string id, ShortcutCombo combo) : void
		- HandleHotKey(nint nextHandler, nint theEvent, nint userData) : int
		- Dispose() : void
*/

public class MacShortcutHook : IGlobalShortcutHook
{
    /*  "MTTS" Signature used to prove that the hotkey belongs to us */
    /* This only matters to differentiate processes inside our own app, as Mac already assigns an id to each app */
    private const uint MoonTtsSignature = 0x4D545453;

    /* Native Carbon surface (the real framework in the app, a dummy in tests) */
    private readonly IMacCarbonHotKeyApi _carbon;

    /* Translates key names into the hardware keycodes of the user's keyboard layout */
    private readonly MacDynamicKeyMap _keyMap;

    private IMacCarbonHotKeyApi.EventHandlerProcPtr? _handlerDelegate;
    private IntPtr _eventHandlerRef = IntPtr.Zero;

    /* Maps the custom ID */
    private readonly Dictionary<uint, (string ShortcutId, IntPtr HotKeyRef)> _registeredCombos = new();
    private uint _nextId = 1;

    public event Action<string>? OnShortcutTriggered;

    /* Talks to the real Carbon framework */
    [SupportedOSPlatform("macos")]
    public MacShortcutHook() : this(new MacCarbonHotKeyApi(), new MacKeyboardLayoutApi())
    {
    }

    /* Talks to the given Carbon and keyboard-layout API implementations */
    public MacShortcutHook(IMacCarbonHotKeyApi carbon, IMacKeyboardLayoutApi keyboardLayout)
    {
        _carbon = carbon;
        _keyMap = new MacDynamicKeyMap(keyboardLayout);
    }

    /* Starts the process and registers it with MacOS */
    public Task InitializeAsync()
    {
        _handlerDelegate = HandleHotKey;
		
        /* This tells the OS that we only care about global hotheys events */
        var eventTypes = new[]
	{
	    new IMacCarbonHotKeyApi.EventTypeSpec
	    {
	        eventClass = IMacCarbonHotKeyApi.kEventClassKeyboard,
	        eventKind = IMacCarbonHotKeyApi.kEventHotKeyPressed
	    }
	};
    	
    	/* Hooks the C# listener into the main MacOS event loop */
    	_carbon.InstallApplicationEventHandler(
	    _handlerDelegate,
	    1,
	    eventTypes,
	    IntPtr.Zero,
	    out _eventHandlerRef);

	return Task.CompletedTask;
    }
	
    /* Resets and reloads all the shortcuts every time the user changes the config */
    public Task RegisterShortcutAsync(ShortcutCombo? toggleShortcut, Dictionary<string, ShortcutCombo> phraseShortcuts)
    {
	/* Unregisters old shortcuts */
	foreach (var kvp in _registeredCombos)
	{
	    _carbon.UnregisterEventHotKey(kvp.Value.HotKeyRef);
	}
	_registeredCombos.Clear();
	_nextId = 1;

	/* Registers main toggle shortcut */
	if (toggleShortcut != null) RegisterSingleShortcut("toggle_app", toggleShortcut);

	/* Registers custom phrase shortcuts */
	foreach (var kvp in phraseShortcuts)
	{
	    RegisterSingleShortcut($"phrase_{kvp.Key}", kvp.Value);
	}
	return Task.CompletedTask;
    }

	/* Individual method for registering the shortcuts */
    private void RegisterSingleShortcut(string id, ShortcutCombo combo)
    {
	 
	    /* The OS expects flags for each special key in the combo */
		uint modifiers = 0;

		if (combo.HasCtrl) modifiers |= IMacCarbonHotKeyApi.controlKey;
		if (combo.HasAlt) modifiers |= IMacCarbonHotKeyApi.optionKey;
		if (combo.HasShift) modifiers |= IMacCarbonHotKeyApi.shiftKey;
		if (combo.HasWin) modifiers |= IMacCarbonHotKeyApi.cmdKey;

		/* Here we use the auxiliary dynamic map on "MacDynamicKeyMap" to get the layout
		   regardless of language or layout type */
		if (!_keyMap.TryGetKeyCode(combo.MainKey, out uint keyCode))
		    return;

		uint hotkeyId = _nextId++;

		/* Adds our app's signature to each hotkey */
		var hotKeyIdStruct = new IMacCarbonHotKeyApi.EventHotKeyID
		{
		    signature = MoonTtsSignature,
		    id = hotkeyId
		};
	    
		/* Registers it with carbon */
		int result = _carbon.RegisterEventHotKey(
		    keyCode,
		    modifiers,
		    hotKeyIdStruct,
		    _carbon.GetApplicationEventTarget(),
		    0,
		    out IntPtr hotKeyRef);

		if (result == 0 && hotKeyRef != IntPtr.Zero)
		{
		    _registeredCombos[hotkeyId] = (id, hotKeyRef);
		}	   
    }

    /* Function called by the OS when the shortcut is pressed */
    private int HandleHotKey(IntPtr nextHandler, IntPtr theEvent, IntPtr userData)
    {
	    /* Uses GetEventParameter to figure the custom ID of the shortcut */
		int result = _carbon.GetEventParameter(
		    theEvent,
		    IMacCarbonHotKeyApi.kEventParamDirectObject,
		    IMacCarbonHotKeyApi.typeEventHotKeyID,
		    IntPtr.Zero,
		    (uint)Marshal.SizeOf<IMacCarbonHotKeyApi.EventHotKeyID>(),
		    IntPtr.Zero,
		    out IMacCarbonHotKeyApi.EventHotKeyID hkId);

		/* If it identifies the ID, Invokes an action */
		if (result == 0 && hkId.signature == MoonTtsSignature)
		{
		    if (_registeredCombos.TryGetValue(hkId.id, out var comboData))
		    {
				OnShortcutTriggered?.Invoke(comboData.ShortcutId);
		    }
		}

		return 0;
	}

    /* Gets rid of everything when the app closes */
    public void Dispose()
    {
		if (_eventHandlerRef != IntPtr.Zero)
		{
		    _carbon.RemoveEventHandler(_eventHandlerRef);
		    _eventHandlerRef = IntPtr.Zero;
		}

		foreach (var kvp in _registeredCombos)
		{
		    _carbon.UnregisterEventHotKey(kvp.Value.HotKeyRef);
		}
		_registeredCombos.Clear();
    }
}
