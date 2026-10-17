using System.Runtime.InteropServices;

namespace MoonTTS.Infrastructure.OS.Windows;

/* File Structure:
        - WM_QUIT : uint
        - WM_HOTKEY : uint
        - WM_WAKEUP : uint
        - MOD_ALT : uint
        - MOD_CONTROL : uint
        - MOD_SHIFT : uint
        - MOD_WIN : uint
        - MOD_NOREPEAT : uint
        - MSG
        - RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk) : bool
        - UnregisterHotKey(nint hWnd, int id) : bool
        - GetMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax) : int
        - PeekMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg) : bool
        - PostThreadMessage(int idThread, uint Msg, nint wParam, nint lParam) : bool
        - GetCurrentThreadId() : int
*/

/* The Win32 calls WindowsShortcutHook makes, so the hook can run against the real user32 (Win32HotkeyApi)
   or against a dummy that answers like Windows would */
public interface IWin32HotkeyApi
{
    /* Message ids */
    public const uint WM_QUIT = 0x0012;
    public const uint WM_HOTKEY = 0x0312;
    public const uint WM_WAKEUP = 0x8001;

    /* RegisterHotKey modifier flags */
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    /* Win32 MSG structure filled by GetMessage / PeekMessage */
    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int pt_x;
        public int pt_y;
    }

    /* Registers a system-wide hotkey for the calling thread (hWnd = 0) */
    bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    /* Releases a hotkey previously registered by the calling thread */
    bool UnregisterHotKey(IntPtr hWnd, int id);

    /* Blocks until the calling thread's queue has a message. Returns 0 for WM_QUIT, -1 on error */
    int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    /* Checks the calling thread's queue without blocking (and creates the queue if it does not exist yet) */
    bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    /* Posts a message to another thread's queue. Fails when that thread has no queue */
    bool PostThreadMessage(int idThread, uint Msg, IntPtr wParam, IntPtr lParam);

    /* Id of the calling OS thread */
    int GetCurrentThreadId();
}
