using System.Collections.Concurrent;
using System.Runtime.Versioning;
using MoonTTS.Domain.Interfaces;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.Infrastructure.OS.Windows;

/* File Structure:
        - WindowsShortcutHook()
        - WindowsShortcutHook(IWin32HotkeyApi win32)
        - InitializeAsync() : Task
        - RegisterShortcutAsync(ShortcutCombo? toggleShortcut, Dictionary<string, ShortcutCombo> phraseShortcuts) : Task
        - RegisterSingleShortcut(string id, ShortcutCombo combo) : void
        - EventLoop() : void
        - Dispose() : void
*/

public class WindowsShortcutHook : IGlobalShortcutHook
{
    /* Native Win32 surface (the real user32 in the app, a dummy in tests) */
    private readonly IWin32HotkeyApi _win32;

    private Thread? _eventThread;
    private int _threadId;
    private readonly ManualResetEvent _readyEvent = new(false);
    
    /* Queue so the UI thread can send registration commands to the background thread */
    private readonly ConcurrentQueue<Action> _dispatchQueue = new();

    /* Maps the Win32 hotkey ID to a shortcut string */
    private readonly Dictionary<int, string> _registeredCombos = new();
    private int _nextHotkeyId = 1;

    public event Action<string>? OnShortcutTriggered;

    /* Talks to the real Windows API */
    [SupportedOSPlatform("windows")]
    public WindowsShortcutHook() : this(new Win32HotkeyApi())
    {
    }

    /* Talks to the given Win32 API implementation */
    public WindowsShortcutHook(IWin32HotkeyApi win32)
    {
        _win32 = win32;
    }

    /* Starts the background message-loop thread and waits until its message queue exists */
    public Task InitializeAsync()
    {
        _eventThread = new Thread(EventLoop) {IsBackground = true};
        _eventThread.Start();
        
        /* Pauses until the background thred finished creating its OS message queue */
        _readyEvent.WaitOne();
        
        return Task.CompletedTask;
    }

    /* Replaces every registered hotkey; the work runs on the message-loop thread, which owns the hotkeys */
    public Task RegisterShortcutAsync(ShortcutCombo? toggleShortcut, Dictionary<string, ShortcutCombo> phraseShortcuts)
    {
        /* Queues the task for the background thread */
        _dispatchQueue.Enqueue(() =>
        {
            /* Unregisters old keys */
            foreach (var id in _registeredCombos.Keys)
            {
                _win32.UnregisterHotKey(IntPtr.Zero, id);
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
        _win32.PostThreadMessage(_threadId, IWin32HotkeyApi.WM_WAKEUP, IntPtr.Zero, IntPtr.Zero);
        
        return Task.CompletedTask;
    }

    /* Translates a combo into Win32 modifiers + virtual key and registers it on the calling (message-loop) thread */
    private void RegisterSingleShortcut(string id, ShortcutCombo combo)
    {
        /* Prevents hotkey spamming */
        uint modifiers = IWin32HotkeyApi.MOD_NOREPEAT;

        if (combo.HasCtrl) modifiers |= IWin32HotkeyApi.MOD_CONTROL;
        if (combo.HasAlt) modifiers |= IWin32HotkeyApi.MOD_ALT;
        if (combo.HasShift) modifiers |= IWin32HotkeyApi.MOD_SHIFT;
        if (combo.HasWin) modifiers |= IWin32HotkeyApi.MOD_WIN;

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

        if (_win32.RegisterHotKey(IntPtr.Zero, hotkeyId, modifiers, vk))
        {
            _registeredCombos[hotkeyId] = id;
        }

    }

    /* Listener waiting for activating or registering hotkeys */
    private void EventLoop()
    {
        _threadId = _win32.GetCurrentThreadId();
        
        /* Forces Windows to create a Message Queue for this thread */
        _win32.PeekMessage(out _, IntPtr.Zero, 0,0,0);
        _readyEvent.Set();

        IWin32HotkeyApi.MSG msg;

        /* GetMessage freezes the thread until windows sends a message */
        while (_win32.GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
        {
            /* Case if Windows notifies that a shortcut has been pressed */
            if (msg.message == IWin32HotkeyApi.WM_HOTKEY)
            {
                int hotkeyId = (int)msg.wParam;
                if (_registeredCombos.TryGetValue(hotkeyId, out string? shortcutId))
                {
                    OnShortcutTriggered?.Invoke(shortcutId);
                }
            } else if (msg.message == IWin32HotkeyApi.WM_WAKEUP)
            {
                /* Case if the UI sends something to register */
                while (_dispatchQueue.TryDequeue(out var action))
                {
                    action();
                }
            }
        }
    }

    /* Asks the message-loop thread to quit */
    public void Dispose()
    {
        _win32.PostThreadMessage(_threadId, IWin32HotkeyApi.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }
}
