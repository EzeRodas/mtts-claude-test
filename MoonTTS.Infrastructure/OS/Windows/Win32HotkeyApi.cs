using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MoonTTS.Infrastructure.OS.Windows;

/* File Structure:
        - RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk) : bool
        - UnregisterHotKey(nint hWnd, int id) : bool
        - GetMessage(out IWin32HotkeyApi.MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax) : int
        - PeekMessage(out IWin32HotkeyApi.MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg) : bool
        - PostThreadMessage(int idThread, uint Msg, nint wParam, nint lParam) : bool
        - GetCurrentThreadId() : int
        - LibWin32
*/

/* Real IWin32HotkeyApi: forwards every call to the native Windows API */
[SupportedOSPlatform("windows")]
internal sealed class Win32HotkeyApi : IWin32HotkeyApi
{
    /* Forwards to user32 RegisterHotKey */
    public bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk) =>
        LibWin32.RegisterHotKey(hWnd, id, fsModifiers, vk);

    /* Forwards to user32 UnregisterHotKey */
    public bool UnregisterHotKey(IntPtr hWnd, int id) => LibWin32.UnregisterHotKey(hWnd, id);

    /* Forwards to user32 GetMessage */
    public int GetMessage(out IWin32HotkeyApi.MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax) =>
        LibWin32.GetMessage(out lpMsg, hWnd, wMsgFilterMin, wMsgFilterMax);

    /* Forwards to user32 PeekMessage */
    public bool PeekMessage(out IWin32HotkeyApi.MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg) =>
        LibWin32.PeekMessage(out lpMsg, hWnd, wMsgFilterMin, wMsgFilterMax, wRemoveMsg);

    /* Forwards to user32 PostThreadMessage */
    public bool PostThreadMessage(int idThread, uint Msg, IntPtr wParam, IntPtr lParam) =>
        LibWin32.PostThreadMessage(idThread, Msg, wParam, lParam);

    /* Forwards to the native GetCurrentThreadId */
    public int GetCurrentThreadId() => LibWin32.GetCurrentThreadId();

    /* Native C code that Windows expects to talk to */
    /* https://learn.microsoft.com/en-us/windows/win32/api/winuser/ , mainly RegisterHotKey */

    private static class LibWin32
    {
        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern int GetMessage(out IWin32HotkeyApi.MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        public static extern bool PeekMessage(
            out IWin32HotkeyApi.MSG lpMsg,
            IntPtr hWnd,
            uint wMsgFilterMin,
            uint wMsgFilterMax,
            uint wRemoveMsg
            );

        [DllImport("user32.dll")]
        public static extern bool PostThreadMessage(int idThread, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern int GetCurrentThreadId();
    }
}
