using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MoonTTS.Infrastructure.OS.MacOS;

/* File Structure:
        - dlopen(string path, int mode) : nint
        - dlsym(nint handle, string symbol) : nint
        - dlclose(nint handle) : nint
        - TTSCopyCurrentKeyboardInputSource() : nint
        - TTSGetInputSourceProperty(nint inputSource, nint propertyKey) : nint
        - LMGetKbdType() : uint
        - UCKeyTranslate(...) : int
        - CFDataGetBytePtr(nint theData) : nint
        - CFRelease(nint cf) : void
        - LibMac
            - CarbonLib : string
            - CoreFoundationLib : string
            - (one extern per method above)
*/

/* Real IMacKeyboardLayoutApi: forwards every call to dyld, Carbon and CoreFoundation */
[SupportedOSPlatform("macos")]
internal sealed class MacKeyboardLayoutApi : IMacKeyboardLayoutApi
{
    /* Forwards to libdl dlopen */
    public IntPtr dlopen(string path, int mode) => LibMac.dlopen(path, mode);

    /* Forwards to libdl dlsym */
    public IntPtr dlsym(IntPtr handle, string symbol) => LibMac.dlsym(handle, symbol);

    /* Forwards to libdl dlclose */
    public IntPtr dlclose(IntPtr handle) => LibMac.dlclose(handle);

    /* Forwards to Carbon TTSCopyCurrentKeyboardInputSource */
    public IntPtr TTSCopyCurrentKeyboardInputSource() => LibMac.TTSCopyCurrentKeyboardInputSource();

    /* Forwards to Carbon TTSGetInputSourceProperty */
    public IntPtr TTSGetInputSourceProperty(IntPtr inputSource, IntPtr propertyKey) =>
        LibMac.TTSGetInputSourceProperty(inputSource, propertyKey);

    /* Forwards to Carbon LMGetKbdType */
    public uint LMGetKbdType() => LibMac.LMGetKbdType();

    /* Forwards to Carbon UCKeyTranslate */
    public int UCKeyTranslate(
        IntPtr keyLayoutPtr,
        uint virtualKeyCode,
        ushort keyAction,
        uint modifierKeyState,
        uint keyboardType,
        uint keyTranslateOptions,
        ref uint deadKeyState,
        uint maxStringLenght,
        ref uint actualStringLenght,
        char[] unicodeString) =>
        LibMac.UCKeyTranslate(keyLayoutPtr, virtualKeyCode, keyAction, modifierKeyState, keyboardType,
            keyTranslateOptions, ref deadKeyState, maxStringLenght, ref actualStringLenght, unicodeString);

    /* Forwards to CoreFoundation CFDataGetBytePtr */
    public IntPtr CFDataGetBytePtr(IntPtr theData) => LibMac.CFDataGetBytePtr(theData);

    /* Forwards to CoreFoundation CFRelease */
    public void CFRelease(IntPtr cf) => LibMac.CFRelease(cf);

    private static class LibMac
    {
        public const string CarbonLib = IMacKeyboardLayoutApi.CarbonLib;
        public const string CoreFoundationLib = "/System/Library/Frameworks/CoreFoundation.framework/Versions/Current/CoreFoundation";

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
