using MoonTTS.Infrastructure.OS.Windows;

namespace MoonTTS.Infrastructure.Tests.OS.Windows;

/* File Structure:
        - RegisteredHotkey(int Id, uint Modifiers, uint VirtualKey, int OwnerThreadId)
        - HotkeyCall(int Id, uint Modifiers, uint VirtualKey, int ThreadId)
        - RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk) : bool
        - UnregisterHotKey(nint hWnd, int id) : bool
        - GetMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax) : int
        - PeekMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg) : bool
        - PostThreadMessage(int idThread, uint Msg, nint wParam, nint lParam) : bool
        - GetCurrentThreadId() : int
        - ReserveForAnotherApplication(uint modifiers, uint virtualKey) : void
        - FailNextGetMessage() : void
        - Press(uint modifiers, uint virtualKey) : bool
        - WaitUntilIdle(TimeSpan timeout) : bool
        - WaitForLoopExit(TimeSpan timeout) : bool
        - HasMessageQueue(int threadId) : bool
*/

/* IWin32HotkeyApi that behaves like user32:
   - every OS thread gets an id and a message queue (created by PeekMessage / GetMessage)
   - PostThreadMessage fails for a thread without a queue
   - GetMessage blocks until a message arrives and returns 0 for WM_QUIT
   - RegisterHotKey(hWnd = 0) binds the hotkey to the calling thread and rejects a combo that is already taken
   - pressing a registered combo posts WM_HOTKEY (wParam = id) to the owning thread's queue */
public sealed class DummyWin32HotkeyApi : IWin32HotkeyApi
{
    /* user32 message ids and modifier flags, written out like the Windows SDK defines them */
    public const uint WmQuit = 0x0012;
    public const uint WmHotkey = 0x0312;
    public const uint ModNoRepeat = 0x4000;

    /* A hotkey currently held by a thread */
    public sealed record RegisteredHotkey(int Id, uint Modifiers, uint VirtualKey, int OwnerThreadId);

    /* One RegisterHotKey / UnregisterHotKey call */
    public sealed record HotkeyCall(int Id, uint Modifiers, uint VirtualKey, int ThreadId);

    private sealed class MessageQueue
    {
        public readonly Queue<IWin32HotkeyApi.MSG> Messages = new();
        public bool Waiting;
    }

    /* Plain object (not System.Threading.Lock) because the queues use Monitor.Wait / PulseAll */
    private readonly object _gate = new();
    private readonly Dictionary<int, MessageQueue> _queues = new();
    private readonly List<RegisteredHotkey> _hotkeys = new();
    private readonly HashSet<(uint Modifiers, uint VirtualKey)> _otherApplications = new();
    private readonly List<HotkeyCall> _registerCalls = new();
    private readonly List<HotkeyCall> _unregisterCalls = new();
    private readonly ThreadLocal<int> _threadIds;
    private readonly ManualResetEventSlim _loopExited = new(false);
    private int _lastThreadId = 1000;
    private bool _failNextGetMessage;

    /* Id of the last thread that created its queue with PeekMessage (the hook's message loop) */
    public int MessageLoopThreadId { get; private set; }

    /* True when the message loop runs on a background thread (it must not keep the app alive) */
    public bool MessageLoopIsBackgroundThread { get; private set; }

    /* Hotkeys currently registered */
    public IReadOnlyList<RegisteredHotkey> RegisteredHotkeys
    {
        get { lock (_gate) return _hotkeys.ToList(); }
    }

    /* Every RegisterHotKey call, successful or not, oldest first */
    public IReadOnlyList<HotkeyCall> RegisterCalls
    {
        get { lock (_gate) return _registerCalls.ToList(); }
    }

    /* Every UnregisterHotKey call, oldest first */
    public IReadOnlyList<HotkeyCall> UnregisterCalls
    {
        get { lock (_gate) return _unregisterCalls.ToList(); }
    }

    /* Gives every managed thread its own fake OS thread id */
    public DummyWin32HotkeyApi()
    {
        _threadIds = new ThreadLocal<int>(() => Interlocked.Increment(ref _lastThreadId));
    }

    /* Registers a thread hotkey; fails like Windows (ERROR_HOTKEY_ALREADY_REGISTERED) when the combo is taken */
    public bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk)
    {
        int threadId = GetCurrentThreadId();
        lock (_gate)
        {
            _registerCalls.Add(new HotkeyCall(id, fsModifiers, vk, threadId));
            if (hWnd != IntPtr.Zero)
                return false;

            uint modifiers = fsModifiers & ~ModNoRepeat;
            bool taken = _otherApplications.Contains((modifiers, vk)) ||
                         _hotkeys.Any(h => (h.Modifiers & ~ModNoRepeat) == modifiers && h.VirtualKey == vk);
            if (taken)
                return false;

            _hotkeys.Add(new RegisteredHotkey(id, fsModifiers, vk, threadId));
            return true;
        }
    }

    /* Releases a hotkey of the calling thread; fails (ERROR_HOTKEY_NOT_REGISTERED) for an unknown id */
    public bool UnregisterHotKey(IntPtr hWnd, int id)
    {
        int threadId = GetCurrentThreadId();
        lock (_gate)
        {
            _unregisterCalls.Add(new HotkeyCall(id, 0, 0, threadId));
            int removed = _hotkeys.RemoveAll(h => h.Id == id && h.OwnerThreadId == threadId);
            return removed > 0;
        }
    }

    /* Blocks until the calling thread's queue has a message; WM_QUIT returns 0 and destroys the queue */
    public int GetMessage(out IWin32HotkeyApi.MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax)
    {
        int threadId = GetCurrentThreadId();
        lock (_gate)
        {
            var queue = GetOrCreateQueue(threadId);
            while (true)
            {
                if (_failNextGetMessage)
                {
                    _failNextGetMessage = false;
                    lpMsg = default;
                    SignalLoopExit(threadId);
                    return -1;
                }
                if (queue.Messages.Count > 0)
                    break;

                queue.Waiting = true;
                Monitor.PulseAll(_gate);
                Monitor.Wait(_gate);
            }
            queue.Waiting = false;

            lpMsg = queue.Messages.Dequeue();
            if (lpMsg.message == WmQuit)
            {
                SignalLoopExit(threadId);
                return 0;
            }
            return 1;
        }
    }

    /* Creates the calling thread's queue and reports (without removing) its first message */
    public bool PeekMessage(out IWin32HotkeyApi.MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg)
    {
        int threadId = GetCurrentThreadId();
        lock (_gate)
        {
            var queue = GetOrCreateQueue(threadId);
            MessageLoopThreadId = threadId;
            MessageLoopIsBackgroundThread = Thread.CurrentThread.IsBackground;

            if (queue.Messages.Count > 0)
            {
                lpMsg = queue.Messages.Peek();
                return true;
            }
            lpMsg = default;
            return false;
        }
    }

    /* Appends a message to a thread's queue; fails (ERROR_INVALID_THREAD_ID) when that thread has no queue */
    public bool PostThreadMessage(int idThread, uint Msg, IntPtr wParam, IntPtr lParam)
    {
        lock (_gate)
        {
            if (!_queues.TryGetValue(idThread, out var queue))
                return false;

            queue.Messages.Enqueue(new IWin32HotkeyApi.MSG { message = Msg, wParam = wParam, lParam = lParam });
            Monitor.PulseAll(_gate);
            return true;
        }
    }

    /* Fake OS id of the calling thread */
    public int GetCurrentThreadId() => _threadIds.Value;

    /* Simulates another program already holding a combo */
    public void ReserveForAnotherApplication(uint modifiers, uint virtualKey)
    {
        lock (_gate) _otherApplications.Add((modifiers, virtualKey));
    }

    /* Makes the next GetMessage call fail with -1 (e.g. an invalid window handle) */
    public void FailNextGetMessage()
    {
        lock (_gate)
        {
            _failNextGetMessage = true;
            Monitor.PulseAll(_gate);
        }
    }

    /* Simulates the user pressing a combo: posts WM_HOTKEY to the owner thread. False when nobody registered it */
    public bool Press(uint modifiers, uint virtualKey)
    {
        lock (_gate)
        {
            var hotkey = _hotkeys.FirstOrDefault(h =>
                (h.Modifiers & ~ModNoRepeat) == (modifiers & ~ModNoRepeat) && h.VirtualKey == virtualKey);
            if (hotkey == null || !_queues.TryGetValue(hotkey.OwnerThreadId, out var queue))
                return false;

            queue.Messages.Enqueue(new IWin32HotkeyApi.MSG
            {
                message = WmHotkey,
                wParam = hotkey.Id,
                lParam = (IntPtr)((virtualKey << 16) | (modifiers & 0xFFFF))
            });
            Monitor.PulseAll(_gate);
            return true;
        }
    }

    /* Waits until the message loop has processed everything posted so far and is blocked in GetMessage again */
    public bool WaitUntilIdle(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        lock (_gate)
        {
            while (true)
            {
                if (_queues.TryGetValue(MessageLoopThreadId, out var queue) && queue.Waiting && queue.Messages.Count == 0)
                    return true;

                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                    return false;
                Monitor.Wait(_gate, remaining);
            }
        }
    }

    /* Waits until a GetMessage call ended the message loop (WM_QUIT or error) */
    public bool WaitForLoopExit(TimeSpan timeout) => _loopExited.Wait(timeout);

    /* True while the thread has a message queue */
    public bool HasMessageQueue(int threadId)
    {
        lock (_gate) return _queues.ContainsKey(threadId);
    }

    /* Returns (creating it on first use) the queue of a thread. Caller holds the lock */
    private MessageQueue GetOrCreateQueue(int threadId)
    {
        if (!_queues.TryGetValue(threadId, out var queue))
        {
            queue = new MessageQueue();
            _queues[threadId] = queue;
        }
        return queue;
    }

    /* The thread leaves its loop: its queue is destroyed. Caller holds the lock */
    private void SignalLoopExit(int threadId)
    {
        _queues.Remove(threadId);
        _loopExited.Set();
        Monitor.PulseAll(_gate);
    }
}
