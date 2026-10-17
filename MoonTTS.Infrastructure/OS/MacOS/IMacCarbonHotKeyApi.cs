using System.Runtime.InteropServices;

namespace MoonTTS.Infrastructure.OS.MacOS;

/* File Structure:
        - kEventClassKeyboard : uint
        - kEventHotKeyPressed : uint
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

/* The Carbon event / hotkey calls MacShortcutHook makes, so the hook can run against the real Carbon framework
   (MacCarbonHotKeyApi) or against a dummy that answers like macOS would */
public interface IMacCarbonHotKeyApi
{
    /* Aple uses ASCII 4-byte codes for lmost everything */
    /* "keyb" is Keyboard Events */
    public const uint kEventClassKeyboard = 0x6B657962;
    public const uint kEventHotKeyPressed = 5;

    /* "----" direct object, "ehki" is Event Hot Key ID */
    public const uint kEventParamDirectObject = 0x2D2D2D2D;
    public const uint typeEventHotKeyID = 0x65686B69;

    /* Mac modifier flags */
    public const uint cmdKey = 0x0100;
    public const uint shiftKey = 0x0200;
    public const uint optionKey = 0x0800;
    public const uint controlKey = 0x1000;

    /* Event class + kind an event handler listens to */
    [StructLayout(LayoutKind.Sequential)]
    public struct EventTypeSpec
    {
        public uint eventClass;
        public uint eventKind;
    }

    /* Signature + id attached to a registered hotkey and reported back when it is pressed */
    [StructLayout(LayoutKind.Sequential)]
    public struct EventHotKeyID
    {
        public uint signature;
        public uint id;
    }

    /* Native callback Carbon invokes for every matching event */
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int EventHandlerProcPtr(IntPtr nextHandler, IntPtr theEvent, IntPtr userData);

    /* Injects a C# function into the OS event pipeline */
    int InstallApplicationEventHandler(
        EventHandlerProcPtr inHandler,
        uint inNumTypes,
        EventTypeSpec[] inList,
        IntPtr inUserData,
        out IntPtr outRef);

    /* The opposite of the above */
    int RemoveEventHandler(IntPtr inHandlerRef);

    /* Returns the raw memory pointer of MTTS event loop */
    IntPtr GetApplicationEventTarget();

    /* Receives the hardware keycode + modifiers + custom ID + MTTS' address and registers it */
    int RegisterEventHotKey(
        uint inHotKeyCode,
        uint inHotKeyModifiers,
        EventHotKeyID inHotKeyID,
        IntPtr inTarget,
        uint inOptions,
        out IntPtr otRef);

    /* Releases a shortcut */
    int UnregisterEventHotKey(IntPtr inHotKeyRef);

    /* Translates the C block received from the OS into somethinf usable by C# */
    int GetEventParameter(
        IntPtr inEvent,
        uint inName,
        uint inDesiredType,
        IntPtr outActualType,
        uint inBufferSize,
        IntPtr outActualSize,
        out EventHotKeyID outData);
}
