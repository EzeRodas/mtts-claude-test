using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using MoonTTS.Domain.Interfaces;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.Infrastructure.OS.Windows;

public class WindowsShortcutHook : IGlobalShortcutHook
{
    private Thread? _eventThread;
    private int _threadId;
    private readonly ManualResetEvent _readyEvent = new(false);
    
    /* Queue so the UI thread can send registration commands to the background thread */
    private readonly ConcurrentQueue<Action> _dispatchQueue = new();

    /* Maps the Win32 hotkey ID to a shortcut string */
    private readonly Dictionary<int, string> _registeredCombos = new();
    private int _nextHotkeyId = 1;

    public event Action<string>? OnShortcutTriggered;

    public Task InitializeAsync()
    {
        _eventThread = new Thread(EventLoop) {IsBackground = true};
        _eventThread.Start();
        
        /* Pauses until the background thred finished creating its OS message queue */
        _readyEvent.WaitOne();
        
        return Task.CompletedTask;
    }

    public Task RegisterShortcutAsync(ShortcutCombo? toggleShortcut, Dictionary<string, ShortcutCombo> phraseShortcuts)
    {
        /* Queues the task for the background thread */
        _dispatchQueue.Enqueue(() =>
        {
            /* Unregisters old keys */
            foreach (var id in _registeredCombos.Keys)
            {
                LibWin32.UnregisterHotKey(IntPtr.Zero, id);
            }
            _registeredCombos.Clear();
            _nextHotkeyId = 1;
            
            /* Registers the main toggle shortcut */
            if (toggleShortcut != null) RegisterSingleShortcut("toggle_app", toggleShortcut);

            /* Registers the rest of the custom shortcuts */
            foreach (var kvp in phraseShortcuts)
            {
                RegisterSingleShortcut($"phrase_{kvp.Key}", kvp.Value);
            }
        });
        
        /* Wakes up the bg thread to process our queue */
        LibWin32.PostThreadMessage(_threadId, LibWin32.WM_WAKEUP, IntPtr.Zero, IntPtr.Zero);
        
        return Task.CompletedTask;
    }

    private void RegisterSingleShortcut(string id, ShortcutCombo combo)
    {
        /* Prevents hotkey spamming */
        uint modifiers = LibWin32.MOD_NOREPEAT;

        if (combo.HasCtrl) modifiers |= LibWin32.MOD_CONTROL;
        if (combo.HasAlt) modifiers |= LibWin32.MOD_ALT;
        if (combo.HasShift) modifiers |= LibWin32.MOD_SHIFT;
        if (combo.HasWin) modifiers |= LibWin32.MOD_WIN;

        uint vk = 0;
        if (combo.MainKey.Length == 1)
        {
            /* Win32 requires the uppercase letter for virtual keys */
            vk = (uint)combo.MainKey[0];
        }
        else
        {
            return;
        }
        
        int hotkeyId = _nextHotkeyId++;

        if (LibWin32.RegisterHotKey(IntPtr.Zero, hotkeyId, modifiers, vk))
        {
            _registeredCombos[hotkeyId] = id;
        }

    }

    /* Listener waiting for activating or registering hotkeys */
    private void EventLoop()
    {
        _threadId = LibWin32.GetCurrentThreadId();
        
        /* Forces Windows to create a Message Queue for this thread */
        LibWin32.PeekMessage(out _, IntPtr.Zero, 0,0,0);
        _readyEvent.Set();

        LibWin32.MSG msg;

        /* GetMessage freezes the thread until windows sends a message */
        while (LibWin32.GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
        {
            /* Case if Windows notifies that a shortcut has been pressed */
            if (msg.message == LibWin32.WM_HOTKEY)
            {
                int hotkeyId = (int)msg.wParam;
                if (_registeredCombos.TryGetValue(hotkeyId, out string? shortcutId))
                {
                    OnShortcutTriggered?.Invoke(shortcutId);
                }
            } else if (msg.message == LibWin32.WM_WAKEUP)
            {
                /* Case if the UI sends something to register */
                while (_dispatchQueue.TryDequeue(out var action))
                {
                    action();
                }
            }
        }
    }

    public void Dispose()
    {
        LibWin32.PostThreadMessage(_threadId, LibWin32.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }
    
    /* Native C code that Windows expects to talk to */
    /* https://learn.microsoft.com/en-us/windows/win32/api/winuser/ , mainly RegisterHotKey */

    private static class LibWin32
    {
        public const uint WM_QUIT = 0x0012;
        public const uint WM_HOTKEY = 0x0312;
        public const uint WM_WAKEUP = 0x8001;
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL= 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;
        
        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        
        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
        
        [DllImport("user32.dll")]
        public static extern bool PeekMessage(
            out MSG lpMsg, 
            IntPtr hWnd, 
            uint wMsgFilterMin, 
            uint wMsgFilterMax, 
            uint wRemoveMsg
            );
        
        [DllImport("user32.dll")]
        public static extern bool PostThreadMessage(int idThread, uint Msg, IntPtr wParam, IntPtr lParam);
        
        [DllImport("user32.dll")]
        public static extern int GetCurrentThreadId();

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
    }
}