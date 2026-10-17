using System.Runtime.InteropServices;
using MoonTTS.Infrastructure.OS.MacOS;

namespace MoonTTS.Infrastructure.Tests.OS.MacOS;

/* File Structure:
        - UsLayout() : DummyMacKeyboardLayoutApi
        - AzertyLayout() : DummyMacKeyboardLayoutApi
        - Translation(uint VirtualKeyCode, ushort KeyAction, uint ModifierKeyState, uint KeyboardType, uint Options, uint MaxLength)
        - dlopen(string path, int mode) : nint
        - dlsym(nint handle, string symbol) : nint
        - dlclose(nint handle) : nint
        - TTSCopyCurrentKeyboardInputSource() : nint
        - TTSGetInputSourceProperty(nint inputSource, nint propertyKey) : nint
        - LMGetKbdType() : uint
        - UCKeyTranslate(...) : int
        - CFDataGetBytePtr(nint theData) : nint
        - CFRelease(nint cf) : void
        - Dispose() : void
*/

/* IMacKeyboardLayoutApi that behaves like dyld + Text Input Sources + UCKeyTranslate for a configurable layout:
   - dlopen only knows the Carbon framework; dlsym returns the address of a variable holding the layout property key
   - the current input source is a retained CF object the caller must CFRelease
   - UCKeyTranslate answers, for each virtual keycode, the characters the layout produces without modifiers */
public sealed class DummyMacKeyboardLayoutApi : IMacKeyboardLayoutApi, IDisposable
{
    /* Real framework path */
    public const string CarbonPath = "/System/Library/Frameworks/Carbon.framework/Versions/Current/Carbon";

    /* Exported variable the app looks up (the real HIToolbox name is kTISPropertyUnicodeKeyLayoutData) */
    public const string LayoutDataSymbol = "kTTSPropertyUnicodeKeyLayoutData";

    public static readonly IntPtr CarbonHandle = 0xD100;
    public static readonly IntPtr PropertyKey = 0x4B45;
    public static readonly IntPtr InputSource = 0x1500;
    public static readonly IntPtr LayoutData = 0xDA7A;
    public static readonly IntPtr LayoutBytes = 0x1A70;

    /* One UCKeyTranslate call */
    public sealed record Translation(uint VirtualKeyCode, ushort KeyAction, uint ModifierKeyState, uint KeyboardType, uint Options, uint MaxLength);

    /* Unmanaged variable that dlsym points at; it stores PropertyKey like the real exported CFStringRef */
    private readonly IntPtr _symbolSlot;
    private int _inputSourceRetainCount;

    /* Characters produced by each virtual keycode (unshifted) */
    public Dictionary<uint, string> Layout { get; } = new();

    public bool HasCurrentInputSource { get; set; } = true;
    public bool HasLayoutData { get; set; } = true;
    public bool ExportsLayoutSymbol { get; set; } = true;
    public uint KeyboardType { get; set; } = 40;

    public List<string> OpenedPaths { get; } = new();
    public List<IntPtr> ClosedHandles { get; } = new();
    public List<IntPtr> ReleasedObjects { get; } = new();
    public List<Translation> Translations { get; } = new();
    public int CopyInputSourceCount { get; private set; }

    /* Retains on the input source that were never released */
    public int LeakedInputSourceRetains => _inputSourceRetainCount;

    /* Allocates the exported variable */
    public DummyMacKeyboardLayoutApi()
    {
        _symbolSlot = Marshal.AllocHGlobal(IntPtr.Size);
        Marshal.WriteIntPtr(_symbolSlot, PropertyKey);
    }

    /* US ANSI layout: letters, digits and a few symbols on their real keycodes */
    public static DummyMacKeyboardLayoutApi UsLayout()
    {
        var api = new DummyMacKeyboardLayoutApi();
        uint[] codes = [0x00, 0x0B, 0x08, 0x02, 0x0E, 0x03, 0x05, 0x04, 0x22, 0x26, 0x28, 0x25, 0x2E,
                        0x2D, 0x1F, 0x23, 0x0C, 0x0F, 0x01, 0x11, 0x20, 0x09, 0x0D, 0x07, 0x10, 0x06];
        for (int i = 0; i < codes.Length; i++)
            api.Layout[codes[i]] = ((char)('a' + i)).ToString();

        uint[] digits = [0x1D, 0x12, 0x13, 0x14, 0x15, 0x17, 0x16, 0x1A, 0x1C, 0x19];
        for (int i = 0; i < digits.Length; i++)
            api.Layout[digits[i]] = ((char)('0' + i)).ToString();

        api.Layout[0x31] = " ";
        api.Layout[0x24] = "\r";
        api.Layout[0x2B] = ",";
        return api;
    }

    /* French AZERTY layout: A/Q, Z/W and M/; are swapped compared with US */
    public static DummyMacKeyboardLayoutApi AzertyLayout()
    {
        var api = UsLayout();
        api.Layout[0x00] = "q";
        api.Layout[0x0C] = "a";
        api.Layout[0x06] = "w";
        api.Layout[0x0D] = "z";
        api.Layout[0x29] = "m";
        api.Layout[0x2E] = ",";
        api.Layout[0x2B] = ";";
        return api;
    }

    /* Opens a framework; only Carbon exists */
    public IntPtr dlopen(string path, int mode)
    {
        OpenedPaths.Add(path);
        return path == CarbonPath ? CarbonHandle : IntPtr.Zero;
    }

    /* Resolves the layout-data variable inside Carbon */
    public IntPtr dlsym(IntPtr handle, string symbol)
    {
        if (handle != CarbonHandle || symbol != LayoutDataSymbol || !ExportsLayoutSymbol)
            return IntPtr.Zero;
        return _symbolSlot;
    }

    /* Closes a dlopen handle */
    public IntPtr dlclose(IntPtr handle)
    {
        ClosedHandles.Add(handle);
        return IntPtr.Zero;
    }

    /* Returns the current input source retained (+1), or 0 when there is none */
    public IntPtr TTSCopyCurrentKeyboardInputSource()
    {
        CopyInputSourceCount++;
        if (!HasCurrentInputSource)
            return IntPtr.Zero;

        _inputSourceRetainCount++;
        return InputSource;
    }

    /* Returns the layout-data property of the input source (not retained) */
    public IntPtr TTSGetInputSourceProperty(IntPtr inputSource, IntPtr propertyKey)
    {
        if (inputSource != InputSource || propertyKey != PropertyKey || !HasLayoutData)
            return IntPtr.Zero;
        return LayoutData;
    }

    /* Physical keyboard type */
    public uint LMGetKbdType() => KeyboardType;

    /* Produces the characters the layout maps to the keycode */
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
        char[] unicodeString)
    {
        Translations.Add(new Translation(virtualKeyCode, keyAction, modifierKeyState, keyboardType, keyTranslateOptions, maxStringLenght));
        actualStringLenght = 0;
        if (keyLayoutPtr != LayoutBytes)
            return DummyMacCarbonHotKeyApi.ParamErr;

        if (Layout.TryGetValue(virtualKeyCode, out var text))
        {
            int length = (int)Math.Min((uint)text.Length, maxStringLenght);
            text.CopyTo(0, unicodeString, 0, length);
            actualStringLenght = (uint)length;
        }
        return DummyMacCarbonHotKeyApi.NoErr;
    }

    /* Returns the bytes of the layout CFData */
    public IntPtr CFDataGetBytePtr(IntPtr theData) => theData == LayoutData ? LayoutBytes : IntPtr.Zero;

    /* Releases a CF object; over-releasing crashes like CoreFoundation would */
    public void CFRelease(IntPtr cf)
    {
        ReleasedObjects.Add(cf);
        if (cf != InputSource)
            return;
        if (_inputSourceRetainCount == 0)
            throw new InvalidOperationException("CFRelease of an already released input source");
        _inputSourceRetainCount--;
    }

    /* Frees the exported variable */
    public void Dispose() => Marshal.FreeHGlobal(_symbolSlot);
}
