using System.Runtime.InteropServices;
using MoonTTS.Domain.Entities;
using MoonTTS.Domain.Interfaces;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.Infrastructure.OS.MacOS;

/* File Structure:
		- InitializeAsync() : Task
		- RegisterShortcutAsync(ShortcutCombo? toggleShortcut, Dictionary<string,ShortcutCombo> phraseShortcuts) : Task
		- RegisterSingleShortcut(string id, ShortcutCombo combo) : void
		- HandleHotKey(nint nextHandler, nint theEvent, nint userData) : int
		- Dispose() : void
		- LibMac
			- CarbonLib : string
			- kEventClassKeyboard : uint
			- kEventHotKeyPressed : uint
			- MoonTtsSignature : uint
			- kEventParamDirectObject : uint
			- typeEventHotKeyID : uint
			- cmdKey : uint
			- shiftKey : uint
			- optionKey : uint
			- controlKey : uint
			- EventTypeSpec
			- EventHotKeyID
			- EventHandlerProcPtr
			- InstallApplicationEventHandler(
				EventHandlerProcPtr inHandler, 
				uint inNumTypes, 
				EventTypeSpec[] inList, 
				nint inUserData, 
				out nint outRef) : int
			- RemoveEventHandler(nint inHandlerRef) : int
			- GetApplicationEventTarget() : nint
			- RegisterEventHotKey(
				uint inHotKeyCode, 
				uint inHotKeyModifiers, 
				EventHotKeyID inHotKeyID, 
				nint inTarget, 
				uint inOptions, 
				out nint otRef) : int
			- UnregisterEventHotKey(nint inHotKeyRef) : int
			- GetEventParameter(
				nint inEvent, 
				uint inName, 
				uint inDesiredType, 
				nint outActualType, 
				uint inBufferSize, 
				nint outActualSize, 
				out EventHotKeyID outData) : int
*/

public class MacShortcutHook : IGlobalShortcutHook
{
    private LibMac.EventHandlerProcPtr? _handlerDelegate;
    private IntPtr _eventHandlerRef = IntPtr.Zero;

    /* Maps the custom ID */
    private readonly Dictionary<uint, (string ShortcutId, IntPtr HotKeyRef)> _registeredCombos = new();
    private uint _nextId = 1;

    public event Action<string>? OnShortcutTriggered;

    /* Starts the process and registers it with MacOS */
    public Task InitializeAsync()
    {
        _handlerDelegate = HandleHotKey;
		
        /* This tells the OS that we only care about global hotheys events */
        var eventTypes = new[]
	{
	    new LibMac.EventTypeSpec
	    {
	        eventClass = LibMac.kEventClassKeyboard,
	        eventKind = LibMac.kEventHotKeyPressed
	    }
	};
    	
    	/* Hooks the C# listener into the main MacOS event loop */
    	LibMac.InstallApplicationEventHandler(
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
	    LibMac.UnregisterEventHotKey(kvp.Value.HotKeyRef);
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

		if (combo.HasCtrl) modifiers |= LibMac.controlKey;
		if (combo.HasAlt) modifiers |= LibMac.optionKey;
		if (combo.HasShift) modifiers |= LibMac.shiftKey;
		if (combo.HasWin) modifiers |= LibMac.cmdKey;

		/* Here we use the auxiliary dynamic map on "MacDynamicKeyMap" to get the layout
		   regardless of language or layout type */
		if (!MacDynamicKeyMap.TryGetKeyCode(combo.MainKey, out uint keyCode))
		    return;

		uint hotkeyId = _nextId++;

		/* Adds our app's signature to each hotkey */
		var hotKeyIdStruct = new LibMac.EventHotKeyID
		{
		    signature = LibMac.MoonTtsSignature,
		    id = hotkeyId
		};
	    
		/* Registers it with carbon */
		int result = LibMac.RegisterEventHotKey(
		    keyCode,
		    modifiers,
		    hotKeyIdStruct,
		    LibMac.GetApplicationEventTarget(),
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
		int result = LibMac.GetEventParameter(
		    theEvent,
		    LibMac.kEventParamDirectObject,
		    LibMac.typeEventHotKeyID,
		    IntPtr.Zero,
		    (uint)Marshal.SizeOf<LibMac.EventHotKeyID>(),
		    IntPtr.Zero,
		    out LibMac.EventHotKeyID hkId);

		/* If it identifies the ID, Invokes an action */
		if (result == 0 && hkId.signature == LibMac.MoonTtsSignature)
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
		    LibMac.RemoveEventHandler(_eventHandlerRef);
		    _eventHandlerRef = IntPtr.Zero;
		}

		foreach (var kvp in _registeredCombos)
		{
		    LibMac.UnregisterEventHotKey(kvp.Value.HotKeyRef);
		}
		_registeredCombos.Clear();
    }


    /* Native C code that Mac expects to talk to */
    /* This is where I would link to you Apple's docs on this. IF THEY WEREN'T ERASED FROM THEIR SITE */
    /* Instead, I adapted HotKey's approach, here's their github it was really useful https://github.com/soffes/HotKey */
    
    private static class LibMac
    {
        private const string CarbonLib = "/System/Library/Frameworks/Carbon.framework/Versions/Current/Carbon";
        
        /* Aple uses ASCII 4-byte codes for lmost everything */
        /* "keyb" is Keyboard Events */
        public const uint kEventClassKeyboard = 0x6B657962;
        public const uint kEventHotKeyPressed = 5;

        /*  "MTTS" Signature used to prove that the hotkey belongs to us */
        /* This only matters to differentiate processes inside our own app, as Mac already assigns an id to each app */
        public const uint MoonTtsSignature = 0x4D545453;
        
        /* "----" direct object, "ehki" is Event Hot Key ID */
        public const uint kEventParamDirectObject = 0x2D2D2D2D;
        public const uint typeEventHotKeyID = 0x65686B69;
        
        /* Mac modifier flags */
        public const uint cmdKey = 0x0100;
        public const uint shiftKey = 0x0200;
        public const uint optionKey = 0x0800;
        public const uint controlKey = 0x1000;
        
	    [StructLayout(LayoutKind.Sequential)]
	    public struct EventTypeSpec
	    {
	        public uint eventClass;
	        public uint eventKind;
	    }

	    [StructLayout(LayoutKind.Sequential)]
	    public struct EventHotKeyID
	    {
	        public uint signature;
	        public uint id;
	    }

	    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	    public delegate int EventHandlerProcPtr(IntPtr nextHandler, IntPtr theEvent, IntPtr userData);

	    /* Injects a C# function into the OS event pipeline */
	    [DllImport(CarbonLib)]
	    public static extern int InstallApplicationEventHandler(
	        EventHandlerProcPtr inHandler,
	        uint inNumTypes,
	        EventTypeSpec[] inList,
	        IntPtr inUserData,
	        out IntPtr outRef);
	        
	    /* The opposite of the above */
	    [DllImport(CarbonLib)]
	    public static extern int RemoveEventHandler(IntPtr inHandlerRef);

	    /* Returns the raw memory pointer of MTTS event loop */
		[DllImport(CarbonLib)]
		public static extern IntPtr GetApplicationEventTarget();

		/* Receives the hardware keycode + modifiers + custom ID + MTTS' address and registers it */
		[DllImport(CarbonLib)]
		public static extern int RegisterEventHotKey(
		    uint inHotKeyCode,
		    uint inHotKeyModifiers,
		    EventHotKeyID inHotKeyID,
		    IntPtr inTarget,
		    uint inOptions,
		    out IntPtr otRef);

		/* Releases a shortcut */
		[DllImport(CarbonLib)]
		public static extern int UnregisterEventHotKey(IntPtr inHotKeyRef);

		/* Translates the C block received from the OS into somethinf usable by C# */
		[DllImport(CarbonLib)]
		public static extern int GetEventParameter(
		    IntPtr inEvent,
		    uint inName,
		    uint inDesiredType,
		    IntPtr outActualType,
		    uint inBufferSize,
		    IntPtr outActualSize,
		    out EventHotKeyID outData);

    }
}
