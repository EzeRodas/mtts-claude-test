using System.Reflection;
using System.Runtime.InteropServices;

namespace MoonTTS.Engine.Mbrola;

/* File Structure:
        - EnsureResolverRegistered() : void
        - SetNativeEnvironmentVariable(string name, string value) : void
        - ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath) : IntPtr
        - setenv(string name, string value, int overwrite) : int
        - _putenv_s(string name, string value) : int
        - espeak_Initialize(int output, int buflength, string? path, int options) : int
        - espeak_SetSynthCallback(delegate* unmanaged[Cdecl]<short*, int, IntPtr, int> callback) : void
        - espeak_SetVoiceByName(string name) : int
        - espeak_SetParameter(int parameter, int value, int relative) : int
        - espeak_Synth(byte* text, nuint size, uint position, int positionType, uint endPosition, uint flags, uint* uniqueIdentifier, void* userData) : int
        - getFreq_MBR() : int
*/

/* Native AOT P/Invoke bindings for the bundled libespeak-ng library with in-memory MBROLA LibOneChannel */
internal static unsafe partial class EspeakMbrolaNative
{
    public const string LibraryName = "espeak-ng";

    public const int AudioOutputSynchronous = 0x02;
    public const int EspeakCharsUtf8 = 1;
    public const int PosCharacter = 1;
    public const int EspeakRate = 1;
    public const int EeOk = 0;

    private static int _resolverRegistered;

    /* Registers the runtime native library resolver once per process */
    public static void EnsureResolverRegistered()
    {
        if (Interlocked.Exchange(ref _resolverRegistered, 1) == 0)
        {
            NativeLibrary.SetDllImportResolver(typeof(EspeakMbrolaNative).Assembly, ResolveLibrary);
        }
    }

    /* Updates both the managed and native C runtime environment variable tables */
    public static void SetNativeEnvironmentVariable(string name, string value)
    {
        Environment.SetEnvironmentVariable(name, value);
        if (OperatingSystem.IsWindows())
            _putenv_s(name, value);
        else
            setenv(name, value, 1);
    }

    /* Sets an environment variable in the POSIX C runtime so native getenv() sees it */
    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int setenv(string name, string value, int overwrite);

    /* Sets an environment variable in the Windows CRT so native getenv() sees it */
    [LibraryImport("ucrtbase", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int _putenv_s(string name, string value);

    /* Resolves the bundled libespeak-ng binary from the engine folder's runtimes/<rid>/native directory */
    private static IntPtr ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, LibraryName, StringComparison.Ordinal))
            return IntPtr.Zero;

        string baseDir = AppContext.BaseDirectory;
        (string rid, string fileName) = OperatingSystem.IsWindows()
            ? (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64", "libespeak-ng.dll")
            : OperatingSystem.IsMacOS()
                ? (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64", "libespeak-ng.dylib")
                : (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64", "libespeak-ng.so");

        string bundledPath = Path.Combine(baseDir, "runtimes", rid, "native", fileName);
        if (File.Exists(bundledPath) && NativeLibrary.TryLoad(bundledPath, out IntPtr handle))
            return handle;

        string flatPath = Path.Combine(baseDir, fileName);
        if (File.Exists(flatPath) && NativeLibrary.TryLoad(flatPath, out handle))
            return handle;

        return IntPtr.Zero;
    }

    /* Initializes eSpeak NG in synchronous callback mode and returns the base sample rate */
    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int espeak_Initialize(int output, int buflength, string? path, int options);

    /* Registers the unmanaged C callback that receives synthesized 16-bit mono PCM chunks */
    [LibraryImport(LibraryName)]
    public static partial void espeak_SetSynthCallback(delegate* unmanaged[Cdecl]<short*, int, IntPtr, int> callback);

    /* Selects a voice wrapper by name (e.g. "mb-us1", "mb-es1") */
    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int espeak_SetVoiceByName(string name);

    /* Sets an engine parameter such as speaking rate (words per minute) */
    [LibraryImport(LibraryName)]
    public static partial int espeak_SetParameter(int parameter, int value, int relative);

    /* Synthesizes UTF-8 text synchronously, invoking the registered callback with PCM samples */
    [LibraryImport(LibraryName)]
    public static partial int espeak_Synth(
        byte* text,
        nuint size,
        uint position,
        int positionType,
        uint endPosition,
        uint flags,
        uint* uniqueIdentifier,
        void* userData);

    /* Queries the active MBROLA voice database's native sample frequency (16000 Hz or 22050 Hz) */
    [LibraryImport(LibraryName)]
    public static partial int getFreq_MBR();
}
