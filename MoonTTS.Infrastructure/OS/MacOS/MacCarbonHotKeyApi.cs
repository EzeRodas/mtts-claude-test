using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MoonTTS.Infrastructure.OS.MacOS;

/* File Structure:
        - InstallApplicationEventHandler(...) : int
        - RemoveEventHandler(nint inHandlerRef) : int
        - GetApplicationEventTarget() : nint
        - RegisterEventHotKey(...) : int
        - UnregisterEventHotKey(nint inHotKeyRef) : int
        - GetEventParameter(...) : int
        - LibMac
            - CarbonLib : string
            - (one extern per method above)
*/

/* Real IMacCarbonHotKeyApi: forwards every call to the Carbon framework */
[SupportedOSPlatform("macos")]
internal sealed class MacCarbonHotKeyApi : IMacCarbonHotKeyApi
{
    /* Forwards to Carbon InstallApplicationEventHandler */
    public int InstallApplicationEventHandler(
        IMacCarbonHotKeyApi.EventHandlerProcPtr inHandler,
        uint inNumTypes,
        IMacCarbonHotKeyApi.EventTypeSpec[] inList,
        IntPtr inUserData,
        out IntPtr outRef) =>
        LibMac.InstallApplicationEventHandler(inHandler, inNumTypes, inList, inUserData, out outRef);

    /* Forwards to Carbon RemoveEventHandler */
    public int RemoveEventHandler(IntPtr inHandlerRef) => LibMac.RemoveEventHandler(inHandlerRef);

    /* Forwards to Carbon GetApplicationEventTarget */
    public IntPtr GetApplicationEventTarget() => LibMac.GetApplicationEventTarget();

    /* Forwards to Carbon RegisterEventHotKey */
    public int RegisterEventHotKey(
        uint inHotKeyCode,
        uint inHotKeyModifiers,
        IMacCarbonHotKeyApi.EventHotKeyID inHotKeyID,
        IntPtr inTarget,
        uint inOptions,
        out IntPtr otRef) =>
        LibMac.RegisterEventHotKey(inHotKeyCode, inHotKeyModifiers, inHotKeyID, inTarget, inOptions, out otRef);

    /* Forwards to Carbon UnregisterEventHotKey */
    public int UnregisterEventHotKey(IntPtr inHotKeyRef) => LibMac.UnregisterEventHotKey(inHotKeyRef);

    /* Forwards to Carbon GetEventParameter */
    public int GetEventParameter(
        IntPtr inEvent,
        uint inName,
        uint inDesiredType,
        IntPtr outActualType,
        uint inBufferSize,
        IntPtr outActualSize,
        out IMacCarbonHotKeyApi.EventHotKeyID outData) =>
        LibMac.GetEventParameter(inEvent, inName, inDesiredType, outActualType, inBufferSize, outActualSize, out outData);

    /* Native C code that Mac expects to talk to */
    /* This is where I would link to you Apple's docs on this. IF THEY WEREN'T ERASED FROM THEIR SITE */
    /* Instead, I adapted HotKey's approach, here's their github it was really useful https://github.com/soffes/HotKey */

    private static class LibMac
    {
        private const string CarbonLib = "/System/Library/Frameworks/Carbon.framework/Versions/Current/Carbon";

	    /* Injects a C# function into the OS event pipeline */
	    [DllImport(CarbonLib)]
	    public static extern int InstallApplicationEventHandler(
	        IMacCarbonHotKeyApi.EventHandlerProcPtr inHandler,
	        uint inNumTypes,
	        IMacCarbonHotKeyApi.EventTypeSpec[] inList,
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
		    IMacCarbonHotKeyApi.EventHotKeyID inHotKeyID,
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
		    out IMacCarbonHotKeyApi.EventHotKeyID outData);

    }
}
