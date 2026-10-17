#if !WINDOWS
using System.Runtime.InteropServices;

namespace MoonTTS.Infrastructure.Audio;

/* File Structure:
        - CFStringToString(this ICoreAudioApi api, nint cfString) : string?
        - StringToCFString(this ICoreAudioApi api, string str) : nint
*/

/* Marshaling helpers */

/* Converts between .NET strings and CoreFoundation CFStrings through an ICoreAudioApi */
public static class MacCoreFoundationStrings
{
    /* Copies a CFString into a .NET string and releases it (null for a null CFString) */
    public static string? CFStringToString(this ICoreAudioApi api, IntPtr cfString)
    {
        if (cfString == IntPtr.Zero) return null;
        try
        {
            nint len = api.CFStringGetLenght(cfString);
            if (len == 0) return string.Empty;

            char[] buffer = new char[len];
            GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                api.CFStringGetCharacters(
                    cfString,
                    new ICoreAudioApi.CFRange { Location = 0, Lenght = len },
                    handle.AddrOfPinnedObject());
                return new string(buffer);
            }
            finally
            {
                handle.Free();
            }
        }
        finally
        {
            api.CFRelease(cfString);
        }
    }

    /* Creates a CFString (owned by the caller, who must CFRelease it) */
    public static IntPtr StringToCFString(this ICoreAudioApi api, string str)
    {
        return api.CFStringCreateWithCharacters(IntPtr.Zero, str, str.Length);
    }
}
#endif
