namespace MoonTTS.Infrastructure.OS.MacOS;

/* File Structure:
        - CarbonLib : string
        - kUCKeyActionDown : ushort
        - kUCKeyTranslateNoDeadKeysBit : uint
        - dlopen(string path, int mode) : nint
        - dlsym(nint handle, string symbol) : nint
        - dlclose(nint handle) : nint
        - TTSCopyCurrentKeyboardInputSource() : nint
        - TTSGetInputSourceProperty(nint inputSource, nint propertyKey) : nint
        - LMGetKbdType() : uint
        - UCKeyTranslate(
            nint keyLayoutPtr,
            uint virtualKeyCode,
            ushort keyAction,
            uint modifierKeyState,
            uint keyboardType,
            uint keyTranslateOptions,
            ref uint deadKeyState,
            uint maxStringLenght,
            ref uint actualStringLenght,
            char[] unicodeString) : int
        - CFDataGetBytePtr(nint theData) : nint
        - CFRelease(nint cf) : void
*/

/* The dyld / Carbon / CoreFoundation calls MacDynamicKeyMap makes to read the user's keyboard layout, so the map
   can be built from the real OS (MacKeyboardLayoutApi) or from a dummy that answers like macOS would */
public interface IMacKeyboardLayoutApi
{
    /* Framework opened with dlopen to look up the layout property key */
    public const string CarbonLib = "/System/Library/Frameworks/Carbon.framework/Versions/Current/Carbon";

    public const ushort kUCKeyActionDown = 0;
    public const uint kUCKeyTranslateNoDeadKeysBit = 0;

    /* Opens a dynamic library and returns its handle (0 on failure) */
    IntPtr dlopen(string path, int mode);

    /* Returns the address of an exported symbol (0 when it does not exist) */
    IntPtr dlsym(IntPtr handle, string symbol);

    /* Closes a handle returned by dlopen */
    IntPtr dlclose(IntPtr handle);

    /* Returns (retained) the current keyboard input source, or 0 */
    IntPtr TTSCopyCurrentKeyboardInputSource();

    /* Returns (not retained) a property of an input source, or 0 */
    IntPtr TTSGetInputSourceProperty(IntPtr inputSource, IntPtr propertyKey);

    /* Physical keyboard type passed to UCKeyTranslate */
    uint LMGetKbdType();

    /* Translates a virtual keycode into the characters the layout produces */
    int UCKeyTranslate(
        IntPtr keyLayoutPtr,
        uint virtualKeyCode,
        ushort keyAction,
        uint modifierKeyState,
        uint keyboardType,
        uint keyTranslateOptions,
        ref uint deadKeyState,
        uint maxStringLenght,
        ref uint actualStringLenght,
        char[] unicodeString);

    /* Returns the raw bytes of a CFData */
    IntPtr CFDataGetBytePtr(IntPtr theData);

    /* Releases a CoreFoundation object */
    void CFRelease(IntPtr cf);
}
