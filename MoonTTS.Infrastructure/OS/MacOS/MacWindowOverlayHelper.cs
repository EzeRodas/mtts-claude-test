using System.Runtime.InteropServices;
using MoonTTS.Domain.Interfaces;

namespace MoonTTS.Infrastructure.OS.MacOS;

/* File Structure:
        - ConfigureOverlayWindow(nint windowHandle, bool alwaysOnTop) : void
        - PrepareBeforeShow(nint mainWindowHandle, bool alwaysOnTop) : void
        - LinkChildOverlayWindow(nint childWindowHandle, nint ownerWindowHandle, bool alwaysOnTop) : void
        - ActivateOverlayWindow(nint windowHandle) : void
        - OnMainWindowDeactivated(nint mainWindowHandle, bool alwaysOnTop) : void
        - OnAfterHide(nint mainWindowHandle) : void
        - LibObjc
            - SelRegisterName(string name) : nint
            - SendCollectionBehavior(nint receiver, nint selector, nuint behavior) : void
*/

/* Overlay helper for macOS. There is no taskbar that reappears over a fullscreen app, the only requirement is that
   the window may join the fullscreen Space of another app, which NSWindowCollectionBehaviorFullScreenAuxiliary
   together with CanJoinAllSpaces allows. The on-top level itself is owned by the UI framework. */
public sealed class MacWindowOverlayHelper : IWindowOverlayHelper
{
    private const nuint CanJoinAllSpaces = 1 << 0;
    private const nuint FullScreenAuxiliary = 1 << 8;

    /* Lets the window appear over fullscreen apps regardless of alwaysOnTop, and join every Space when alwaysOnTop is set */
    public void ConfigureOverlayWindow(nint windowHandle, bool alwaysOnTop)
    {
        if (windowHandle == 0) return;

        nuint behavior = alwaysOnTop ? CanJoinAllSpaces | FullScreenAuxiliary : FullScreenAuxiliary;
        LibObjc.SendCollectionBehavior(
            windowHandle,
            LibObjc.SelRegisterName("setCollectionBehavior:"),
            behavior);
    }

    /* Nothing to capture on macOS: Spaces keep the fullscreen app and the menu bar behavior consistent on their own */
    public void PrepareBeforeShow(nint mainWindowHandle, bool alwaysOnTop)
    {
    }

    /* A secondary window needs the same collection behavior as the main window to overlay fullscreen Spaces */
    public void LinkChildOverlayWindow(nint childWindowHandle, nint ownerWindowHandle, bool alwaysOnTop) =>
        ConfigureOverlayWindow(childWindowHandle, alwaysOnTop);

    /* Activation is handled by Avalonia's Window.Activate on macOS */
    public void ActivateOverlayWindow(nint windowHandle)
    {
    }

    /* Nothing to unlink on deactivation on macOS */
    public void OnMainWindowDeactivated(nint mainWindowHandle, bool alwaysOnTop)
    {
    }

    /* Nothing to restore on macOS: closing the HUD returns focus to the fullscreen app through the system */
    public void OnAfterHide(nint mainWindowHandle)
    {
    }

    /* Objective-C runtime entry points */
    private static class LibObjc
    {
        private const string LibName = "/usr/lib/libobjc.A.dylib";

        [DllImport(LibName, EntryPoint = "sel_registerName")]
        public static extern nint SelRegisterName(string name);

        /* objc_msgSend for a selector taking one NSUInteger argument and returning nothing */
        [DllImport(LibName, EntryPoint = "objc_msgSend")]
        public static extern void SendCollectionBehavior(nint receiver, nint selector, nuint behavior);
    }
}
