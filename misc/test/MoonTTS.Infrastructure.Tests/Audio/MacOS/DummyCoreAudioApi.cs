#if !WINDOWS
using System.Runtime.InteropServices;
using MoonTTS.Infrastructure.Audio;

namespace MoonTTS.Infrastructure.Tests.Audio.MacOS;

/* File Structure:
        - FourCC(string code) : uint
        - DummyAudioDevice
        - DummyAudioQueue
        - Devices : List<DummyAudioDevice>
        - Queues : IReadOnlyList<DummyAudioQueue>
        - AddDevice(uint id, string name, string uid, uint outputChannels) : DummyAudioDevice
        - CreateString(string value) : nint
        - WaitForStartedQueuesAsync(int count, CancellationToken cancellationToken) : Task<IReadOnlyList<DummyAudioQueue>>
        - CompleteAsync(DummyAudioQueue queue) : Task
        - (one method per ICoreAudioApi member)
        - Dispose() : void
*/

/* ICoreAudioApi that behaves like CoreAudio + AudioToolbox + CoreFoundation:
   - the system object lists device ids; each device reports its output stream configuration, name and UID
     (as CFStrings the caller owns)
   - AudioQueueNewOutput validates the linear-PCM description, AudioQueueAllocateBuffer hands out real
     AudioQueueBuffer memory, AudioQueueEnqueueBuffer copies mAudioDataByteSize bytes out of it
   - after AudioQueueStart the queue reports every enqueued buffer as played through its output callback on another
     thread (AutoComplete), or when the test calls CompleteAsync
   - the current-device property only accepts the UID of an existing device
   - CFStrings are reference counted; releasing a dead one throws like a CoreFoundation crash */
public sealed class DummyCoreAudioApi : ICoreAudioApi, IDisposable
{
    /* OSStatus codes */
    public const int NoErr = 0;
    public const int ParamErr = -50;
    public static readonly int UnknownPropertyError = (int)FourCC("who?");
    public static readonly int BadObjectError = (int)FourCC("!obj");
    public static readonly int BadPropertySizeError = (int)FourCC("!siz");
    public static readonly int UnsupportedDataFormatError = (int)FourCC("fmt?");
    public const int QueueInvalidBufferError = -66687;
    public const int QueueBufferEmptyError = -66686;
    public const int QueueInvalidPropertyError = -66684;
    public const int QueueInvalidPropertySizeError = -66683;
    public const int QueueInvalidParameterError = -66682;
    public const int QueueInvalidDeviceError = -66680;

    /* A CoreAudio device */
    public sealed class DummyAudioDevice
    {
        public required uint Id { get; init; }
        public required string Name { get; init; }
        public required string Uid { get; init; }
        public uint OutputChannels { get; init; }
        public bool NameUnavailable { get; set; }
        public bool UidUnavailable { get; set; }
        public bool StreamConfigurationUnavailable { get; set; }
    }

    /* An AudioQueue created by the code under test */
    public sealed class DummyAudioQueue
    {
        public required IntPtr Handle { get; init; }
        public required ICoreAudioApi.AudioStreamBasicDescription Format { get; init; }
        public required ICoreAudioApi.AudioQueueOutputCallback Callback { get; init; }
        public List<string> RequestedDeviceUids { get; } = new();
        public string? DeviceUid { get; set; }
        public List<float> Volumes { get; } = new();
        public List<IntPtr> AllocatedBuffers { get; } = new();
        public List<(IntPtr Buffer, byte[] Pcm)> EnqueuedBuffers { get; } = new();
        public int StartCount { get; set; }
        public int PauseCount { get; set; }
        public bool IsPaused { get; set; }
        public bool Stopped { get; set; }
        public bool StoppedImmediately { get; set; }
        public bool Disposed { get; set; }
        public bool CompletionScheduled { get; set; }

        /* Last volume set on the queue */
        public float? Volume => Volumes.Count > 0 ? Volumes[^1] : null;
    }

    private sealed class CFStringEntry
    {
        public required string Value { get; init; }
        public int RetainCount { get; set; } = 1;
    }

    private readonly object _gate = new();
    private readonly List<DummyAudioQueue> _queues = new();
    private readonly Dictionary<IntPtr, CFStringEntry> _strings = new();
    private readonly List<IntPtr> _allocations = new();
    private TaskCompletionSource _stateChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _nextHandle = 0x5000;
    private int _newOutputCalls;

    public List<DummyAudioDevice> Devices { get; } = new();

    /* When true every AudioQueueStart schedules the "buffer played" callbacks right away */
    public bool AutoComplete { get; set; } = true;

    public bool FailDeviceListSize { get; set; }
    public bool FailDeviceListData { get; set; }
    public Exception? ThrowOnDeviceList { get; set; }

    /* 1-based AudioQueueNewOutput calls that fail with UnsupportedDataFormatError */
    public HashSet<int> FailingNewOutputCalls { get; } = new();
    public bool FailAllocateBuffer { get; set; }

    /* Calls made on a queue that does not exist (or was already disposed) */
    public int InvalidQueueCalls { get; private set; }

    /* Queues created so far, oldest first */
    public IReadOnlyList<DummyAudioQueue> Queues
    {
        get { lock (_gate) return _queues.ToList(); }
    }

    /* CFStrings still alive (created or handed out but not released) */
    public int LiveStringCount
    {
        get { lock (_gate) return _strings.Count; }
    }

    /* Builds a four-character code ("dev#" -> 0x64657623) */
    public static uint FourCC(string code) =>
        (uint)code[0] << 24 | (uint)code[1] << 16 | (uint)code[2] << 8 | code[3];

    /* Adds an output (or input-only when outputChannels is 0) device */
    public DummyAudioDevice AddDevice(uint id, string name, string uid, uint outputChannels = 2)
    {
        var device = new DummyAudioDevice { Id = id, Name = name, Uid = uid, OutputChannels = outputChannels };
        Devices.Add(device);
        return device;
    }

    /* Creates a CFString owned by the caller (retain count 1) */
    public IntPtr CreateString(string value)
    {
        lock (_gate)
        {
            IntPtr handle = NewHandle();
            _strings[handle] = new CFStringEntry { Value = value };
            return handle;
        }
    }

    /* Completes once at least `count` queues have been started */
    public async Task<IReadOnlyList<DummyAudioQueue>> WaitForStartedQueuesAsync(int count, CancellationToken cancellationToken)
    {
        while (true)
        {
            Task changed;
            lock (_gate)
            {
                var started = _queues.Where(q => q.StartCount > 0).ToList();
                if (started.Count >= count)
                    return started;
                changed = _stateChanged.Task;
            }
            await changed.WaitAsync(cancellationToken);
        }
    }

    /* Reports every enqueued buffer of the queue as played, from a CoreAudio-like worker thread */
    public Task CompleteAsync(DummyAudioQueue queue) => Task.Run(() => FireCallbacks(queue));

    /* System object: device list size. Devices: output stream configuration size */
    public int AudioObjectGetPropertyDataSize(uint inObjectID, ref ICoreAudioApi.AudioObjectPropertyAddress inAddress,
        uint inQualifierDataSize, IntPtr inQualifierData, out uint outDataSize)
    {
        outDataSize = 0;
        if (inObjectID == 1 && inAddress.mSelector == FourCC("dev#"))
        {
            if (ThrowOnDeviceList != null) throw ThrowOnDeviceList;
            if (FailDeviceListSize) return UnknownPropertyError;
            outDataSize = (uint)(Devices.Count * sizeof(uint));
            return NoErr;
        }

        var device = Devices.FirstOrDefault(d => d.Id == inObjectID);
        if (device == null)
            return BadObjectError;

        if (inAddress.mSelector == FourCC("slpt") && inAddress.mScope == FourCC("outp"))
        {
            if (device.StreamConfigurationUnavailable) return UnknownPropertyError;
            /* An AudioBufferList with no buffers is only its 8-byte header */
            outDataSize = device.OutputChannels > 0
                ? (uint)Marshal.SizeOf<ICoreAudioApi.AudioBufferList>()
                : (uint)Marshal.OffsetOf<ICoreAudioApi.AudioBufferList>(nameof(ICoreAudioApi.AudioBufferList.mBuffers));
            return NoErr;
        }
        return UnknownPropertyError;
    }

    /* System object: device ids. Devices: output stream configuration */
    public int AudioObjectGetPropertyData(uint inObjectID, ref ICoreAudioApi.AudioObjectPropertyAddress inAddress,
        uint inQualifierDataSize, IntPtr inQualifierData, ref uint ioDataSize, IntPtr outData)
    {
        if (inObjectID == 1 && inAddress.mSelector == FourCC("dev#"))
        {
            if (FailDeviceListData) return UnknownPropertyError;
            int count = Math.Min((int)(ioDataSize / sizeof(uint)), Devices.Count);
            for (int i = 0; i < count; i++)
                Marshal.WriteInt32(outData, i * sizeof(uint), (int)Devices[i].Id);
            ioDataSize = (uint)(count * sizeof(uint));
            return NoErr;
        }

        var device = Devices.FirstOrDefault(d => d.Id == inObjectID);
        if (device == null)
            return BadObjectError;

        if (inAddress.mSelector == FourCC("slpt") && inAddress.mScope == FourCC("outp"))
        {
            int header = (int)Marshal.OffsetOf<ICoreAudioApi.AudioBufferList>(nameof(ICoreAudioApi.AudioBufferList.mBuffers));
            int needed = device.OutputChannels > 0 ? Marshal.SizeOf<ICoreAudioApi.AudioBufferList>() : header;
            if (ioDataSize < needed) return BadPropertySizeError;

            Marshal.WriteInt32(outData, 0, device.OutputChannels > 0 ? 1 : 0);
            if (device.OutputChannels > 0)
            {
                Marshal.WriteInt32(outData, header, (int)device.OutputChannels);
                Marshal.WriteInt32(outData, header + sizeof(uint), 4096);
                Marshal.WriteIntPtr(outData, header + 2 * sizeof(uint), IntPtr.Zero);
            }
            ioDataSize = (uint)needed;
            return NoErr;
        }
        return UnknownPropertyError;
    }

    /* Devices: name and UID, returned as CFStrings the caller must release */
    public int AudioObjectGetPropertyData(uint inObjectID, ref ICoreAudioApi.AudioObjectPropertyAddress inAddress,
        uint inQualifierDataSize, IntPtr inQualifierData, ref uint ioDataSize, out IntPtr outData)
    {
        outData = IntPtr.Zero;
        var device = Devices.FirstOrDefault(d => d.Id == inObjectID);
        if (device == null)
            return BadObjectError;
        if (ioDataSize < IntPtr.Size)
            return BadPropertySizeError;

        if (inAddress.mSelector == FourCC("lnam") && !device.NameUnavailable)
            outData = CreateString(device.Name);
        else if (inAddress.mSelector == FourCC("uid ") && !device.UidUnavailable)
            outData = CreateString(device.Uid);
        else
            return UnknownPropertyError;

        ioDataSize = (uint)IntPtr.Size;
        return NoErr;
    }

    /* Creates a queue for a valid packed linear-PCM description */
    public int AudioQueueNewOutput(ref ICoreAudioApi.AudioStreamBasicDescription inFormat,
        ICoreAudioApi.AudioQueueOutputCallback inCallBackProc, IntPtr inUserData, IntPtr inCallbackRunLoop,
        IntPtr inCallbackRunLoopMode, uint inFlags, out IntPtr outAQ)
    {
        outAQ = IntPtr.Zero;
        lock (_gate)
        {
            _newOutputCalls++;
            bool valid = inFormat.mFormatID == FourCC("lpcm") &&
                         inFormat.mSampleRate > 0 &&
                         inFormat.mChannelsPerFrame > 0 &&
                         inFormat.mBitsPerChannel > 0 &&
                         inFormat.mBytesPerFrame == inFormat.mChannelsPerFrame * inFormat.mBitsPerChannel / 8 &&
                         inFormat.mBytesPerPacket == inFormat.mBytesPerFrame * inFormat.mFramesPerPacket;
            if (!valid || FailingNewOutputCalls.Contains(_newOutputCalls))
                return UnsupportedDataFormatError;

            outAQ = NewHandle();
            _queues.Add(new DummyAudioQueue { Handle = outAQ, Format = inFormat, Callback = inCallBackProc });
            Changed();
            return NoErr;
        }
    }

    /* Allocates an AudioQueueBuffer header plus its data block */
    public int AudioQueueAllocateBuffer(IntPtr inAQ, uint inBufferByteSize, out IntPtr outBuffer)
    {
        outBuffer = IntPtr.Zero;
        lock (_gate)
        {
            var queue = FindQueue(inAQ);
            if (queue == null) return ParamErr;
            if (FailAllocateBuffer) return QueueInvalidBufferError;

            IntPtr data = Allocate((int)Math.Max(inBufferByteSize, 1));
            outBuffer = Allocate(Marshal.SizeOf<ICoreAudioApi.AudioQueueBuffer>());
            Marshal.StructureToPtr(new ICoreAudioApi.AudioQueueBuffer
            {
                mAudioDataBytesCapacity = inBufferByteSize,
                mAudioData = data
            }, outBuffer, false);
            queue.AllocatedBuffers.Add(outBuffer);
            return NoErr;
        }
    }

    /* Copies the filled part (mAudioDataByteSize) of a buffer into the queue's playback list */
    public int AudioQueueEnqueueBuffer(IntPtr inAQ, IntPtr inBuffer, uint inNumPacketDescs, IntPtr inPacketDescs)
    {
        lock (_gate)
        {
            var queue = FindQueue(inAQ);
            if (queue == null) return ParamErr;
            if (!queue.AllocatedBuffers.Contains(inBuffer)) return QueueInvalidBufferError;

            var header = Marshal.PtrToStructure<ICoreAudioApi.AudioQueueBuffer>(inBuffer);
            if (header.mAudioDataByteSize == 0) return QueueBufferEmptyError;
            if (header.mAudioDataByteSize > header.mAudioDataBytesCapacity) return QueueInvalidBufferError;

            byte[] pcm = new byte[header.mAudioDataByteSize];
            Marshal.Copy(header.mAudioData, pcm, 0, pcm.Length);
            queue.EnqueuedBuffers.Add((inBuffer, pcm));
            return NoErr;
        }
    }

    /* Starts or resumes the queue; with AutoComplete the buffers are reported as played */
    public int AudioQueueStart(IntPtr inAQ, IntPtr inStartTime)
    {
        lock (_gate)
        {
            var queue = FindQueue(inAQ);
            if (queue == null) return ParamErr;

            queue.StartCount++;
            queue.IsPaused = false;
            if (AutoComplete && !queue.CompletionScheduled && queue.EnqueuedBuffers.Count > 0)
            {
                queue.CompletionScheduled = true;
                _ = CompleteAsync(queue);
            }
            Changed();
            return NoErr;
        }
    }

    /* Pauses the queue */
    public int AudioQueuePause(IntPtr inAQ)
    {
        lock (_gate)
        {
            var queue = FindQueue(inAQ);
            if (queue == null) return ParamErr;
            queue.PauseCount++;
            queue.IsPaused = true;
            return NoErr;
        }
    }

    /* Stops the queue */
    public int AudioQueueStop(IntPtr inAQ, bool inImmediate)
    {
        lock (_gate)
        {
            var queue = FindQueue(inAQ);
            if (queue == null) return ParamErr;
            queue.Stopped = true;
            queue.StoppedImmediately = inImmediate;
            return NoErr;
        }
    }

    /* Disposes the queue; it never calls back afterwards */
    public int AudioQueueDispose(IntPtr inAQ, bool inImmediate)
    {
        lock (_gate)
        {
            var queue = FindQueue(inAQ);
            if (queue == null) return ParamErr;
            queue.Disposed = true;
            Changed();
            return NoErr;
        }
    }

    /* Only the volume parameter (1) exists */
    public int AudioQueueSetParameter(IntPtr inAQ, uint inParamID, float inValue)
    {
        lock (_gate)
        {
            var queue = FindQueue(inAQ);
            if (queue == null) return ParamErr;
            if (inParamID != 1) return QueueInvalidParameterError;
            queue.Volumes.Add(inValue);
            return NoErr;
        }
    }

    /* Only the current-device property ('aqcd', a CFString UID) is supported */
    public int AudioQueueSetProperty(IntPtr inAQ, uint inParamID, ref IntPtr inPropertyData, uint inDatasize)
    {
        lock (_gate)
        {
            var queue = FindQueue(inAQ);
            if (queue == null) return ParamErr;
            if (inParamID != FourCC("aqcd")) return QueueInvalidPropertyError;
            if (inDatasize != IntPtr.Size) return QueueInvalidPropertySizeError;
            if (!_strings.TryGetValue(inPropertyData, out var uid)) return ParamErr;

            queue.RequestedDeviceUids.Add(uid.Value);
            if (Devices.All(d => d.Uid != uid.Value)) return QueueInvalidDeviceError;
            queue.DeviceUid = uid.Value;
            return NoErr;
        }
    }

    /* Length of a live CFString */
    public nint CFStringGetLenght(IntPtr theString)
    {
        lock (_gate) return LiveString(theString).Value.Length;
    }

    /* Copies UTF-16 code units of a live CFString into native memory */
    public void CFStringGetCharacters(IntPtr theString, ICoreAudioApi.CFRange range, IntPtr buffer)
    {
        lock (_gate)
        {
            char[] chars = LiveString(theString).Value.ToCharArray((int)range.Location, (int)range.Lenght);
            Marshal.Copy(chars, 0, buffer, chars.Length);
        }
    }

    /* Creates a CFString from the first numChars characters */
    public IntPtr CFStringCreateWithCharacters(IntPtr alloc, string chars, nint numChars) =>
        CreateString(chars[..(int)numChars]);

    /* Releases a CFString; releasing a dead one crashes */
    public void CFRelease(IntPtr cf)
    {
        lock (_gate)
        {
            var entry = LiveString(cf);
            entry.RetainCount--;
            if (entry.RetainCount == 0)
                _strings.Remove(cf);
        }
    }

    /* Frees every buffer handed out */
    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var block in _allocations)
                Marshal.FreeHGlobal(block);
            _allocations.Clear();
        }
    }

    /* Invokes the output callback once per enqueued buffer unless the queue was disposed */
    private void FireCallbacks(DummyAudioQueue queue)
    {
        List<IntPtr> buffers;
        lock (_gate)
        {
            if (queue.Disposed) return;
            buffers = queue.EnqueuedBuffers.Select(b => b.Buffer).ToList();
        }
        foreach (var buffer in buffers)
            queue.Callback(IntPtr.Zero, queue.Handle, buffer);
    }

    /* Live queue by handle; counts calls on unknown / disposed queues. Caller holds the lock */
    private DummyAudioQueue? FindQueue(IntPtr handle)
    {
        var queue = _queues.FirstOrDefault(q => q.Handle == handle && !q.Disposed);
        if (queue == null) InvalidQueueCalls++;
        return queue;
    }

    /* Live CFString by handle. Caller holds the lock */
    private CFStringEntry LiveString(IntPtr handle) =>
        _strings.TryGetValue(handle, out var entry)
            ? entry
            : throw new InvalidOperationException($"CoreFoundation crash: 0x{handle:X} is not a live CFString");

    /* Unmanaged block freed in Dispose. Caller holds the lock */
    private IntPtr Allocate(int size)
    {
        IntPtr block = Marshal.AllocHGlobal(size);
        _allocations.Add(block);
        return block;
    }

    /* Fresh opaque handle */
    private IntPtr NewHandle() => (IntPtr)Interlocked.Increment(ref _nextHandle);

    /* Wakes WaitForStartedQueuesAsync. Caller holds the lock */
    private void Changed()
    {
        _stateChanged.TrySetResult();
        _stateChanged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
#endif
