using System.Runtime.InteropServices;

namespace MoonTTS.Infrastructure.OS.MacOS;

/* Dynamically queries the MacOS kernel for the user's specific keyboard layout.
   Then grabs that and build a dictionary for global hotkeys*/

/* File Structure:
        - TryGetKeyCode(string mainKey, out uint keyCode) : bool
        - BuildLayoutMap() : void
        - AddFallbackNonPrintables() : void
        - LoadQwertyFallback() : void
        - LibMac
            - CarbonLib : string
            - CoreFoundationLib : string
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

internal static class MacDynamicKeyMap
{
    private static Dictionary<string, uint>? _dynamicMap;

    public static bool TryGetKeyCode(string mainKey, out uint keyCode)
    {
        if (_dynamicMap == null)
        {
            BuildLayoutMap();
        }
        
        return _dynamicMap!.TryGetValue(mainKey.ToUpperInvariant(), out keyCode);
    }

    /* Builds the correct mapping for the user's layout */
    private static void BuildLayoutMap()
    {
        _dynamicMap = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

        /* Gets the Apple CoreFoundation pointer for the layout property */
        IntPtr libHandle = LibMac.dlopen(LibMac.CarbonLib,  2);
        IntPtr symbolPtr = LibMac.dlsym(libHandle, "kTTSPropertyUnicodeKeyLayoutData");
        IntPtr propertyKey = Marshal.ReadIntPtr(symbolPtr);
        
        /* Gets the user's current keyboard layout */
        IntPtr currentKeyboard = LibMac.TTSCopyCurrentKeyboardInputSource();
        if (currentKeyboard == IntPtr.Zero)
        {
            LoadQwertyFallback();
            return;
        }
        /* Gets raw byte data of the layout */
        IntPtr layoutData = LibMac.TTSGetInputSourceProperty(currentKeyboard, propertyKey);
        if (layoutData == IntPtr.Zero)
        {
            LibMac.CFRelease(currentKeyboard);
            LoadQwertyFallback();
            return;
        }
        IntPtr keyboardLayoutPtr = LibMac.CFDataGetBytePtr(layoutData);
        
        /* Loops through every physical key (0-127) and checks what letter produces */
        for (uint i = 0; i < 128; i++)
        {
            uint deadKeyState = 0;
            char[] chars = new char[4];
            uint actualStringLenght = 0;

            /* Retrieves the produced result of pressing the key "i" */
            LibMac.UCKeyTranslate(
                keyboardLayoutPtr,
                i,
                LibMac.kUCKeyActionDown,
                0, /* No modifiers like shift, ctrl, etc. */
                LibMac.LMGetKbdType(),
                LibMac.kUCKeyTranslateNoDeadKeysBit,
                ref deadKeyState,
                4,
                ref actualStringLenght,
                chars);

            if (actualStringLenght > 0)
            {
                string keyChar = new string(chars, 0, (int)actualStringLenght).ToUpperInvariant();

                /* Saves to map */
                if (!_dynamicMap.ContainsKey(keyChar))
                {
                    _dynamicMap[keyChar] = i;
                }
            }
        }
        
        LibMac.CFRelease(currentKeyboard);
        LibMac.dlclose(libHandle);
        
        /* Adds the keys that never change */
        AddFallbackNonPrintables();
    }

    private static void AddFallbackNonPrintables()
    {
        var staicKeys = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            {"F1", 0x7A}, {"F2", 0x78}, {"F3", 0x63}, {"F4", 0x76}, {"F5", 0x60},
            {"F6", 0x61}, {"F7", 0x62}, {"F8", 0x64}, {"F9", 0x65}, {"F10", 0x6D},
            {"F11", 0x67}, {"F12", 0x6F},
            {"SPACE", 0x31}, {"RETURN", 0x24}, {"ENTER", 0x24}, {"TAB", 0x30},
            {"ESCAPE", 0x35}, {"ESC", 0x35}, {"DELETE", 0x33}, {"BACKSPACE", 0x33},
            {"HOME", 0x73}, {"END", 0x77}, {"PAGEUP", 0x74}, {"PAGEDOWN", 0x79},
            {"LEFTARROW", 0x7B}, {"RIGHTARROW", 0x7C}, {"DOWNARROW", 0x7D}, {"UPARROW", 0x7E}
        };

        foreach (var kvp in  staicKeys)
        {
            if (!_dynamicMap!.ContainsKey(kvp.Key))
            {
                _dynamicMap[kvp.Key] = kvp.Value;    
            }
        }
    }
    
    /* Loads default qwerty layout in case it couldn't detect the correct one */
    private static void LoadQwertyFallback()
    {
        var qwertyMap = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            {"A", 0x00}, {"B", 0x0B}, {"C", 0x08}, {"D", 0x02}, {"E", 0x0E}, {"F", 0x03},
            {"G", 0x05}, {"H", 0x04}, {"I", 0x22}, {"J", 0x26}, {"K", 0x28}, {"L", 0x25},
            {"M", 0x2E}, {"N", 0x2D}, {"O", 0x1F}, {"P", 0x23}, {"Q", 0x0C}, {"R", 0x0F},
            {"S", 0x01}, {"T", 0x11}, {"U", 0x20}, {"V", 0x09}, {"W", 0x0D}, {"X", 0x07},
            {"Y", 0x10}, {"Z", 0x06},
            {"0", 0x1D}, {"1", 0x12}, {"2", 0x13}, {"3", 0x14}, {"4", 0x15},
            {"5", 0x17}, {"6", 0x16}, {"7", 0x1A}, {"8", 0x1C}, {"9", 0x19}
        };

        foreach (var kvp in qwertyMap)
        {
            _dynamicMap![kvp.Key] = kvp.Value;
        }

        AddFallbackNonPrintables();
    }
    
    
    private static class LibMac
    {
        public const string CarbonLib = "/System/Library/Frameworks/Carbon.framework/Versions/Current/Carbon";
        public const string CoreFoundationLib = "/System/Library/Frameworks/CoreFoundation.framework/Versions/Current/CoreFoundation";

        public const ushort kUCKeyActionDown = 0;
        public const uint kUCKeyTranslateNoDeadKeysBit = 0;

        [DllImport("libdl.dylib")]
        public static extern IntPtr dlopen(string path, int mode);
        
        [DllImport("libdl.dylib")]
        public static extern IntPtr dlsym(IntPtr handle, string symbol);
        
        [DllImport("libdl.dylib")]
        public static extern IntPtr dlclose(IntPtr handle);

        [DllImport(CarbonLib)]
        public static extern IntPtr TTSCopyCurrentKeyboardInputSource();
        
        [DllImport(CarbonLib)]
        public static extern IntPtr TTSGetInputSourceProperty(IntPtr inputSource, IntPtr propertyKey);
        
        [DllImport(CarbonLib)]
        public static extern uint LMGetKbdType();

        [DllImport(CarbonLib)]
        public static extern int UCKeyTranslate(
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

        [DllImport(CoreFoundationLib)]
        public static extern IntPtr CFDataGetBytePtr(IntPtr theData);
        
        [DllImport(CoreFoundationLib)]
        public static extern void CFRelease(IntPtr cf);
    }
}